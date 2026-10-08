using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TabDeck.Estensioni;

/// <summary>Una spunta che l'estensione dichiara nel suo estensione.json.</summary>
public sealed record Opzione(string Chiave, string Etichetta, bool Predefinita);

/// <summary>estensione.json: chi e', e dove stanno le sue due parti.</summary>
public sealed class Manifesto
{
    public string Id { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Versione { get; init; } = "";
    public string Descrizione { get; init; } = "";
    /// <summary>La DLL del PC, accanto al manifesto.</summary>
    public string Assembly { get; init; } = "";
    /// <summary>La classe che implementa <see cref="IEstensione"/>.</summary>
    public string Tipo { get; init; } = "";
    /// <summary>Il pacchetto del tablet, firmato con la stessa chiave di TabDeck.</summary>
    public string Apk { get; init; } = "";
    /// <summary>La classe che implementa dev.tabdeck.Plugin, dentro il pacchetto del tablet.</summary>
    public string Classe { get; init; } = "";
    public List<Opzione> Opzioni { get; init; } = new();
}

/// <summary>Un'estensione che sta in config\estensioni, accesa o no.</summary>
public sealed class Installata
{
    internal Installata(Manifesto manifesto, string cartella, byte[] apk)
    {
        Manifesto = manifesto;
        Cartella = cartella;
        Apk = apk;
        Impronta = Convert.ToHexStringLower(SHA256.HashData(apk));
    }

    public Manifesto Manifesto { get; }
    public string Cartella { get; }
    internal byte[] Apk { get; }
    internal string Impronta { get; }

    internal Assembly? Codice;
    internal IEstensione? Istanza;
    internal GestoreEstensioni.Ospite? Ospite;
    internal volatile bool Pronta;

    public bool Accesa => Istanza is not null;
    public string Stato => Istanza?.Stato ?? "Spenta.";

    /// <summary>Com'e' messa la parte del tablet. Vuota a tablet scollegato.</summary>
    public string Tablet { get; internal set; } = "";
}

/// <summary>
/// Le estensioni: installarle da un file .tabdeck, accenderle, portare la loro
/// parte sul tablet, toglierle.
///
/// <para><b>Il pacchetto.</b> Uno zip con estensione.json, la DLL del PC e il
/// pacchetto del tablet, tutti in cima. Si installa in config\estensioni\&lt;id&gt;;
/// togliere quella cartella e' togliere l'estensione, e TabDeck resta quello di
/// sempre.</para>
///
/// <para><b>Il tablet.</b> L'APK di TabDeck non contiene nessuna estensione. Il PC
/// annuncia quelle accese con l'impronta del loro pacchetto; il tablet, se non ha
/// quella, la chiede, e il PC la manda sul collegamento stesso — cavo o rete, senza
/// adb. Il tablet la accetta solo firmata con la chiave di TabDeck. Solo quando dice
/// « pronta » l'estensione comincia a parlargli.</para>
///
/// <para><b>Una accesa alla volta.</b> I frame da 0x51 a 0x5D non portano il nome
/// di chi li manda: con due estensioni accese finirebbero l'uno nei pannelli
/// dell'altra.</para>
/// </summary>
public sealed partial class GestoreEstensioni : IDisposable
{
    public const string EstensioneFile = ".tabdeck";
    private const string FileManifesto = "estensione.json";

    private static readonly JsonSerializerOptions Lettura = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Engine engine;
    private readonly Settings settings;
    private readonly Action salva;
    private readonly string radice;
    private readonly List<Installata> installate = new();

    /// <summary>Qualcosa e' cambiato: elenco, stato, tablet. Da qualunque thread.</summary>
    public event Action? Cambiato;
    public event Action<string>? Log;

    public GestoreEstensioni(Engine engine, Settings settings, Action salva, string radice)
    {
        this.engine = engine;
        this.settings = settings;
        this.salva = salva;
        this.radice = radice;
        engine.ConnectionChanged += SuTablet;
        engine.FramePlugin += SuFrame;
    }

    public IReadOnlyList<Installata> Elenco
    {
        get { lock (installate) return installate.ToList(); }
    }

