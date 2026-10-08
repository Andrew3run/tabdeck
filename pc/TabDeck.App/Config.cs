using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabDeck;

/// <summary>
/// Impostazioni della finestra, salvate in config/tabdeck.json.
///
/// Il file lo scrive l'applicazione quando premi Salva: non e' pensato per
/// essere modificato a mano mentre il programma gira, e nessuno lo rilegge di
/// nascosto. Aprirlo con un editor resta possibile, ma serve solo a guardarci
/// dentro.
/// </summary>
public sealed class Settings
{
    /// <summary>"usb" per il cavo (adb), "wifi" per il collegamento in rete.</summary>
    public string Transport { get; set; } = "usb";

    /// <summary>Indirizzo del tablet quando Transport vale "wifi". Vuoto = da cercare.</summary>
    public string Host { get; set; } = "";

    /// <summary>Porta su cui il tablet ascolta in rete.</summary>
    public int Port { get; set; } = 8765;

    /// <summary>
    /// Porta del PC che adb inoltra alla socket del tablet, sul cavo.
    ///
    /// Non e' la 8765 perche' sullo stesso PC la 8765 e' gia' del canale locale di
    /// AiWork OS, che ascolta su tutte le interfacce: con le due applicazioni
    /// accese adb non riusciva a prenderla, e il cavo smetteva di collegarsi. Il
    /// lato tablet non cambia — adb inoltra verso la socket astratta, non verso
    /// una porta — quindi questo numero vive solo qui.
    /// </summary>
    public int UsbPort { get; set; } = 18765;

    /// <summary>Percorso di adb.exe; vuoto = quello dentro tools/platform-tools.</summary>
    public string AdbPath { get; set; } = "";

    /// <summary>
    /// Nome Windows dello schermo da mandare al tablet, tipo "\\\\.\\DISPLAY2".
    /// Si salva il nome e non l'indice perche' l'indice cambia da solo appena
    /// si attacca o si stacca un monitor.
    ///
    /// **Vuoto vuol dire lo schermo virtuale**, ed e' il valore normale. Quello
    /// non si cerca per nome, e non e' una dimenticanza: il suo nome cambia a
    /// ogni ricreazione del monitor, e cercarlo cosi' faceva ricadere la
    /// cattura sullo schermo principale - al tablet arrivava il desktop grande
    /// rimpicciolito invece del monitor appena acceso.
    /// </summary>
    public string Screen { get; set; } = "";

    /// <summary>Frame al secondo massimi. Oltre i 20 il PXA986 non tiene il passo.</summary>
    public int Fps { get; set; } = 20;

    /// <summary>Qualita' JPEG, 1-100. Sotto 55 gli artefatti sul testo si vedono.</summary>
    public int Quality { get; set; } = 62;

    public int TileCols { get; set; } = 8;
    public int TileRows { get; set; } = 5;

    /// <summary>Ogni quanto rimandare l'intero schermo anche se nulla e' cambiato.</summary>
    public int FullRefreshSeconds { get; set; } = 5;

    /// <summary>
    /// Quanti frame si possono avere in volo senza conferma. A 1 il PC aspetta
    /// che il tablet abbia finito di disegnare prima ancora di ricominciare a
    /// trasmettere, e trasferimento e decodifica si sommano: sul cavo si vedeva
    /// mezzo megabyte al secondo su sette disponibili. Con 2 il PC trasmette il
    /// frame successivo mentre il tablet decodifica il precedente. Alzarlo
    /// ancora riporta la coda che il freno doveva togliere.
    /// </summary>
    public int MaxInFlight { get; set; } = 2;

    public TouchSettings Touch { get; set; } = new();

    /// <summary>Come deve comportarsi il tablet: sono scelte sue, non del PC.</summary>
    public TabletSettings Tablet { get; set; } = new();

    /// <summary>Disegna il puntatore del mouse dentro il frame.</summary>
    public bool DrawCursor { get; set; } = true;

