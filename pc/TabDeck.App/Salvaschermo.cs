using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TabDeck;

/// <summary>
/// Il salvaschermo del tablet, dal lato del PC: le foto e le scelte.
///
/// <para><b>Le foto.</b> Due fonti. Bing, l'immagine del giorno: le ultime sedici
/// dall'archivio pubblico, chieste al piu' ogni sei ore e solo col salvaschermo
/// acceso su quella fonte. La galleria: le immagini scelte qui, copiate in
/// config\salvaschermo\galleria. In tutti e due i casi il PC le taglia a 1024x600
/// e le comprime: sul tablet arrivano pronte da disegnare, e se le tiene.</para>
///
/// <para><b>Il tablet.</b> A ogni collegamento e a ogni cambio si manda la
/// configurazione con l'elenco delle foto; il tablet toglie quelle che non servono
/// piu' e chiede quelle che non ha. Cosi' le foto viaggiano una volta sola.</para>
/// </summary>
public sealed class Salvaschermo : IDisposable
{
    public const int Larghezza = 1024;
    public const int Altezza = 600;
    private const int FotoBing = 16;
    private static readonly TimeSpan PassoBing = TimeSpan.FromHours(6);

    public sealed record Foto(string Nome, string Percorso, string Didascalia);

    private readonly Engine engine;
    private readonly Settings settings;
    private readonly Action salva;
    private readonly string cartella;
    private readonly Timer controllo;
    private int bingInCorso;

    public event Action<string>? Log;
    /// <summary>Le foto sono cambiate. Da qualunque thread.</summary>
    public event Action? Cambiato;

    public Salvaschermo(Engine engine, Settings settings, Action salva, string cartella)
    {
        this.engine = engine;
        this.settings = settings;
        this.salva = salva;
        this.cartella = cartella;
        Directory.CreateDirectory(CartellaBing);
        Directory.CreateDirectory(CartellaGalleria);
        engine.ConnectionChanged += collegato => { if (collegato) Manda(); };
        engine.FrameSalvaschermo += SuRichiesta;
        // Un'occhiata ogni ora costa niente, e chiede davvero a Bing solo se sono
        // passate sei ore e il salvaschermo la usa.
        controllo = new Timer(_ => _ = AggiornaBing(false), null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(1));
    }

    private SalvaschermoSettings Scelte => settings.Salvaschermo;
    public string CartellaBing => Path.Combine(cartella, "bing");
    public string CartellaGalleria => Path.Combine(cartella, "galleria");
    private string FileDidascalie => Path.Combine(CartellaBing, "didascalie.json");

