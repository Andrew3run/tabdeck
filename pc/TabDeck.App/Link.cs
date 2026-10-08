using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TabDeck;

/// <summary>Tipi di frame, gemelli di quelli in Proto.java.</summary>
public static class Proto
{
    public const byte Hello = 0x01;
    public const byte Mode = 0x02;
    public const byte Config = 0x03;
    public const byte Cmd = 0x04;
    public const byte Tile = 0x10;
    public const byte Present = 0x11;
    public const byte Deck = 0x21;
    /// <summary>
    /// Le luci di casa. Il tablet se le salva, a differenza del deck: le
    /// lampade le comanda lui, parlando loro sulla rete, e deve poterlo fare a
    /// PC spento. Si rimanda solo quando l'elenco cambia.
    /// </summary>
    public const byte Luci = 0x30;
    /// <summary>
    /// Un'icona: [u16 lunghezza nome][nome][PNG]. Va mandata prima del DECK che
    /// la nomina, e il tablet se la salva come fa con le luci — cosi'
    /// all'accensione i pulsanti hanno gia' la loro faccia, senza aspettare il PC.
    /// </summary>
    public const byte Icon = 0x40;

    /// <summary>
    /// Da 0x50 a 0x5F i frame sono delle estensioni, nei due versi. Il nucleo legge
    /// solo il 0x50 (annunci, richieste del pacchetto, « pronta ») e il 0x5E (il
    /// pacchetto del tablet); da 0x51 a 0x5D li passa all'estensione accesa senza
    /// guardarci dentro. I nomi dicono l'uso che ne fa l'estensione AiWork OS, che
    /// e' quella per cui sono nati: un'altra li usa come vuole.
    /// </summary>
    public const byte Plugin = 0x50;
    public const byte PluginContesto = 0x51;
    public const byte PluginStato = 0x52;
    public const byte PluginComando = 0x53;
    public const byte PluginTratto = 0x54;
    public const byte PluginVoce = 0x55;
    public const byte PluginRisposta = 0x56;
    public const byte PluginIcona = 0x57;
    public const byte PluginAnteprima = 0x58;
    /// <summary>PC -> tablet: [u8 lunghezza id][id][pacchetto Android dell'estensione].</summary>
    public const byte PluginCodice = 0x5E;
    public const byte PluginUltimo = 0x5F;

    /// <summary>
    /// Il salvaschermo. PC -> tablet, JSON: scelte ed elenco delle foto; tablet ->
    /// PC, JSON: {"mancano":[nomi]}.
    /// </summary>
    public const byte Salvaschermo = 0x60;
    /// <summary>PC -> tablet: [u16 lunghezza nome][nome][JPEG 1024x600].</summary>
    public const byte SalvaschermoFoto = 0x61;

    public const byte Press = 0x20;
    public const byte Touch = 0x22;
    public const byte Wheel = 0x23;
    public const byte Ready = 0x24;
    public const byte Ack = 0x25;

    public const byte TouchDown = 0;
    public const byte TouchMove = 1;
    public const byte TouchUp = 2;
    public const byte TouchRight = 3;

    public const int DiscoveryPort = 8766;
    public const string DiscoveryProbe = "TABDECK?";
    public const string DiscoveryReply = "TABDECK";

    /// <summary>Dove il PC aspetta gli annunci del tablet. Vedi <see cref="Richiamo"/>.</summary>
    public const int AnnouncePort = 8767;
    /// <summary>"TABDECK! &lt;porta TCP&gt; &lt;modello&gt;", mandato dal tablet.</summary>
    public const string Announce = "TABDECK!";
}

/// <summary>Un tablet trovato in rete locale.</summary>
public readonly record struct FoundTablet(string Address, string Model);

/// <summary>
/// Lato PC del collegamento. Il tablet e' il server su tutti e due i
/// trasporti — su Android 4.4 "adb reverse" non esiste, quindi in USB la
/// direzione e' obbligata, e per coerenza il WiFi fa lo stesso.
///
///   USB   "adb forward tcp:18765 localabstract:tabdeck", poi ci si collega a
///         127.0.0.1. Il cavo regge circa 7 MB/s: non e' mai il collo di
///         bottiglia.
///   WiFi  connessione diretta all'indirizzo del tablet. Comodo, ma su un
///         b/g/n a 7 pollici la banda utile sta sotto il megabyte al secondo:
///         va bene per guardare, meno per lavorarci.
/// </summary>
public sealed class Link : IDisposable
{
    private readonly object writeLock = new();

    private TcpClient? client;
    private BufferedStream? stream;
    private readonly byte[] header = new byte[5];