    /// <summary>
    /// Vero dopo che il fumetto « la finestra si e' ritirata qui » e' stato
    /// mostrato. Si dice una volta sola nella vita dell'installazione: la
    /// seconda volta chi chiude sa gia' dove guardare.
    /// </summary>
    public bool AvvisoVassoio { get; set; }

    /// <summary>
    /// Vero quando il deck e' stato salvato qui ma non e' ancora andato al
    /// tablet. Sta nel file e non solo in memoria perche' la domanda « quello
    /// di la' e' questo? » sopravvive alla chiusura della finestra: il tablet
    /// il suo deck se lo tiene, e nessuno puo' chiederglielo.
    /// </summary>
    public bool DeckDaMandare { get; set; }

    /// <summary>Come <see cref="DeckDaMandare"/>, per le luci di casa.</summary>
    public bool LuciDaMandare { get; set; }

    /// <summary>Quello che si sceglie in Gestione › Impostazioni.</summary>
    public ComportamentoSettings Comportamento { get; set; } = new();

    /// <summary>
    /// Le scelte fatte in Sistema › Estensioni, per id. Le estensioni stanno in
    /// config\estensioni: qui c'e' solo se sono accese e come sono messe le loro
    /// spunte, e togliendone una se ne va anche la sua voce.
    /// </summary>
    public Dictionary<string, SceltaEstensione> Estensioni { get; set; } = new();

    /// <summary>Quello che si sceglie in Gestione › Salvaschermo.</summary>
    public SalvaschermoSettings Salvaschermo { get; set; } = new();
}

public sealed class SceltaEstensione
{
    /// <summary>Spenta, un'estensione non e' nemmeno caricata: nessun socket, nessun thread, nessun frame.</summary>
    public bool Accesa { get; set; }

    /// <summary>Le spunte dichiarate dall'estensione. Quelle mai toccate valgono quanto dice lei.</summary>
    public Dictionary<string, bool> Opzioni { get; set; } = new();
}

/// <summary>
/// Le cose che TabDeck puo' fare senza che si prema un pulsante: collegarsi,
/// riprovare, avvisare. Ognuna e' una spunta, e i valori di partenza lasciano
/// il programma com'era — tutto a mano — tranne le due notifiche, che non
/// fanno niente se non dire quello che e' successo.
///
/// L'avvio con Windows non sta qui: la verita' e' l'attivita' pianificata, e
/// una copia nel file si metterebbe a raccontare il passato appena qualcuno la
/// togliesse dall'Utilita' di pianificazione. Vedi <see cref="Accesso"/>.
/// </summary>
public sealed class ComportamentoSettings
{
    /// <summary>Partito all'accesso, resta accanto all'orologio invece di aprire la finestra.</summary>
    public bool PartiNascosto { get; set; } = true;

    /// <summary>"no", "usb", "wifi", oppure "auto": prima il cavo, poi la rete.</summary>
    public string CollegaAllApertura { get; set; } = "no";

    /// <summary>Attaccato il cavo, si collega da solo.</summary>
    public bool CollegaColCavo { get; set; }

    /// <summary>Caduto il collegamento, o fallito quello all'apertura, riprova a intervalli.</summary>
    public bool Riprova { get; set; }

    /// <summary>
    /// Il tablet preme « Connetti al PC » e ci si collega. Vero di partenza,
    /// perche' era gia' cosi': la richiesta e' la pressione stessa.
    /// </summary>
    public bool AccettaChiamata { get; set; } = true;

    public bool NotificaCollegato { get; set; } = true;
    public bool NotificaCaduto { get; set; } = true;

    /// <summary>Un collegamento automatico non e' riuscito. Spento: chi riprova ogni quindici secondi non vuole un fumetto ogni quindici secondi.</summary>
    public bool NotificaTentativi { get; set; }

    /// <summary>Con la finestra davanti le notizie si leggono gia' li', e il fumetto sarebbe doppio.</summary>
    public bool NotificheSoloNascosta { get; set; } = true;
}

