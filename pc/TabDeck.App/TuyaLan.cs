using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TabDeck;

/// <summary>
/// Parla alle lampade Tuya dalla rete di casa, dal lato PC.
///
/// E' lo stesso protocollo di `tablet/src/dev/tabdeck/Tuya.java`, scritto due
/// volte di proposito: sul tablet serve perche' le luci devono funzionare a PC
/// spento, qui serve perche' mentre si prepara una routine si vuole vedere la
/// lampada accendersi davvero, senza alzarsi e senza passare dal tablet.
///
/// I due punti in cui le versioni si comportano diversamente, e che sono
/// facili da sbagliare:
///
///   3.3  si cifra il JSON, POI si mette davanti l'intestazione "3.3"+12 zeri,
///        e in coda va un CRC32.
///   3.4  l'intestazione va PRIMA di cifrare, in coda c'e' un HMAC-SHA256, e
///        prima di ogni comando si negozia una chiave di sessione.
/// </summary>
public sealed class TuyaLan
{
    private const int Porta = 6668;
    private const uint Prefisso = 0x000055AA;
    private const uint Suffisso = 0x0000AA55;

    private const int CmdSessInizio = 0x03;
    private const int CmdSessRisposta = 0x04;
    private const int CmdSessFine = 0x05;
    private const int CmdControllo = 0x07;
    private const int CmdInterroga = 0x0A;
    private const int CmdControlloNew = 0x0D;
    private const int CmdInterrogaNew = 0x10;

    private const int AttesaMs = 4000;

    private readonly string id;
    private readonly byte[] chiave;
    private readonly float versione;
    private string ip;

    // Quale numero e' l'interruttore, quale la luce, quale il colore: si scopre
    // dalla prima risposta e poi non si tocca piu'.
    private char tipo = '?';
    private string dpAcceso = "1";
    private string? dpModo, dpLuminosita, dpColore, dpTemperatura;
    private int lumMin = 25, lumMax = 255;
    private int tempMax = 255;
    private bool mappaFatta;

    /// <summary>
    /// Lo stato completo conosciuto. Dopo un comando la lampada rimanda solo
    /// quello che e' cambiato: sovrascriverlo tutto vorrebbe dire concludere
    /// che una lampada appena accesa non regola piu' niente.
    /// </summary>
    private readonly Dictionary<string, JsonNode?> noto = new();