    public event Action<string>? OnPress;
    public event Action<byte, int, int>? OnTouch;
    public event Action<int>? OnWheel;
    /// <summary>Larghezza e altezza utili del pannello, e se il tablet sta guardando lo schermo.</summary>
    public event Action<int, int, bool>? OnReady;
    /// <summary>Il tablet ha finito di disegnare: si puo' mandare il frame dopo.</summary>
    public event Action? OnAck;

    /// <summary>
    /// Un frame di plugin. Arriva sul thread di lettura con il buffer che il
    /// ciclo riusa: chi ascolta si copia quello che gli serve prima di tornare.
    /// </summary>
    public event Action<byte, byte[], int>? OnPlugin;

    /// <summary>Il tablet chiede le foto del salvaschermo che non ha. Thread di lettura.</summary>
    public event Action<string>? OnSalvaschermo;

    /// <summary>Scelte fatte sul tablet, {"sezioni":{...}}. Thread di lettura.</summary>
    public event Action<string>? OnConfig;

    public bool IsConnected => client?.Connected == true;

    /// <summary>"USB" o "WiFi": come si e' collegato, per scriverlo in finestra.</summary>
    public string Transport { get; private set; } = "";

    /// <summary>Con chi si e' collegato: il numero di serie adb o l'indirizzo IP.</summary>
    public string Peer { get; private set; } = "";

    // ---- USB ----

    /// <summary>Tablet visti da adb, pronti a ricevere.</summary>
    public static IReadOnlyList<string> UsbDevices(string adb)
    {
        var (exit, output) = RunAdb(adb, "devices");
        if (exit != 0) return Array.Empty<string>();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Where(l => l.TrimEnd().EndsWith("\tdevice", StringComparison.Ordinal))
            .Select(l => l.Split('\t')[0].Trim())
            .ToList();
    }

    /// <summary>
    /// Apre il collegamento sul cavo. Restituisce stringa vuota se e' andata,
    /// altrimenti il motivo, gia' scritto per essere letto in finestra.
    /// </summary>
    public string ConnectUsb(string adb, int port)
    {
        var devices = UsbDevices(adb);
        if (devices.Count == 0)
            return "nessun tablet visto da adb: controlla il cavo, il debug USB e l'autorizzazione sul tablet";

        var (exit, output) = RunAdb(adb, $"forward tcp:{port} localabstract:tabdeck");
        if (exit != 0)
            return $"adb forward non riuscito: {output.Trim()}";

        string error = Open("127.0.0.1", port);
        if (error.Length > 0)
            return "il tablet e' collegato ma TabDeck non risponde: aprilo sul tablet";

        Transport = "USB";
        Peer = devices[0];
        return "";
    }

    // ---- WiFi ----

    /// <summary>Apre il collegamento in rete verso un indirizzo gia' noto.</summary>
    public string ConnectWifi(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) return "manca l'indirizzo del tablet";

        string error = Open(host.Trim(), port);
        if (error.Length > 0) return $"{host} non risponde sulla porta {port}: {error}";