    /// <summary>All'apertura: legge quel che c'e' e riaccende quel che era acceso.</summary>
    public void Carica()
    {
        Directory.CreateDirectory(radice);
        foreach (var cartella in Directory.GetDirectories(radice))
        {
            // Un'installazione interrotta a meta' lascia la cartella di passaggio.
            if (cartella.EndsWith(".nuova", StringComparison.OrdinalIgnoreCase))
            {
                TryElimina(cartella);
                continue;
            }
            try
            {
                var installata = Leggi(cartella);
                lock (installate) installate.Add(installata);
            }
            catch (Exception e)
            {
                Log?.Invoke($"L'estensione in {Path.GetFileName(cartella)} non si legge: {e.Message}");
            }
        }

        foreach (var installata in Elenco)
        {
            // Una cartella messa li' senza passare dalla pagina vale come un'installazione:
            // si accende, come quando la si installa da file.
            bool mai = !settings.Estensioni.ContainsKey(installata.Manifesto.Id);
            if ((mai || Scelta(installata.Manifesto.Id).Accesa) && Elenco.All(i => !i.Accesa))
                Prova(() => AccendiLocked(installata));
            if (mai) Scelta(installata.Manifesto.Id).Accesa = installata.Accesa;
        }
        salva();
        Cambiato?.Invoke();
    }

    /// <summary>
    /// Installa un pacchetto .tabdeck. Un'estensione con lo stesso id si sostituisce,
    /// e resta accesa se lo era. Lancia con un messaggio da mostrare.
    /// </summary>
    public Installata Installa(string pacchetto)
    {
        Directory.CreateDirectory(radice);
        Manifesto manifesto;
        string passaggio;
        using (var zip = ZipFile.OpenRead(pacchetto))
        {
            var voce = zip.GetEntry(FileManifesto)
                       ?? throw new InvalidDataException("nel pacchetto manca estensione.json");
            using (var flusso = voce.Open())
                manifesto = JsonSerializer.Deserialize<Manifesto>(flusso, Lettura)
                            ?? throw new InvalidDataException("estensione.json e' vuoto");
            Valida(manifesto);

            passaggio = Path.Combine(radice, manifesto.Id + ".nuova");
            TryElimina(passaggio);
            Directory.CreateDirectory(passaggio);
            try
            {
                foreach (var file in zip.Entries)
                {
                    if (file.FullName.EndsWith('/')) continue;
                    // Solo file in cima: un nome con dentro un percorso potrebbe scrivere fuori dalla cartella.
                    if (file.FullName != Path.GetFileName(file.FullName))
                        throw new InvalidDataException($"il pacchetto contiene un percorso: {file.FullName}");
                    file.ExtractToFile(Path.Combine(passaggio, file.FullName));
                }
                Leggi(passaggio);
            }
            catch
            {
                TryElimina(passaggio);
                throw;
            }
        }

        string cartella = Path.Combine(radice, manifesto.Id);
        var vecchia = Trova(manifesto.Id);
        bool accendere = vecchia?.Accesa ?? true;
        if (vecchia is not null)
        {
            Spegni(vecchia, annuncia: false);
            lock (installate) installate.Remove(vecchia);
        }
        TryElimina(cartella);
        Directory.Move(passaggio, cartella);

        var installata = Leggi(cartella);
        lock (installate) installate.Add(installata);
        Log?.Invoke(vecchia is null
            ? $"Estensione {manifesto.Nome} {manifesto.Versione} installata."
            : $"Estensione {manifesto.Nome} aggiornata dalla {vecchia.Manifesto.Versione} alla {manifesto.Versione}.");

        MandaInstallate();
        if (accendere) Prova(() => AccendiLocked(installata));
        Scelta(manifesto.Id).Accesa = installata.Accesa;
        salva();
        Cambiato?.Invoke();
        return installata;
    }

