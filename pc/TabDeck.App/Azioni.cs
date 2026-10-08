namespace TabDeck;

/// <summary>
/// Che cosa puo' fare un pulsante del deck, descritto una volta sola.
///
/// L'elenco sta qui e non nella finestra perche' erano due elenchi paralleli —
/// uno nel codice che esegue, uno nelle voci del menu a discesa — e bastava
/// aggiungere un'azione da una parte per avere una voce che non faceva niente.
/// Adesso la finestra si costruisce da questa tabella e chi esegue legge lo
/// stesso codice.
/// </summary>
/// <param name="Codice">Quel che finisce in deck.json, e che ActionRunner riconosce.</param>
/// <param name="Nome">Come si chiama nella finestra.</param>
/// <param name="EtichettaValore">Come si chiama il campo principale.</param>
/// <param name="Aiuto">La riga di spiegazione sotto i campi.</param>
/// <param name="HaArgomenti">Se usa anche il secondo campo.</param>
/// <param name="ComeAzione">Se un pulsante intero puo' essere di questo tipo.</param>
/// <param name="ComePasso">Se puo' comparire dentro una sequenza.</param>
/// <param name="Scelte">
/// I soli valori ammessi, quando sono pochi e noti. Dove ci sono, la finestra
/// mostra un elenco invece di un campo da riempire: scrivere "destra" al posto
/// di "destro" e' un errore che non ha nessun motivo di essere possibile.
/// </param>
/// <param name="Cattura">
/// Se il valore si puo' prendere premendo davvero i tasti invece di scriverli.
/// </param>
/// <param name="Subito">
/// Se « Prova » la esegue subito invece che dopo tre secondi: OBS e Twitch non
/// guardano quale finestra ha il fuoco.
/// </param>
public sealed record TipoAzione(
    string Codice,
    string Nome,
    string EtichettaValore,
    string Aiuto,
    bool HaArgomenti = false,
    bool ComeAzione = true,
    bool ComePasso = true,
    string[]? Scelte = null,
    bool Cattura = false,
    bool Subito = false);

