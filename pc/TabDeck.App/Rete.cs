using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Channels;

namespace TabDeck;

/// <summary>
/// « Internet col cavo: tutto il tablet », dal lato del PC.
///
/// Sul tablet una VPN locale (Tunnel.java) raccoglie i pacchetti IP di tutte le app e li
/// manda qui sul secondo canale del cavo, <c>adb forward tcp:18766 localabstract:tabdeck-rete</c>:
/// [u16 lunghezza][pacchetto]. Qui si leggono, si aprono le connessioni vere dal PC e le
/// risposte tornano indietro rimesse in forma di pacchetti. E' lo schema di gnirehtet:
/// su Android 4.4 manca « adb reverse », quindi e' il PC a collegarsi al tablet.
///
/// Si gestiscono IPv4, TCP e UDP: quel che usano le app. Il canale e' un flusso affidabile
/// e in ordine, quindi il TCP verso il tablet non ritrasmette e non riordina: risponde
/// solo, con le sue finestre. Il DNS va all'indirizzo finto 10.111.0.1 e qui lo si gira
/// al DNS del PC.
/// </summary>
public sealed class Rete : IDisposable
{
    /// <summary>La porta del PC per il secondo canale. La 18765 e' del primo.</summary>
    public const int Porta = 18766;

    private static readonly uint Tablet = Ip("10.111.0.2");
    private static readonly uint DnsFinto = Ip("10.111.0.1");
    private const int MssMassimo = 1360;

    private const byte Fin = 1, Syn = 2, Rst = 4, Psh = 8, Ack = 16;

    private readonly string adb;
    private CancellationTokenSource? vita;
    private TcpClient? canale;
    private NetworkStream? flusso;
    private readonly object scrittura = new();
    private readonly ConcurrentDictionary<(ushort, uint, ushort), Tcp> tcp = new();
    private readonly ConcurrentDictionary<(ushort, uint, ushort), Udp> udp = new();
    private int idPacchetto;

    /// <summary>Una riga per la pagina Tablet: com'e' messo il canale.</summary>
    public string Stato { get; private set; } = "Spento";

    /// <summary>Da qualunque thread.</summary>
    public event Action? StatoCambiato;
    public event Action<string>? Log;

    public Rete(string adb) => this.adb = adb;

    public bool Acceso => vita is not null;

    /// <summary>Si collega al tablet e ci riprova finche' non lo si ferma.</summary>
    public void Avvia()
    {
        if (vita is not null) return;
        vita = new CancellationTokenSource();
        var token = vita.Token;
        Task.Run(() => Giro(token));
    }

    public void Ferma()
    {
        if (vita is null) return;
        vita.Cancel();
        vita = null;
        ChiudiCanale();
        Cambia("Spento");
    }

    public void Dispose() => Ferma();

    private void Cambia(string stato)
    {
        if (stato == Stato) return;
        Stato = stato;
        StatoCambiato?.Invoke();
    }

    // ---- il canale ----

    private async Task Giro(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool arrivato = false;
            try
            {
                var (uscita, testo) = Adb.Esegui(adb, $"forward tcp:{Porta} localabstract:tabdeck-rete");
                if (uscita != 0) throw new IOException("adb forward: " + testo.Trim());

                var c = new TcpClient { NoDelay = true };
                await c.ConnectAsync(IPAddress.Loopback, Porta, token);
                lock (scrittura)
                {
                    canale = c;
                    flusso = c.GetStream();
                }
                Cambia("In attesa del tablet");

                var testa = new byte[2];
                var pacchetto = new byte[65535];
                var s = c.GetStream();
                while (!token.IsCancellationRequested)
                {
                    await s.ReadExactlyAsync(testa, token);
                    int n = BinaryPrimitives.ReadUInt16BigEndian(testa);
                    await s.ReadExactlyAsync(pacchetto.AsMemory(0, n), token);
                    if (!arrivato)
                    {
                        arrivato = true;
                        Cambia("Attivo");
                        Log?.Invoke("Internet col cavo: il tablet usa la rete del PC.");
                    }
                    Gestisci(pacchetto, n);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or SocketException or EndOfStreamException)
            {
                // adb accetta il collegamento anche se dall'altra parte non ascolta nessuno, e
                // lo chiude subito: e' il caso del consenso alla VPN non ancora dato.
                if (!arrivato) Cambia("Il tablet non ha aperto la VPN: accetta la richiesta sul tablet");
            }

            ChiudiCanale();
            if (arrivato && !token.IsCancellationRequested) Log?.Invoke("Internet col cavo: canale chiuso, riprovo.");
            try { await Task.Delay(5000, token); } catch (OperationCanceledException) { }
        }
    }