/// <summary>
/// Comportamento del tablet, deciso dalla finestra sul PC.
///
/// Stanno qui e non sul tablet perche' e' al PC che si sta quando si vogliono
/// cambiare, e perche' il tablet non ha piu' un posto dove metterle: la sua
/// unica schermata e' il deck.
/// </summary>
public sealed class TabletSettings
{
    /// <summary>
    /// Quando tenere acceso il pannello: "never", "screen" (solo mentre si
    /// guarda il monitor remoto) o "always".
    ///
    /// Il valore normale non e' "always" per una ragione di corrente: un 7
    /// pollici acceso assorbe piu' di quanto dia una porta USB del PC, e il
    /// tablet si scarica pur essendo attaccato.
    /// </summary>
    public string KeepAwake { get; set; } = "screen";

    /// <summary>Luminosita' 0-100, oppure -1 per lasciare quella di sistema.</summary>
    public int Brightness { get; set; } = -1;

    /// <summary>
    /// Quali sezioni compaiono nella barra col PC collegato. Impostazioni c'e' sempre: e'
    /// la via d'uscita. Il momento dell'ultima scelta, per tutte e due, sta qui.
    /// </summary>
    public SezioniTablet Sezioni { get; set; } = new();

    /// <summary>Quali sezioni compaiono senza PC. Lo schermo remoto qui non conta: senza PC non c'e'.</summary>
    public SezioniTablet SenzaPc { get; set; } = new();
}

public sealed class SezioniTablet
{
    /// <summary>La pagina di casa del tablet: l'ora, le scene, il PC, le estensioni.</summary>
    public bool Dashboard { get; set; } = true;
    public bool Deck { get; set; } = true;
    /// <summary>Solo la voce: lo schermo acceso dal PC si mostra lo stesso, e' un gesto esplicito.</summary>
    public bool Schermo { get; set; } = true;
    public bool Casa { get; set; } = true;
    public bool Orologio { get; set; } = true;

    /// <summary>
    /// Quando si sono scelte l'ultima volta, qui o sul tablet, in millisecondi. Le sezioni
    /// si scelgono dalle due parti anche scollegate, e al collegamento vince la scelta piu'
    /// recente: senza, una delle due si perdeva senza dirlo.
    /// </summary>
    public long Cambiate { get; set; }
}

/// <summary>
/// Il salvaschermo del tablet. Spento di partenza, come tutto quello che TabDeck
/// fa da solo. Le foto le prepara il PC a 1024x600 e il tablet se le tiene: gira
/// anche a PC spento.
/// </summary>
public sealed class SalvaschermoSettings
{
    public bool Acceso { get; set; }
    public int DopoMinuti { get; set; } = 5;
    /// <summary>"foto", "aurora" oppure "orologio".</summary>
    public string Stile { get; set; } = "foto";
    /// <summary>"bing" per l'immagine del giorno, "galleria" per le foto scelte qui.</summary>
    public string Fonte { get; set; } = "bing";
    public int OgniSecondi { get; set; } = 60;
    public bool Ora { get; set; } = true;
    public bool Data { get; set; } = true;
    /// <summary>0-80: quanto si abbassa l'immagine.</summary>
    public int Attenuazione { get; set; } = 15;
    public bool PannelloAcceso { get; set; }
    /// <summary>L'ultima volta che si e' chiesto a Bing: non piu' di una volta ogni sei ore.</summary>
    public DateTime BingAggiornato { get; set; }
}

/// <summary>Come il tablet deve tradurre i gesti in eventi del mouse.</summary>
public sealed class TouchSettings
{
    /// <summary>"touch" = swipe scorre, oppure "pointer" = il dito e' il mouse.</summary>
    public string Mode { get; set; } = "touch";

    /// <summary>Millisecondi di pressione ferma che valgono un tasto destro.</summary>
    public int LongPressMs { get; set; } = 550;