public static class Azioni
{
    public static readonly TipoAzione[] Tutti =
    {
        new("hotkey", "Combinazione di tasti", "Combinazione",
            "Una combinazione come ctrl+shift+m, alt+f4, win+d. I nomi dei tasti si scrivono in minuscolo, separati da +. Con « Cattura » la si preme invece di scriverla.",
            Cattura: true),

        new("media", "Tasto multimediale", "Tasto",
            "Un tasto multimediale. Sono gli unici che funzionano anche quando il programma che deve riceverli non ha il fuoco: play, volume e traccia arrivano lo stesso.",
            Scelte: InputInjector.Multimediali),

        new("text", "Scrivi un testo", "Testo",
            "Il testo viene digitato carattere per carattere, qualunque sia la disposizione della tastiera. Buono per indirizzi, firme, risposte che si ripetono."),

        new("run", "Avvia un programma", "Programma",
            "Percorso del programma da avviare, per esempio explorer.exe oppure code. Le variabili come %USERPROFILE% vengono espanse. Gli argomenti vanno nel campo sotto.",
            HaArgomenti: true),

        new("url", "Apri un indirizzo", "Indirizzo",
            "Un indirizzo, aperto nel browser predefinito. Senza http:// davanti viene aggiunto https://."),

        new("click", "Clic del mouse", "Pulsante",
            "Un clic dove si trova il puntatore in quel momento: la macro non lo sposta. Serve a confermare una finestra che si e' appena aperta sempre nello stesso punto.",
            Scelte: new[] { "sinistro", "destro", "centrale", "doppio" }),

        new("wheel", "Rotella del mouse", "Tacche",
            "Quante tacche di rotella, da -20 a 20. Positivo scorre in su, negativo in giu'.",
            ComeAzione: true),

        // Da solo non ha senso — un pulsante che aspetta e basta — ma dentro
        // una sequenza e' quello che separa due passi che si pesterebbero i piedi.
        new("delay", "Aspetta", "Millisecondi",
            "Quanto aspettare prima del passo dopo, in millisecondi. Da 0 a 10000.",
            ComeAzione: false),

        // Tenere e rilasciare esistono solo dentro una sequenza, e per forza:
        // un pulsante che preme shift e finisce li' lascerebbe il tasto premuto
        // e il PC inutilizzabile, senza niente sullo schermo che dica perche'.
        // Dentro una sequenza invece quel che resta premuto viene mollato alla
        // fine, anche se ci si e' dimenticati il passo « Rilascia ».
        new("keydown", "Tieni premuto", "Tasto",
            "Preme il tasto e lo lascia premuto per i passi che seguono. Quel che non viene rilasciato a mano si rilascia da solo alla fine della sequenza.",
            ComeAzione: false, Cattura: true),

        new("keyup", "Rilascia", "Tasto",
            "Molla un tasto tenuto premuto da un passo precedente.",
            ComeAzione: false, Cattura: true),

        // OBS e Twitch: il comando si sceglie da un elenco, e il secondo campo
        // cambia nome e compare secondo il comando (Streaming.Argomento). Il
        // collegamento si imposta nello stesso riquadro, sotto.
        new("obs", "OBS", "Comando",
            "",
            HaArgomenti: true, Scelte: Streaming.ComandiObs, Subito: true),

        new("twitch", "Twitch", "Comando",
            "",
            HaArgomenti: true, Scelte: Streaming.ComandiTwitch, Subito: true),

        // Una sequenza dentro una sequenza e' un gruppo di passi con le sue
        // ripetizioni: e' il modo di dire « questi tre, cinque volte » senza
        // scriverli quindici volte. L'editor non li mostra tutti insieme -
        // sarebbe un albero - ma ci si entra dentro come in una cartella, con
        // le briciole sopra l'elenco che dicono dove si e'.
        new("seq", "Sequenza di passi", "",
            "I passi vengono eseguiti in ordine, dall'alto in basso. Fra un passo e l'altro passano 30 millisecondi; per aspettare di piu' si mette un passo « Aspetta ». Una sequenza dentro un'altra e' un gruppo con le sue ripetizioni.",
            ComeAzione: true, ComePasso: true),

        // L'unica azione che non arriva mai al PC: la cartella la apre il
        // tablet da solo, come cambia pagina da solo. Sta comunque in questa
        // tabella perche' e' li' che la finestra va a leggere cosa puo' fare un
        // pulsante, e una cartella e' una delle cose che puo' fare.
        // Anche questa non arriva mai al PC come azione da eseguire: la
        // intercetta la finestra, che e' l'unica ad avere in mano tutti i
        // profili e il modo di mandarne uno al tablet.
        new("profilo", "Cambia profilo del deck", "Profilo",
            "Il nome di un altro profilo: premendo il pulsante, al tablet va quella griglia al posto di questa. Il deck viene salvato prima di cambiare, cosi' non si perde niente per strada. Dentro una sequenza si mette in fondo: quel che viene dopo parte mentre il tablet sta gia' cambiando griglia.",
            ComeAzione: true, ComePasso: true),

        new("cartella", "Cartella di pulsanti", "",
            "Premendola, sul tablet si apre l'elenco che contiene: una pagina di pulsanti con il nome della cartella in cima e la freccia per tornare indietro. La apre il tablet, quindi funziona anche a PC spento.",
            ComeAzione: true, ComePasso: false),
    };

    public static TipoAzione[] PerPulsante => Tutti.Where(t => t.ComeAzione).ToArray();

    public static TipoAzione[] PerPasso => Tutti.Where(t => t.ComePasso).ToArray();

    /// <summary>
    /// Quante sequenze si possono infilare una dentro l'altra. E' lo stesso
    /// numero dei piani di cartelle, e per lo stesso motivo: piu' giu' di cosi'
    /// ritrovare il passo che si cerca costa piu' che riscriverlo.
    /// </summary>
    public const int Annidamento = 4;

    /// <summary>Quante sequenze annidate porta con se' un'azione: zero se non ne ha.</summary>
    public static int Profondita(ActionSpec? azione)
    {
        if (azione is null || !Di(azione.Type).Codice.Equals("seq", StringComparison.Ordinal)) return 0;
        int giu = 0;
        foreach (var passo in azione.Steps ?? new List<ActionSpec>())
            giu = Math.Max(giu, Profondita(passo));
        return giu + 1;
    }

    /// <summary>Tutti i passi di una sequenza, quelli annidati compresi.</summary>
    public static IEnumerable<ActionSpec> Ovunque(ActionSpec? azione)
    {
        if (azione is null) yield break;
        yield return azione;
        foreach (var passo in azione.Steps ?? new List<ActionSpec>())
            foreach (var giu in Ovunque(passo)) yield return giu;
    }