    /// <summary>Toglie l'estensione dal PC e dal tablet, con le sue scelte.</summary>
    public void Rimuovi(string id)
    {
        if (Trova(id) is not { } installata) return;
        Spegni(installata, annuncia: false);
        lock (installate) installate.Remove(installata);
        settings.Estensioni.Remove(id);
        salva();
        engine.MandaPlugin(Proto.Plugin, new JsonObject { ["id"] = id, ["rimossa"] = true }.ToJsonString());
        TryElimina(installata.Cartella);
        Log?.Invoke($"Estensione {installata.Manifesto.Nome} rimossa, anche dal tablet.");
        Cambiato?.Invoke();
    }

    /// <summary>Accende o spegne. Torna il motivo se non si accende, altrimenti null.</summary>
    public string? Accendi(string id, bool accesa)
    {
        if (Trova(id) is not { } installata) return null;
        string? errore = null;
        if (accesa) errore = Prova(() => AccendiLocked(installata));
        else Spegni(installata, annuncia: true);

        Scelta(id).Accesa = installata.Accesa;
        salva();
        Cambiato?.Invoke();
        return errore;
    }

    public bool Opzione(Installata installata, string chiave)
    {
        if (Scelta(installata.Manifesto.Id).Opzioni.TryGetValue(chiave, out bool valore)) return valore;
        return installata.Manifesto.Opzioni.FirstOrDefault(o => o.Chiave == chiave)?.Predefinita ?? false;
    }

    public void ImpostaOpzione(string id, string chiave, bool valore)
    {
        Scelta(id).Opzioni[chiave] = valore;
        salva();
    }

    // ---- accendere e spegnere ----

    private void AccendiLocked(Installata installata)
    {
        if (installata.Accesa) return;
        foreach (var altra in Elenco)
        {
            if (altra == installata || !altra.Accesa) continue;
            Spegni(altra, annuncia: true);
            Scelta(altra.Manifesto.Id).Accesa = false;
            Log?.Invoke($"{altra.Manifesto.Nome} spenta: le estensioni si accendono una alla volta.");
        }

        installata.Codice ??= new ContestoEstensione(installata.Manifesto.Id, installata.Cartella)
            .Principale(Path.Combine(installata.Cartella, installata.Manifesto.Assembly));
        var tipo = installata.Codice.GetType(installata.Manifesto.Tipo, throwOnError: false)
                   ?? throw new InvalidDataException($"nella DLL non c'e' {installata.Manifesto.Tipo}");
        if (Activator.CreateInstance(tipo) is not IEstensione istanza)
            throw new InvalidDataException($"{installata.Manifesto.Tipo} non e' un'estensione di TabDeck");

        var ospite = new Ospite(this, installata);
        installata.Ospite = ospite;
        installata.Istanza = istanza;
        istanza.StatoCambiato += () => Cambiato?.Invoke();
        try
        {
            istanza.Accendi(ospite);
        }
        catch
        {
            installata.Istanza = null;
            installata.Ospite = null;
            istanza.Dispose();
            throw;
        }
        Log?.Invoke($"{installata.Manifesto.Nome} accesa.");
        Annuncia(installata);
    }

    private void Spegni(Installata installata, bool annuncia)
    {
        if (installata.Istanza is not { } istanza) return;
        installata.Istanza = null;
        installata.Ospite = null;
        installata.Pronta = false;
        installata.Tablet = "";
        try
        {
            istanza.Dispose();
        }
        catch (Exception e)
        {
            Log?.Invoke($"{installata.Manifesto.Nome} non si e' spenta pulita: {e.Message}");
        }
        if (annuncia)
            engine.MandaPlugin(Proto.Plugin, new JsonObject { ["id"] = installata.Manifesto.Id, ["acceso"] = false }.ToJsonString());
    }

    private string? Prova(Action azione)
    {
        try
        {
            azione();
            return null;
        }
        catch (Exception e)
        {
            string motivo = (e as TargetInvocationException)?.InnerException?.Message ?? e.Message;
            Log?.Invoke($"L'estensione non si accende: {motivo}");
            return motivo;
        }
    }

    // ---- il tablet ----