    /// <summary>Pixel di swipe che valgono una tacca di rotella.</summary>
    public int WheelNotchPx { get; set; } = 48;

    /// <summary>A false il contenuto segue il dito, come su un telefono.</summary>
    public bool InvertScroll { get; set; }
}

public sealed class DeckConfig
{
    /// <summary>
    /// Come si chiama questo profilo. Vuoto solo nei file scritti prima che i
    /// profili esistessero: al caricamento ne prende uno.
    /// </summary>
    public string Nome { get; set; } = "";

    public int Cols { get; set; } = 5;
    public int Rows { get; set; } = 3;

    /// <summary>
    /// L'immagine dietro il deck, in config/icone, oppure vuoto per il fondo
    /// grafite. E' del profilo: il deck del lavoro e quello dei film possono
    /// avere due sfondi diversi.
    /// </summary>
    public string Sfondo { get; set; } = "";
    public List<DeckButton> Buttons { get; set; } = new();

    /// <summary>
    /// Quanto si puo' scendere di cartella in cartella. Il tablet reggerebbe
    /// qualunque profondita', ma dopo il quarto piano non si ricorda piu' dove
    /// si e' messa una cosa, ed e' l'opposto del motivo per cui esistono.
    /// </summary>
    public const int Piani = 4;

    /// <summary>
    /// Tutti i pulsanti del deck, quelli dentro le cartelle compresi. Serve
    /// dove non conta dove stanno ma che ci sono: le icone da mandare, gli
    /// identificativi gia' presi, i controlli prima di salvare.
    /// </summary>
    public IEnumerable<DeckButton> Ovunque() => Scendendo(Buttons);

    private static IEnumerable<DeckButton> Scendendo(List<DeckButton> lista)
    {
        foreach (var b in lista)
        {
            yield return b;
            if (b.Buttons is null) continue;
            foreach (var dentro in Scendendo(b.Buttons)) yield return dentro;
        }
    }

    /// <summary>
    /// L'elenco aperto seguendo un percorso di indici — [] e' la radice, [2]
    /// e' il contenuto della terza cartella, e cosi' via.
    ///
    /// Il percorso e' fatto di numeri e non di riferimenti perche' deve
    /// sopravvivere a un « Annulla », che rimette in piedi un albero nuovo di
    /// zecca: i riferimenti di prima punterebbero a pulsanti che non stanno
    /// piu' in nessun deck.
    /// </summary>
    public List<DeckButton> Contenuto(IReadOnlyList<int> percorso)
    {
        var lista = Buttons;
        foreach (int i in percorso)
        {
            if (i < 0 || i >= lista.Count || lista[i].Buttons is null) return lista;
            lista = lista[i].Buttons!;
        }
        return lista;
    }

    /// <summary>
    /// Il percorso accorciato fino a dove regge davvero. Una cartella tolta
    /// mentre la si guardava lascerebbe altrimenti la finestra dentro un posto
    /// che non esiste piu'.
    /// </summary>
    public List<int> PercorsoValido(IReadOnlyList<int> percorso)
    {
        var buono = new List<int>();
        var lista = Buttons;
        foreach (int i in percorso)
        {
            if (i < 0 || i >= lista.Count || lista[i].Buttons is null) break;
            buono.Add(i);
            lista = lista[i].Buttons!;
        }
        return buono;
    }

    /// <summary>Le cartelle che si attraversano seguendo il percorso.</summary>
    public IEnumerable<DeckButton> Cartelle(IReadOnlyList<int> percorso)
    {
        var lista = Buttons;
        foreach (int i in percorso)
        {
            if (i < 0 || i >= lista.Count || lista[i].Buttons is null) yield break;
            yield return lista[i];
            lista = lista[i].Buttons!;
        }
    }

