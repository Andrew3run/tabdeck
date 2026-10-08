using System.Diagnostics;

namespace TabDeck;

/// <summary>
/// Esegue le azioni associate ai pulsanti del deck.
///
/// Ogni pressione parte su un thread suo: se un'azione lancia un programma che
/// impiega mezzo secondo ad avviarsi, il flusso video non deve fermarsi.
/// </summary>
public sealed class ActionRunner
{
    /// <summary>Pausa fra un passo e l'altro di una sequenza.</summary>
    private const int RespiroMs = 30;

    private readonly InputInjector input;
    private readonly Action<string> log;

    public ActionRunner(InputInjector input, Action<string> log)
    {
        this.input = input;
        this.log = log;
    }

    /// <summary>
    /// Chi sa cambiare profilo al deck. Il passo « Cambia profilo » non lo
    /// esegue il PC: i profili li tiene la finestra, che e' l'unica ad averli
    /// tutti in mano. Fuori da una sequenza ci pensa il motore a smistarlo
    /// prima ancora di arrivare qui; dentro, il passo si incontra a meta'
    /// strada ed e' questo il solo modo di farlo uscire.
    /// </summary>
    public Action<string>? Profilo { get; set; }

    public void Run(DeckButton button)
    {
        if (button.Action is null)
        {
            log($"'{button.Id}' non ha un'azione configurata");
            return;
        }
        Run(button.Action, button.Label.Length > 0 ? button.Label : button.Id);
    }

    /// <summary>
    /// Esegue un'azione qualsiasi, non per forza legata a un pulsante: e' cosi'
    /// che la finestra fa provare quello che si sta scrivendo prima di salvarlo.
    /// </summary>
    public void Run(ActionSpec action, string nome)
    {
        var thread = new Thread(() => Execute(action, nome, 0, null)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);   // serve a ShellExecute
        thread.Start();
    }

    /// <param name="tenuti">
    /// I tasti che la sequenza in corso ha premuto e non ha ancora mollato.
    /// Null fuori da una sequenza: li' « tieni premuto » non esiste, proprio
    /// perche' non ci sarebbe nessuno a rilasciarli.
    /// </param>
    private void Execute(ActionSpec action, string nome, int profondita, List<ushort>? tenuti)
    {
        // Le sequenze annidate si scrivono anche dalla finestra, che ne conta i
        // piani mentre le si monta; un deck.json scritto a mano no, e una
        // ricorsione senza fondo riempirebbe lo stack.
        if (profondita > Azioni.Annidamento)
        {
            log($"azione '{nome}': sequenze annidate troppo in profondita'");
            return;
        }

        try
        {
            switch (action.Type.ToLowerInvariant())
            {
                case "hotkey":
                case "media":
                    if (!input.Hotkey(action.Value))
                        log($"combinazione non valida su '{nome}': {action.Value}");
                    break;

                case "text":
                    input.TypeText(action.Value);
                    break;

                case "run":
                    Process.Start(new ProcessStartInfo(
                        Environment.ExpandEnvironmentVariables(action.Value.Trim()),
                        Environment.ExpandEnvironmentVariables(action.Args))
                    { UseShellExecute = true });
                    break;

                case "url":
                    Process.Start(new ProcessStartInfo(Indirizzo(action.Value)) { UseShellExecute = true });
                    break;

                case "delay":
                    Thread.Sleep(Math.Clamp(int.TryParse(action.Value, out int ms) ? ms : 100, 0, 10_000));
                    break;

                case "click":
                    switch (action.Value.Trim().ToLowerInvariant())
                    {
                        case "destro": case "right": input.RightClick(); break;
                        case "centrale": case "middle": input.MiddleClick(); break;
                        case "doppio": case "double": input.DoubleClick(); break;
                        default: input.LeftClick(); break;
                    }
                    break;

                case "wheel":
                    // Una tacca vale 120: e' l'unita' con cui Windows misura la
                    // rotella, e le applicazioni si aspettano multipli di quella.
                    input.Wheel(Math.Clamp(int.TryParse(action.Value, out int tacche) ? tacche : 1, -20, 20) * 120);
                    break;

                case "keydown":
                case "keyup":
                    Tieni(action, nome, tenuti);
                    break;

                case "profilo":
                    if (Profilo is null) log($"azione '{nome}': non c'e' nessuno che possa cambiare profilo");
                    else Profilo(action.Value.Trim());
                    break;

                case "obs":
                case "twitch":
                    log(Streaming.Esegui(action));
                    break;

                case "seq":
                    var passi = action.Steps ?? new List<ActionSpec>();
                    var premuti = new List<ushort>();
                    int volte = Math.Clamp(action.Ripeti, 1, 50);
                    for (int giro = 0; giro < volte; giro++)
                    {
                        foreach (var step in passi)
                        {
                            Execute(step, nome, profondita + 1, premuti);
                            // Dopo un'attesa esplicita non serve il respiro: chi l'ha
                            // messa ha gia' detto quanto voleva aspettare.
                            if (!step.Type.Equals("delay", StringComparison.OrdinalIgnoreCase))
                                Thread.Sleep(RespiroMs);
                        }
                    }
                    // Rete di sicurezza: un tasto rimasto premuto perche' manca
                    // il passo che lo rilascia lascerebbe il PC inutilizzabile,
                    // e senza niente sullo schermo che spieghi il perche'.
                    if (premuti.Count > 0)
                    {
                        input.PressKeys(premuti, true);
                        log($"azione '{nome}': {premuti.Count} tasti erano rimasti premuti, li ho rilasciati io");
                    }
                    break;

                default:
                    log($"tipo di azione sconosciuto su '{nome}': '{action.Type}'");
                    break;
            }
        }
        catch (Exception e)
        {
            log($"azione '{nome}' fallita: {e.Message}");
        }
    }

    /// <summary>
    /// Preme o rilascia i tasti di un passo, tenendo il conto di quel che resta
    /// giu'. Fuori da una sequenza non fa niente e lo dice: un pulsante che
    /// preme un tasto e non lo molla piu' e' un guasto, non una funzione.
    /// </summary>
    private void Tieni(ActionSpec action, string nome, List<ushort>? tenuti)
    {
        if (tenuti is null)
        {
            log($"azione '{nome}': tenere premuto ha senso solo dentro una sequenza");
            return;
        }

        if (!InputInjector.TryReadCombo(action.Value, out var tasti, out string ignoto))
        {
            log($"tasto non valido su '{nome}': {(ignoto.Length > 0 ? ignoto : action.Value)}");
            return;
        }

        bool giu = action.Type.Equals("keydown", StringComparison.OrdinalIgnoreCase);
        input.PressKeys(tasti, !giu);

        foreach (var vk in tasti)
        {
            if (giu)
            {
                if (!tenuti.Contains(vk)) tenuti.Add(vk);
            }
            else
            {
                tenuti.Remove(vk);
            }
        }
    }

    /// <summary>
    /// Un indirizzo senza schema non e' un indirizzo: ShellExecute lo prenderebbe
    /// per un percorso e aprirebbe una finestra di errore invece del browser.
    /// </summary>
    private static string Indirizzo(string valore)
    {
        string pulito = valore.Trim();
        return pulito.Contains("://", StringComparison.Ordinal) || pulito.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? pulito
            : "https://" + pulito;
    }
}