    private void ChiudiCanale()
    {
        lock (scrittura)
        {
            try { canale?.Close(); } catch (Exception) { }
            canale = null;
            flusso = null;
        }
        foreach (var t in tcp.Values) t.Chiudi();
        tcp.Clear();
        foreach (var u in udp.Values) u.Chiudi();
        udp.Clear();
    }

    private void Manda(byte[] pacchetto)
    {
        lock (scrittura)
        {
            if (flusso is null) return;
            try
            {
                Span<byte> testa = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(testa, (ushort)pacchetto.Length);
                flusso.Write(testa);
                flusso.Write(pacchetto);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                try { canale?.Close(); } catch (Exception) { }
            }
        }
    }

    // ---- i pacchetti del tablet ----

    private void Gestisci(byte[] p, int n)
    {
        if (n < 20 || p[0] >> 4 != 4) return;          // solo IPv4
        int ihl = (p[0] & 0x0F) * 4;
        int totale = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2)), n);
        if (ihl < 20 || totale < ihl) return;
        if ((BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(6)) & 0x3FFF) != 0) return;   // frammenti: non qui
        uint dst = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(16));

        switch (p[9])
        {
            case 17 when totale >= ihl + 8:
                PacchettoUdp(p, ihl, totale, dst);
                break;
            case 6 when totale >= ihl + 20:
                PacchettoTcp(p, ihl, totale, dst);
                break;
        }
    }

    // ---- UDP ----

    private sealed class Udp
    {
        public required UdpClient Client;
        public required IPEndPoint Verso;
        public DateTime Ultimo = DateTime.UtcNow;
        public void Chiudi() { try { Client.Dispose(); } catch (Exception) { } }
    }

    private void PacchettoUdp(byte[] p, int ihl, int totale, uint dst)
    {
        ushort sorgente = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(ihl));
        ushort porta = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(ihl + 2));
        var dati = p.AsSpan(ihl + 8, totale - ihl - 8).ToArray();
        var chiave = (sorgente, dst, porta);

        if (!udp.TryGetValue(chiave, out var u))
        {
            var verso = dst == DnsFinto && porta == 53
                ? new IPEndPoint(DnsDelPc(), 53)
                : new IPEndPoint(new IPAddress(BinaryPrimitives.ReverseEndianness(dst)), porta);
            u = new Udp { Client = new UdpClient(AddressFamily.InterNetwork), Verso = verso };
            udp[chiave] = u;
            _ = RiceviUdp(chiave, u);
        }
        u.Ultimo = DateTime.UtcNow;
        try { u.Client.Send(dati, dati.Length, u.Verso); }
        catch (SocketException) { }
        PulisciUdp();
    }

    private async Task RiceviUdp((ushort Sorgente, uint Dst, ushort Porta) chiave, Udp u)
    {
        try
        {
            while (true)
            {
                var r = await u.Client.ReceiveAsync();
                u.Ultimo = DateTime.UtcNow;
                Manda(Pacchetto(chiave.Dst, Tablet, 17, IntestazioneUdp(chiave.Porta, chiave.Sorgente, r.Buffer)));
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
        udp.TryRemove(new KeyValuePair<(ushort, uint, ushort), Udp>(chiave, u));
    }

    private void PulisciUdp()
    {
        var adesso = DateTime.UtcNow;
        foreach (var (chiave, u) in udp)
        {
            // Il DNS e' una domanda e una risposta; il resto (giochi, chiamate) puo' tacere un po'.
            var limite = chiave.Item3 == 53 ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2);
            if (adesso - u.Ultimo > limite && udp.TryRemove(chiave, out var via)) via.Chiudi();
        }
    }

    private static byte[] IntestazioneUdp(ushort da, ushort a, byte[] dati)
    {
        var u = new byte[8 + dati.Length];
        BinaryPrimitives.WriteUInt16BigEndian(u.AsSpan(0), da);
        BinaryPrimitives.WriteUInt16BigEndian(u.AsSpan(2), a);
        BinaryPrimitives.WriteUInt16BigEndian(u.AsSpan(4), (ushort)u.Length);
        dati.CopyTo(u, 8);
        return u;
    }

    /// <summary>Il DNS che usa il PC; uno pubblico se non se ne trova uno IPv4.</summary>
    private static IPAddress DnsDelPc()
    {
        foreach (var scheda in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (scheda.OperationalStatus != OperationalStatus.Up) continue;
            var ip = scheda.GetIPProperties();
            if (ip.GatewayAddresses.Count == 0) continue;
            var dns = ip.DnsAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            if (dns is not null) return dns;
        }
        return IPAddress.Parse("1.1.1.1");
    }

    // ---- TCP ----

    private sealed class Tcp
    {
        public required (ushort Sorgente, uint Dst, ushort Porta) Chiave;
        public readonly Socket Socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        public readonly Channel<byte[]?> Uscita = Channel.CreateUnbounded<byte[]?>(new UnboundedChannelOptions { SingleReader = true });
        public readonly SemaphoreSlim Finestra = new(0);
        public uint SeqIniziale;         // il SYN del tablet, per riconoscerne le ripetizioni
        public uint DaTablet;            // il prossimo byte che ci aspettiamo dal tablet
        public uint Nostro;              // il prossimo byte che mandiamo al tablet
        public uint Confermato;          // fin dove il tablet ha confermato
        public int FinestraTablet;
        public int Mss;
        public bool Aperta, FinTablet, FinNostro, Chiusa;
        public DateTime Ultimo = DateTime.UtcNow;

        public void Chiudi()
        {
            Chiusa = true;
            Uscita.Writer.TryComplete();
            try { Socket.Close(); } catch (Exception) { }
            Finestra.Release();
        }
    }

    private void PacchettoTcp(byte[] p, int ihl, int totale, uint dst)
    {
        int t = ihl;
        ushort sorgente = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(t));
        ushort porta = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(t + 2));
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(t + 4));
        uint ack = BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(t + 8));
        int offset = (p[t + 12] >> 4) * 4;
        byte flag = p[t + 13];
        int finestra = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(t + 14));
        if (offset < 20 || t + offset > totale) return;
        int lunghezza = totale - t - offset;
        var chiave = (sorgente, dst, porta);

        if ((flag & Syn) != 0 && (flag & Ack) == 0)
        {
            if (tcp.TryGetValue(chiave, out var gia))
            {
                if (gia.SeqIniziale == seq) return;      // lo stesso SYN ripetuto: si sta gia' collegando
                Rimuovi(gia);
            }
            Apri(chiave, seq, finestra, Mss(p, t, offset));
            return;
        }

        if (!tcp.TryGetValue(chiave, out var c))
        {
            // Una connessione che qui non esiste (il canale e' ripartito): la si chiude,
            // cosi' l'app del tablet non aspetta per sempre.
            if ((flag & Rst) == 0)
                Manda(Segmento(chiave, Rst | Ack, ack, seq + (uint)Math.Max(lunghezza, 1), ReadOnlySpan<byte>.Empty, false));
            return;
        }

        if ((flag & Rst) != 0)
        {
            Rimuovi(c);
            return;
        }

        lock (c)
        {
            c.Ultimo = DateTime.UtcNow;
            if ((flag & Ack) != 0)
            {
                if ((int)(ack - c.Confermato) > 0) c.Confermato = ack;
                c.FinestraTablet = finestra;
                if (c.Finestra.CurrentCount == 0) c.Finestra.Release();
            }
            if (!c.Aperta) return;

            bool rispondi = false;
            if (lunghezza > 0)
            {
                if (seq == c.DaTablet && !c.FinTablet)
                {
                    c.Uscita.Writer.TryWrite(p.AsSpan(t + offset, lunghezza).ToArray());
                    c.DaTablet += (uint)lunghezza;
                }
                rispondi = true;
            }
            if ((flag & Fin) != 0 && seq + (uint)lunghezza == c.DaTablet && !c.FinTablet)
            {
                c.DaTablet += 1;
                c.FinTablet = true;
                c.Uscita.Writer.TryWrite(null);       // dopo i dati, chiude l'invio verso il server
                rispondi = true;
            }
            if (rispondi) Manda(Segmento(chiave, Ack, c.Nostro, c.DaTablet, ReadOnlySpan<byte>.Empty, false));

            if (c.FinTablet && c.FinNostro && c.Confermato == c.Nostro) Rimuovi(c);
        }
        PulisciTcp();
    }

    private void Apri((ushort Sorgente, uint Dst, ushort Porta) chiave, uint seq, int finestra, int mss)
    {
        var c = new Tcp
        {
            Chiave = chiave,
            SeqIniziale = seq,
            DaTablet = seq + 1,
            Nostro = (uint)Random.Shared.Next(),
            FinestraTablet = finestra,
            Mss = Math.Min(mss, MssMassimo),
        };
        c.Confermato = c.Nostro;
        tcp[chiave] = c;
        _ = Collega(c);
    }

    private async Task Collega(Tcp c)
    {
        var (_, dst, porta) = c.Chiave;
        try
        {
            using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await c.Socket.ConnectAsync(new IPAddress(BinaryPrimitives.ReverseEndianness(dst)), porta, tempo.Token);
        }
        catch (Exception)
        {
            Manda(Segmento(c.Chiave, Rst | Ack, 0, c.DaTablet, ReadOnlySpan<byte>.Empty, false));
            Rimuovi(c);
            return;
        }

        lock (c)
        {
            if (c.Chiusa) return;
            Manda(Segmento(c.Chiave, Syn | Ack, c.Nostro, c.DaTablet, ReadOnlySpan<byte>.Empty, true));
            c.Nostro += 1;
            c.Confermato = c.Nostro;
            c.Aperta = true;
        }
        _ = Scrivi(c);
        _ = Leggi(c);
    }

    /// <summary>Dal tablet al server, in ordine.</summary>
    private static async Task Scrivi(Tcp c)
    {
        try
        {
            await foreach (var dati in c.Uscita.Reader.ReadAllAsync())
            {
                if (dati is null)
                {
                    c.Socket.Shutdown(SocketShutdown.Send);
                    continue;
                }
                await c.Socket.SendAsync(dati, SocketFlags.None);
            }
        }
        catch (Exception) { }
    }

    /// <summary>Dal server al tablet, rispettando la finestra che il tablet concede.</summary>
    private async Task Leggi(Tcp c)
    {
        var buf = new byte[c.Mss];
        try
        {
            while (!c.Chiusa)
            {
                // Quanto il tablet puo' ancora ricevere: senza, una pagina grossa gli
                // arriverebbe tutta insieme e la scarterebbe.
                int libero = 0;
                while (!c.Chiusa)
                {
                    int inViaggio, finestra;
                    lock (c)
                    {
                        inViaggio = (int)(c.Nostro - c.Confermato);
                        finestra = c.FinestraTablet;
                    }
                    libero = finestra - inViaggio;
                    if (libero >= Math.Min(c.Mss, Math.Max(finestra, 1))) break;
                    await c.Finestra.WaitAsync(200);
                }
                if (c.Chiusa) return;

                int n = await c.Socket.ReceiveAsync(buf.AsMemory(0, Math.Min(buf.Length, libero)), SocketFlags.None);
                lock (c)
                {
                    if (c.Chiusa) return;
                    if (n == 0)
                    {
                        Manda(Segmento(c.Chiave, Fin | Ack, c.Nostro, c.DaTablet, ReadOnlySpan<byte>.Empty, false));
                        c.Nostro += 1;
                        c.FinNostro = true;
                        return;
                    }
                    Manda(Segmento(c.Chiave, Psh | Ack, c.Nostro, c.DaTablet, buf.AsSpan(0, n), false));
                    c.Nostro += (uint)n;
                    c.Ultimo = DateTime.UtcNow;
                }
            }
        }
        catch (Exception)
        {
            if (!c.Chiusa) Manda(Segmento(c.Chiave, Rst | Ack, c.Nostro, c.DaTablet, ReadOnlySpan<byte>.Empty, false));
            Rimuovi(c);
        }
    }

    private void Rimuovi(Tcp c)
    {
        tcp.TryRemove(new KeyValuePair<(ushort, uint, ushort), Tcp>(c.Chiave, c));
        c.Chiudi();
    }

    private void PulisciTcp()
    {
        var adesso = DateTime.UtcNow;
        foreach (var c in tcp.Values)
            if (adesso - c.Ultimo > TimeSpan.FromMinutes(10)) Rimuovi(c);
    }

    /// <summary>Il valore MSS fra le opzioni del SYN, o quello minimo se non c'e'.</summary>
    private static int Mss(byte[] p, int t, int offset)
    {
        for (int i = t + 20; i < t + offset;)
        {
            byte tipo = p[i];
            if (tipo == 0) break;
            if (tipo == 1) { i++; continue; }
            if (i + 1 >= t + offset) break;
            int lung = p[i + 1];
            if (tipo == 2 && lung == 4 && i + 3 < t + offset) return BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(i + 2));
            if (lung < 2) break;
            i += lung;
        }
        return 536;
    }

    private byte[] Segmento((ushort Sorgente, uint Dst, ushort Porta) chiave, int flag, uint seq, uint ack,
        ReadOnlySpan<byte> dati, bool conMss)
    {
        int testa = conMss ? 24 : 20;
        var s = new byte[testa + dati.Length];
        BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(0), chiave.Porta);
        BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(2), chiave.Sorgente);
        BinaryPrimitives.WriteUInt32BigEndian(s.AsSpan(4), seq);
        BinaryPrimitives.WriteUInt32BigEndian(s.AsSpan(8), ack);
        s[12] = (byte)(testa / 4 << 4);
        s[13] = (byte)flag;
        BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(14), 65535);
        if (conMss)
        {
            s[20] = 2;
            s[21] = 4;
            BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(22), MssMassimo);
        }
        dati.CopyTo(s.AsSpan(testa));
        return Pacchetto(chiave.Dst, Tablet, 6, s);
    }

    // ---- IPv4 ----

    /// <summary>Intestazione IPv4 piu' il segmento, con le due somme di controllo.</summary>
    private byte[] Pacchetto(uint da, uint a, byte protocollo, byte[] segmento)
    {
        var p = new byte[20 + segmento.Length];
        p[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), (ushort)p.Length);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), (ushort)Interlocked.Increment(ref idPacchetto));
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), 0x4000);
        p[8] = 64;
        p[9] = protocollo;
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(12), da);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(16), a);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(10), Somma(p.AsSpan(0, 20), 0));
        segmento.CopyTo(p, 20);

        // La pseudo-intestazione: indirizzi, protocollo, lunghezza.
        uint pseudo = (da >> 16) + (da & 0xFFFF) + (a >> 16) + (a & 0xFFFF) + protocollo + (uint)segmento.Length;
        int posto = protocollo == 6 ? 16 : 6;
        ushort somma = Somma(p.AsSpan(20), pseudo);
        if (protocollo == 17 && somma == 0) somma = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(20 + posto), somma);
        return p;
    }

    private static ushort Somma(ReadOnlySpan<byte> dati, uint iniziale)
    {
        uint s = iniziale;
        int i = 0;
        for (; i + 1 < dati.Length; i += 2) s += (uint)(dati[i] << 8 | dati[i + 1]);
        if (i < dati.Length) s += (uint)(dati[i] << 8);
        while (s >> 16 != 0) s = (s & 0xFFFF) + (s >> 16);
        return (ushort)~s;
    }

    private static uint Ip(string testo) => BinaryPrimitives.ReadUInt32BigEndian(IPAddress.Parse(testo).GetAddressBytes());
}