    /// <summary>
    /// Mette a posto quello che arriva dal file, che puo' essere stato scritto
    /// a mano o da una versione precedente.
    ///
    /// Tre cose: una cartella deve avere il suo elenco anche se vuoto, un
    /// pulsante con dentro dei figli e' una cartella anche se nessuno l'ha
    /// detto, e gli identificativi devono essere unici in tutto l'albero —
    /// e' l'unica cosa che il tablet rimanda indietro, e due pulsanti con lo
    /// stesso nome ne farebbero eseguire sempre uno solo.
    /// </summary>
    public void Normalizza()
    {
        var presi = new HashSet<string>(StringComparer.Ordinal);
        Sistema(Buttons, presi);
    }

    private static void Sistema(List<DeckButton> lista, HashSet<string> presi)
    {
        foreach (var b in lista)
        {
            if (b.Cartella) b.Buttons ??= new();
            else if (b.Buttons is not null) b.Action = new ActionSpec { Type = "cartella" };

            if (b.Id.Length == 0 || !presi.Add(b.Id))
            {
                string radice = b.Id.Length > 0 ? b.Id : "pulsante";
                for (int n = 2; ; n++)
                {
                    string id = radice + n;
                    if (presi.Add(id)) { b.Id = id; break; }
                }
            }

            if (b.Buttons is not null) Sistema(b.Buttons, presi);
        }
    }
}

/// <summary>
/// Il contenuto di config/deck.json: piu' deck, e quale dei due il tablet sta
/// mostrando adesso.
///
/// Un deck solo non basta appena il tablet serve a due cose diverse — lavorare
/// e guardare un film vogliono pulsanti diversi, non gli stessi in ordine
/// diverso. I profili sono la risposta piu' corta: la griglia intera si cambia
/// in blocco, e a cambiarla puo' essere anche un pulsante del deck stesso.
///
/// Stanno tutti in un file solo, e non uno per file, perche' e' il file che si
/// copia quando si vuole portare il deck su un altro PC: uno solo da copiare,
/// e dentro c'e' tutto.
/// </summary>
public sealed class DeckFile
{
    /// <summary>Il nome del profilo che il tablet sta mostrando.</summary>
    public string Attivo { get; set; } = "";

    public List<DeckConfig> Profili { get; set; } = new();

    /// <summary>
    /// Legge il file, accettando anche quelli scritti prima che i profili
    /// esistessero: li' i pulsanti stanno in cima, senza nessun profilo
    /// attorno, e diventano il profilo « Deck ».
    /// </summary>
    public static DeckFile Carica(string percorso)
    {
        var file = ConfigFile.Load<DeckFile>(percorso);

        if (file.Profili.Count == 0)
        {
            var solo = ConfigFile.Load<DeckConfig>(percorso);
            solo.Nome = "Deck";
            file.Profili.Add(solo);
            file.Attivo = solo.Nome;
        }

        file.Sistema();
        return file;
    }

    /// <summary>
    /// Nomi non vuoti e non ripetuti, un profilo attivo che esiste davvero, e
    /// ogni deck messo a posto. Un file scritto a mano puo' avere tutto storto,
    /// e la finestra non deve aprirsi rotta.
    /// </summary>
    public void Sistema()
    {
        if (Profili.Count == 0) Profili.Add(new DeckConfig { Nome = "Deck" });

        var presi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profilo in Profili)
        {
            string nome = profilo.Nome.Trim();
            if (nome.Length == 0) nome = "Deck";
            if (!presi.Add(nome))
            {
                string radice = nome;
                for (int n = 2; ; n++)
                {
                    nome = radice + " " + n;
                    if (presi.Add(nome)) break;
                }
            }
            profilo.Nome = nome;
            profilo.Normalizza();
        }