    private void SuTablet(bool collegato)
    {
        foreach (var installata in Elenco)
        {
            installata.Pronta = false;
            installata.Tablet = "";
        }
        if (collegato)
        {
            MandaInstallate();
            foreach (var installata in Elenco)
                if (installata.Accesa) Annuncia(installata);
        }
        Cambiato?.Invoke();
    }

    /// <summary>Il tablet toglie da se' quelle che qui non ci sono piu', anche se sono state rimosse mentre era spento.</summary>
    private void MandaInstallate()
    {
        var elenco = new JsonArray();
        foreach (var installata in Elenco) elenco.Add(installata.Manifesto.Id);
        engine.MandaPlugin(Proto.Plugin, new JsonObject { ["installate"] = elenco }.ToJsonString());
    }

    private void Annuncia(Installata installata)
    {
        if (!engine.Connected || !installata.Accesa) return;
        installata.Pronta = false;
        installata.Tablet = "In attesa del tablet.";
        var m = installata.Manifesto;
        engine.MandaPlugin(Proto.Plugin, new JsonObject
        {
            ["id"] = m.Id,
            ["acceso"] = true,
            ["nome"] = m.Nome,
            ["versione"] = m.Versione,
            ["classe"] = m.Classe,
            ["impronta"] = installata.Impronta,
        }.ToJsonString());
    }

    /// <summary>Thread di rete, buffer riusato.</summary>
    private void SuFrame(byte tipo, byte[] dati, int lunghezza)
    {
        if (tipo == Proto.Plugin)
        {
            SuRisposta(Encoding.UTF8.GetString(dati, 0, lunghezza));
            return;
        }
        Installata[] tutte;
        lock (installate) tutte = installate.ToArray();
        foreach (var installata in tutte)
            if (installata.Pronta) installata.Ospite?.Arriva(tipo, dati, lunghezza);
    }

