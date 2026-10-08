using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TabDeck;

/// <summary>
/// Le icone dei pulsanti: immagini vere, prese dal PC, e non piu' soltanto i
/// pochi glifi che il font di KitKat sa disegnare.
///
/// Un'immagine scelta dalla finestra non resta dov'era: viene ridotta a un
/// quadrato di {@link Lato} pixel, riscritta in PNG e messa in config/icone con
/// un nome che e' l'impronta del suo contenuto. Ne segue tre cose che servono
/// tutte:
///
///   - due pulsanti con la stessa immagine occupano un file solo;
///   - spostare o cancellare il file originale non rompe il deck;
///   - il PC sa dire al tablet « questa icona si chiama cosi' » senza doversi
///     inventare un numero che poi cambierebbe al salvataggio successivo.
///
/// La riduzione si fa qui e una volta sola perche' di la' non si puo' fare: il
/// PXA986 impiegherebbe piu' a ridimensionare una fotografia da tre megapixel
/// che a disegnare l'intero deck, e la banda del WiFi non regge un'immagine
/// intera per pulsante.
/// </summary>
public static class Icone
{
    /// <summary>
    /// Lato massimo dell'icona salvata. Sul pannello la cella piu' grande —
    /// una griglia 3x2 su 1024x600 — lascia all'icona circa 130 pixel: oltre
    /// questo si manderebbero pixel che il tablet butta via.
    /// </summary>
    public const int Lato = 128;