    /// <summary>Il tipo con quel codice, o la combinazione di tasti se e' ignoto.</summary>
    public static TipoAzione Di(string? codice)
    {
        if (codice is null) return Tutti[0];
        foreach (var t in Tutti)
            if (string.Equals(t.Codice, codice, StringComparison.OrdinalIgnoreCase))
                return t;
        return Tutti[0];
    }

    /// <summary>
    /// Che cosa non va, in una riga, oppure stringa vuota se l'azione e'
    /// eseguibile. Non e' un controllo formale: sono le cose che poi falliscono
    /// in silenzio sul tablet, dove non c'e' modo di accorgersene.
    /// </summary>
    public static string Controlla(ActionSpec? azione) => Controlla(azione, 0);

    private static string Controlla(ActionSpec? azione, int profondita)
    {
        if (azione is null) return "Nessuna azione: premendolo non succede niente.";

        var tipo = Di(azione.Type);
        string valore = azione.Value.Trim();

        switch (tipo.Codice)
        {
            case "hotkey":
            case "keydown":
            case "keyup":
                if (valore.Length == 0) return "Manca la combinazione.";
                if (!InputInjector.TryReadCombo(valore, out _, out string ignoto))
                    return ignoto.Length > 0
                        ? $"'{ignoto}' non e' un nome di tasto."
                        : "Combinazione non leggibile.";
                return "";

            case "media":
                if (valore.Length == 0) return "Manca il tasto.";
                return InputInjector.Multimediali.Contains(valore, StringComparer.OrdinalIgnoreCase)
                    ? ""
                    : $"'{valore}' non e' un tasto multimediale.";

            case "click":
                if (valore.Length == 0) return "Manca il pulsante del mouse.";
                return tipo.Scelte!.Contains(valore, StringComparer.OrdinalIgnoreCase)
                    ? ""
                    : $"'{valore}' non e' un pulsante del mouse.";

            case "wheel":
                return int.TryParse(valore, out int tacche) && tacche is >= -20 and <= 20 && tacche != 0
                    ? ""
                    : "Le tacche devono essere un numero da -20 a 20, zero escluso.";

            case "text":
                return valore.Length == 0 ? "Il testo e' vuoto." : "";

            case "run":
                if (valore.Length == 0) return "Manca il programma da avviare.";
                return Trovabile(valore) ? "" : $"Non trovo '{valore}': verra' provato lo stesso, ma probabilmente fallira'.";

            case "url":
                return valore.Length == 0 ? "Manca l'indirizzo." : "";

            case "delay":
                return int.TryParse(valore, out int ms) && ms is >= 0 and <= 10_000
                    ? ""
                    : "I millisecondi devono essere un numero da 0 a 10000.";

            case "profilo":
                return valore.Length == 0 ? "Manca il nome del profilo." : "";

            case "obs":
            case "twitch":
                return Streaming.Controlla(azione);

            // Una cartella non esegue niente, quindi non c'e' niente che possa
            // fallire: se e' vuota lo dice il controllo del pulsante, che e'
            // l'unico posto da cui si vede cosa contiene.
            case "cartella":
                return "";

            case "seq":
                var passi = azione.Steps ?? new List<ActionSpec>();
                if (passi.Count == 0) return "La sequenza non ha passi.";
                if (azione.Ripeti is < 1 or > 50) return "Le ripetizioni devono essere da 1 a 50.";
                if (profondita >= Annidamento)
                    return $"Piu' di {Annidamento} sequenze una dentro l'altra non si eseguono.";
                for (int i = 0; i < passi.Count; i++)
                {
                    string male = Controlla(passi[i], profondita + 1);
                    if (male.Length > 0) return $"Passo {i + 1}: {male}";
                }
                return "";

            default:
                return $"Tipo di azione sconosciuto: '{azione.Type}'.";
        }
    }