        Transport = "WiFi";
        Peer = host.Trim();
        return "";
    }

    /// <summary>
    /// Cerca il tablet in rete locale, per non dover leggere l'indirizzo dalle
    /// impostazioni ogni volta che il router glielo cambia.
    ///
    /// Il richiamo va sia in broadcast sia, uno per uno, a tutti gli indirizzi
    /// della sottorete. La ripetizione non e' sprecata: la risposta a un
    /// broadcast arriva da un indirizzo diverso da quello a cui si e' scritto,
    /// e il firewall di Windows non la riconosce come traffico di ritorno —
    /// quindi la scarta, e la ricerca non trovava mai niente. La risposta a un
    /// richiamo unicast invece rientra da sola, senza chiedere all'utente di
    /// aprire niente.
    /// </summary>
    public static List<FoundTablet> Discover(int timeoutMs = 1500)
    {
        var found = new List<FoundTablet>();
        var seen = new HashSet<string>();

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            byte[] probe = Encoding.ASCII.GetBytes(Proto.DiscoveryProbe);
            foreach (var target in ProbeTargets())
            {
                try
                {
                    udp.Send(probe, probe.Length, new IPEndPoint(target, Proto.DiscoveryPort));
                }
                catch (SocketException)
                {
                    // Una scheda giu' o senza indirizzo: si passa alla prossima.
                }
            }

            var deadline = Stopwatch.StartNew();
            udp.Client.ReceiveTimeout = 200;
            while (deadline.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    IPEndPoint from = new(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    string text = Encoding.ASCII.GetString(data).Trim();
                    if (!text.StartsWith(Proto.DiscoveryReply, StringComparison.Ordinal)) continue;

                    string address = from.Address.ToString();
                    if (!seen.Add(address)) continue;

                    var parts = text.Split(' ', 3);
                    found.Add(new FoundTablet(address, parts.Length >= 3 ? parts[2] : "tablet"));
                }
                catch (SocketException)
                {
                    // Scaduto il tempo d'attesa del singolo Receive: si insiste
                    // finche' non scade quello complessivo.
                }
            }
        }
        catch (SocketException)
        {
            // Nessuna rete utilizzabile: l'elenco resta vuoto e la finestra lo dice.
        }

        return found;
    }

    /// <summary>
    /// Dove mandare il richiamo: il broadcast generale, quello di ogni
    /// sottorete, e ogni singolo indirizzo delle sottoreti abbastanza piccole
    /// da poterle percorrere tutte (una /24 sono 254 datagrammi, spediti in
    /// pochi millisecondi).
    ///
    /// Il 255.255.255.255 da solo non basterebbe comunque: con piu' schede di
    /// rete Windows lo manda su una sola, tipicamente quella sbagliata.
    /// </summary>
    private static List<IPAddress> ProbeTargets()
    {
        var targets = new List<IPAddress> { IPAddress.Broadcast };

        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;

                byte[] ip = ua.Address.GetAddressBytes();
                byte[] mask = ua.IPv4Mask?.GetAddressBytes() ?? new byte[] { 255, 255, 255, 0 };

                var broadcast = new byte[4];
                for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                targets.Add(new IPAddress(broadcast));

                // Percorrere la sottorete uno a uno ha senso solo se e' piccola:
                // oltre una /24 sarebbero migliaia di pacchetti per niente.
                if (mask[0] == 255 && mask[1] == 255 && mask[2] == 255)
                {
                    for (int host = 1; host <= 254; host++)
                    {
                        if (host == ip[3]) continue;   // noi stessi
                        targets.Add(new IPAddress(new[] { ip[0], ip[1], ip[2], (byte)host }));
                    }
                }
            }
        }

        return targets;
    }

    // ---- connessione ----

    private string Open(string host, int port)
    {
        Disconnect();
        try
        {
            var c = new TcpClient();
            c.NoDelay = true;
            c.SendBufferSize = 512 * 1024;
            // Senza scadenza, un indirizzo sbagliato bloccherebbe la finestra
            // per i venti secondi del timeout di sistema.
            if (!c.ConnectAsync(host, port).Wait(2500))
            {
                c.Dispose();
                return "nessuna risposta";
            }
            lock (writeLock)
            {
                client = c;
                stream = new BufferedStream(c.GetStream(), 256 * 1024);
            }
            return "";
        }
        catch (Exception e)
        {
            return e is AggregateException agg ? agg.GetBaseException().Message : e.Message;
        }
    }

    public void Disconnect()
    {
        lock (writeLock)
        {
            stream = null;
            client?.Close();
            client = null;
        }
        Transport = "";
        Peer = "";
    }

    private static (int exit, string output) RunAdb(string adb, string args) => Adb.Esegui(adb, args);

    /// <summary>Ciclo di lettura, da eseguire su un thread dedicato.</summary>
    public void ReadLoop(CancellationToken token)
    {
        NetworkStream ns;
        try
        {
            ns = client!.GetStream();
        }
        catch (Exception)
        {
            return;
        }

        // Parte piccolo e cresce: i frame di tutti i giorni sono di pochi byte, ma
        // un tratto lungo o un blocco di voce superano i 64 KB che prima erano il
        // tetto — e oltre il tetto la sessione si chiudeva senza dire niente.
        const int Massimo = 1024 * 1024;
        var payload = new byte[64 * 1024];
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!ReadExact(ns, header, 0, 5)) return;
                byte type = header[0];
                int len = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
                if (len < 0 || len > Massimo) return;
                if (len > payload.Length) payload = new byte[len];
                if (!ReadExact(ns, payload, 0, len)) return;
                Dispatch(type, payload, len);
            }
            catch (IOException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Dispatch(byte type, byte[] p, int len)
    {
        switch (type)
        {
            case Proto.Touch when len >= 5:
                OnTouch?.Invoke(p[0],
                    BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(1, 2)),
                    BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(3, 2)));
                break;

            case Proto.Wheel when len >= 2:
                OnWheel?.Invoke(BinaryPrimitives.ReadInt16BigEndian(p.AsSpan(0, 2)));
                break;

            case Proto.Press:
                {
                    string id = ExtractJsonString(Encoding.UTF8.GetString(p, 0, len), "id");
                    if (id.Length > 0) OnPress?.Invoke(id);
                    break;
                }

            case Proto.Ack:
                OnAck?.Invoke();
                break;

            case >= Proto.Plugin and <= Proto.PluginUltimo:
                OnPlugin?.Invoke(type, p, len);
                break;

            case Proto.Salvaschermo:
                OnSalvaschermo?.Invoke(Encoding.UTF8.GetString(p, 0, len));
                break;

            case Proto.Config:
                OnConfig?.Invoke(Encoding.UTF8.GetString(p, 0, len));
                break;

            case Proto.Ready:
                {
                    string json = Encoding.UTF8.GetString(p, 0, len);
                    OnReady?.Invoke(ExtractJsonInt(json, "panelW"), ExtractJsonInt(json, "panelH"),
                        !json.Contains("\"streaming\":false", StringComparison.Ordinal));
                    break;
                }
        }
    }

    // I due frame JSON in arrivo hanno tre campi in tutto: un parser completo
    // sarebbe piu' codice di quanto serva.
    private static string ExtractJsonString(string json, string key)
    {
        int i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (i < 0) return "";
        i = json.IndexOf(':', i);
        if (i < 0) return "";
        int start = json.IndexOf('"', i + 1);
        if (start < 0) return "";
        int end = json.IndexOf('"', start + 1);
        return end < 0 ? "" : json[(start + 1)..end];
    }

    private static int ExtractJsonInt(string json, string key)
    {
        int i = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (i < 0) return 0;
        i = json.IndexOf(':', i);
        if (i < 0) return 0;
        int j = i + 1;
        while (j < json.Length && !char.IsDigit(json[j]) && json[j] != '-') j++;
        int start = j;
        if (j < json.Length && json[j] == '-') j++;
        while (j < json.Length && char.IsDigit(json[j])) j++;
        return int.TryParse(json.AsSpan(start, j - start), out int v) ? v : 0;
    }

    private static bool ReadExact(NetworkStream ns, byte[] buffer, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = ns.Read(buffer, offset + read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    // ---- invio ----

    public void SendJson(byte type, string json) => Send(type, Encoding.UTF8.GetBytes(json));

    public void SendBytes(byte type, byte[] payload) => Send(type, payload);

    public void SendTile(int x, int y, int w, int h, ReadOnlySpan<byte> jpeg)
    {
        lock (writeLock)
        {
            if (stream is null) return;
            Span<byte> head = stackalloc byte[13];
            head[0] = Proto.Tile;
            BinaryPrimitives.WriteInt32BigEndian(head[1..5], 8 + jpeg.Length);
            BinaryPrimitives.WriteUInt16BigEndian(head[5..7], (ushort)x);
            BinaryPrimitives.WriteUInt16BigEndian(head[7..9], (ushort)y);
            BinaryPrimitives.WriteUInt16BigEndian(head[9..11], (ushort)w);
            BinaryPrimitives.WriteUInt16BigEndian(head[11..13], (ushort)h);
            TryWrite(head);
            TryWrite(jpeg);
        }
    }

    /// <summary>
    /// Manda un'icona. Il nome viaggia col PNG perche' il tablet la mette in
    /// cache con quel nome, ed e' con quel nome che il frame DECK la chiedera'.
    /// </summary>
    public void SendIcon(string nome, ReadOnlySpan<byte> png)
    {
        byte[] name = Encoding.UTF8.GetBytes(nome);
        lock (writeLock)
        {
            if (stream is null) return;
            Span<byte> head = stackalloc byte[7];
            head[0] = Proto.Icon;
            BinaryPrimitives.WriteInt32BigEndian(head[1..5], 2 + name.Length + png.Length);
            BinaryPrimitives.WriteUInt16BigEndian(head[5..7], (ushort)name.Length);
            TryWrite(head);
            TryWrite(name);
            TryWrite(png);
            Flush();
        }
    }

    /// <summary>Chiude il batch di tile e svuota il buffer: qui parte il disegno sul tablet.</summary>
    public void SendPresent()
    {
        lock (writeLock)
        {
            if (stream is null) return;
            Span<byte> head = stackalloc byte[5];
            head[0] = Proto.Present;
            TryWrite(head);
            Flush();
        }
    }

    private void Send(byte type, byte[] payload)
    {
        lock (writeLock)
        {
            if (stream is null) return;
            Span<byte> head = stackalloc byte[5];
            head[0] = type;
            BinaryPrimitives.WriteInt32BigEndian(head[1..5], payload.Length);
            TryWrite(head);
            TryWrite(payload);
            Flush();
        }
    }

    private void Flush()
    {
        try
        {
            stream?.Flush();
        }
        catch (IOException)
        {
            stream = null;
        }
        catch (ObjectDisposedException)
        {
            stream = null;
        }
    }

    private void TryWrite(ReadOnlySpan<byte> data)
    {
        try
        {
            stream?.Write(data);
        }
        catch (IOException)
        {
            stream = null;
        }
        catch (ObjectDisposedException)
        {
            stream = null;
        }
    }

    public void Dispose() => Disconnect();
}