    public TuyaLan(string id, string ip, string chiave, string versione)
    {
        this.id = id;
        this.ip = ip;
        this.chiave = Encoding.ASCII.GetBytes(chiave);
        this.versione = float.TryParse(versione, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 3.3f;
    }

    public void SetIp(string nuovo) => ip = nuovo;

    public sealed class Stato
    {
        public bool Raggiunta;
        public string Errore = "";
        public bool Accesa;
        /// <summary>Da 1 a 100, oppure -1 se questa lampada non la regola.</summary>
        public int Luminosita = -1;
        public bool HaLuminosita;
        public bool HaColore;
        /// <summary>Il bianco regolabile da caldo a freddo.</summary>
        public bool HaTemperatura;
    }

    // ---- comandi ----

    public Task<Stato> Leggi() => Con(async s => Interpreta(await s.Interroga()));

    public Task<Stato> Accendi(bool on) => Con(async s =>
    {
        Interpreta(await s.Interroga());
        return Interpreta(await s.Comanda(new() { [dpAcceso] = on }));
    });

    public Task<Stato> Inverti() => Con(async s =>
    {
        var prima = Interpreta(await s.Interroga());
        return Interpreta(await s.Comanda(new() { [dpAcceso] = !prima.Accesa }));
    });

    public Task<Stato> Luminosita(int percento) => Con(async s =>
    {
        Interpreta(await s.Interroga());
        if (dpLuminosita is null) return Errore("questa lampada non regola la luce");
        int chiesto = Math.Clamp(percento, 1, 100);
        int v = lumMin + (int)Math.Round((lumMax - lumMin) * (chiesto / 100.0));
        return Interpreta(await s.Comanda(new() { [dpAcceso] = true, [dpLuminosita] = v }));
    });

    public Task<Stato> Colore(int rgb) => Con(async s =>
    {
        Interpreta(await s.Interroga());
        if (dpColore is null) return Errore("questa lampada non fa colori");
        var dps = new Dictionary<string, object> { [dpAcceso] = true };
        if (dpModo is not null) dps[dpModo] = "colour";
        dps[dpColore] = EsadecimaleColore(rgb);
        return Interpreta(await s.Comanda(dps));
    });

    /// <summary>Il bianco da 0 (caldo) a 100 (luce del giorno).</summary>
    public Task<Stato> Temperatura(int percento) => Con(async s =>
    {
        Interpreta(await s.Interroga());
        if (dpTemperatura is null) return Errore("questa lampada ha un bianco solo");
        int v = (int)Math.Round(tempMax * (Math.Clamp(percento, 0, 100) / 100.0));
        var dps = new Dictionary<string, object> { [dpAcceso] = true, [dpTemperatura] = v };
        if (dpModo is not null) dps[dpModo] = "white";
        return Interpreta(await s.Comanda(dps));
    });

    public Task<Stato> Bianco() => Con(async s =>
    {
        Interpreta(await s.Interroga());
        if (dpModo is null) return Errore("questa lampada ha solo il bianco");
        return Interpreta(await s.Comanda(new() { [dpAcceso] = true, [dpModo] = "white" }));
    });

    private async Task<Stato> Con(Func<Sessione, Task<Stato>> cosa)
    {
        Sessione? s = null;
        try
        {
            s = new Sessione(this);
            await s.Apri();
            return await cosa(s);
        }
        catch (Exception e)
        {
            return Errore(e.Message);
        }
        finally
        {
            s?.Dispose();
        }
    }

    // ---- lettura ----

    private Stato Interpreta(JsonObject? risposta)
    {
        var arrivati = EstraiDps(risposta);
        if (arrivati is null) return Errore("risposta senza stato");

        foreach (var (k, v) in arrivati) noto[k] = v?.DeepClone();
        Mappa();

        var stato = new Stato
        {
            Raggiunta = true,
            Accesa = Vero(dpAcceso),
            HaLuminosita = dpLuminosita is not null,
            HaColore = dpColore is not null,
            HaTemperatura = dpTemperatura is not null,
        };
        if (stato.HaLuminosita && noto.TryGetValue(dpLuminosita!, out var b) && b is not null)
        {
            int grezzo = (int)b.GetValue<double>();
            stato.Luminosita = Math.Clamp(
                (int)Math.Round((grezzo - lumMin) * 100.0 / Math.Max(1, lumMax - lumMin)), 1, 100);
        }
        return stato;
    }

    private bool Vero(string dp) =>
        noto.TryGetValue(dp, out var v) && v is not null
        && v.GetValueKind() == JsonValueKind.True;

    private static JsonObject? EstraiDps(JsonObject? risposta)
    {
        if (risposta is null) return null;
        if (risposta["dps"] is JsonObject d) return d;
        return risposta["data"]?["dps"] as JsonObject;
    }

    /// <summary>
    /// Le lampade Tuya si dividono in tre famiglie e non lo dichiarano: si
    /// riconoscono da quali numeri compaiono. Il 20 e' la firma delle recenti.
    /// </summary>
    private void Mappa()
    {
        if (mappaFatta) return;
        mappaFatta = true;

        if (noto.ContainsKey("20"))
        {
            tipo = 'B';
            dpAcceso = "20"; dpModo = "21"; dpLuminosita = "22"; dpColore = "24";
            dpTemperatura = "23";
            lumMin = 10; lumMax = 1000; tempMax = 1000;
        }
        else if (noto.ContainsKey("3") || noto.ContainsKey("5"))
        {
            tipo = 'A';
            dpAcceso = "1"; dpModo = "2"; dpLuminosita = "3"; dpColore = "5";
            dpTemperatura = "4";
            lumMin = 25; lumMax = 255; tempMax = 255;
        }
        else
        {
            tipo = 'C';
            dpAcceso = "1"; dpModo = null; dpColore = null; dpTemperatura = null;
            dpLuminosita = noto.ContainsKey("2") ? "2" : null;
            lumMin = 25; lumMax = 255;
        }

        if (dpLuminosita is not null && !noto.ContainsKey(dpLuminosita)) dpLuminosita = null;
        if (dpColore is not null && !noto.ContainsKey(dpColore)) dpColore = null;
        if (dpModo is not null && !noto.ContainsKey(dpModo)) dpModo = null;
        if (dpTemperatura is not null && !noto.ContainsKey(dpTemperatura)) dpTemperatura = null;
    }

    /// <summary>
    /// Le famiglie recenti vogliono tinta, saturazione e valore su quattro
    /// cifre esadecimali ciascuno; le vecchie prima l'RGB e poi gli stessi tre
    /// numeri su misure piu' piccole.
    /// </summary>
    private string EsadecimaleColore(int rgb)
    {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        (double h, double s, double v) = VersoHsv(r, g, b);

        return tipo == 'B'
            ? $"{(int)Math.Round(h):x4}{(int)Math.Round(s * 1000):x4}{(int)Math.Round(v * 1000):x4}"
            : $"{r:x2}{g:x2}{b:x2}{(int)Math.Round(h):x4}{(int)Math.Round(s * 255):x2}{(int)Math.Round(v * 255):x2}";
    }

    private static (double H, double S, double V) VersoHsv(int r, int g, int b)
    {
        double max = Math.Max(r, Math.Max(g, b)) / 255.0;
        double min = Math.Min(r, Math.Min(g, b)) / 255.0;
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
            if (max == rr) h = 60 * (((gg - bb) / delta) % 6);
            else if (max == gg) h = 60 * ((bb - rr) / delta + 2);
            else h = 60 * ((rr - gg) / delta + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : delta / max, max);
    }

    private static Stato Errore(string testo) => new() { Errore = testo };

    // ---- la connessione ----

    private sealed class Sessione : IDisposable
    {
        private readonly TuyaLan padre;
        private readonly TcpClient socket = new();
        private NetworkStream? flusso;
        private int sequenza = 1;
        private byte[] chiaveCorrente;

        public Sessione(TuyaLan padre)
        {
            this.padre = padre;
            chiaveCorrente = padre.chiave;
        }

        public async Task Apri()
        {
            using var scadenza = new CancellationTokenSource(AttesaMs);
            await socket.ConnectAsync(padre.ip, Porta, scadenza.Token);
            socket.NoDelay = true;
            flusso = socket.GetStream();
            flusso.ReadTimeout = AttesaMs;
            flusso.WriteTimeout = AttesaMs;
            if (padre.versione >= 3.4f) await Negozia();
        }

        public void Dispose() => socket.Dispose();

        /// <summary>
        /// La stretta di mano della 3.4: due numeri casuali, la prova che
        /// entrambi conoscono la chiave, e da li' la chiave di sessione.
        /// </summary>
        private async Task Negozia()
        {
            byte[] mio = RandomNumberGenerator.GetBytes(16);
            byte[] risposta = await Scambia(CmdSessInizio, Cifra(chiaveCorrente, mio, true), CmdSessRisposta);
            if (risposta.Length < 16) throw new Exception("stretta di mano rifiutata");

            byte[] suo = risposta[..16];
            await Manda(CmdSessFine, Cifra(chiaveCorrente, Hmac(chiaveCorrente, suo), true));

            var misto = new byte[16];
            for (int i = 0; i < 16; i++) misto[i] = (byte)(mio[i] ^ suo[i]);
            chiaveCorrente = Cifra(chiaveCorrente, misto, false);
        }

        public async Task<JsonObject?> Interroga()
        {
            if (padre.versione >= 3.4f)
            {
                await Manda(CmdInterrogaNew, CorpoSenzaIntestazione("{}"));
            }
            else
            {
                long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await Manda(CmdInterroga, CorpoSenzaIntestazione(
                    $"{{\"gwId\":\"{padre.id}\",\"devId\":\"{padre.id}\",\"uid\":\"{padre.id}\",\"t\":\"{t}\"}}"));
            }
            return await AttendiStato();
        }

        public async Task<JsonObject?> Comanda(Dictionary<string, object> dps)
        {
            string valori = "{" + string.Join(",", dps.Select(
                p => $"\"{p.Key}\":{Valore(p.Value)}")) + "}";
            long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (padre.versione >= 3.4f)
            {
                await Manda(CmdControlloNew, Corpo(
                    $"{{\"protocol\":5,\"t\":{t},\"data\":{{\"dps\":{valori}}}}}"));
            }
            else
            {
                await Manda(CmdControllo, Corpo(
                    $"{{\"devId\":\"{padre.id}\",\"uid\":\"{padre.id}\",\"t\":\"{t}\",\"dps\":{valori}}}"));
            }

            // La conferma non porta lo stato: quello si richiede a parte.
            try { await Ricevi(); } catch { /* qualche lampada non conferma */ }
            return await Interroga();
        }

        private static string Valore(object v) => v switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(),
            _ => "\"" + v + "\"",
        };

        /// <summary>
        /// Legge finche' non arriva qualcosa che contenga davvero uno stato:
        /// dopo un comando le lampade mandano anche aggiornamenti che nessuno
        /// ha chiesto, e prendere per buona la prima risposta disallinea tutto
        /// il resto della conversazione.
        /// </summary>
        private async Task<JsonObject?> AttendiStato()
        {
            Exception? ultima = null;
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var o = Json(await Ricevi());
                    if (EstraiDps(o) is not null) return o;
                }
                catch (Exception e)
                {
                    ultima = e;
                }
            }
            throw ultima ?? new Exception("nessuno stato nella risposta");
        }

        // ---- pacchetti ----

        private byte[] Corpo(string json)
        {
            byte[] chiaro = Encoding.UTF8.GetBytes(json);
            return padre.versione >= 3.4f
                // In 3.4 l'intestazione di versione va PRIMA di cifrare...
                ? Cifra(chiaveCorrente, Unisci(IntestazioneVersione(), chiaro), true)
                // ...in 3.3 DOPO. E' la differenza che fa fallire in silenzio
                // chi scrive un'implementazione sola per tutt'e due.
                : Unisci(IntestazioneVersione(), Cifra(chiaveCorrente, chiaro, true));
        }

        private byte[] CorpoSenzaIntestazione(string json) =>
            Cifra(chiaveCorrente, Encoding.UTF8.GetBytes(json), true);

        private byte[] IntestazioneVersione() =>
            Unisci(Encoding.ASCII.GetBytes(padre.versione >= 3.4f ? "3.4" : "3.3"), new byte[12]);

        private async Task<byte[]> Scambia(int comando, byte[] payload, int atteso)
        {
            await Manda(comando, payload);
            var (testa, corpo) = await RicevoTutto();
            int cmd = (int)BinaryPrimitives.ReadUInt32BigEndian(testa.AsSpan(8));
            if (atteso != 0 && cmd != atteso)
                throw new Exception($"la lampada ha risposto {cmd} invece di {atteso}: chiave locale sbagliata?");
            return corpo;
        }

        private async Task Manda(int comando, byte[] payload)
        {
            bool conHmac = padre.versione >= 3.4f;
            int coda = conHmac ? 36 : 8;

            var pacchetto = new byte[16 + payload.Length + coda];
            BinaryPrimitives.WriteUInt32BigEndian(pacchetto, Prefisso);
            BinaryPrimitives.WriteUInt32BigEndian(pacchetto.AsSpan(4), (uint)sequenza++);
            BinaryPrimitives.WriteUInt32BigEndian(pacchetto.AsSpan(8), (uint)comando);
            BinaryPrimitives.WriteUInt32BigEndian(pacchetto.AsSpan(12), (uint)(payload.Length + coda));
            payload.CopyTo(pacchetto.AsSpan(16));

            var finora = pacchetto.AsSpan(0, 16 + payload.Length);
            if (conHmac)
            {
                Hmac(chiaveCorrente, finora.ToArray()).CopyTo(pacchetto.AsSpan(16 + payload.Length));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    pacchetto.AsSpan(16 + payload.Length), Crc32(finora));
            }
            BinaryPrimitives.WriteUInt32BigEndian(pacchetto.AsSpan(pacchetto.Length - 4), Suffisso);

            await flusso!.WriteAsync(pacchetto);
        }

        private async Task<byte[]> Ricevi() => (await RicevoTutto()).Corpo;

        private async Task<(byte[] Testa, byte[] Corpo)> RicevoTutto()
        {
            byte[] testa = await Esatti(16);
            if (BinaryPrimitives.ReadUInt32BigEndian(testa) != Prefisso)
                throw new Exception("risposta non riconosciuta");

            int lunghezza = (int)BinaryPrimitives.ReadUInt32BigEndian(testa.AsSpan(12));
            if (lunghezza is < 12 or > 65536) throw new Exception("risposta di lunghezza assurda");

            byte[] resto = await Esatti(lunghezza);
            int coda = padre.versione >= 3.4f ? 36 : 8;
            // I primi quattro byte sono l'esito; in fondo firma e suffisso.
            int quanti = lunghezza - coda - 4;
            if (quanti <= 0) return (testa, Array.Empty<byte>());

            return (testa, Chiaro(resto[4..(4 + quanti)]));
        }

        private byte[] Chiaro(byte[] payload)
        {
            if (payload.Length == 0) return payload;
            return padre.versione >= 3.4f
                ? SenzaIntestazione(Decifra(chiaveCorrente, payload))
                : Decifra(chiaveCorrente, SenzaIntestazione(payload));
        }

        private static byte[] SenzaIntestazione(byte[] d) =>
            d.Length > 15 && d[0] == '3' && d[1] == '.' && d[2] is (byte)'1' or (byte)'3' or (byte)'4' or (byte)'5'
                ? d[15..]
                : d;

        private async Task<byte[]> Esatti(int quanti)
        {
            var buf = new byte[quanti];
            int letti = 0;
            while (letti < quanti)
            {
                using var scadenza = new CancellationTokenSource(AttesaMs);
                int n = await flusso!.ReadAsync(buf.AsMemory(letti, quanti - letti), scadenza.Token);
                if (n <= 0) throw new Exception("la lampada ha chiuso il collegamento");
                letti += n;
            }
            return buf;
        }

        private static JsonObject? Json(byte[] chiaro)
        {
            string testo = Encoding.UTF8.GetString(chiaro).Trim();
            int graffa = testo.IndexOf('{');
            if (graffa < 0) throw new Exception("la lampada non ha mandato uno stato");
            return JsonNode.Parse(testo[graffa..]) as JsonObject;
        }
    }

    // ---- crittografia ----

    private static byte[] Cifra(byte[] chiave, byte[] dati, bool conRiempimento)
    {
        using var aes = Aes.Create();
        aes.Key = chiave;
        return aes.EncryptEcb(dati, conRiempimento ? PaddingMode.PKCS7 : PaddingMode.None);
    }

    private static byte[] Decifra(byte[] chiave, byte[] dati)
    {
        using var aes = Aes.Create();
        aes.Key = chiave;
        return aes.DecryptEcb(dati, PaddingMode.PKCS7);
    }

    private static byte[] Hmac(byte[] chiave, byte[] dati) => HMACSHA256.HashData(chiave, dati);

    private static byte[] Unisci(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }

    private static uint Crc32(ReadOnlySpan<byte> dati)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in dati)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)(-(crc & 1)));
        }
        return ~crc;
    }
}