        if (!Profili.Any(p => string.Equals(p.Nome, Attivo, StringComparison.OrdinalIgnoreCase)))
            Attivo = Profili[0].Nome;
    }

    /// <summary>Il profilo attivo. Ce n'e' sempre uno: <see cref="Sistema"/> se ne accerta.</summary>
    public DeckConfig Attuale() =>
        Profili.FirstOrDefault(p => string.Equals(p.Nome, Attivo, StringComparison.OrdinalIgnoreCase))
        ?? Profili[0];

    /// <summary>Il profilo con quel nome, senza badare alle maiuscole, o null.</summary>
    public DeckConfig? Cerca(string nome) =>
        Profili.FirstOrDefault(p => string.Equals(p.Nome, nome.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Un nome libero, per un profilo nuovo o duplicato.</summary>
    public string NomeLibero(string radice)
    {
        string pulita = radice.Trim();
        if (pulita.Length == 0) pulita = "Deck";
        if (Cerca(pulita) is null) return pulita;

        for (int n = 2; ; n++)
        {
            string nome = pulita + " " + n;
            if (Cerca(nome) is null) return nome;
        }
    }
}

public sealed class DeckButton
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Color { get; set; } = "";

    /// <summary>
    /// Nome del file dentro config/icone, oppure vuoto per usare il glifo.
    ///
    /// Si salva il nome e non il percorso dell'immagine originale: quella viene
    /// copiata e ridotta al momento della scelta, cosi' spostarla o cancellarla
    /// dopo non lascia un pulsante senza faccia. Il nome e' l'impronta del
    /// contenuto, quindi due pulsanti con la stessa immagine puntano allo
    /// stesso file, e il tablet la riceve una volta sola.
    /// </summary>
    public string Icon { get; set; } = "";

    public ActionSpec? Action { get; set; }

    /// <summary>
    /// I pulsanti che stanno dentro la cartella, oppure null se questo pulsante
    /// non e' una cartella.
    ///
    /// Le cartelle sono l'unica cosa del deck che il tablet sa gia' fare da
    /// solo: aprirne una non chiede niente al PC, come non glielo chiede
    /// cambiare pagina. E' voluto — un deck si sfoglia anche a computer spento
    /// — e ha un prezzo: l'elenco annidato viaggia per intero nel frame DECK.
    /// </summary>
    public List<DeckButton>? Buttons { get; set; }

    /// <summary>
    /// Vero se premendolo si apre un elenco invece di succedere qualcosa sul
    /// PC. Comanda il tipo dell'azione: <see cref="Buttons"/> e' quel che la
    /// cartella contiene, non quel che la rende tale.
    /// </summary>
    [JsonIgnore]
    public bool Cartella =>
        string.Equals(Action?.Type, "cartella", StringComparison.OrdinalIgnoreCase);

    /// <summary>Il contenuto della cartella, creato al volo la prima volta.</summary>
    [JsonIgnore]
    public List<DeckButton> Dentro => Buttons ??= new();

    /// <summary>
    /// Quanti pulsanti ci sono qui dentro, a qualunque profondita', cartelle
    /// comprese. Serve a dire cosa si porta via chi toglie una cartella: quel
    /// che sta due piani piu' giu' non si vede, ma sparisce lo stesso.
    /// </summary>
    public int ContaDentro()
    {
        if (Buttons is null) return 0;
        int quanti = Buttons.Count;
        foreach (var f in Buttons) quanti += f.ContaDentro();
        return quanti;
    }

    /// <summary>Una copia che non condivide niente: serve a duplicare e a salvare.</summary>
    public DeckButton Copia() => new()
    {
        Id = Id,
        Label = Label,
        Glyph = Glyph,
        Color = Color,
        Icon = Icon,
        Action = Action?.Copia(),
        Buttons = Buttons?.Select(b => b.Copia()).ToList(),
    };
}

/// <summary>
/// Azione eseguita sul PC alla pressione di un pulsante. Il tablet non ne sa
/// nulla: manda solo l'identificativo, tutta la logica resta qui.
/// </summary>
public sealed class ActionSpec
{
    /// <summary>
    /// hotkey | media | text | run | url | click | wheel | seq | delay |
    /// keydown | keyup
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>Argomento principale: la combinazione, il testo, il percorso, l'URL.</summary>
    public string Value { get; set; } = "";

