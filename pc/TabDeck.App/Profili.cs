using System.Text.Json;

namespace TabDeck;

/// <summary>
/// Profili del deck che escono da qui ed entrano altrove.
///
/// <c>config/deck.json</c> si copia gia' a mano, ma copiarlo non basta e non e'
/// preciso: non basta perche' i pulsanti con un'immagine nominano file che
/// stanno in <c>config/icone</c>, e di la' quei file non ci sono — arriverebbe
/// un deck di quadratini vuoti; non e' preciso perche' il file li contiene
/// tutti, e chi vuole passare *un* profilo si porterebbe dietro anche gli
/// altri, sovrascrivendo quelli di chi lo riceve.
///
/// Un pacchetto e' un file solo, di testo, con dentro i profili scelti e le
/// immagini che nominano. Si apre con un editor, si manda per posta, si tiene
/// da parte prima di rifare il deck: e' anche il modo piu' corto di dire
/// « questo com'era la settimana scorsa ».
///
/// All'ingresso niente viene sovrascritto. Un profilo che si chiama come uno
/// che c'e' gia' entra col nome libero accanto — « Lavoro 2 » — e i pulsanti
/// « Cambia profilo » che lo nominavano vengono corretti di conseguenza:
/// altrimenti porterebbero al profilo di chi importa, che e' un altro deck.
/// </summary>
public static class Profili
{
    /// <summary>Come si riconosce un pacchetto, e da che versione.</summary>
    private const string Tipo = "tabdeck-profili";

    public const string Filtro =
        "Profili TabDeck (*.tabdeck.json)|*.tabdeck.json|Tutti i file (*.*)|*.*";

    /// <summary>Quel che sta nel file: i profili scelti e le immagini che usano.</summary>
    public sealed class Pacchetto
    {
        public string Tipo { get; set; } = "tabdeck-profili";
        public int Versione { get; set; } = 1;

        /// <summary>Da che macchina e quando: serve a chi ritrova il file fra un anno.</summary>
        public string Origine { get; set; } = "";
        public string Quando { get; set; } = "";

        public List<DeckConfig> Profili { get; set; } = new();

        /// <summary>Nome del file dentro config/icone, e il PNG in base64.</summary>
        public Dictionary<string, string> Icone { get; set; } = new();
    }

    /// <summary>
    /// Scrive il pacchetto. Le immagini viaggiano dentro il file e non accanto:
    /// un pacchetto e' un file solo, altrimenti si perde per strada la cartella
    /// che gli stava a fianco.
    /// </summary>
    public static void Esporta(string percorso, IEnumerable<DeckConfig> quali, out int icone)
    {
        var scelti = quali.Select(ConfigFile.Clona).ToList();

        var pacchetto = new Pacchetto
        {
            Origine = Environment.MachineName,
            Quando = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Profili = scelti,
        };

        foreach (var nome in scelti.SelectMany(p => p.Ovunque())
                     .Select(b => b.Icon)
                     .Concat(scelti.Select(p => p.Sfondo))
                     .Where(n => n.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byte[]? png = Icone.Bytes(nome);
            if (png is not null) pacchetto.Icone[nome] = Convert.ToBase64String(png);
        }

        icone = pacchetto.Icone.Count;
        ConfigFile.Save(percorso, pacchetto);
    }

    /// <summary>
    /// Legge un pacchetto. Torna null con il motivo scritto in
    /// <paramref name="male"/>: un file scelto per sbaglio non deve fare altro
    /// che una riga nel registro.
    /// </summary>
    public static Pacchetto? Leggi(string percorso, out string male)
    {
        male = "";
        try
        {
            var pacchetto = JsonSerializer.Deserialize<Pacchetto>(
                File.ReadAllText(percorso), Lettura);

            if (pacchetto is null || !string.Equals(pacchetto.Tipo, Tipo, StringComparison.OrdinalIgnoreCase))
            {
                male = "non e' un file di profili TabDeck";
                return null;
            }
            if (pacchetto.Profili.Count == 0)
            {
                male = "dentro non c'e' nessun profilo";
                return null;
            }
            return pacchetto;
        }
        catch (JsonException)
        {
            male = "il file e' rovinato o non e' JSON";
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            male = e.Message;
            return null;
        }
    }

    /// <summary>
    /// Mette i profili del pacchetto dentro il file del deck, senza toccare
    /// quelli che ci sono gia'.
    /// </summary>
    /// <returns>I nomi con cui i profili sono entrati davvero.</returns>
    public static List<string> Aggiungi(Pacchetto pacchetto, DeckFile dentro, out int icone)
    {
        icone = 0;
        foreach (var (nome, base64) in pacchetto.Icone)
        {
            try
            {
                if (Icone.Accogli(nome, Convert.FromBase64String(base64))) icone++;
            }
            catch (FormatException)
            {
                // Un'immagine illeggibile costa un pulsante col glifo al posto
                // della faccia, non l'importazione intera.
            }
        }

        // Prima tutti i nomi, poi i pulsanti: un profilo che ne nomina un altro
        // dello stesso pacchetto deve trovarlo col nome nuovo, e il nome nuovo
        // del secondo non si sa finche' non sono stati battezzati tutti.
        var battesimi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entrati = new List<DeckConfig>();

        foreach (var profilo in pacchetto.Profili)
        {
            var copia = ConfigFile.Clona(profilo);
            string chiesto = copia.Nome.Trim();
            if (chiesto.Length == 0) chiesto = "Deck";

            copia.Nome = dentro.NomeLibero(chiesto);
            battesimi[chiesto] = copia.Nome;

            dentro.Profili.Add(copia);
            entrati.Add(copia);
        }

        foreach (var profilo in entrati) Rinomina(profilo.Buttons, battesimi);

        dentro.Sistema();
        return entrati.Select(p => p.Nome).ToList();
    }

    /// <summary>
    /// I pulsanti « Cambia profilo » che nominavano un profilo del pacchetto
    /// devono nominare il nome con cui e' entrato. Quelli che nominano un
    /// profilo che nel pacchetto non c'era si lasciano stare: magari di qua
    /// esiste davvero, e se non esiste lo dira' il controllo del salvataggio.
    /// </summary>
    private static void Rinomina(List<DeckButton> lista, Dictionary<string, string> battesimi)
    {
        foreach (var b in lista)
        {
            if (b.Action is { } azione
                && string.Equals(azione.Type, "profilo", StringComparison.OrdinalIgnoreCase)
                && battesimi.TryGetValue(azione.Value.Trim(), out string? adesso))
            {
                azione.Value = adesso;
            }

            if (b.Buttons is not null) Rinomina(b.Buttons, battesimi);
        }
    }

    private static readonly JsonSerializerOptions Lettura = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