    /// <summary>{"chiedi":"codice","id"}, {"pronta":id,"impronta"} o {"errore","id"} dal tablet.</summary>
    private void SuRisposta(string json)
    {
        JsonObject? risposta;
        try
        {
            risposta = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (risposta is null) return;

        string id = (string?)risposta["id"] ?? (string?)risposta["pronta"] ?? "";
        if (Trova(id) is not { Accesa: true } installata) return;

        if ((string?)risposta["chiedi"] == "codice")
        {
            installata.Tablet = $"Invio della parte del tablet ({installata.Apk.Length / 1024} KB).";
            Cambiato?.Invoke();
            byte[] nome = Encoding.ASCII.GetBytes(id);
            var frame = new byte[1 + nome.Length + installata.Apk.Length];
            frame[0] = (byte)nome.Length;
            nome.CopyTo(frame, 1);
            installata.Apk.CopyTo(frame, 1 + nome.Length);
            engine.MandaPluginBytes(Proto.PluginCodice, frame);
        }
        else if (risposta["pronta"] is not null)
        {
            if ((string?)risposta["impronta"] != installata.Impronta) return;
            installata.Pronta = true;
            installata.Tablet = $"Pronta sul tablet, versione {installata.Manifesto.Versione}.";
            Cambiato?.Invoke();
            installata.Ospite?.Pronto();
        }
        else if ((string?)risposta["errore"] is { Length: > 0 } errore)
        {
            installata.Tablet = $"Il tablet non l'ha caricata: {errore}.";
            Log?.Invoke($"{installata.Manifesto.Nome}: il tablet non l'ha caricata ({errore}).");
            Cambiato?.Invoke();
        }
    }

    // ---- file ----

    private Installata Leggi(string cartella)
    {
        string file = Path.Combine(cartella, FileManifesto);
        if (!File.Exists(file)) throw new InvalidDataException("manca estensione.json");
        var manifesto = JsonSerializer.Deserialize<Manifesto>(File.ReadAllText(file), Lettura)
                        ?? throw new InvalidDataException("estensione.json e' vuoto");
        Valida(manifesto);
        foreach (var parte in new[] { manifesto.Assembly, manifesto.Apk })
            if (!File.Exists(Path.Combine(cartella, parte)))
                throw new InvalidDataException($"manca {parte}");
        return new Installata(manifesto, cartella, File.ReadAllBytes(Path.Combine(cartella, manifesto.Apk)));
    }

    /// <summary>
    /// L'id diventa il nome del pacchetto sul tablet (dev.tabdeck.&lt;id&gt;) e il
    /// nome della cartella qui: lettere minuscole e cifre, e basta.
    /// </summary>
    private static void Valida(Manifesto m)
    {
        if (!IdValido().IsMatch(m.Id)) throw new InvalidDataException($"id non valido: « {m.Id} »");
        if (m.Nome.Length == 0) throw new InvalidDataException("manca il nome");
        foreach (var (campo, valore) in new[] { ("assembly", m.Assembly), ("apk", m.Apk) })
            if (valore.Length == 0 || valore != Path.GetFileName(valore))
                throw new InvalidDataException($"« {campo} » dev'essere il nome di un file del pacchetto");
        if (m.Tipo.Length == 0 || m.Classe.Length == 0)
            throw new InvalidDataException("mancano le classi da caricare");
    }

    [GeneratedRegex("^[a-z][a-z0-9]{1,31}$")]
    private static partial Regex IdValido();

    private Installata? Trova(string id)
    {
        lock (installate) return installate.FirstOrDefault(i => i.Manifesto.Id == id);
    }

    private SceltaEstensione Scelta(string id)
    {
        if (!settings.Estensioni.TryGetValue(id, out var scelta))
            settings.Estensioni[id] = scelta = new SceltaEstensione();
        return scelta;
    }

    private void TryElimina(string cartella)
    {
        try
        {
            if (Directory.Exists(cartella)) Directory.Delete(cartella, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke($"{cartella} non si cancella: {e.Message}");
        }
    }

    public void Dispose()
    {
        engine.ConnectionChanged -= SuTablet;
        engine.FramePlugin -= SuFrame;
        foreach (var installata in Elenco) Spegni(installata, annuncia: true);
    }

    /// <summary>
    /// Le DLL di un'estensione si leggono in memoria e non dal disco: caricate da
    /// file resterebbero bloccate fino all'uscita, e « Rimuovi » o un aggiornamento
    /// non potrebbero cancellarle. TabDeck e .NET vengono dal contesto di sempre:
    /// con una seconda copia, IEstensione di la' non sarebbe questa.
    /// </summary>
    private sealed class ContestoEstensione(string id, string cartella) : AssemblyLoadContext("estensione " + id)
    {
        public Assembly Principale(string file) => LoadFromStream(new MemoryStream(File.ReadAllBytes(file)));

        protected override Assembly? Load(AssemblyName nome)
        {
            if (nome.Name is null || nome.Name == typeof(IEstensione).Assembly.GetName().Name) return null;
            string file = Path.Combine(cartella, nome.Name + ".dll");
            return File.Exists(file) ? LoadFromStream(new MemoryStream(File.ReadAllBytes(file))) : null;
        }
    }

    internal sealed class Ospite(GestoreEstensioni gestore, Installata installata) : IOspite
    {
        public event Action? TabletPronto;
        public event Action<byte, byte[], int>? Frame;

        private bool Viva => installata.Ospite == this;

        public bool Opzione(string chiave) => gestore.Opzione(installata, chiave);

        public void Registra(string messaggio) => gestore.Log?.Invoke($"{installata.Manifesto.Nome} · {messaggio}");

        public void Manda(byte tipo, string json)
        {
            if (Viva && installata.Pronta && tipo > Proto.Plugin && tipo < Proto.PluginCodice)
                gestore.engine.MandaPlugin(tipo, json);
        }

        public void MostraSulTablet()
        {
            if (Viva && installata.Pronta) gestore.engine.MostraPlugin(installata.Manifesto.Id);
        }

        internal void Pronto() => TabletPronto?.Invoke();

        internal void Arriva(byte tipo, byte[] dati, int lunghezza)
        {
            if (Viva && tipo > Proto.Plugin && tipo < Proto.PluginCodice) Frame?.Invoke(tipo, dati, lunghezza);
        }
    }
}