    /// <summary>Argomenti da riga di comando per "run".</summary>
    public string Args { get; set; } = "";

    /// <summary>Passi in sequenza quando Type vale "seq".</summary>
    public List<ActionSpec>? Steps { get; set; }

    /// <summary>
    /// Quante volte rifare la sequenza, da 1 a 50. Vale solo per "seq".
    ///
    /// Uno e' il valore normale, ed e' anche quello che si ritrova aprendo un
    /// deck.json scritto prima che questo campo esistesse: il campo assente
    /// lascia in piedi il valore iniziale invece di azzerarlo.
    /// </summary>
    public int Ripeti { get; set; } = 1;

    /// <summary>Copia in profondita': i passi vanno duplicati, non condivisi.</summary>
    public ActionSpec Copia() => new()
    {
        Type = Type,
        Value = Value,
        Args = Args,
        Ripeti = Ripeti,
        Steps = Steps?.Select(s => s.Copia()).ToList(),
    };
}

/// <summary>
/// Le luci di casa, in config/luci.json.
///
/// A differenza del deck, questo elenco non serve al PC per eseguire qualcosa:
/// serve solo a essere mandato al tablet, che poi parla alle lampade da solo.
/// Il PC e' il posto dove si scrivono nomi e chiavi perche' e' l'unico con una
/// tastiera vera - una chiave di sedici caratteri non si batte su un pannello
/// da sette pollici.
/// </summary>
public sealed class LuciConfig
{
    public List<LuceSpec> Luci { get; set; } = new();

    /// <summary>
    /// Il codice utente dell'app Smart Life (Io - Impostazioni - Account e
    /// sicurezza - Codice utente). Non e' un segreto: da solo non apre niente,
    /// serve a dire a Tuya di chi e' l'account da mostrare sul telefono quando
    /// si inquadra il QR.
    /// </summary>
    public string CodiceUtente { get; set; } = "";

    /// <summary>Le scene: un pulsante, piu' luci che cambiano insieme.</summary>
    public List<RoutineSpec> Routine { get; set; } = new();
}

/// <summary>
/// Una routine: un pulsante solo che tocca piu' lampade in fila.
///
/// Vive tutta sul tablet, come le luci: e' li' che si preme, spesso a PC
/// spento. Il PC la scrive e gliela manda, poi non serve piu'.
/// </summary>
public sealed class RoutineSpec
{
    public string Nome { get; set; } = "";
    public string Glifo { get; set; } = "◉";
    public string Colore { get; set; } = "#1F6F4A";
    public List<PassoLuce> Passi { get; set; } = new();

    /// <summary>
    /// Vero per una scena: lo stato delle luci da ritrovare con un tocco, di solito
    /// catturato com'e' in quel momento. Falso per una routine, che e' una sequenza
    /// e puo' aspettare fra un passo e l'altro. Sul tablet stanno in due file diverse.
    /// </summary>
    public bool Scena { get; set; }

    public RoutineSpec Copia() => new()
    {
        Nome = Nome,
        Glifo = Glifo,
        Colore = Colore,
        Scena = Scena,
        Passi = Passi.Select(p => p.Copia()).ToList(),
    };
}

/// <summary>Un passo di routine: a quale lampada, cosa fare, con che valore.</summary>
public sealed class PassoLuce
{
    /// <summary>Identificativo della lampada, oppure vuoto per dire « tutte ».</summary>
    public string Luce { get; set; } = "";

    /// <summary>on | off | inverti | luce | colore | bianco | bianchezza | attesa</summary>
    public string Azione { get; set; } = "off";

    /// <summary>La percentuale per « luce », il colore RRGGBB per « colore », i secondi per « attesa ».</summary>
    public string Valore { get; set; } = "";

    /// <summary>
    /// I secondi di un passo « attesa », fra uno e dieci minuti. La stessa
    /// regola di Routine.secondiAttesa sul tablet: la prova dal PC deve durare
    /// quanto la scena vera.
    /// </summary>
    public static int SecondiAttesa(string valore) =>
        Math.Clamp(int.TryParse(valore.Trim(), out int s) ? s : 5, 1, 600);