    /// <summary>Le foto della fonte scelta, dalla piu' recente per Bing, in ordine d'arrivo per la galleria.</summary>
    public IReadOnlyList<Foto> Elenco(string fonte)
    {
        if (fonte == "galleria")
            return new DirectoryInfo(CartellaGalleria).GetFiles("*.jpg")
                .OrderBy(f => f.CreationTimeUtc)
                .Select(f => new Foto(f.Name, f.FullName, ""))
                .ToList();

        var didascalie = LeggiDidascalie();
        return new DirectoryInfo(CartellaBing).GetFiles("bing-*.jpg")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new Foto(f.Name, f.FullName, didascalie.GetValueOrDefault(f.Name, "")))
            .ToList();
    }

    /// <summary>Le scelte e l'elenco delle foto, al tablet. Scollegati non parte niente.</summary>
    public void Manda()
    {
        if (!engine.Connected) return;
        var foto = new JsonArray();
        foreach (var f in Elenco(Scelte.Fonte))
            foto.Add(new JsonObject { ["nome"] = f.Nome, ["didascalia"] = f.Didascalia });
        engine.MandaSalvaschermo(new JsonObject
        {
            ["acceso"] = Scelte.Acceso,
            ["dopo"] = Scelte.DopoMinuti,
            ["stile"] = Scelte.Stile,
            ["ogni"] = Scelte.OgniSecondi,
            ["ora"] = Scelte.Ora,
            ["data"] = Scelte.Data,
            ["attenuazione"] = Scelte.Attenuazione,
            ["pannello"] = Scelte.PannelloAcceso,
            ["foto"] = foto,
        }.ToJsonString());
        if (Scelte.Acceso && Scelte.Fonte == "bing") _ = AggiornaBing(false);
    }

    /// <summary>{"mancano":[nomi]} dal tablet. Thread di rete: le foto partono da un task.</summary>
    private void SuRichiesta(string json)
    {
        if (JsonNode.Parse(json)?["mancano"] is not JsonArray mancano || mancano.Count == 0) return;
        var nomi = mancano.Select(n => (string?)n).OfType<string>().ToList();
        _ = Task.Run(() =>
        {
            var tutte = Elenco("bing").Concat(Elenco("galleria")).ToDictionary(f => f.Nome, StringComparer.Ordinal);
            int mandate = 0;
            foreach (var nome in nomi)
            {
                if (!tutte.TryGetValue(nome, out var foto)) continue;
                try
                {
                    engine.MandaFotoSalvaschermo(nome, File.ReadAllBytes(foto.Percorso));
                    mandate++;
                }
                catch (IOException e)
                {
                    Log?.Invoke($"Foto del salvaschermo {nome} non letta: {e.Message}");
                }
            }
            if (mandate > 0) Log?.Invoke($"Salvaschermo: {mandate} foto mandate al tablet.");
        });
    }

    // ---- galleria ----

    /// <summary>Copia un'immagine nella galleria, gia' tagliata per il tablet. Lancia se non si legge.</summary>
    public void Aggiungi(string file)
    {
        byte[] pronta = Prepara(File.ReadAllBytes(file));
        string nome = $"g-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}.jpg";
        File.WriteAllBytes(Path.Combine(CartellaGalleria, nome), pronta);
        Cambiato?.Invoke();
    }

    public void Togli(string nome)
    {
        string file = Path.Combine(CartellaGalleria, Path.GetFileName(nome));
        if (File.Exists(file)) File.Delete(file);
        Cambiato?.Invoke();
        Manda();
    }

    // ---- Bing ----

    /// <summary>
    /// Le immagini del giorno delle ultime due settimane. Senza <paramref name="forza"/>
    /// chiede solo se il salvaschermo le usa e sono passate sei ore.
    /// </summary>
    public async Task<string> AggiornaBing(bool forza)
    {
        if (!forza && (!Scelte.Acceso || Scelte.Fonte != "bing" || DateTime.Now - Scelte.BingAggiornato < PassoBing))
            return "";
        if (Interlocked.Exchange(ref bingInCorso, 1) == 1) return "Sta gia' scaricando.";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var didascalie = LeggiDidascalie();
            int nuove = 0;
            // L'archivio da' otto immagini per volta, e al massimo quindici giorni indietro.
            foreach (int da in new[] { 0, 8 })
            {
                string risposta = await http.GetStringAsync(
                    $"https://www.bing.com/HPImageArchive.aspx?format=js&idx={da}&n=8&mkt=it-IT");
                foreach (var immagine in JsonNode.Parse(risposta)?["images"] as JsonArray ?? new JsonArray())
                {
                    string data = (string?)immagine?["startdate"] ?? "";
                    string base64 = (string?)immagine?["urlbase"] ?? "";
                    if (data.Length != 8 || !data.All(char.IsAsciiDigit) || !base64.StartsWith('/')) continue;
                    string nome = $"bing-{data}.jpg";
                    didascalie[nome] = (string?)immagine?["copyright"] ?? "";
                    string file = Path.Combine(CartellaBing, nome);
                    if (File.Exists(file)) continue;
                    byte[] originale = await http.GetByteArrayAsync($"https://www.bing.com{base64}_1920x1080.jpg");
                    byte[] pronta = await Task.Run(() => Prepara(originale));
                    await File.WriteAllBytesAsync(file, pronta);
                    nuove++;
                }
            }

            var tenute = new DirectoryInfo(CartellaBing).GetFiles("bing-*.jpg")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
            foreach (var vecchia in tenute.Skip(FotoBing)) vecchia.Delete();
            var nomi = tenute.Take(FotoBing).Select(f => f.Name).ToHashSet();
            File.WriteAllText(FileDidascalie, JsonSerializer.Serialize(
                didascalie.Where(d => nomi.Contains(d.Key)).ToDictionary(d => d.Key, d => d.Value)));

            Scelte.BingAggiornato = DateTime.Now;
            Application.Current?.Dispatcher.BeginInvoke(salva);
            string esito = nuove == 0 ? "Bing: nessuna immagine nuova." : $"Bing: {nuove} immagini nuove.";
            Log?.Invoke(esito);
            Cambiato?.Invoke();
            if (nuove > 0) Manda();
            return esito;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException or NotSupportedException)
        {
            string esito = $"Bing non raggiungibile: {e.Message}";
            Log?.Invoke(esito);
            return esito;
        }
        finally
        {
            Interlocked.Exchange(ref bingInCorso, 0);
        }
    }

    private Dictionary<string, string> LeggiDidascalie()
    {
        try
        {
            return File.Exists(FileDidascalie)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FileDidascalie)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    // ---- immagini ----

    /// <summary>
    /// Taglia al centro a 1024x600 riempiendo tutto, raddrizza le foto del telefono
    /// e comprime in JPEG. Ogni oggetto WPF nasce e muore su questo thread.
    /// </summary>
    public static byte[] Prepara(byte[] dati)
    {
        var decoder = BitmapDecoder.Create(new MemoryStream(dati),
            BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource immagine = decoder.Frames[0];

        double angolo = Orientamento(decoder.Frames[0]) switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (angolo != 0) immagine = new TransformedBitmap(immagine, new RotateTransform(angolo));

        double scala = Math.Max(Larghezza / (double)immagine.PixelWidth, Altezza / (double)immagine.PixelHeight) * 1.002;
        var scalata = new TransformedBitmap(immagine, new ScaleTransform(scala, scala));
        int l = Math.Min(Larghezza, scalata.PixelWidth);
        int a = Math.Min(Altezza, scalata.PixelHeight);
        var taglio = new CroppedBitmap(scalata, new Int32Rect((scalata.PixelWidth - l) / 2, (scalata.PixelHeight - a) / 2, l, a));

        // 84: sul sette pollici non si distingue dal 95, e sedici foto viaggiano sul Wi-Fi del tablet.
        var encoder = new JpegBitmapEncoder { QualityLevel = 84 };
        encoder.Frames.Add(BitmapFrame.Create(taglio));
        using var uscita = new MemoryStream();
        encoder.Save(uscita);
        return uscita.ToArray();
    }

    private static int Orientamento(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata m && m.GetQuery("/app1/ifd/{ushort=274}") is ushort o ? o : 1;
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // PNG e altri formati senza EXIF: sono gia' dritti.
            return 1;
        }
    }

    public void Dispose() => controllo.Dispose();
}