    /// <summary>
    /// Le icone gia' lette da disco, per non ridecodificare un PNG a ogni
    /// ridisegno dell'anteprima. Il valore nullo e' un « questa non c'e' »
    /// ricordato: senza, un nome rimasto in deck.json e mai piu' trovato
    /// farebbe ritentare l'apertura del file trenta volte al secondo.
    /// </summary>
    private static readonly Dictionary<string, BitmapSource?> memoria =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>config/icone, creata alla prima icona importata.</summary>
    public static string Cartella()
    {
        string dir = Path.Combine(ConfigFile.Directory(), "icone");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Percorso(string nome) => Path.Combine(Cartella(), nome);

    public static bool Esiste(string nome) =>
        nome.Length > 0 && File.Exists(Percorso(nome));

    /// <summary>
    /// L'icona pronta da disegnare, o null se il nome non corrisponde a niente.
    /// Chi disegna deve poter chiamare questo metodo senza controlli: un
    /// pulsante con l'icona sparita torna a mostrare il glifo, e si vede.
    /// </summary>
    public static BitmapSource? Carica(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        if (memoria.TryGetValue(nome, out var gia)) return gia;

        BitmapSource? letta = null;
        try
        {
            string percorso = Percorso(nome);
            if (File.Exists(percorso))
            {
                var immagine = new BitmapImage();
                immagine.BeginInit();
                // OnLoad e non OnDemand: il file va chiuso subito, altrimenti
                // resterebbe bloccato e la pulizia non potrebbe toglierlo.
                immagine.CacheOption = BitmapCacheOption.OnLoad;
                immagine.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                immagine.UriSource = new Uri(percorso);
                immagine.EndInit();
                immagine.Freeze();
                letta = immagine;
            }
        }
        catch (Exception)
        {
            // Un PNG rovinato non deve impedire di disegnare il resto del deck.
            letta = null;
        }

        memoria[nome] = letta;
        return letta;
    }

    /// <summary>Il file cosi' com'e', per mandarlo al tablet.</summary>
    public static byte[]? Bytes(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        try
        {
            string percorso = Percorso(nome);
            return File.Exists(percorso) ? File.ReadAllBytes(percorso) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Prende un'immagine dal disco e la mette fra le icone. Restituisce il
    /// nome da scrivere nel pulsante, oppure stringa vuota con il motivo in
    /// <paramref name="male"/>.
    /// </summary>
    public static string ImportaImmagine(string percorso, out string male)
    {
        male = "";
        try
        {
            var sorgente = new Uri(percorso);
            var decodificatore = BitmapDecoder.Create(sorgente,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            if (decodificatore.Frames.Count == 0)
            {
                male = "il file non contiene nessuna immagine";
                return "";
            }

            // Un .ico ha dentro piu' misure: si prende la piu' grande, che e'
            // quella che regge la riduzione senza sgranare.
            var scelta = decodificatore.Frames
                .OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight)
                .First();

            return Salva(scelta, out male);
        }
        catch (Exception e)
        {
            male = Motivo(e);
            return "";
        }
    }

    /// <summary>
    /// Lo sfondo del deck: un'immagine qualsiasi, ritagliata al centro alle
    /// misure del pannello - 1024 x 600 - e salvata in JPEG accanto alle icone.
    /// Come le icone il nome e' l'impronta del contenuto, e come le icone parte
    /// per il tablet prima della griglia che lo nomina.
    /// </summary>
    public static string ImportaSfondo(string percorso, out string male)
    {
        male = "";
        try
        {
            var decodificatore = BitmapDecoder.Create(new Uri(percorso),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decodificatore.Frames.Count == 0)
            {
                male = "il file non contiene nessuna immagine";
                return "";
            }
            BitmapSource foto = decodificatore.Frames[0];
            if (foto.Format != PixelFormats.Bgr32 && foto.Format != PixelFormats.Bgra32)
                foto = new FormatConvertedBitmap(foto, PixelFormats.Bgr32, null, 0);

            // Ritaglio al centro: la scala che riempie il pannello, il resto fuori.
            const int L = 1024, A = 600;
            double k = Math.Max(L / (double)foto.PixelWidth, A / (double)foto.PixelHeight);
            BitmapSource scalata = new TransformedBitmap(foto, new ScaleTransform(k, k));
            int x = Math.Max(0, (scalata.PixelWidth - L) / 2), y = Math.Max(0, (scalata.PixelHeight - A) / 2);
            var ritaglio = new CroppedBitmap(scalata, new Int32Rect(x, y,
                Math.Min(L, scalata.PixelWidth - x), Math.Min(A, scalata.PixelHeight - y)));

            var jpeg = new JpegBitmapEncoder { QualityLevel = 84 };
            jpeg.Frames.Add(BitmapFrame.Create(ritaglio));
            using var flusso = new MemoryStream();
            jpeg.Save(flusso);
            byte[] dati = flusso.ToArray();

            string nome = Impronta(dati) + ".jpg";
            string dove = Percorso(nome);
            if (!File.Exists(dove)) File.WriteAllBytes(dove, dati);
            memoria.Remove(nome);
            return nome;
        }
        catch (Exception e)
        {
            male = Motivo(e);
            return "";
        }
    }

    /// <summary>
    /// L'icona di un programma, presa da dentro l'eseguibile.
    ///
    /// E' la strada piu' corta per un pulsante « Avvia »: l'icona giusta ce
    /// l'ha gia' il programma, e cercarla come file da qualche parte nel disco
    /// e' una caccia che non deve toccare a chi monta il deck.
    /// </summary>
    public static string ImportaProgramma(string percorso, out string male)
    {
        male = "";
        string espanso = Environment.ExpandEnvironmentVariables(percorso.Trim());
        if (espanso.Length == 0)
        {
            male = "manca il programma";
            return "";
        }

        if (!File.Exists(espanso))
        {
            string? trovato = NelPath(espanso);
            if (trovato is null)
            {
                male = $"non trovo '{percorso}'";
                return "";
            }
            espanso = trovato;
        }

        var immagine = IconaDi(espanso);
        if (immagine is null)
        {
            male = "quel file non ha un'icona dentro";
            return "";
        }
        return Salva(immagine, out male);
    }

    /// <summary>
    /// L'icona piu' grande che il file contiene.
    ///
    /// Prima PrivateExtractIcons, che sceglie da sola la risorsa piu' vicina
    /// alla misura chiesta: e' l'unico modo di avere 128 pixel veri invece dei
    /// 32 della vecchia ExtractIcon ingranditi male. Se il file non e' un
    /// eseguibile — un collegamento, per esempio — si ripiega sulla shell, che
    /// sa risolverlo ma restituisce solo l'icona grande di sistema.
    /// </summary>
    private static BitmapSource? IconaDi(string percorso)
    {
        var handles = new IntPtr[1];
        uint quante = Native.PrivateExtractIcons(percorso, 0, Lato, Lato, handles, new IntPtr[1], 1, 0);
        if (quante > 0 && handles[0] != IntPtr.Zero) return DaHandle(handles[0]);

        var info = new Native.SHFILEINFO();
        IntPtr esito = Native.SHGetFileInfo(percorso, 0, ref info,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.SHFILEINFO>(),
            Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
        return esito != IntPtr.Zero && info.hIcon != IntPtr.Zero ? DaHandle(info.hIcon) : null;
    }

    private static BitmapSource? DaHandle(IntPtr hIcon)
    {
        try
        {
            var immagine = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            immagine.Freeze();
            return immagine;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Native.DestroyIcon(hIcon);
        }
    }

    private static string? NelPath(string programma)
    {
        string[] estensioni = Path.HasExtension(programma)
            ? new[] { "" }
            : new[] { ".exe", ".cmd", ".bat", ".com" };

        foreach (var cartella in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (cartella.Length == 0) continue;
            foreach (var estensione in estensioni)
            {
                try
                {
                    string candidato = Path.Combine(cartella, programma + estensione);
                    if (File.Exists(candidato)) return candidato;
                }
                catch (ArgumentException)
                {
                    // Cartella con caratteri non validi dentro PATH: si salta.
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Riduce, ricodifica in PNG e scrive. Il nome e' l'impronta del PNG: la
    /// stessa immagine importata due volte non produce due file.
    /// </summary>
    private static string Salva(BitmapSource immagine, out string male)
    {
        male = "";
        try
        {
            byte[] png = Codifica(Riduci(immagine));
            string nome = Impronta(png) + ".png";
            string percorso = Percorso(nome);

            if (!File.Exists(percorso)) File.WriteAllBytes(percorso, png);
            memoria.Remove(nome);   // se era stata cancellata e rimessa
            return nome;
        }
        catch (Exception e)
        {
            male = Motivo(e);
            return "";
        }
    }

    private static BitmapSource Riduci(BitmapSource immagine)
    {
        // Sempre a Bgra32: il tablet si aspetta un PNG con canale alfa, e le
        // icone su sfondo scuro senza trasparenza si vedono dentro un francobollo
        // bianco.
        BitmapSource lavoro = immagine.Format == PixelFormats.Bgra32
            ? immagine
            : new FormatConvertedBitmap(immagine, PixelFormats.Bgra32, null, 0);

        int piu = Math.Max(lavoro.PixelWidth, lavoro.PixelHeight);
        if (piu > Lato && piu > 0)
        {
            double k = Lato / (double)piu;
            lavoro = new TransformedBitmap(lavoro, new ScaleTransform(k, k));
        }

        lavoro.Freeze();
        return lavoro;
    }

    private static byte[] Codifica(BitmapSource immagine)
    {
        var codificatore = new PngBitmapEncoder();
        codificatore.Frames.Add(BitmapFrame.Create(immagine));
        using var memoriaFile = new MemoryStream();
        codificatore.Save(memoriaFile);
        return memoriaFile.ToArray();
    }

    /// <summary>
    /// Mette fra le icone un PNG che arriva da fuori — da un file di profili
    /// esportato altrove — tenendo il nome che aveva la'.
    ///
    /// Il nome e' l'impronta del contenuto, quindi un file gia' presente con
    /// quel nome e' gia' quella immagine e non si riscrive. Un nome che non e'
    /// un nome — un percorso, un « .. » — si rifiuta: qui dentro si scrive solo
    /// in config/icone, e un file esportato non e' roba di cui fidarsi.
    /// </summary>
    public static bool Accogli(string nome, byte[] png)
    {
        if (nome.Length == 0 || png.Length == 0) return false;
        if (Path.GetFileName(nome) != nome) return false;
        // PNG le icone, JPEG gli sfondi del deck.
        if (!nome.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && !nome.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            string percorso = Percorso(nome);
            if (!File.Exists(percorso)) File.WriteAllBytes(percorso, png);
            memoria.Remove(nome);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Sedici cifre esadecimali: bastano, e il nome resta leggibile.</summary>
    private static string Impronta(byte[] dati) =>
        Convert.ToHexString(SHA256.HashData(dati).AsSpan(0, 8)).ToLowerInvariant();

    /// <summary>
    /// Toglie da config/icone i file che nessun pulsante usa piu'.
    ///
    /// Si fa al salvataggio e non prima: finche' il deck non e' salvato, un
    /// pulsante appena cancellato puo' ancora tornare indietro, e la sua icona
    /// deve essere ancora li'.
    /// </summary>
    public static void Pulisci(IEnumerable<string> usate)
    {
        var tenere = new HashSet<string>(
            usate.Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in Directory.GetFiles(Cartella(), "*.png"))
            {
                if (tenere.Contains(Path.GetFileName(file))) continue;
                memoria.Remove(Path.GetFileName(file));
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Un file aperto da qualcun altro resta li': non e' un guaio,
                    // e' solo spazio.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Nessuna icona e' mai stata importata.
        }
    }

    private static string Motivo(Exception e) => e switch
    {
        NotSupportedException => "formato non riconosciuto: servono PNG, JPG, BMP, GIF o ICO",
        FileNotFoundException => "il file non c'e' piu'",
        UnauthorizedAccessException => "non ho il permesso di leggere quel file",
        _ => e.Message,
    };
}