    public PassoLuce Copia() => new() { Luce = Luce, Azione = Azione, Valore = Valore };
}

public sealed class LuceSpec
{
    /// <summary>Come si chiama nell'app Smart Life, ridotto a una parola.</summary>
    public string Nome { get; set; } = "";

    /// <summary>Identificativo della lampada. Non cambia mai.</summary>
    public string Id { get; set; } = "";

    /// <summary>Indirizzo sulla rete di casa. Questo invece invecchia.</summary>
    public string Ip { get; set; } = "";

    /// <summary>
    /// La chiave locale, sedici caratteri. Senza, la lampada non risponde a
    /// nessuno: la rilascia il cloud Tuya una volta sola, con chiavi.py.
    /// </summary>
    public string Chiave { get; set; } = "";

    /// <summary>"3.3" o "3.4": cambia il formato dei pacchetti.</summary>
    public string Versione { get; set; } = "3.3";

    /// <summary>
    /// Il segno disegnato sulla scheda del tablet. Si sceglie da un elenco e
    /// non si scrive: il font di KitKat copre pochi simboli, e quelli che non
    /// ha escono come quadratini vuoti senza modo di accorgersene da qui.
    /// </summary>
    public string Glifo { get; set; } = "○";

    /// <summary>Tinta della scheda sul tablet.</summary>
    public string Colore { get; set; } = "#33565E";

    /// <summary>
    /// La stanza, scritta a mano. Vuota vuol dire senza stanza. Sul tablet e sul
    /// pannello le luci si raggruppano per stanza: si cerca una luce da dove sta,
    /// non dal suo nome.
    /// </summary>
    public string Stanza { get; set; } = "";

    /// <summary>Utilizzabile solo se c'e' tutto: senza chiave e' solo una riga.</summary>
    [JsonIgnore]
    public bool Completa =>
        Id.Length > 0 && Ip.Length > 0 && Chiave.Length == 16;

    /// <summary>
    /// Cosa mostrare nell'elenco al posto della chiave. La chiave vera non si
    /// stampa: leggerla non serve a niente e averla sullo schermo, magari
    /// mentre si condivide la finestra, e' solo un modo di regalarla.
    /// </summary>
    [JsonIgnore]
    public string StatoChiave => Chiave.Length == 16
        ? "presente"
        : Chiave.Length == 0
            ? "manca - premi « Rileva chiavi »"
            : $"lunghezza sbagliata ({Chiave.Length} invece di 16)";
}

public static class ConfigFile
{
    private static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Cerca la cartella config risalendo dall'eseguibile, cosi' funziona sia
    /// dal sorgente sia da una build pubblicata. Se non la trova la crea
    /// accanto al programma: alla prima accensione non deve mancare niente.
    /// </summary>
    public static string Directory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config");
            if (System.IO.Directory.Exists(candidate)) return candidate;
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "config");
        System.IO.Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Read) ?? new T();
        }
        catch (JsonException)
        {
            // Un file rovinato non deve impedire di aprire la finestra: si
            // riparte dai valori normali e si riscrive al primo salvataggio.
            return new T();
        }
    }

    public static void Save(string path, object value)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Write));
    }

    /// <summary>Serializzazione compatta, per i frame JSON mandati al tablet.</summary>
    public static string Wire(object value) => JsonSerializer.Serialize(value, Read);

    /// <summary>
    /// Una copia che non condivide niente con l'originale, passando dal JSON.
    ///
    /// E' il modo piu' corto di fotografare un deck intero per i passi
    /// indietro, e ha il pregio di non potersi dimenticare un campo: quello che
    /// finisce nel file e' esattamente quello che viene copiato.
    /// </summary>
    public static T Clona<T>(T valore) where T : new() =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(valore, Read), Read) ?? new T();
}