    /// <summary>Il programma esiste come file, o e' un nome che il PATH risolve.</summary>
    private static bool Trovabile(string programma)
    {
        string espanso = Environment.ExpandEnvironmentVariables(programma);
        if (File.Exists(espanso)) return true;
        if (Path.IsPathRooted(espanso)) return false;

        // Senza percorso, ShellExecute cerca nel PATH: si guarda dove guardera' lui.
        string[] estensioni = Path.HasExtension(espanso)
            ? new[] { "" }
            : new[] { ".exe", ".cmd", ".bat", ".com" };

        foreach (var cartella in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (cartella.Length == 0) continue;
            foreach (var estensione in estensioni)
            {
                try
                {
                    if (File.Exists(Path.Combine(cartella, espanso + estensione))) return true;
                }
                catch (ArgumentException)
                {
                    // Una cartella con caratteri non validi dentro PATH: si salta.
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Una riga sola che dice cosa fa un'azione, per l'elenco dei passi e per
    /// l'elenco dei pulsanti: "Tasti · ctrl+c", "Avvia · notepad.exe".
    /// </summary>
    public static string Riassunto(ActionSpec? azione)
    {
        if (azione is null) return "niente";

        var tipo = Di(azione.Type);
        if (tipo.Codice == "seq")
        {
            int quanti = azione.Steps?.Count ?? 0;
            string conto = quanti == 1 ? "Sequenza · 1 passo" : $"Sequenza · {quanti} passi";
            return azione.Ripeti > 1 ? $"{conto} × {azione.Ripeti}" : conto;
        }

        string valore = azione.Value.Trim();
        if (tipo.Codice == "run" && azione.Args.Trim().Length > 0)
            valore += " " + azione.Args.Trim();
        if (tipo.Codice is "obs" or "twitch" && azione.Args.Trim().Length > 0)
            valore += " · " + azione.Args.Trim();
        if (valore.Length > 46) valore = valore[..45] + "…";

        string nome = tipo.Codice switch
        {
            "hotkey" => "Tasti",
            "media" => "Media",
            "text" => "Scrivi",
            "run" => "Avvia",
            "url" => "Apri",
            "delay" => "Aspetta",
            "click" => "Clic",
            "wheel" => "Rotella",
            "keydown" => "Tieni",
            "keyup" => "Rilascia",
            "profilo" => "Profilo",
            "obs" => "OBS",
            "twitch" => "Twitch",
            _ => tipo.Nome,
        };
        return valore.Length == 0 ? nome : $"{nome} · {valore}";
    }

    /// <summary>
    /// Quanto ci mettera' la sequenza, in millisecondi. E' una stima: conta le
    /// attese scritte e i trenta millisecondi di respiro fra un passo e l'altro,
    /// e non sa quanto impieghera' un programma ad aprirsi.
    ///
    /// Serve a rendersi conto di quello che si sta montando: dodici passi con
    /// mezzo secondo d'attesa fanno sei secondi in cui il PC va per conto suo, e
    /// scoprirlo premendo il pulsante e' il modo peggiore.
    /// </summary>
    public static int DurataStimata(ActionSpec? azione)
    {
        if (azione is null || Di(azione.Type).Codice != "seq") return 0;

        int giro = 0;
        foreach (var passo in azione.Steps ?? new List<ActionSpec>())
        {
            giro += Di(passo.Type).Codice switch
            {
                "delay" => Math.Clamp(int.TryParse(passo.Value.Trim(), out int ms) ? ms : 0, 0, 10_000),
                // Una sequenza annidata dura quanto dura lei, piu' il respiro
                // che la separa dal passo dopo.
                "seq" => DurataStimata(passo) + 30,
                _ => 30,   // ActionRunner.RespiroMs
            };
        }
        return giro * Math.Clamp(azione.Ripeti, 1, 50);
    }

    /// <summary>"1,4 s", oppure "260 ms" quando dire i secondi sarebbe ridicolo.</summary>
    public static string Durata(int ms) =>
        ms < 1000 ? $"{ms} ms" : (ms / 1000.0).ToString("0.#") + " s";

    /// <summary>
    /// Come <see cref="Controlla(ActionSpec)"/>, ma per un pulsante intero: e'
    /// da qui che si vede se una cartella e' vuota, cosa che l'azione da sola
    /// non puo' sapere.
    /// </summary>
    public static string Controlla(DeckButton b) => b.Cartella
        ? (b.Dentro.Count == 0 ? "La cartella e' vuota: premendola si apre una pagina senza niente." : "")
        : Controlla(b.Action);

    /// <summary>La riga che descrive un pulsante nell'elenco.</summary>
    public static string Riassunto(DeckButton b)
    {
        if (!b.Cartella) return Riassunto(b.Action);
        int quanti = b.Dentro.Count;
        int cartelle = b.Dentro.Count(f => f.Cartella);
        string conto = quanti switch
        {
            0 => "vuota",
            1 => "1 pulsante",
            _ => $"{quanti} pulsanti",
        };
        return cartelle > 0
            ? $"Cartella · {conto}, di cui {(cartelle == 1 ? "1 cartella" : cartelle + " cartelle")}"
            : $"Cartella · {conto}";
    }
}
