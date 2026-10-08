using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TabDeck;

/// <summary>
/// La scheda Deck: quali pulsanti ci sono, come si vedono, cosa fanno.
///
/// Sta in un file suo perche' e' l'unica parte della finestra con una forma
/// propria — un elenco, un pulsante scelto, i passi di una sequenza, le
/// cartelle dentro cui i pulsanti stanno — mentre tutto il resto legge e scrive
/// una riga di configurazione.
///
/// Le cartelle rendono il deck un albero, e un albero in una finestra si
/// naviga male se lo si mostra tutto insieme. Qui non lo si mostra tutto: si
/// sta sempre dentro una cartella sola — la radice e' una cartella come le
/// altre — e a dire dove si e' ci pensano le briciole in cima all'elenco e la
/// fascia in cima all'anteprima, che e' la stessa che disegna il tablet. Il
/// « dove » e' una fila di indici e non di riferimenti: cosi' sopravvive a un
/// « Annulla », che rimette in piedi un albero nuovo di zecca.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// I colori del deck. Sono scuri di proposito: il pannello del tablet e'
    /// poco luminoso, e su una tinta piena il glifo bianco sparisce.
    /// </summary>
    private static readonly string[] Colori =
    {
        "#2B2E35", "#3A3F4A", "#1F6F4A", "#33565E", "#2F6B8F", "#3B4E9B",
        "#6B3F8F", "#8C3A3A", "#8A4B2A", "#7A6A20",
    };

    /// <summary>Il glifo con cui nasce una cartella: e' una griglia in miniatura.</summary>
    private const string GlifoCartella = "folder";

    /// <summary>Tutti i profili e quale e' attivo: e' quel che sta in deck.json.</summary>
    private DeckFile deckFile = new();

    /// <summary>Il profilo attivo. E' uno di quelli dentro <see cref="deckFile"/>.</summary>
    private DeckConfig deck = new();

    private bool deckModificato;

    /// <summary>La cartella aperta nell'editor: vuoto vuol dire la radice.</summary>
    private readonly List<int> percorso = new();

    /// <summary>La cartella aperta nel deck grande della pagina Uso.</summary>
    private readonly List<int> percorsoVivo = new();

    private readonly List<ToggleButton> gettoniGlifo = new();
    private readonly List<ToggleButton> gettoniColore = new();

    /// <summary>
    /// I passi indietro e quelli in avanti, come copie intere del deck.
    ///
    /// Un deck e' qualche decina di righe di JSON: copiarlo per intero costa
    /// meno che tenere il conto di cosa e' cambiato, e non ha il difetto di
    /// dimenticarsi un caso. Cinquanta bastano — chi ne vuole disfare di piu'
    /// sta in realta' chiudendo senza salvare.
    /// </summary>
    private readonly List<DeckConfig> passato = new();
    private readonly List<DeckConfig> futuro = new();

    /// <summary>Perche' e' stato preso l'ultimo passo indietro, e quando.</summary>
    private string motivoPasso = "";
    private long quandoPasso;

    /// <summary>Il pulsante copiato o tagliato, in attesa di essere incollato.</summary>
    private DeckButton? appunti;

    /// <summary>Quel che la ricerca ha trovato, quando c'e' qualcosa da cercare.</summary>
    private readonly List<Trovato> risultati = new();

    /// <summary>Un pulsante trovato dalla ricerca: dov'e' e come ci si arriva.</summary>
    private sealed record Trovato(int[] Percorso, int Indice, DeckButton Pulsante, string Dove);

    /// <summary>Conto alla rovescia della prova, per dare il tempo di cambiare finestra.</summary>
    private DispatcherTimer? conto;
    private int contoRimasto;
    private Button? contoPulsante;
    private string contoTesto = "";

    // ---- montaggio ----

    private void PreparaDeck()
    {
        foreach (var tipo in Azioni.PerPulsante) ElencoTipiAzione.Items.Add(tipo.Nome);
        foreach (var tipo in Azioni.PerPasso) ElencoTipiPasso.Items.Add(tipo.Nome);

        MontaGettoni();

        // L'anteprima dell'editor: il primo clic sceglie — anche una cartella ha
        // un nome e un colore da cambiare — e il doppio clic entra.
        Anteprima.Riordinabile = true;
        Anteprima.ApriAlPrimoClic = false;
        Anteprima.Premuto += indice =>
        {
            if (indice >= 0 && indice < Correnti.Count) Scegli(indice);
        };
        // Una cella vuota e' il posto piu' naturale dove dire "qui ne voglio
        // un altro": il pulsante nuovo finisce in fondo, che e' li'.
        Anteprima.PremutoVuoto += _ => AggiungiPulsante(false);
        Anteprima.Apri += EntraIn;
        Anteprima.Esci += Risali;
        // Togliere un pulsante li' dove lo si vede, invece di cercarlo
        // nell'elenco a sinistra: il pulsantino compare passando sopra la
        // cella, e il tasto destro apre lo stesso menu dell'elenco.
        Anteprima.Scorciatoie = true;
        Anteprima.Tolto += indice =>
        {
            SmettiDiCercare();
            TogliDove(percorso, indice);
        };
        Anteprima.MenuChiesto += indice =>
        {
            if (indice < 0 || indice >= Correnti.Count) return;
            Scegli(indice);
            ApriMenuPulsante(Anteprima);
        };
        // L'ordine dei pulsanti e' la loro posizione sul tablet, e si cambia
        // dove la posizione si vede: trascinandoli nell'anteprima. Le frecce
        // dell'elenco fanno la stessa cosa una cella per volta, e restano per
        // quando si vuole spostare di un posto senza prendere la mira.
        Anteprima.Portato += Porta;

        // Quella della dashboard e' un colpo d'occhio, non una tastiera:
        // premerla porta alla pagina del deck, dove i pulsanti funzionano. Le
        // cartelle non ci si aprono, per lo stesso motivo.
        AnteprimaViva.Navigabile = false;
        AnteprimaViva.Premuto += _ => VaiA(SchedaDeckVivo);
        AnteprimaViva.PremutoVuoto += _ => VaiA(SchedaDeckVivo);

        // Il deck grande invece preme davvero, con lo stesso conto alla
        // rovescia di « Prova »: una combinazione di tasti fatta partire da
        // qui finirebbe altrimenti dentro TabDeck, che e' la finestra col fuoco.
        //
        // A meno che non si stia disponendo: li' il dito serve a spostare, e
        // far partire un'azione mentre si prende in mano un pulsante sarebbe
        // esattamente il contrario di quel che si voleva.
        DeckVivo.Premuto += indice =>
        {
            var lista = deck.Contenuto(percorsoVivo);
            if (Disponendo || indice < 0 || indice >= lista.Count) return;
            var b = lista[indice];
            if (b.Action is null) return;
            Prova(b.Action, Nome(b), null);
        };
        DeckVivo.PremutoVuoto += _ =>
        {
            if (!Disponendo) VaiA(SchedaDeck);
        };
        // Le cartelle si aprono anche mentre si dispone: e' l'unico modo di
        // sistemare quel che sta dentro.
        DeckVivo.Apri += indice =>
        {
            percorsoVivo.Add(indice);
            AggiornaAnteprima();
        };
        DeckVivo.Esci += () =>
        {
            if (percorsoVivo.Count == 0) return;
            percorsoVivo.RemoveAt(percorsoVivo.Count - 1);
            AggiornaAnteprima();
        };
        DeckVivo.Portato += Porta;
        DeckVivo.Tolto += indice => TogliDove(percorsoVivo, indice);
        // Il menu comanda quello che l'editor ha scelto: prima ci si porta
        // sopra il pulsante premuto, poi si apre. La pagina Pulsanti non e'
        // quella davanti, quindi non si vede niente muoversi.
        DeckVivo.MenuChiesto += indice =>
        {
            if (indice < 0 || indice >= deck.Contenuto(percorsoVivo).Count) return;
            SmettiDiCercare();
            VaiDentro(percorsoVivo, indice);
            ApriMenuPulsante(DeckVivo);
        };

        AggiornaModoDeck();

        RiempiProfili();

        // Annulla e rifai valgono su tutta la pagina, non solo sull'elenco:
        // sono i due tasti che uno prova senza guardare dove ha il fuoco.
        PreviewKeyDown += ScorciatoieDeck;

        CaricaDeck();
    }

    /// <summary>
    /// Ctrl+Z e Ctrl+Y, ma solo dove hanno senso.
    ///
    /// Fuori dalla pagina Pulsanti non c'e' niente da disfare. Dentro una
    /// casella di testo Ctrl+Z e' quello della casella, che disfa la parola
    /// appena scritta: rubarglielo per rimettere a posto un deck sarebbe una
    /// sorpresa. E mentre si cattura o si registra i tasti sono tutti della
    /// cattura, per definizione.
    /// </summary>
    private void ScorciatoieDeck(object sender, KeyEventArgs e)
    {
        if (Schede.SelectedItem != SchedaDeck) return;
        if (catturaCampo is not null || BtnRegistra.IsChecked == true) return;
        if (Keyboard.FocusedElement is TextBox) return;

        // Gli elenchi dei passi e del contenuto di una cartella hanno le loro,
        // e vogliono dire un'altra cosa: Canc li' dentro toglie un passo, non
        // il pulsante. Questa arriva prima — e' un Preview sulla finestra —
        // quindi la si lascia passare.
        var fuoco = Keyboard.FocusedElement as DependencyObject;
        if (Sotto(fuoco, ElencoPassi) || Sotto(fuoco, ElencoDentro)) return;

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        switch (e.Key)
        {
            case Key.Z when ctrl: Passo(passato, futuro, "Annullato"); break;
            case Key.Y when ctrl: Passo(futuro, passato, "Rifatto"); break;
            case Key.Delete when !ctrl: TogliPulsante(); break;
            case Key.F2: Rinomina(); break;
            case Key.D when ctrl: DuplicaPulsante(); break;
            case Key.C when ctrl: Copia(false); break;
            case Key.X when ctrl: Copia(true); break;
            case Key.V when ctrl: Incolla(); break;
            default: return;
        }

        e.Handled = true;
    }

    /// <summary>Se un elemento sta dentro un altro, risalendo di padre in padre.</summary>
    private static bool Sotto(DependencyObject? nodo, DependencyObject padre)
    {
        while (nodo is not null)
        {
            if (ReferenceEquals(nodo, padre)) return true;
            nodo = nodo is Visual ? VisualTreeHelper.GetParent(nodo) : LogicalTreeHelper.GetParent(nodo);
        }
        return false;
    }

    // ---- il deck grande: premere oppure disporre ----

    /// <summary>Vero mentre il deck grande e' in modo « Disponi ».</summary>
    private bool Disponendo => BtnDisponi.IsChecked == true;

    private void Disponi_Checked(object sender, RoutedEventArgs e) => AggiornaModoDeck();

    private void Disponi_Unchecked(object sender, RoutedEventArgs e) => AggiornaModoDeck();

    /// <summary>
    /// Accende o spegne il trascinamento sul deck grande, e cambia quel che la
    /// riga in alto racconta. Il pulsante « Salva » compare solo mentre si
    /// dispone: sulla pagina di tutti i giorni non c'e' niente da salvare.
    /// </summary>
    private void AggiornaModoDeck()
    {
        bool disponi = Disponendo;

        DeckVivo.Riordinabile = disponi;
        // Il pulsantino « togli » e il tasto destro solo mentre si dispone:
        // sulla pagina di tutti i giorni il dito serve a premere, e una croce
        // che compare sotto il puntatore sarebbe un modo di cancellare un
        // pulsante credendo di usarlo.
        DeckVivo.Scorciatoie = disponi;
        RigaSalvaDeckVivo.Visibility = disponi ? Visibility.Visible : Visibility.Collapsed;
        TestoModoDeck.Text = disponi
            ? "Trascina i pulsanti dove li vuoi; lasciandone uno sopra una cartella ci finisce dentro. Fermandoti sul bordo destro o sinistro si cambia pagina, e il pulsante ti segue. Finche' non salvi, sul tablet resta com'era."
            : "Premi un pulsante e l'azione parte qui sul PC dopo tre secondi: il tempo di andare sulla finestra a cui serve. Le cartelle si aprono subito, come sul tablet.";

        // Annullare il conto alla rovescia: passare a « Disponi » un istante
        // dopo aver premuto farebbe partire lo stesso l'azione di prima, con la
        // mano gia' sul pulsante da spostare.
        AnnullaConto();
    }

    /// <summary>
    /// I glifi che il font di KitKat sa disegnare, provati uno per uno sul
    /// pannello. Il deck usa le icone a tratto; restano per le lampade di Casa,
    /// che il tablet scrive come testo.
    /// </summary>
    private static readonly string[] Glifi =
    {
        "▶", "◀", "▲", "▼", "■", "□", "▢", "▣", "▤", "▥", "▦", "▧", "▨", "▩",
        "▪", "▫", "◆", "◇", "○", "●", "◉", "◎", "◐", "×", "÷", "±", "–", "+", "↻",
    };

    /// <summary>
    /// Cambia pagina, ma al giro dopo del dispatcher.
    ///
    /// Fatta dentro l'evento del mouse che arriva da un'anteprima, la scelta
    /// viene messa e subito rimessa a posto: l'evento sta ancora risalendo la
    /// pagina che si vorrebbe lasciare, e quando finisce la selezione torna
    /// dov'era. Rimandarla di un giro la fa attecchire.
    /// </summary>
    private void VaiA(TabItem pagina) => Dispatcher.BeginInvoke(() => Schede.SelectedItem = pagina);

    private void MontaGettoni()
    {
        // Le icone a tratto, le stesse che disegna il tablet. Il nome resta nel
        // Tag: il contenuto e' il disegno, e la scelta si confronta col nome.
        foreach (var glifo in Pittogrammi.Nomi)
        {
            var gettone = new ToggleButton
            {
                Content = IconaGettone(glifo),
                Tag = glifo,
                Style = (Style)FindResource("GettoneIcona"),
                ToolTip = glifo,
            };
            gettone.Click += (_, _) =>
            {
                CampoGlifo.Text = gettone.IsChecked == true ? glifo : "";
            };
            gettoniGlifo.Add(gettone);
            PannelloGlifi.Children.Add(gettone);
        }

        foreach (var colore in Colori)
        {
            var gettone = new ToggleButton
            {
                Style = (Style)FindResource("GettoneColore"),
                Background = new SolidColorBrush(AnteprimaDeck.Tinta(colore)),
                Width = 28,
                ToolTip = colore,
            };
            gettone.Click += (_, _) => CampoColore.Text = colore;
            gettoniColore.Add(gettone);
            PannelloColori.Children.Add(gettone);
        }
    }

    /// <summary>
    /// Un'icona del deck come elemento della finestra. Il colore, se non e' dato,
    /// lo prende dal testo del controllo che la contiene: cosi' il gettone
    /// scelto la accende senza saperne niente.
    /// </summary>
    private static FrameworkElement IconaGettone(string nome, double lato = 17, Brush? colore = null)
    {
        var tratto = new System.Windows.Shapes.Path
        {
            Data = Pittogrammi.Forma(nome),
            StrokeThickness = 1.75,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
        };
        if (colore is not null) tratto.Stroke = colore;
        else tratto.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Control), 1),
        });
        return new Viewbox
        {
            Width = lato,
            Height = lato,
            Child = tratto,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void CaricaDeck()
    {
        // Il file puo' essere stato scritto a mano, o da una versione di prima
        // che le cartelle non le aveva: si mette a posto una volta, qui.
        deck.Normalizza();

        percorso.Clear();
        percorsoVivo.Clear();

        caricamento = true;
        RiempiElencoPulsanti(deck.Buttons.Count > 0 ? 0 : -1);
        caricamento = false;

        AggiornaGriglia();
        Salvato();
        AggiornaPassi();
    }

    // ---- profili ----

    /// <summary>
    /// Riempie i due elenchi dei profili — quello della pagina Pulsanti e
    /// quello del deck grande — e ci mette dentro quello attivo.
    /// </summary>
    private void RiempiProfili()
    {
        bool prima = caricamento;
        caricamento = true;

        foreach (var elenco in new[] { ElencoProfili, ElencoProfiliVivo })
        {
            elenco.Items.Clear();
            foreach (var profilo in deckFile.Profili) elenco.Items.Add(profilo.Nome);
            elenco.SelectedItem = deck.Nome;
        }

        caricamento = prima;
    }

    private void Profilo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento || ElencoProfili.SelectedItem is not string nome) return;
        CambiaProfilo(nome);
    }

    private void ProfiloVivo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento || ElencoProfiliVivo.SelectedItem is not string nome) return;
        CambiaProfilo(nome);
    }

    /// <summary>
    /// Passa a un altro profilo.
    ///
    /// Il file li contiene tutti, quindi cambiare vuol dire scriverlo: quel che
    /// si stava montando nel profilo di prima finisce su disco invece di
    /// restare per aria, e al tablet arriva subito la griglia nuova. E' anche
    /// il motivo per cui non si chiede niente a nessuno: non c'e' niente da
    /// perdere.
    /// </summary>
    private void CambiaProfilo(string nome)
    {
        var altro = deckFile.Cerca(nome);
        if (altro is null || ReferenceEquals(altro, deck))
        {
            RiempiProfili();   // rimette la scelta su quello vero
            return;
        }

        ChiudiNome();
        deckFile.Attivo = altro.Nome;
        deck = altro;

        // I passi indietro erano del profilo di prima: qui non vogliono dire
        // niente, e applicarli rimetterebbe in piedi l'albero sbagliato.
        passato.Clear();
        futuro.Clear();
        motivoPasso = "";

        CaricaDeck();
        RiempiProfili();
        SalvaDeck();
        Registra($"Profilo « {altro.Nome} »: {deck.Buttons.Count} pulsanti in cima, "
                 + $"griglia {deck.Cols} × {deck.Rows}.");
    }

    /// <summary>
    /// Un pulsante premuto sul tablet ha chiesto un altro profilo. Arriva dal
    /// thread di rete, e la finestra si tocca solo dal suo.
    /// </summary>
    private void ProfiloDalTablet(string nome)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ProfiloDalTablet(nome));
            return;
        }

        if (deckFile.Cerca(nome) is null)
        {
            Registra($"Nessun profilo si chiama '{nome}': il pulsante non porta da nessuna parte.", true);
            return;
        }
        CambiaProfilo(nome);
    }

    /// <summary>Cosa fara' il nome che si sta scrivendo: nuovo, duplica o rinomina.</summary>
    private string modoNome = "";

    private void NuovoProfilo_Click(object sender, RoutedEventArgs e) =>
        ChiediNome("nuovo", "Come si chiama il profilo nuovo? Nasce vuoto, con la griglia di questo.",
            deckFile.NomeLibero("Deck"));

    private void DuplicaProfilo_Click(object sender, RoutedEventArgs e) =>
        ChiediNome("duplica", $"Come si chiama la copia di « {deck.Nome} »? Dentro ci saranno gli stessi pulsanti.",
            deckFile.NomeLibero(deck.Nome));

    private void RinominaProfilo_Click(object sender, RoutedEventArgs e) =>
        ChiediNome("rinomina", $"Come si deve chiamare « {deck.Nome} »?", deck.Nome);

    /// <summary>
    /// Chiede un nome nella riga sotto l'elenco. Non in una finestrella a
    /// parte: e' una riga sola, e coprire quello che si stava guardando per
    /// chiedere una parola e' sproporzionato.
    /// </summary>
    private void ChiediNome(string modo, string domanda, string proposta)
    {
        // Le due righe stanno nello stesso posto: una domanda per volta.
        RigaEsporta.Visibility = Visibility.Collapsed;

        modoNome = modo;
        TestoNomeProfilo.Text = domanda;
        CampoNomeProfilo.Text = proposta;
        RigaNomeProfilo.Visibility = Visibility.Visible;
        CampoNomeProfilo.Focus();
        CampoNomeProfilo.SelectAll();
    }

    private void ChiudiNome()
    {
        modoNome = "";
        RigaNomeProfilo.Visibility = Visibility.Collapsed;
    }

    private void AnnullaNome_Click(object sender, RoutedEventArgs e) => ChiudiNome();

    private void CampoNomeProfilo_Tasto(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ConfermaNome();
        else if (e.Key == Key.Escape) ChiudiNome();
        else return;
        e.Handled = true;
    }

    private void ConfermaNome_Click(object sender, RoutedEventArgs e) => ConfermaNome();

    private void ConfermaNome()
    {
        string modo = modoNome;
        if (modo.Length == 0) return;

        string nome = CampoNomeProfilo.Text.Trim();
        if (nome.Length == 0)
        {
            Registra("Un profilo senza nome non si distingue dagli altri.", true);
            return;
        }

        var gia = deckFile.Cerca(nome);
        if (gia is not null && !(modo == "rinomina" && ReferenceEquals(gia, deck)))
        {
            Registra($"C'e' gia' un profilo che si chiama « {nome} ».", true);
            return;
        }

        ChiudiNome();

        switch (modo)
        {
            case "rinomina":
                string prima = deck.Nome;
                deck.Nome = nome;
                deckFile.Attivo = nome;
                RiempiProfili();
                SalvaDeck();
                Registra($"« {prima} » adesso si chiama « {nome} ».");
                // I pulsanti che ci portavano nominano il nome vecchio: e' una
                // cosa che si scopre premendoli, quindi si dice adesso.
                RicordaChiCiPorta(prima, nome);
                break;

            case "duplica":
                var copia = ConfigFile.Clona(deck);
                copia.Nome = nome;
                deckFile.Profili.Add(copia);
                CambiaProfilo(nome);
                break;

            default:
                deckFile.Profili.Add(new DeckConfig { Nome = nome, Cols = deck.Cols, Rows = deck.Rows });
                CambiaProfilo(nome);
                break;
        }
    }

    /// <summary>
    /// Dopo un cambio di nome, avverte se qualche pulsante — in un profilo
    /// qualsiasi — chiamava ancora quello vecchio.
    /// </summary>
    private void RicordaChiCiPorta(string prima, string adesso)
    {
        int quanti = deckFile.Profili
            .SelectMany(p => p.Ovunque())
            .Count(b => b.Action is { } azione
                        && string.Equals(azione.Type, "profilo", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(azione.Value.Trim(), prima, StringComparison.OrdinalIgnoreCase));

        if (quanti == 0) return;
        Registra(quanti == 1
            ? $"Un pulsante portava a « {prima} »: adesso non porta piu' da nessuna parte, cambialo in « {adesso} »."
            : $"{quanti} pulsanti portavano a « {prima} »: adesso non portano piu' da nessuna parte, vanno cambiati in « {adesso} ».",
            true);
    }

    // ---- portare i profili altrove ----

    /// <summary>
    /// « Esporta » non fa niente da solo: chiede prima quanto portarsi via,
    /// perche' le due risposte servono a due cose diverse. Un profilo solo e'
    /// quello che si manda a qualcuno o si tiene da parte prima di rifarlo;
    /// tutti sono il deck intero, che e' quello che si porta su un altro PC.
    /// </summary>
    private void EsportaProfili_Click(object sender, RoutedEventArgs e)
    {
        ChiudiNome();
        BtnEsportaUno.Content = $"Solo « {deck.Nome} »";
        BtnEsportaTutti.Content = $"Tutti e {deckFile.Profili.Count} i profili";
        BtnEsportaTutti.Visibility = deckFile.Profili.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        RigaEsporta.Visibility = Visibility.Visible;
    }

    private void AnnullaEsporta_Click(object sender, RoutedEventArgs e) =>
        RigaEsporta.Visibility = Visibility.Collapsed;

    private void EsportaUno_Click(object sender, RoutedEventArgs e) =>
        Esporta(p => string.Equals(p.Nome, deck.Nome, StringComparison.Ordinal), deck.Nome);

    private void EsportaTutti_Click(object sender, RoutedEventArgs e) =>
        Esporta(_ => true, "Deck di " + Environment.MachineName);

    /// <summary>
    /// Scrive il pacchetto.
    ///
    /// Quel che ci finisce e' il deck ripulito come se lo si stesse salvando —
    /// stessa forma del file — ma senza salvarlo e senza mandarlo a nessuno:
    /// esportare e' guardare, non decidere. Le modifiche non ancora salvate ci
    /// sono comunque, perche' il ripulito si costruisce da quello che si vede.
    /// </summary>
    private void Esporta(Func<DeckConfig, bool> quali, string proposta)
    {
        RigaEsporta.Visibility = Visibility.Collapsed;

        var scelti = PerIlFile().Profili.Where(quali).ToList();
        if (scelti.Count == 0)
        {
            Registra("Non c'e' niente da esportare.", true);
            return;
        }

        var finestra = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Dove metto i profili",
            Filter = Profili.Filtro,
            FileName = Pulito(proposta) + ".tabdeck.json",
            AddExtension = false,
            OverwritePrompt = true,
        };
        if (finestra.ShowDialog(this) != true) return;

        // Chi cancella il nome proposto e scrive « lavoro » vuole un file di
        // profili, non un file senza estensione che poi non si riapre da qui.
        string percorso = finestra.FileName;
        if (!percorso.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            percorso += ".tabdeck.json";

        try
        {
            Profili.Esporta(percorso, scelti, out int icone);
            Registra(scelti.Count == 1
                ? $"Profilo « {scelti[0].Nome} » esportato in {percorso}"
                  + (icone > 0 ? $", con {icone} icone dentro." : ".")
                : $"{scelti.Count} profili esportati in {percorso}"
                  + (icone > 0 ? $", con {icone} icone dentro." : "."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Registra($"Profili non esportati: {ex.Message}", true);
        }
    }

    /// <summary>Un nome di profilo diventa un nome di file: via quel che non ci sta.</summary>
    private static string Pulito(string nome)
    {
        string ripulito = new(nome.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        ripulito = ripulito.Trim();
        return ripulito.Length > 0 ? ripulito : "profili";
    }

    /// <summary>
    /// Legge un pacchetto e ci aggiunge quello che ha dentro. Non sovrascrive
    /// niente: i profili entrano accanto a quelli che ci sono, col nome libero
    /// piu' vicino a quello che avevano.
    /// </summary>
    private void ImportaProfili_Click(object sender, RoutedEventArgs e)
    {
        ChiudiNome();
        RigaEsporta.Visibility = Visibility.Collapsed;

        var finestra = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Profili da aggiungere",
            Filter = Profili.Filtro,
            CheckFileExists = true,
        };
        if (finestra.ShowDialog(this) != true) return;

        var pacchetto = Profili.Leggi(finestra.FileName, out string male);
        if (pacchetto is null)
        {
            Registra($"Profili non importati: {male}", true);
            return;
        }

        var entrati = Profili.Aggiungi(pacchetto, deckFile, out int icone);
        RiempiProfili();

        // Si salva senza mandare, e il « da mandare » resta com'era: al tablet
        // arrivano i pulsanti del profilo attivo, e quello non l'ha toccato
        // nessuno. I profili nuovi ci vanno quando li si sceglie.
        bool eraDaMandare = settings.DeckDaMandare;
        SalvaDeck(false);
        DeckDaMandare(eraDaMandare);

        string da = pacchetto.Origine.Length > 0 ? $" da {pacchetto.Origine}" : "";
        Registra(entrati.Count == 1
            ? $"Profilo « {entrati[0]} » importato{da}"
              + (icone > 0 ? $", con {icone} icone nuove." : ".")
            : $"{entrati.Count} profili importati{da}: « {string.Join(" », « ", entrati)} »"
              + (icone > 0 ? $", con {icone} icone nuove." : "."));
        Registra("I profili importati sono in fondo all'elenco: sceglili per mandarli al tablet.");
    }

    private void TogliProfilo_Click(object sender, RoutedEventArgs e)
    {
        if (deckFile.Profili.Count == 1)
        {
            Registra("Questo e' l'unico profilo: toglierlo lascerebbe il tablet senza deck.", true);
            return;
        }

        int quanti = deck.Ovunque().Count();
        var scelta = MessageBox.Show(this,
            $"Butto via il profilo « {deck.Nome} », con i suoi {quanti} pulsanti?\n\n"
            + "Questo non si annulla.",
            "TabDeck", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (scelta != MessageBoxResult.OK) return;

        ChiudiNome();
        string andato = deck.Nome;
        int posto = deckFile.Profili.IndexOf(deck);
        deckFile.Profili.Remove(deck);

        deck = deckFile.Profili[Math.Clamp(posto, 0, deckFile.Profili.Count - 1)];
        deckFile.Attivo = deck.Nome;
        passato.Clear();
        futuro.Clear();
        motivoPasso = "";

        CaricaDeck();
        RiempiProfili();
        SalvaDeck();
        Registra($"Profilo « {andato} » buttato via. Adesso sul tablet c'e' « {deck.Nome} ».");
    }

    // ---- dove si e' ----

    /// <summary>I pulsanti della cartella aperta nell'editor.</summary>
    private List<DeckButton> Correnti => deck.Contenuto(percorso);

    /// <summary>Vero mentre l'elenco mostra i risultati di una ricerca.</summary>
    private bool Cercando => CampoCerca.Text.Trim().Length > 0;

    /// <summary>La riga scelta nell'elenco, o -1. Durante una ricerca non conta.</summary>
    private int Scelto => Cercando ? -1 : ElencoPulsanti.SelectedIndex;

    /// <summary>
    /// Entra nella cartella che sta a quell'indice. Non e' un modo di
    /// modificare il deck: e' solo dove si sta guardando, e infatti non
    /// sporca niente.
    /// </summary>
    private void EntraIn(int indice)
    {
        var lista = Correnti;
        if (indice < 0 || indice >= lista.Count || !lista[indice].Cartella) return;

        percorso.Add(indice);
        RiempiElencoPulsanti(lista[indice].Dentro.Count > 0 ? 0 : -1);
        AggiornaGriglia();
    }

    /// <summary>Torna alla cartella che contiene questa, scegliendola.</summary>
    private void Risali()
    {
        if (percorso.Count == 0) return;
        int venivoDa = percorso[^1];
        percorso.RemoveAt(percorso.Count - 1);
        RiempiElencoPulsanti(venivoDa);
        AggiornaGriglia();
    }

    /// <summary>Va' a una cartella qualsiasi, scegliendoci dentro un pulsante.</summary>
    private void VaiDentro(IReadOnlyList<int> dove, int scegli)
    {
        percorso.Clear();
        percorso.AddRange(deck.PercorsoValido(dove));
        RiempiElencoPulsanti(scegli);
        AggiornaGriglia();
    }

    /// <summary>
    /// Le briciole: « Deck › Musica › Ascolto ». L'ultima e' spenta perche' e'
    /// dove si sta gia'; le altre riportano li' con un clic.
    /// </summary>
    private void AggiornaBriciole()
    {
        Briciole.Children.Clear();

        var strada = deck.Cartelle(percorso).ToList();
        AggiungiBriciola("Deck", 0, strada.Count == 0);

        for (int i = 0; i < strada.Count; i++)
        {
            Briciole.Children.Add(new TextBlock
            {
                Text = "›",
                Margin = new Thickness(1, 3, 1, 0),
                Foreground = (Brush)FindResource("TestoTenue"),
                FontSize = 12,
            });
            AggiungiBriciola(Nome(strada[i]), i + 1, i == strada.Count - 1);
        }
    }

    private void AggiungiBriciola(string testo, int quanti, bool ultima)
    {
        var pezzo = new Button
        {
            Content = testo,
            Style = (Style)FindResource("Briciola"),
            IsEnabled = !ultima,
            ToolTip = ultima ? null : $"Torna a « {testo} »",
        };
        pezzo.Click += (_, _) =>
        {
            var dove = percorso.Take(quanti).ToList();
            int scegli = percorso.Count > quanti ? percorso[quanti] : -1;
            VaiDentro(dove, scegli);
        };
        Briciole.Children.Add(pezzo);
    }

    // ---- elenco dei pulsanti ----

    private void RiempiElencoPulsanti(int scegli)
    {
        bool prima = caricamento;
        caricamento = true;

        ElencoPulsanti.Items.Clear();
        risultati.Clear();

        if (Cercando)
        {
            Cerca(deck.Buttons, Array.Empty<int>(), "", CampoCerca.Text.Trim());
            foreach (var t in risultati) ElencoPulsanti.Items.Add(Voce(t.Pulsante, t.Dove));
            ElencoPulsanti.SelectedIndex = -1;
        }
        else
        {
            foreach (var b in Correnti) ElencoPulsanti.Items.Add(Voce(b, null));
            ElencoPulsanti.SelectedIndex = Math.Clamp(scegli, -1, Correnti.Count - 1);
        }

        caricamento = prima;

        AggiornaBriciole();
        MostraPulsante();
    }

    /// <summary>
    /// Cerca in tutto l'albero. Guarda l'etichetta, l'identificativo e quel che
    /// il pulsante fa: « notepad » deve trovare il pulsante che avvia notepad
    /// anche se si chiama « Blocco note ».
    /// </summary>
    private void Cerca(List<DeckButton> lista, int[] dove, string strada, string cosa)
    {
        for (int i = 0; i < lista.Count; i++)
        {
            var b = lista[i];
            string paglia = $"{b.Label} {b.Id} {Azioni.Riassunto(b)}";
            if (paglia.Contains(cosa, StringComparison.OrdinalIgnoreCase))
                risultati.Add(new Trovato(dove, i, b, strada));

            if (!b.Cartella) continue;
            var giu = dove.Append(i).ToArray();
            Cerca(b.Dentro, giu, strada.Length == 0 ? Nome(b) : $"{strada} › {Nome(b)}", cosa);
        }
    }

    private void Cerca_Changed(object sender, TextChangedEventArgs e)
    {
        SuggerimentoCerca.Visibility = CampoCerca.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (caricamento) return;
        RiempiElencoPulsanti(-1);
    }

    /// <summary>
    /// Una riga dell'elenco: il pulsante com'e' sul tablet, e sotto in piccolo
    /// quello che fa. Il solo nome non bastava — due pulsanti "Vol" uguali si
    /// distinguono solo dall'azione. Nei risultati di una ricerca c'e' una riga
    /// in piu': in quale cartella sta, che e' la cosa che si sta cercando.
    /// </summary>
    private UIElement Voce(DeckButton b, string? dove)
    {
        var riga = new Grid();
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icona = Icone.Carica(b.Icon);
        UIElement dentro = icona is null
            ? Pittogrammi.Conosce(b.Glyph)
                ? IconaGettone(b.Glyph, 15, Brushes.White)
                : new TextBlock
                {
                    Text = b.Glyph,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            : new Image
            {
                Source = icona,
                Margin = new Thickness(3),
                Stretch = System.Windows.Media.Stretch.Uniform,
            };

        var quadro = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(AnteprimaDeck.Tinta(b.Color)),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = dentro,
        };
        Grid.SetColumn(quadro, 0);
        riga.Children.Add(quadro);

        var testi = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        testi.Children.Add(new TextBlock
        {
            Text = Nome(b),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        testi.Children.Add(new TextBlock
        {
            Text = Azioni.Riassunto(b),
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = (Brush)FindResource("TestoTenue"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (dove is not null)
        {
            testi.Children.Add(new TextBlock
            {
                Text = dove.Length == 0 ? "nel deck" : "in " + dove,
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = (Brush)FindResource("Accento"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Grid.SetColumn(testi, 1);
        riga.Children.Add(testi);

        // La freccetta dice che quella riga si apre, non che si preme: e' la
        // stessa differenza che sul tablet fanno i quattro quadratini.
        if (b.Cartella)
        {
            var freccia = new TextBlock
            {
                Text = "›",
                FontSize = 15,
                Margin = new Thickness(8, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TestoTenue"),
            };
            Grid.SetColumn(freccia, 2);
            riga.Children.Add(freccia);
        }

        return riga;
    }

    private DeckButton? PulsanteScelto()
    {
        int i = Scelto;
        var lista = Correnti;
        return i >= 0 && i < lista.Count ? lista[i] : null;
    }

    /// <summary>Sceglie una riga senza far scattare il salvataggio.</summary>
    private void Scegli(int indice)
    {
        if (Cercando)
        {
            caricamento = true;
            CampoCerca.Text = "";
            SuggerimentoCerca.Visibility = Visibility.Visible;
            caricamento = false;
        }
        if (ElencoPulsanti.SelectedIndex == indice)
        {
            MostraPulsante();
            return;
        }
        ElencoPulsanti.SelectedIndex = indice;
    }

    /// <summary>
    /// Chiude la ricerca senza rifare l'elenco: lo rifa' chi ha chiamato,
    /// perche' sta per cambiare anche il deck.
    /// </summary>
    private void SmettiDiCercare()
    {
        if (!Cercando) return;
        caricamento = true;
        CampoCerca.Text = "";
        SuggerimentoCerca.Visibility = Visibility.Visible;
        caricamento = false;
    }

    private void ElencoPulsanti_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // In ricerca la riga scelta non e' un pulsante da modificare ma un
        // posto dove andare: ci si va, e l'elenco torna quello della cartella.
        if (Cercando)
        {
            int i = ElencoPulsanti.SelectedIndex;
            if (caricamento || i < 0 || i >= risultati.Count) return;

            var trovato = risultati[i];
            Dispatcher.BeginInvoke(() =>
            {
                caricamento = true;
                CampoCerca.Text = "";
                SuggerimentoCerca.Visibility = Visibility.Visible;
                caricamento = false;
                VaiDentro(trovato.Percorso, trovato.Indice);
                ElencoPulsanti.Focus();
            });
            return;
        }

        MostraPulsante();
    }

    private void ElencoPulsanti_DoppioClic(object sender, MouseButtonEventArgs e)
    {
        if (PulsanteScelto() is { Cartella: true }) EntraIn(Scelto);
    }

    /// <summary>
    /// Le scorciatoie dell'elenco. Stanno qui e non sulla finestra intera
    /// perche' Canc dentro una casella di testo deve cancellare una lettera,
    /// non un pulsante.
    /// </summary>
    private void ElencoPulsanti_Tasto(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        switch (e.Key)
        {
            case Key.Delete when !ctrl:
                TogliPulsante();
                break;
            case Key.Enter:
                if (PulsanteScelto() is { Cartella: true }) EntraIn(Scelto);
                else CampoEtichetta.Focus();
                break;
            case Key.Back:
                Risali();
                break;
            case Key.F2:
                Rinomina();
                break;
            case Key.D when ctrl:
                DuplicaPulsante();
                break;
            case Key.C when ctrl:
                Copia(false);
                break;
            case Key.X when ctrl:
                Copia(true);
                break;
            case Key.V when ctrl:
                Incolla();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---- il pulsante scelto ----

    /// <summary>Riempie i campi col pulsante scelto, senza far scattare il salvataggio.</summary>
    private void MostraPulsante()
    {
        var b = PulsanteScelto();
        PannelloPulsante.IsEnabled = b is not null;
        BtnProva.IsEnabled = b is not null && !b.Cartella;

        // Cambiare pulsante annulla quel che era in corso sul precedente:
        // una cattura aperta si mangerebbe i tasti, e un conto alla rovescia
        // farebbe partire l'azione di un pulsante che non si guarda piu'.
        AnnullaConto();
        BtnCattura.IsChecked = false;
        BtnCatturaPasso.IsChecked = false;
        BtnRegistra.IsChecked = false;

        if (b is null)
        {
            RiquadroPassi.Visibility = Visibility.Collapsed;
            RiquadroCartella.Visibility = Visibility.Collapsed;
            RigaControllo.Visibility = Visibility.Collapsed;
            MostraIcona("");
            AggiornaAnteprima();
            return;
        }

        bool prima = caricamento;
        caricamento = true;

        CampoEtichetta.Text = b.Label;
        CampoGlifo.Text = b.Glyph;
        CampoColore.Text = b.Color;

        var tipo = Azioni.Di(b.Action?.Type);
        int indice = Array.FindIndex(Azioni.PerPulsante, t => t.Codice == tipo.Codice);
        ElencoTipiAzione.SelectedIndex = Math.Max(0, indice);
        CampoValore.Text = b.Action?.Value ?? "";
        CampoArgomenti.Text = b.Action?.Args ?? "";

        caricamento = prima;

        MostraIcona(b.Icon);

        // I gruppi aperti erano di un altro pulsante: qui dentro gli stessi
        // numeri porterebbero da tutt'altra parte.
        percorsoPassi.Clear();

        AggiornaFormaAzione();
        RiempiPassi(0);
        AggiornaSpecchi();
    }

    /// <summary>Quel che deriva dai campi: gettoni accesi, anteprime, controllo.</summary>
    private void AggiornaSpecchi()
    {
        var b = PulsanteScelto();
        AggiornaAnteprimaColore(CampoColore.Text);

        foreach (var g in gettoniGlifo)
            g.IsChecked = (string)g.Tag == CampoGlifo.Text;
        foreach (var g in gettoniColore)
            g.IsChecked = string.Equals((string?)g.ToolTip, CampoColore.Text.Trim(),
                StringComparison.OrdinalIgnoreCase);

        MostraControllo(b);
        AggiornaAnteprima();
    }

    /// <summary>
    /// Il francobollo dell'icona accanto ai pulsanti che la cambiano, e la riga
    /// che dice cos'e'. Un'icona che c'era e non c'e' piu' — il file tolto a
    /// mano da config/icone — si dice: altrimenti il pulsante tornerebbe al
    /// glifo senza che nessuno sappia perche'.
    /// </summary>
    private void MostraIcona(string nome)
    {
        var immagine = Icone.Carica(nome);
        ImmagineIcona.Source = immagine;
        TestoNessunaIcona.Visibility = immagine is null ? Visibility.Visible : Visibility.Collapsed;

        TestoIcona.Text = nome.Length == 0
            ? "Nessuna: sul tablet si vede l'icona."
            : immagine is null
                ? $"'{nome}' non e' piu' in config/icone: sul tablet si vede l'icona."
                : $"{immagine.PixelWidth}x{immagine.PixelHeight} in config/icone/{nome}";
    }

    private void MostraControllo(DeckButton? b)
    {
        string male = b is null ? "" : Azioni.Controlla(b);
        RigaControllo.Visibility = male.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TestoControllo.Text = male;
    }

    private void AggiornaAnteprimaColore(string colore)
    {
        AnteprimaColore.Background = new SolidColorBrush(AnteprimaDeck.Tinta(colore));
    }

    // ---- lettura dei campi ----

    private void CampoPulsante_Changed(object sender, TextChangedEventArgs e)
    {
        if (caricamento) return;

        // Un passo indietro per raffica di tasti, non uno per lettera: chi
        // scrive « Photoshop » e poi preme Annulla si aspetta di ritrovare il
        // nome di prima, non « Photosho ».
        Ricorda(sender is TextBox campo ? "campo:" + campo.Name : "campo");
        LeggiPulsante();
    }

    private void Scelte_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento || ElencoScelte.SelectedItem is not string valore) return;
        CampoValore.Text = valore;   // il campo resta la verita', l'elenco e' un modo per riempirlo
        AggiornaFormaAzione();       // per OBS e Twitch il comando decide il secondo campo
    }

    private void SceltePasso_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento || ElencoSceltePasso.SelectedItem is not string valore) return;
        CampoValorePasso.Text = valore;
        AggiornaFormaPasso();
    }

    private void ElencoArgomenti_Changed(object sender, TextChangedEventArgs e)
    {
        if (!caricamento && CampoArgomenti.Text != ElencoArgomenti.Text) CampoArgomenti.Text = ElencoArgomenti.Text;
    }

    private void ElencoArgomentiPasso_Changed(object sender, TextChangedEventArgs e)
    {
        if (!caricamento && CampoArgomentiPasso.Text != ElencoArgomentiPasso.Text) CampoArgomentiPasso.Text = ElencoArgomentiPasso.Text;
    }

    /// <summary>
    /// Scene e fonti si chiedono a OBS quando si apre l'elenco, non prima:
    /// aprire l'editor non deve collegarsi a niente.
    /// </summary>
    private async void ElencoArgomenti_Aperto(object? sender, EventArgs e)
    {
        if (sender is not ComboBox elenco || elenco.Tag is not string cosa) return;
        try
        {
            var voci = await Task.Run(() => cosa == "scene" ? Streaming.Obs.Scene() : Streaming.Obs.Fonti());
            string testo = elenco.Text;
            bool prima = caricamento;
            caricamento = true;
            elenco.Items.Clear();
            foreach (var v in voci) elenco.Items.Add(v);
            elenco.Text = testo;
            caricamento = prima;
        }
        catch (StreamingErrore x)
        {
            Registra(x.Message, true);
        }
    }

    /// <summary>
    /// Mostra il secondo campo di OBS e Twitch col nome che ha per quel
    /// comando, come elenco se i valori stanno in OBS, o lo nasconde.
    /// </summary>
    private void FormaArgomento(TipoAzione tipo, string comando, FrameworkElement riga, TextBlock etichetta,
                                TextBox campo, ComboBox elenco, PannelloStreaming collegamento)
    {
        collegamento.Mostra(tipo.Codice);
        if (tipo.Codice is not ("obs" or "twitch"))
        {
            etichetta.Text = "Argomenti";
            elenco.Visibility = Visibility.Collapsed;
            return;
        }

        var arg = Streaming.Argomento(tipo.Codice, comando);
        riga.Visibility = arg is null ? Visibility.Collapsed : Visibility.Visible;
        etichetta.Text = arg?.Etichetta ?? "";

        bool daObs = arg?.DaObs is not null;
        campo.Visibility = daObs ? Visibility.Collapsed : Visibility.Visible;
        elenco.Visibility = daObs ? Visibility.Visible : Visibility.Collapsed;
        if (!Equals(elenco.Tag, arg?.DaObs)) elenco.Items.Clear();
        elenco.Tag = arg?.DaObs;
        if (daObs && elenco.Text != campo.Text)
        {
            bool prima = caricamento;
            caricamento = true;
            elenco.Text = campo.Text;
            caricamento = prima;
        }
    }

    /// <summary>Se il secondo campo conta, per quel tipo e quel comando.</summary>
    private static bool UsaArgomenti(TipoAzione tipo, string valore) =>
        tipo.HaArgomenti && (tipo.Codice is not ("obs" or "twitch") || Streaming.Argomento(tipo.Codice, valore) is not null);

    /// <summary>
    /// Riempie un elenco di scelte e ci mette dentro il valore corrente. Non si
    /// ricostruisce se le voci sono gia' quelle: rifarlo a ogni tasto battuto
    /// farebbe saltare la selezione sotto le dita.
    /// </summary>
    private void RiempiScelte(ComboBox elenco, string[]? scelte, string valore)
    {
        bool prima = caricamento;
        caricamento = true;

        if (scelte is not null && !elenco.Items.Cast<object>().Select(v => v as string).SequenceEqual(scelte))
        {
            elenco.Items.Clear();
            foreach (var voce in scelte) elenco.Items.Add(voce);
        }
        elenco.SelectedItem = scelte?.FirstOrDefault(
            s => string.Equals(s, valore.Trim(), StringComparison.OrdinalIgnoreCase));

        caricamento = prima;
    }

    private void TipoAzione_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!caricamento) Ricorda("tipo");
        AggiornaFormaAzione();
        LeggiPulsante();
        RiempiPassi(0);
    }

    /// <summary>
    /// La riga del valore cambia forma secondo l'azione: un elenco per i tasti
    /// multimediali, « Sfoglia » per un programma, « Cattura » per una
    /// combinazione, niente del tutto per una sequenza o per una cartella.
    /// </summary>
    private void AggiornaFormaAzione()
    {
        var tipo = TipoAzioneScelto();

        AiutoAzione.Text = tipo.Aiuto;
        EtichettaValore.Text = tipo.EtichettaValore;

        bool sequenza = tipo.Codice == "seq";
        bool cartella = tipo.Codice == "cartella";
        bool senzaValore = sequenza || cartella;

        RigaValore.Visibility = senzaValore ? Visibility.Collapsed : Visibility.Visible;
        RigaArgomenti.Visibility = tipo.HaArgomenti && !senzaValore
            ? Visibility.Visible
            : Visibility.Collapsed;
        RiquadroPassi.Visibility = sequenza ? Visibility.Visible : Visibility.Collapsed;
        RiquadroCartella.Visibility = cartella ? Visibility.Visible : Visibility.Collapsed;

        // L'icona presa dal programma ha senso solo dove un programma c'e'.
        BtnIconaProgramma.IsEnabled = !cartella;

        // I profili non stanno nella tabella delle azioni — cambiano mentre
        // il programma gira — ma si scelgono da un elenco come tutto il resto:
        // scrivere « Lavro » e scoprirlo premendo sul tablet non serve a nessuno.
        var scelte = tipo.Codice == "profilo"
            ? deckFile.Profili.Select(x => x.Nome).ToArray()
            : tipo.Scelte;

        bool daElenco = scelte is not null;
        CampoValore.Visibility = daElenco ? Visibility.Collapsed : Visibility.Visible;
        ElencoScelte.Visibility = daElenco ? Visibility.Visible : Visibility.Collapsed;
        if (daElenco) RiempiScelte(ElencoScelte, scelte, CampoValore.Text);

        FormaArgomento(tipo, CampoValore.Text, RigaArgomenti, EtichettaArgomenti, CampoArgomenti,
                       ElencoArgomenti, CollegamentoAzione);

        BtnCattura.Visibility = tipo.Cattura ? Visibility.Visible : Visibility.Collapsed;
        BtnSfoglia.Visibility = tipo.Codice == "run" ? Visibility.Visible : Visibility.Collapsed;
        if (!tipo.Cattura) BtnCattura.IsChecked = false;

        if (cartella) AggiornaRiquadroCartella();

        // I passi stanno in fondo a una colonna piu' alta della finestra:
        // scegliendo « Sequenza » ci si porta davanti da soli, altrimenti si
        // cambia il tipo e non si vede succedere niente.
        if (sequenza && !caricamento) Dispatcher.BeginInvoke(RiquadroPassi.BringIntoView);
    }

    /// <summary>Cosa contiene la cartella scelta, senza doverci entrare.</summary>
    private void AggiornaRiquadroCartella()
    {
        var b = PulsanteScelto();
        ElencoDentro.Items.Clear();
        if (b is null) return;

        foreach (var f in b.Dentro) ElencoDentro.Items.Add(Voce(f, null));

        int quanti = b.Dentro.Count;
        int perPagina = Math.Max(1, deck.Cols * deck.Rows);
        int pagine = Math.Max(1, (quanti + perPagina - 1) / perPagina);

        TestoCartella.Text = quanti == 0
            ? "Vuota. Aggiungici qualcosa, oppure trascinaci sopra un pulsante nell'anteprima: lasciandolo sulla cartella ci finisce dentro."
            : pagine == 1
                ? $"{(quanti == 1 ? "Un pulsante" : quanti + " pulsanti")}: sul tablet stanno in una pagina sola. "
                  + "Premendo la cartella si aprono, e la fascia in cima riporta indietro."
                : $"{quanti} pulsanti, che qui dentro occupano {pagine} pagine: sul tablet si passa dall'una all'altra scorrendo di lato.";
    }

    private void ElencoDentro_DoppioClic(object sender, MouseButtonEventArgs e)
    {
        int i = ElencoDentro.SelectedIndex;
        if (Scelto < 0 || i < 0) return;

        int cartella = Scelto;
        EntraIn(cartella);
        Scegli(i);
    }

    private TipoAzione TipoAzioneScelto()
    {
        int i = Math.Max(0, ElencoTipiAzione.SelectedIndex);
        var elenco = Azioni.PerPulsante;
        return elenco[Math.Min(i, elenco.Length - 1)];
    }

    private void LeggiPulsante()
    {
        if (caricamento) return;
        var b = PulsanteScelto();
        if (b is null) return;

        b.Label = CampoEtichetta.Text;
        b.Glyph = CampoGlifo.Text;
        b.Color = CampoColore.Text;

        var tipo = TipoAzioneScelto();
        bool eraCartella = b.Cartella;
        b.Action ??= new ActionSpec();
        b.Action.Type = tipo.Codice;

        if (tipo.Codice == "seq")
        {
            // I passi restano quelli che sono: li cambia il riquadro sotto.
            b.Action.Steps ??= new List<ActionSpec>();
            b.Action.Value = "";
            b.Action.Args = "";
        }
        else if (tipo.Codice == "cartella")
        {
            b.Buttons ??= new List<DeckButton>();
            b.Action.Value = "";
            b.Action.Args = "";
        }
        else
        {
            b.Action.Value = CampoValore.Text;
            b.Action.Args = UsaArgomenti(tipo, CampoValore.Text) ? CampoArgomenti.Text : "";
        }

        // Smettere di essere una cartella non butta via il contenuto — resta in
        // memoria, e cambiare idea lo ritrova — ma sul tablet quei pulsanti
        // spariscono, e non e' una cosa da scoprire dopo.
        if (eraCartella && tipo.Codice != "cartella" && b.Dentro.Count > 0)
            Registra($"'{Nome(b)}' non e' piu' una cartella: i {b.Dentro.Count} pulsanti che aveva dentro "
                     + "non andranno al tablet. Tornando a « Cartella di pulsanti » sono ancora li'.", true);

        RinfrescaVoce();
        AggiornaSpecchi();
        Modificato();
    }

    /// <summary>Rifa' la riga dell'elenco senza far saltare la selezione.</summary>
    private void RinfrescaVoce()
    {
        int sel = Scelto;
        var b = PulsanteScelto();
        if (b is null || sel < 0 || sel >= ElencoPulsanti.Items.Count) return;

        bool prima = caricamento;
        caricamento = true;
        ElencoPulsanti.Items[sel] = Voce(b, null);
        ElencoPulsanti.SelectedIndex = sel;
        caricamento = prima;

        if (b.Cartella) AggiornaRiquadroCartella();
        AggiornaTotale();
    }

    // ---- cattura di una combinazione ----

    private void Cattura_Checked(object sender, RoutedEventArgs e) => IniziaCattura(CampoValore);

    private void Cattura_Unchecked(object sender, RoutedEventArgs e) => ChiudiCattura();

    private void CatturaPasso_Checked(object sender, RoutedEventArgs e) => IniziaCattura(CampoValorePasso);

    private void CatturaPasso_Unchecked(object sender, RoutedEventArgs e) => ChiudiCattura();

    /// <summary>Il campo che sta aspettando la combinazione, o null.</summary>
    private TextBox? catturaCampo;

    private void IniziaCattura(TextBox campo)
    {
        // Due catture aperte insieme si mangerebbero gli stessi tasti: chi
        // arriva dopo spegne l'altra, e il registratore con lei.
        BtnRegistra.IsChecked = false;
        if (!ReferenceEquals(campo, CampoValore)) BtnCattura.IsChecked = false;
        if (!ReferenceEquals(campo, CampoValorePasso)) BtnCatturaPasso.IsChecked = false;

        catturaCampo = campo;
        PreviewKeyDown += CatturaTasto;
        Registra("Premi la combinazione da assegnare. Esc annulla.");
    }

    private void ChiudiCattura()
    {
        PreviewKeyDown -= CatturaTasto;
        catturaCampo = null;
    }

    private void SpegniCattura()
    {
        BtnCattura.IsChecked = false;
        BtnCatturaPasso.IsChecked = false;
    }

    /// <summary>
    /// Scrivere "ctrl+shitf+m" e scoprirlo solo perche' il pulsante non fa
    /// niente e' un modo pessimo di perdere dieci minuti: qui la combinazione
    /// si preme, e viene scritta com'e'.
    /// </summary>
    private void CatturaTasto(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var campo = catturaCampo;
        if (campo is null) return;

        string combinazione = Combinazione(e, out string male);
        if (combinazione.Length == 0)
        {
            if (male == "annulla")
            {
                SpegniCattura();
                Registra("Cattura annullata.");
            }
            else if (male.Length > 0)
            {
                Registra(male, true);
            }
            return;
        }

        SpegniCattura();
        campo.Text = combinazione;
        Registra($"Catturata: {combinazione}");
    }

    /// <summary>
    /// La combinazione premuta, scritta come la rilegge InputInjector.
    ///
    /// Stringa vuota quando non c'e' ancora niente da scrivere: un modificatore
    /// da solo non e' una combinazione, e si aspetta il tasto vero. In
    /// <paramref name="male"/> finisce "annulla" per l'Esc, oppure il motivo per
    /// cui quel tasto non si puo' usare.
    /// </summary>
    private static string Combinazione(KeyEventArgs e, out string male)
    {
        male = "";

        // Il tasto tenuto giu' si ripete da solo: durante una registrazione
        // riempirebbe la sequenza di passi identici che nessuno ha voluto.
        if (e.IsRepeat) return "";

        var tasto = e.Key == Key.System ? e.SystemKey : e.Key;

        if (tasto == Key.Escape)
        {
            male = "annulla";
            return "";
        }

        if (tasto is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return "";

        string nome = NomeTasto(tasto);
        if (nome.Length == 0)
        {
            male = $"Il tasto {tasto} non ha un nome usabile in una combinazione.";
            return "";
        }

        var pezzi = new List<string>();
        var mod = Keyboard.Modifiers;
        if (mod.HasFlag(ModifierKeys.Control)) pezzi.Add("ctrl");
        if (mod.HasFlag(ModifierKeys.Alt)) pezzi.Add("alt");
        if (mod.HasFlag(ModifierKeys.Shift)) pezzi.Add("shift");
        if (mod.HasFlag(ModifierKeys.Windows)) pezzi.Add("win");
        pezzi.Add(nome);
        return string.Join("+", pezzi);
    }

    // ---- registrazione di una sequenza ----

    /// <summary>Quando e' arrivato l'ultimo tasto registrato, per misurare le pause.</summary>
    private long ultimaBattuta;

    private void Registra_Checked(object sender, RoutedEventArgs e)
    {
        SpegniCattura();
        ultimaBattuta = 0;
        Ricorda();
        PreviewKeyDown += RegistraTasto;
        TestoRegistra.Text = "Sto registrando: batti la macro. Esc chiude.";
        TestoRegistra.Foreground = (Brush)FindResource("Attenzione");
        Registra("Registrazione aperta: ogni combinazione premuta diventa un passo.");
    }

    private void Registra_Unchecked(object sender, RoutedEventArgs e)
    {
        PreviewKeyDown -= RegistraTasto;
        TestoRegistra.Text = "";
        TestoRegistra.Foreground = (Brush)FindResource("TestoTenue");
    }

    /// <summary>
    /// Ogni combinazione premuta diventa un passo, e il tempo vero passato fra
    /// una e l'altra diventa un passo « Aspetta ».
    ///
    /// E' il modo naturale di montare una macro: la si fa, invece di scriverla
    /// riga per riga cercando di ricordare in che ordine andavano i tasti. Le
    /// pause si registrano perche' quasi sempre servono davvero — sono il tempo
    /// che il programma dall'altra parte impiega ad aprire la sua finestra — e
    /// toglierne una e' molto piu' facile che accorgersi che mancava.
    /// </summary>
    private void RegistraTasto(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (PulsanteScelto() is null) return;

        string combinazione = Combinazione(e, out string male);
        if (combinazione.Length == 0)
        {
            if (male == "annulla")
            {
                BtnRegistra.IsChecked = false;
                Registra($"Registrazione chiusa: {Passi().Count} passi.");
            }
            else if (male.Length > 0)
            {
                Registra(male, true);
            }
            return;
        }

        var passi = Passi();
        long adesso = Environment.TickCount64;

        // Sotto un quinto di secondo la pausa e' quella di chi batte veloce,
        // non una che serva: metterla renderebbe l'elenco illeggibile.
        if (ultimaBattuta > 0)
        {
            int pausa = (int)Math.Min(10_000, adesso - ultimaBattuta);
            if (pausa >= 200)
                passi.Add(new ActionSpec { Type = "delay", Value = (pausa / 10 * 10).ToString() });
        }
        ultimaBattuta = adesso;

        passi.Add(new ActionSpec { Type = "hotkey", Value = combinazione });
        RiempiPassi(passi.Count - 1);
        ElencoPassi.ScrollIntoView(ElencoPassi.SelectedItem);
        RinfrescaVoce();
        Modificato();
    }

    /// <summary>Il nome che InputInjector sa rileggere, o vuoto se non c'e'.</summary>
    private static string NomeTasto(Key tasto) => tasto switch
    {
        >= Key.A and <= Key.Z => tasto.ToString().ToLowerInvariant(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (tasto - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (tasto - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => "f" + (tasto - Key.F1 + 1),
        Key.Enter => "enter",
        Key.Space => "space",
        Key.Tab => "tab",
        Key.Back => "backspace",
        Key.Delete => "delete",
        Key.Insert => "insert",
        Key.Home => "home",
        Key.End => "end",
        Key.PageUp => "pageup",
        Key.PageDown => "pagedown",
        Key.Up => "up",
        Key.Down => "down",
        Key.Left => "left",
        Key.Right => "right",
        Key.PrintScreen or Key.Snapshot => "printscreen",
        Key.CapsLock => "capslock",
        _ => "",
    };

    private void Sfoglia_Click(object sender, RoutedEventArgs e)
    {
        string? scelto = ScegliProgramma();
        if (scelto is null) return;

        Ricorda();
        CampoValore.Text = scelto;

        // Un pulsante appena creato si chiama « Nuovo »: chi ha appena scelto
        // Photoshop.exe vuole quasi sempre che si chiami Photoshop, e cambiarlo
        // resta piu' facile che scriverlo.
        if (CampoEtichetta.Text is "" or "Nuovo")
            CampoEtichetta.Text = Path.GetFileNameWithoutExtension(scelto);
    }

    // ---- icona del pulsante ----

    /// <summary>
    /// Lo sfondo dietro il deck: un'immagine del disco, ritagliata a 1024 x 600.
    /// Arriva al tablet con « Manda al tablet », come il resto del profilo.
    /// </summary>
    private void ScegliSfondo_Click(object sender, RoutedEventArgs e)
    {
        var finestra = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Immagine dietro il deck",
            Filter = "Immagini (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
        };
        if (finestra.ShowDialog(this) != true) return;

        string nome = Icone.ImportaSfondo(finestra.FileName, out string male);
        if (nome.Length == 0)
        {
            Registra($"Sfondo non preso: {male}", true);
            return;
        }
        Ricorda("sfondo");
        deck.Sfondo = nome;
        AggiornaGriglia();
        Modificato();
        Registra($"Sfondo del deck preso da {Path.GetFileName(finestra.FileName)}.");
    }

    private void TogliSfondo_Click(object sender, RoutedEventArgs e)
    {
        if (deck.Sfondo.Length == 0) return;
        Ricorda("sfondo");
        deck.Sfondo = "";
        AggiornaGriglia();
        Modificato();
    }

    private void ScegliIcona_Click(object sender, RoutedEventArgs e)
    {
        if (PulsanteScelto() is null) return;

        var finestra = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Immagine da usare come icona",
            Filter = "Immagini (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico"
                     + "|Programmi (*.exe;*.dll;*.lnk)|*.exe;*.dll;*.lnk"
                     + "|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
        };
        if (finestra.ShowDialog(this) != true) return;

        string scelto = finestra.FileName;
        // Un .exe scelto dalla finestra delle immagini non e' un'immagine, ed e'
        // una cosa che uno prova: si prende l'icona che ha dentro invece di
        // rispondere che il formato non si legge.
        string estensione = Path.GetExtension(scelto).ToLowerInvariant();
        string nome = estensione is ".exe" or ".dll" or ".lnk"
            ? Icone.ImportaProgramma(scelto, out string male)
            : Icone.ImportaImmagine(scelto, out male);

        if (nome.Length == 0)
        {
            Registra($"Icona non presa: {male}", true);
            return;
        }
        MettiIcona(nome, $"Icona presa da {Path.GetFileName(scelto)}.");
    }

    /// <summary>
    /// L'icona del programma che il pulsante avvia. E' la strada corta per i
    /// pulsanti « Avvia »: l'immagine giusta ce l'ha gia' il programma.
    /// </summary>
    private void IconaDalProgramma_Click(object sender, RoutedEventArgs e)
    {
        var b = PulsanteScelto();
        if (b is null) return;

        string programma = Azioni.Di(b.Action?.Type).Codice == "run"
            ? (b.Action?.Value ?? "")
            : "";

        if (programma.Trim().Length == 0)
        {
            string? scelto = ScegliProgramma();
            if (scelto is null) return;
            programma = scelto;
        }

        string nome = Icone.ImportaProgramma(programma, out string male);
        if (nome.Length == 0)
        {
            Registra($"Icona non presa: {male}", true);
            return;
        }
        MettiIcona(nome, $"Icona presa da {Path.GetFileName(programma)}.");
    }

    private void TogliIcona_Click(object sender, RoutedEventArgs e)
    {
        var b = PulsanteScelto();
        if (b is null || b.Icon.Length == 0) return;
        MettiIcona("", "Immagine tolta: torna l'icona. Il file resta in config/icone fino al salvataggio.");
    }

    private void MettiIcona(string nome, string detto)
    {
        var b = PulsanteScelto();
        if (b is null) return;

        Ricorda();
        b.Icon = nome;
        MostraIcona(nome);
        RinfrescaVoce();
        AggiornaAnteprima();
        Modificato();
        Registra(detto);
    }

    private string? ScegliProgramma()
    {
        var finestra = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Programma da avviare",
            Filter = "Programmi (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
        };
        return finestra.ShowDialog(this) == true ? finestra.FileName : null;
    }

    // ---- aggiungere, togliere, spostare ----

    private void AggiungiPulsante_Click(object sender, RoutedEventArgs e) => AggiungiPulsante(false);

    private void NuovaCartella_Click(object sender, RoutedEventArgs e) => AggiungiPulsante(true);

    /// <summary>
    /// Un pulsante nuovo, o una cartella nuova, in fondo alla cartella aperta.
    /// Il fuoco va sull'etichetta gia' selezionata: la prima cosa che si fa e'
    /// dargli un nome, e doverci cliccare sopra sarebbe un clic in piu' ogni
    /// volta.
    /// </summary>
    private void AggiungiPulsante(bool cartella)
    {
        SmettiDiCercare();

        if (cartella && percorso.Count >= DeckConfig.Piani)
        {
            Registra($"Piu' di {DeckConfig.Piani} piani di cartelle non si fanno: a quel punto "
                     + "trovare un pulsante costa piu' che premerlo.", true);
            return;
        }

        Ricorda();
        var lista = Correnti;
        lista.Add(new DeckButton
        {
            Id = IdLibero(cartella ? "cartella" : "pulsante"),
            Label = cartella ? "Cartella" : "Nuovo",
            Glyph = cartella ? GlifoCartella : "circle-dot",
            Color = Colori[cartella ? 1 : 0],
            Action = new ActionSpec { Type = cartella ? "cartella" : "hotkey", Value = "" },
            Buttons = cartella ? new List<DeckButton>() : null,
        });

        RiempiElencoPulsanti(lista.Count - 1);
        AggiornaGriglia();
        Modificato();
        CampoEtichetta.Focus();
        CampoEtichetta.SelectAll();
    }

    /// <summary>Un pulsante dentro la cartella scelta, senza doverci entrare.</summary>
    private void AggiungiDentro_Click(object sender, RoutedEventArgs e) => AggiungiDentro(false);

    private void NuovaCartellaDentro_Click(object sender, RoutedEventArgs e) => AggiungiDentro(true);

    private void AggiungiDentro(bool cartella)
    {
        int quale = Scelto;
        var b = PulsanteScelto();
        if (b is null || !b.Cartella) return;

        EntraIn(quale);
        AggiungiPulsante(cartella);
    }

    private void ApriCartella_Click(object sender, RoutedEventArgs e)
    {
        if (PulsanteScelto() is { Cartella: true }) EntraIn(Scelto);
    }

    private void Rinomina_Click(object sender, RoutedEventArgs e) => Rinomina();

    private void Rinomina()
    {
        if (PulsanteScelto() is null) return;
        CampoEtichetta.Focus();
        CampoEtichetta.SelectAll();
    }

    private void DuplicaPulsante_Click(object sender, RoutedEventArgs e) => DuplicaPulsante();

    private void DuplicaPulsante()
    {
        var b = PulsanteScelto();
        if (b is null) return;

        Ricorda();
        var copia = b.Copia();
        RibattezzaAlbero(copia, b.Id);
        copia.Label = b.Label.Length > 0 ? b.Label + " 2" : "";

        var lista = Correnti;
        lista.Insert(Scelto + 1, copia);

        RiempiElencoPulsanti(Scelto + 1);
        AggiornaGriglia();
        Modificato();
    }

    /// <summary>
    /// Da' identificativi liberi a un pulsante copiato e a tutto quel che ha
    /// dentro. Duplicare una cartella duplica anche i suoi figli, e due
    /// pulsanti con lo stesso identificativo vorrebbero dire che premendone uno
    /// il PC esegue sempre e solo l'altro.
    /// </summary>
    private void RibattezzaAlbero(DeckButton b, string radice)
    {
        b.Id = IdLibero(radice.Length > 0 ? radice : "pulsante");
        foreach (var f in b.Buttons ?? new List<DeckButton>()) RibattezzaAlbero(f, f.Id);
    }

    /// <summary>
    /// L'identificativo non si vede da nessuna parte, ma e' quello che il
    /// tablet rimanda indietro: deve solo essere unico in tutto l'albero e
    /// restare stabile.
    /// </summary>
    private string IdLibero(string radice)
    {
        string pulita = radice.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (pulita.Length == 0) pulita = "pulsante";

        var presi = deck.Ovunque().Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        for (int n = presi.Count + 1; ; n++)
        {
            string id = pulita + n;
            if (!presi.Contains(id)) return id;
        }
    }

    private void TogliPulsante_Click(object sender, RoutedEventArgs e) => TogliPulsante();

    private void TogliPulsante() => TogliDove(percorso, Scelto);

    /// <summary>
    /// Toglie il pulsante che sta in quella posizione dentro quella cartella.
    ///
    /// La cartella si dice per esteso e non si da' per scontata: la croce
    /// dell'anteprima grande toglie un pulsante che sta dentro la cartella
    /// aperta di la', che non e' per forza quella aperta nell'editor.
    /// </summary>
    private void TogliDove(IReadOnlyList<int> dove, int i)
    {
        var lista = deck.Contenuto(dove);
        if (i < 0 || i >= lista.Count) return;

        var b = lista[i];

        // Una cartella si porta via quel che ha dentro, e quel che ha dentro
        // non si vede da qui: si conta prima di toglierla.
        if (b.Cartella && b.Dentro.Count > 0)
        {
            int quanti = b.ContaDentro();
            var scelta = MessageBox.Show(this,
                $"« {Nome(b)} » contiene {(quanti == 1 ? "un pulsante" : quanti + " pulsanti")}.\n"
                + "Togliendola vanno via anche quelli.\n\nProcedo?",
                "TabDeck", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (scelta != MessageBoxResult.OK) return;
        }

        Ricorda();
        string nome = Nome(b);
        bool inVista = ReferenceEquals(lista, Correnti);
        lista.RemoveAt(i);

        // Una cartella tolta si porta via anche il posto in cui si stava
        // guardando: i due percorsi si accorciano fino a dove reggono ancora.
        Riallinea(percorso);
        Riallinea(percorsoVivo);

        RiempiElencoPulsanti(inVista
            ? Math.Min(i, Correnti.Count - 1)
            : Math.Min(Scelto, Correnti.Count - 1));
        AggiornaGriglia();
        Modificato();
        Registra($"Tolto '{nome}'. Torna com'era con Annulla, o chiudendo senza salvare.");
    }

    /// <summary>Accorcia un percorso fino a dove esiste ancora.</summary>
    private void Riallinea(List<int> dove)
    {
        var buono = deck.PercorsoValido(dove);
        if (buono.Count == dove.Count) return;
        dove.Clear();
        dove.AddRange(buono);
    }

    private void SuPulsante_Click(object sender, RoutedEventArgs e) => SpostaDiUno(-1);

    private void GiuPulsante_Click(object sender, RoutedEventArgs e) => SpostaDiUno(+1);

    private void SpostaDiUno(int passo)
    {
        int i = Scelto;
        if (i < 0) return;
        Porta(percorso.ToArray(), i, percorso.ToArray(), i + passo);
    }

    /// <summary>
    /// Porta un pulsante da un posto a un altro, che puo' essere un'altra
    /// cartella.
    ///
    /// Nell'elenco non ci sono buchi — la griglia si riempie in ordine — quindi
    /// non e' uno scambio fra due celle: il pulsante si toglie e si rimette
    /// dov'e' stato portato, e gli altri scorrono di conseguenza. I due elenchi
    /// si risolvono prima di togliere: togliendo per primi, l'indice della
    /// cartella di destinazione potrebbe non essere piu' quello.
    /// </summary>
    private void Porta(int[] daPercorso, int daIndice, int[] aPercorso, int aIndice)
    {
        var partenza = deck.Contenuto(daPercorso);
        if (daIndice < 0 || daIndice >= partenza.Count) return;

        var b = partenza[daIndice];
        var arrivo = deck.Contenuto(aPercorso);

        // Dentro se stessa una cartella non ci va: sparirebbe dal deck insieme
        // a tutto quello che contiene, e non ci sarebbe piu' modo di tirarla
        // fuori.
        if (b.Cartella && deck.Cartelle(aPercorso).Any(c => ReferenceEquals(c, b)))
        {
            Registra($"'{Nome(b)}' non puo' finire dentro se stessa.", true);
            return;
        }

        int piani = aPercorso.Length + Profondita(b);
        if (piani > DeckConfig.Piani)
        {
            Registra($"Li' dentro '{Nome(b)}' farebbe {piani} piani di cartelle, e il massimo e' "
                     + $"{DeckConfig.Piani}.", true);
            return;
        }

        bool stessoPosto = ReferenceEquals(partenza, arrivo);
        int posto = Math.Clamp(aIndice, 0, stessoPosto ? arrivo.Count - 1 : arrivo.Count);
        if (stessoPosto && posto == daIndice) return;

        // Il nome di dove sta andando si legge adesso: appena il pulsante viene
        // tolto, gli indici che vengono dopo di lui scalano di uno, e il
        // percorso d'arrivo non indica piu' la stessa cartella.
        string dove = DescriviCartella(aPercorso);

        Ricorda();
        partenza.RemoveAt(daIndice);
        arrivo.Insert(posto, b);
        Modificato();

        // La selezione segue il pulsante solo se e' rimasto sott'occhio:
        // sceglierne uno che sta in un'altra cartella vorrebbe dire cambiare
        // pagina sotto le mani di chi ha appena trascinato.
        bool visibile = ReferenceEquals(arrivo, Correnti);
        RiempiElencoPulsanti(visibile ? posto : Math.Min(Scelto, Correnti.Count - 1));
        AggiornaGriglia();

        int perPagina = Math.Max(1, deck.Cols * deck.Rows);
        Registra(Anteprima.Pagine > 1 && visibile
            ? $"'{Nome(b)}' spostato in {dove}: pagina {posto / perPagina + 1}, cella {posto % perPagina + 1}."
            : $"'{Nome(b)}' spostato in {dove}, cella {posto + 1}.");
    }

    /// <summary>Quanti piani di cartelle porta con se' un pulsante: zero se non ne ha.</summary>
    private static int Profondita(DeckButton b)
    {
        if (!b.Cartella) return 0;
        int giu = 0;
        foreach (var f in b.Dentro) giu = Math.Max(giu, Profondita(f));
        return giu + 1;
    }

    private string DescriviCartella(IReadOnlyList<int> dove)
    {
        var strada = deck.Cartelle(dove).Select(Nome).ToList();
        return strada.Count == 0 ? "« Deck »" : "« " + string.Join(" › ", strada) + " »";
    }

    // ---- copia, taglia, incolla, impacchetta ----

    private void Copia_Click(object sender, RoutedEventArgs e) => Copia(false);

    private void Taglia_Click(object sender, RoutedEventArgs e) => Copia(true);

    private void Copia(bool taglia)
    {
        var b = PulsanteScelto();
        if (b is null) return;

        appunti = b.Copia();
        if (taglia)
        {
            Ricorda();
            int i = Scelto;
            Correnti.RemoveAt(i);
            RiempiElencoPulsanti(Math.Min(i, Correnti.Count - 1));
            AggiornaGriglia();
            Modificato();
        }

        Registra(taglia
            ? $"'{Nome(b)}' tagliato: incollalo dove lo vuoi con Ctrl+V."
            : $"'{Nome(b)}' copiato.");
    }

    private void Incolla_Click(object sender, RoutedEventArgs e) => Incolla();

    private void Incolla()
    {
        if (appunti is null) return;

        int piani = percorso.Count + Profondita(appunti);
        if (piani > DeckConfig.Piani)
        {
            Registra($"Qui dentro farebbe {piani} piani di cartelle, e il massimo e' {DeckConfig.Piani}.", true);
            return;
        }

        Ricorda();
        var copia = appunti.Copia();
        RibattezzaAlbero(copia, copia.Id);

        var lista = Correnti;
        int posto = Scelto >= 0 ? Scelto + 1 : lista.Count;
        lista.Insert(posto, copia);

        RiempiElencoPulsanti(posto);
        AggiornaGriglia();
        Modificato();
        Registra($"'{Nome(copia)}' incollato in {DescriviCartella(percorso)}.");
    }

    /// <summary>
    /// Prende il pulsante scelto e lo mette dentro una cartella nuova, al suo
    /// posto. E' il modo in cui una cartella nasce davvero: non si decide di
    /// farne una e poi si cerca cosa metterci, si ha qualcosa da mettere via.
    /// </summary>
    private void ImpacchettaInCartella_Click(object sender, RoutedEventArgs e)
    {
        var b = PulsanteScelto();
        if (b is null) return;

        int piani = percorso.Count + 1 + Profondita(b);
        if (piani > DeckConfig.Piani)
        {
            Registra($"Verrebbero {piani} piani di cartelle, e il massimo e' {DeckConfig.Piani}.", true);
            return;
        }

        Ricorda();
        int i = Scelto;
        var lista = Correnti;
        var cartella = new DeckButton
        {
            Id = IdLibero("cartella"),
            Label = "Cartella",
            Glyph = GlifoCartella,
            Color = Colori[1],
            Action = new ActionSpec { Type = "cartella" },
            Buttons = new List<DeckButton> { b },
        };
        lista[i] = cartella;

        RiempiElencoPulsanti(i);
        AggiornaGriglia();
        Modificato();
        Registra($"'{Nome(b)}' e' finito in una cartella nuova. Dalle un nome.");
        CampoEtichetta.Focus();
        CampoEtichetta.SelectAll();
    }

    // ---- il menu del tasto destro ----

    private void AltriComandi_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button pulsante) return;
        MenuPulsante.PlacementTarget = pulsante;
        MenuPulsante.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MenuPulsante.IsOpen = true;
    }

    /// <summary>
    /// Lo stesso menu del tasto destro dell'elenco, aperto sotto il puntatore
    /// dentro un'anteprima. Il menu vive nell'elenco a sinistra ma i comandi
    /// sono quelli del pulsante scelto, e quale sia il pulsante scelto l'ha
    /// gia' deciso chi chiama.
    /// </summary>
    private void ApriMenuPulsante(UIElement dove)
    {
        MenuPulsante.PlacementTarget = dove;
        MenuPulsante.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        MenuPulsante.IsOpen = true;
    }

    /// <summary>
    /// Il menu si accende e si spegne secondo quel che si puo' fare adesso: una
    /// voce che c'e' sempre e a volte non fa niente e' peggio di una voce
    /// spenta, perche' non dice perche'.
    /// </summary>
    private void MenuPulsante_Opened(object sender, RoutedEventArgs e)
    {
        var b = PulsanteScelto();
        foreach (var voce in MenuPulsante.Items.OfType<MenuItem>())
            voce.IsEnabled = b is not null;

        VoceApri.IsEnabled = b is { Cartella: true };
        VoceProva.IsEnabled = b is { Cartella: false };
        VoceIncolla.IsEnabled = appunti is not null;
        VoceSposta.IsEnabled = b is not null;

        MontaMenuSposta(b);
    }

    /// <summary>
    /// « Sposta in » elencato per esteso: tutte le cartelle del deck, con il
    /// loro percorso. Il trascinamento e' piu' veloce ma vuole la mira e la
    /// cartella davanti agli occhi; questo funziona sempre, anche verso una
    /// cartella che sta tre pagine piu' in la'.
    /// </summary>
    private void MontaMenuSposta(DeckButton? b)
    {
        VoceSposta.Items.Clear();
        if (b is null) return;

        foreach (var (dove, nome) in Destinazioni(b))
        {
            var voce = new MenuItem { Header = nome };
            var arrivo = dove;
            voce.Click += (_, _) => Porta(percorso.ToArray(), Scelto, arrivo, int.MaxValue);
            VoceSposta.Items.Add(voce);
        }

        if (VoceSposta.Items.Count == 0)
            VoceSposta.Items.Add(new MenuItem { Header = "Non c'e' nessun altro posto", IsEnabled = false });
    }

    /// <summary>
    /// Dove si puo' portare un pulsante: la radice e ogni cartella, tolte
    /// quella in cui sta gia', se stessa e quelle che ha dentro.
    /// </summary>
    private List<(int[] Dove, string Nome)> Destinazioni(DeckButton b)
    {
        var posti = new List<(int[], string)>();
        Guarda(deck.Buttons, Array.Empty<int>(), "Deck");
        return posti;

        void Guarda(List<DeckButton> lista, int[] dove, string nome)
        {
            bool eDiCasa = dove.SequenceEqual(percorso);
            if (!eDiCasa && dove.Length + Profondita(b) <= DeckConfig.Piani)
                posti.Add((dove, nome));

            for (int i = 0; i < lista.Count; i++)
            {
                if (!lista[i].Cartella || ReferenceEquals(lista[i], b)) continue;
                Guarda(lista[i].Dentro, dove.Append(i).ToArray(), nome + " › " + Nome(lista[i]));
            }
        }
    }

    // ---- passi della sequenza ----

    /// <summary>
    /// La sequenza aperta dentro il pulsante scelto. Vuoto vuol dire quella
    /// del pulsante; ogni numero e' un passo « Sequenza » in cui si e' entrati.
    ///
    /// Una sequenza dentro un'altra e' un gruppo di passi con le sue
    /// ripetizioni — « questi tre, cinque volte » — e mostrarle tutte insieme
    /// vorrebbe dire disegnare un albero dentro un elenco. Ci si entra invece
    /// come nelle cartelle, e a dire dove si e' ci pensano le briciole sopra
    /// l'elenco.
    ///
    /// E' una fila di indici e non di riferimenti per la stessa ragione del
    /// percorso fra le cartelle: un « Annulla » rimette in piedi un albero
    /// nuovo di zecca, e i riferimenti di prima punterebbero a passi che non
    /// stanno piu' dentro nessuna sequenza.
    /// </summary>
    private readonly List<int> percorsoPassi = new();

    /// <summary>I passi copiati o tagliati, in attesa di essere incollati.</summary>
    private List<ActionSpec> appuntiPassi = new();

    private static bool Sequenza(ActionSpec? azione) =>
        azione is not null && Azioni.Di(azione.Type).Codice == "seq";

    /// <summary>
    /// Accorcia il percorso fino a dove regge davvero. Un passo tolto — o
    /// cambiato di tipo — mentre ci si stava dentro lascerebbe altrimenti la
    /// finestra dentro una sequenza che non esiste piu'.
    /// </summary>
    private void SistemaPercorsoPassi()
    {
        var buono = new List<int>();
        var corrente = PulsanteScelto()?.Action;

        if (Sequenza(corrente))
        {
            foreach (int i in percorsoPassi)
            {
                var passi = corrente!.Steps;
                if (passi is null || i < 0 || i >= passi.Count || !Sequenza(passi[i])) break;
                buono.Add(i);
                corrente = passi[i];
            }
        }

        if (buono.SequenceEqual(percorsoPassi)) return;
        percorsoPassi.Clear();
        percorsoPassi.AddRange(buono);
    }

    /// <summary>La sequenza di cui si stanno guardando i passi, o null.</summary>
    private ActionSpec? SeqAperta()
    {
        SistemaPercorsoPassi();

        var corrente = PulsanteScelto()?.Action;
        if (!Sequenza(corrente)) return null;

        foreach (int i in percorsoPassi) corrente = corrente!.Steps![i];
        return corrente;
    }

    private List<ActionSpec> Passi()
    {
        var seq = SeqAperta();
        if (seq is null) return new List<ActionSpec>();
        return seq.Steps ??= new List<ActionSpec>();
    }

    /// <summary>Il passo scelto, quando ce n'e' esattamente uno.</summary>
    private ActionSpec? PassoScelto()
    {
        var scelti = IndiciPassi();
        if (scelti.Count != 1) return null;
        var passi = Passi();
        int i = scelti[0];
        return i >= 0 && i < passi.Count ? passi[i] : null;
    }

    /// <summary>Le righe scelte nell'elenco dei passi, in ordine.</summary>
    private List<int> IndiciPassi()
    {
        var indici = new List<int>();
        foreach (var voce in ElencoPassi.SelectedItems)
        {
            int i = ElencoPassi.Items.IndexOf(voce);
            if (i >= 0) indici.Add(i);
        }
        indici.Sort();
        return indici;
    }

    /// <summary>
    /// Una riga dell'elenco dei passi: il numero, cosa fa, e il segno di quel
    /// che non va. Un passo sbagliato sul tablet non fa niente e non lo dice:
    /// e' qui che deve vedersi.
    /// </summary>
    private UIElement VocePasso(int numero, ActionSpec passo)
    {
        var riga = new Grid();
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var conto = new TextBlock
        {
            Text = numero.ToString(),
            Width = 22,
            TextAlignment = TextAlignment.Right,
            FontFamily = new FontFamily("Consolas"),
            Foreground = (Brush)FindResource("TestoTenue"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(conto, 0);
        riga.Children.Add(conto);

        var cosa = new TextBlock
        {
            Text = Azioni.Riassunto(passo),
            Margin = new Thickness(10, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(cosa, 1);
        riga.Children.Add(cosa);

        var coda = new StackPanel { Orientation = Orientation.Horizontal };
        string male = Azioni.Controlla(passo);
        if (male.Length > 0)
        {
            coda.Children.Add(new TextBlock
            {
                Text = "●",
                FontSize = 9,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("Attenzione"),
                ToolTip = male,
            });
        }
        if (Sequenza(passo))
        {
            coda.Children.Add(new TextBlock
            {
                Text = "›",
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TestoTenue"),
                ToolTip = "Doppio clic per entrarci",
            });
        }
        Grid.SetColumn(coda, 2);
        riga.Children.Add(coda);

        return riga;
    }

    private void RiempiPassi(int scegli) =>
        RiempiPassi(scegli >= 0 ? new[] { scegli } : Array.Empty<int>());

    private void RiempiPassi(IReadOnlyList<int> scegli)
    {
        bool prima = caricamento;
        caricamento = true;

        ElencoPassi.Items.Clear();
        var passi = TipoAzioneScelto().Codice == "seq" ? Passi() : new List<ActionSpec>();
        for (int i = 0; i < passi.Count; i++) ElencoPassi.Items.Add(VocePasso(i + 1, passi[i]));

        ElencoPassi.SelectedItems.Clear();
        foreach (int i in scegli)
            if (i >= 0 && i < passi.Count) ElencoPassi.SelectedItems.Add(ElencoPassi.Items[i]);

        caricamento = prima;
        AggiornaBricolePassi();
        MostraPasso();
        AggiornaSequenza();
    }

    /// <summary>
    /// Le briciole della sequenza: « Passi › 3. Sequenza › … ». L'ultima e'
    /// spenta perche' e' dove si sta gia'; le altre riportano su di un piano.
    /// </summary>
    private void AggiornaBricolePassi()
    {
        BricolePassi.Children.Clear();

        var strada = new List<string>();
        var corrente = PulsanteScelto()?.Action;
        if (Sequenza(corrente))
        {
            foreach (int i in percorsoPassi)
            {
                var passo = corrente!.Steps![i];
                int quanti = passo.Steps?.Count ?? 0;
                strada.Add($"{i + 1}. {(quanti == 1 ? "1 passo" : quanti + " passi")}");
                corrente = passo;
            }
        }

        // Con la sequenza del pulsante e basta non c'e' nessuna strada da
        // raccontare: una briciola sola direbbe solo « sei dove sei ».
        if (strada.Count == 0) return;

        AggiungiBricolaPasso("Passi", 0, false);
        for (int i = 0; i < strada.Count; i++)
        {
            BricolePassi.Children.Add(new TextBlock
            {
                Text = "›",
                Margin = new Thickness(1, 3, 1, 0),
                Foreground = (Brush)FindResource("TestoTenue"),
                FontSize = 12,
            });
            AggiungiBricolaPasso(strada[i], i + 1, i == strada.Count - 1);
        }
    }

    private void AggiungiBricolaPasso(string testo, int quanti, bool ultima)
    {
        var pezzo = new Button
        {
            Content = testo,
            Style = (Style)FindResource("Briciola"),
            IsEnabled = !ultima,
            ToolTip = ultima ? null : "Torna su di un piano",
        };
        pezzo.Click += (_, _) =>
        {
            int venivoDa = percorsoPassi.Count > quanti ? percorsoPassi[quanti] : -1;
            percorsoPassi.RemoveRange(quanti, percorsoPassi.Count - quanti);
            RiempiPassi(venivoDa);
        };
        BricolePassi.Children.Add(pezzo);
    }

    /// <summary>
    /// Le ripetizioni e la durata stimata della sequenza aperta. Dodici passi
    /// con mezzo secondo d'attesa fanno sei secondi in cui il PC va per conto
    /// suo: e' bene saperlo mentre li si scrive, non premendo il pulsante.
    /// </summary>
    private void AggiornaSequenza()
    {
        var seq = SeqAperta();
        int volte = Math.Clamp(seq?.Ripeti ?? 1, 1, 50);
        TestoRipeti.Text = volte.ToString();

        int ms = Azioni.DurataStimata(seq);
        string durata = ms == 0
            ? (volte == 1 ? "volta" : "volte")
            : $"{(volte == 1 ? "volta" : "volte")} · durata stimata {Azioni.Durata(ms)}";

        // Dentro un gruppo la durata e' quella del gruppo, non del pulsante: se
        // non lo si dice, il numero sembra sbagliato.
        TestoDurata.Text = percorsoPassi.Count > 0 ? durata + " (questo gruppo)" : durata;

        var scelti = IndiciPassi();
        int quanti = Passi().Count;
        BtnApriPasso.IsEnabled = PassoScelto() is { } solo && Sequenza(solo);
        BtnRaggruppa.IsEnabled = scelti.Count > 0;
        BtnIncollaPasso.IsEnabled = appuntiPassi.Count > 0;
        BtnProvaPasso.IsEnabled = PassoScelto() is not null;
        BtnTogliPasso.IsEnabled = scelti.Count > 0;
        BtnDuplicaPasso.IsEnabled = scelti.Count > 0;
        BtnCopiaPasso.IsEnabled = scelti.Count > 0;
        BtnSuPasso.IsEnabled = scelti.Count > 0 && scelti[0] > 0;
        BtnGiuPasso.IsEnabled = scelti.Count > 0 && scelti[^1] < quanti - 1;

        TestoPassiScelti.Text = scelti.Count > 1 ? $"{scelti.Count} passi scelti" : "";
    }

    private void RipetiSu_Click(object sender, RoutedEventArgs e) => CambiaRipeti(+1);

    private void RipetiGiu_Click(object sender, RoutedEventArgs e) => CambiaRipeti(-1);

    private void CambiaRipeti(int passo)
    {
        var seq = SeqAperta();
        if (seq is null) return;

        int volte = Math.Clamp(Math.Clamp(seq.Ripeti, 1, 50) + passo, 1, 50);
        if (volte == seq.Ripeti) return;

        Ricorda("ripeti");
        seq.Ripeti = volte;
        AggiornaSequenza();
        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        Modificato();
    }

    private void ElencoPassi_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento) return;
        MostraPasso();
        AggiornaSequenza();
    }

    /// <summary>Doppio clic su un passo « Sequenza »: ci si entra dentro.</summary>
    private void ElencoPassi_DoppioClic(object sender, MouseButtonEventArgs e)
    {
        if (PassoScelto() is { } passo && Sequenza(passo)) EntraNelPasso();
    }

    private void ApriPasso_Click(object sender, RoutedEventArgs e) => EntraNelPasso();

    private void EntraNelPasso()
    {
        var scelti = IndiciPassi();
        if (scelti.Count != 1) return;

        var passi = Passi();
        int i = scelti[0];
        if (i < 0 || i >= passi.Count || !Sequenza(passi[i])) return;

        percorsoPassi.Add(i);
        RiempiPassi(passi[i].Steps is { Count: > 0 } ? 0 : -1);
    }

    /// <summary>
    /// Le scorciatoie dell'elenco dei passi. Canc, Ctrl+D, Ctrl+C, Ctrl+X,
    /// Ctrl+V e Invio fanno qui quel che fanno nell'elenco dei pulsanti;
    /// Backspace risale di un piano, come nelle cartelle.
    /// </summary>
    private void ElencoPassi_Tasto(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        switch (e.Key)
        {
            case Key.Delete when !ctrl:
                TogliPassi();
                break;
            case Key.Enter:
                if (PassoScelto() is { } passo && Sequenza(passo)) EntraNelPasso();
                else CampoValorePasso.Focus();
                break;
            case Key.Back when percorsoPassi.Count > 0:
                int venivoDa = percorsoPassi[^1];
                percorsoPassi.RemoveAt(percorsoPassi.Count - 1);
                RiempiPassi(venivoDa);
                break;
            case Key.D when ctrl:
                DuplicaPassi();
                break;
            case Key.C when ctrl:
                CopiaPassi(false);
                break;
            case Key.X when ctrl:
                CopiaPassi(true);
                break;
            case Key.V when ctrl:
                IncollaPassi();
                break;
            case Key.A when ctrl:
                ElencoPassi.SelectAll();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void MostraPasso()
    {
        var passo = PassoScelto();
        PannelloPasso.IsEnabled = passo is not null;
        PannelloPasso.Visibility = passo is null ? Visibility.Collapsed : Visibility.Visible;
        if (passo is null) return;

        bool prima = caricamento;
        caricamento = true;

        var tipo = Azioni.Di(passo.Type);
        int indice = Array.FindIndex(Azioni.PerPasso, t => t.Codice == tipo.Codice);
        ElencoTipiPasso.SelectedIndex = Math.Max(0, indice);
        CampoValorePasso.Text = passo.Value;
        CampoArgomentiPasso.Text = passo.Args;

        caricamento = prima;
        AggiornaFormaPasso();
    }

    private void DuplicaPasso_Click(object sender, RoutedEventArgs e) => DuplicaPassi();

    /// <summary>
    /// Duplica i passi scelti, tutti insieme e subito dopo l'ultimo: duplicare
    /// tre passi uno per volta li mescolerebbe con gli originali.
    /// </summary>
    private void DuplicaPassi()
    {
        var scelti = IndiciPassi();
        if (scelti.Count == 0) return;

        var passi = Passi();
        if (scelti[^1] >= passi.Count) return;

        Ricorda();
        int dove = scelti[^1] + 1;
        var copie = scelti.Select(i => passi[i].Copia()).ToList();
        passi.InsertRange(dove, copie);

        RiempiPassi(Enumerable.Range(dove, copie.Count).ToList());
        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        Modificato();
    }

    private void CopiaPasso_Click(object sender, RoutedEventArgs e) => CopiaPassi(false);

    private void TagliaPasso_Click(object sender, RoutedEventArgs e) => CopiaPassi(true);

    private void CopiaPassi(bool taglia)
    {
        var scelti = IndiciPassi();
        if (scelti.Count == 0) return;

        var passi = Passi();
        appuntiPassi = scelti.Where(i => i < passi.Count).Select(i => passi[i].Copia()).ToList();
        if (appuntiPassi.Count == 0) return;

        if (taglia)
        {
            Ricorda();
            for (int k = scelti.Count - 1; k >= 0; k--)
                if (scelti[k] < passi.Count) passi.RemoveAt(scelti[k]);

            RiempiPassi(Math.Min(scelti[0], passi.Count - 1));
            RinfrescaVoce();
            MostraControllo(PulsanteScelto());
            Modificato();
        }

        AggiornaSequenza();
        Registra(appuntiPassi.Count == 1
            ? (taglia ? "Passo tagliato: incollalo dove lo vuoi con Ctrl+V." : "Passo copiato.")
            : $"{appuntiPassi.Count} passi {(taglia ? "tagliati" : "copiati")}.");
    }

    private void IncollaPasso_Click(object sender, RoutedEventArgs e) => IncollaPassi();

    /// <summary>
    /// Incolla i passi copiati dopo quello scelto. Funzionano anche fra un
    /// pulsante e l'altro: e' il modo in cui una macro provata su un pulsante
    /// finisce dentro le altre.
    /// </summary>
    private void IncollaPassi()
    {
        if (appuntiPassi.Count == 0) return;
        var seq = SeqAperta();
        if (seq is null) return;

        int piani = percorsoPassi.Count + 1 + appuntiPassi.Max(Azioni.Profondita);
        if (piani > Azioni.Annidamento)
        {
            Registra($"Qui dentro farebbero {piani} sequenze una dentro l'altra, e il massimo e' "
                     + $"{Azioni.Annidamento}.", true);
            return;
        }

        Ricorda();
        var passi = Passi();
        var scelti = IndiciPassi();
        int dove = scelti.Count > 0 ? scelti[^1] + 1 : passi.Count;
        var copie = appuntiPassi.Select(x => x.Copia()).ToList();
        passi.InsertRange(dove, copie);

        RiempiPassi(Enumerable.Range(dove, copie.Count).ToList());
        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        Modificato();
        Registra(copie.Count == 1 ? "Passo incollato." : $"{copie.Count} passi incollati.");
    }

    /// <summary>
    /// Prende i passi scelti e li mette dentro una sequenza nuova, al loro
    /// posto. E' cosi' che nasce un gruppo: non si decide di farne uno e poi si
    /// cerca cosa metterci — si hanno tre passi da ripetere cinque volte.
    /// </summary>
    private void Raggruppa_Click(object sender, RoutedEventArgs e)
    {
        var scelti = IndiciPassi();
        if (scelti.Count == 0) return;

        var passi = Passi();
        if (scelti[^1] >= passi.Count) return;

        int piani = percorsoPassi.Count + 2 + scelti.Max(i => Azioni.Profondita(passi[i]));
        if (piani > Azioni.Annidamento)
        {
            Registra($"Verrebbero {piani} sequenze una dentro l'altra, e il massimo e' "
                     + $"{Azioni.Annidamento}.", true);
            return;
        }

        Ricorda();
        int dove = scelti[0];
        var dentro = scelti.Select(i => passi[i]).ToList();
        for (int k = scelti.Count - 1; k >= 0; k--) passi.RemoveAt(scelti[k]);
        passi.Insert(dove, new ActionSpec { Type = "seq", Ripeti = 1, Steps = dentro });

        RiempiPassi(dove);
        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        Modificato();
        Registra(dentro.Count == 1
            ? "Il passo e' finito dentro una sequenza sua: aprila per dargli delle ripetizioni."
            : $"{dentro.Count} passi in un gruppo solo. Aprilo per dirgli quante volte ripeterli.");
    }

    private TipoAzione TipoPassoScelto()
    {
        int i = Math.Max(0, ElencoTipiPasso.SelectedIndex);
        var elenco = Azioni.PerPasso;
        return elenco[Math.Min(i, elenco.Length - 1)];
    }

    private void AggiornaFormaPasso()
    {
        var tipo = TipoPassoScelto();
        EtichettaValorePasso.Text = tipo.EtichettaValore;
        AiutoPasso.Text = tipo.Aiuto;
        BtnSfogliaPasso.Visibility = tipo.Codice == "run" ? Visibility.Visible : Visibility.Collapsed;

        // Una sequenza annidata non ha un valore da riempire: ha dei passi, e
        // quelli si guardano entrandoci.
        bool sequenza = tipo.Codice == "seq";
        EtichettaValorePasso.Visibility = sequenza ? Visibility.Collapsed : Visibility.Visible;

        // Come per il pulsante intero: i profili non stanno nella tabella delle
        // azioni, cambiano mentre il programma gira, ma si scelgono lo stesso
        // da un elenco invece di scriverli a memoria.
        var scelte = tipo.Codice == "profilo"
            ? deckFile.Profili.Select(x => x.Nome).ToArray()
            : tipo.Scelte;

        bool daElenco = scelte is not null;
        CampoValorePasso.Visibility = daElenco || sequenza ? Visibility.Collapsed : Visibility.Visible;
        ElencoSceltePasso.Visibility = daElenco ? Visibility.Visible : Visibility.Collapsed;
        if (daElenco) RiempiScelte(ElencoSceltePasso, scelte, CampoValorePasso.Text);

        BtnCatturaPasso.Visibility = tipo.Cattura ? Visibility.Visible : Visibility.Collapsed;
        if (!tipo.Cattura) BtnCatturaPasso.IsChecked = false;

        var mostra = UsaArgomenti(tipo, CampoValorePasso.Text) ? Visibility.Visible : Visibility.Collapsed;
        EtichettaArgomentiPasso.Visibility = mostra;
        CampoArgomentiPasso.Visibility = mostra;
        ElencoArgomentiPasso.Visibility = Visibility.Collapsed;
        if (mostra == Visibility.Visible)
            FormaArgomento(tipo, CampoValorePasso.Text, EtichettaArgomentiPasso, EtichettaArgomentiPasso,
                           CampoArgomentiPasso, ElencoArgomentiPasso, CollegamentoPasso);
        else
            CollegamentoPasso.Mostra(tipo.Codice);
    }

    private void TipoPasso_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!caricamento) Ricorda("tipopasso");
        AggiornaFormaPasso();
        LeggiPasso();
    }

    private void CampoPasso_Changed(object sender, TextChangedEventArgs e)
    {
        if (caricamento) return;
        Ricorda(sender is TextBox campo ? "campo:" + campo.Name : "campo");
        LeggiPasso();
    }

    private void LeggiPasso()
    {
        if (caricamento) return;
        var passo = PassoScelto();
        if (passo is null) return;

        var tipo = TipoPassoScelto();

        // Diventando una sequenza il passo si porta dietro i suoi passi, se ne
        // aveva gia': cambiare tipo due volte non deve buttare via niente.
        if (tipo.Codice == "seq") passo.Steps ??= new List<ActionSpec>();

        passo.Type = tipo.Codice;
        passo.Value = tipo.Codice == "seq" ? "" : CampoValorePasso.Text;
        passo.Args = UsaArgomenti(tipo, CampoValorePasso.Text) ? CampoArgomentiPasso.Text : "";

        int sel = IndiciPassi().FirstOrDefault(-1);
        if (sel >= 0 && sel < ElencoPassi.Items.Count)
        {
            bool prima = caricamento;
            caricamento = true;
            ElencoPassi.Items[sel] = VocePasso(sel + 1, passo);
            ElencoPassi.SelectedItems.Clear();
            ElencoPassi.SelectedItems.Add(ElencoPassi.Items[sel]);
            caricamento = prima;
        }

        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        AggiornaSequenza();
        Modificato();
    }

    private void AggiungiPasso_Click(object sender, RoutedEventArgs e)
    {
        if (SeqAperta() is null) return;
        Ricorda();

        var passi = Passi();
        var scelti = IndiciPassi();
        int dove = scelti.Count > 0 ? scelti[^1] + 1 : passi.Count;
        passi.Insert(dove, new ActionSpec { Type = "hotkey", Value = "" });

        RiempiPassi(dove);
        RinfrescaVoce();
        Modificato();
        CampoValorePasso.Focus();
    }

    /// <summary>Una sequenza vuota in fondo: il gruppo si riempie entrandoci.</summary>
    private void AggiungiGruppo_Click(object sender, RoutedEventArgs e)
    {
        if (SeqAperta() is null) return;

        int piani = percorsoPassi.Count + 2;
        if (piani > Azioni.Annidamento)
        {
            Registra($"Verrebbero {piani} sequenze una dentro l'altra, e il massimo e' "
                     + $"{Azioni.Annidamento}.", true);
            return;
        }

        Ricorda();
        var passi = Passi();
        var scelti = IndiciPassi();
        int dove = scelti.Count > 0 ? scelti[^1] + 1 : passi.Count;
        passi.Insert(dove, new ActionSpec { Type = "seq", Ripeti = 1, Steps = new List<ActionSpec>() });

        RiempiPassi(dove);
        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        Modificato();
    }

    private void TogliPasso_Click(object sender, RoutedEventArgs e) => TogliPassi();

    private void TogliPassi()
    {
        var scelti = IndiciPassi();
        if (scelti.Count == 0) return;

        var passi = Passi();

        // Un gruppo si porta via i passi che ha dentro, e quelli da qui non si
        // vedono: si contano prima di toglierlo.
        int dentro = scelti.Where(i => i < passi.Count)
                           .Sum(i => Azioni.Ovunque(passi[i]).Count() - 1);
        if (dentro > 0)
        {
            var scelta = MessageBox.Show(this,
                $"Fra i passi scelti c'e' un gruppo con dentro {(dentro == 1 ? "un altro passo" : dentro + " altri passi")}.\n"
                + "Togliendolo vanno via anche quelli.\n\nProcedo?",
                "TabDeck", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (scelta != MessageBoxResult.OK) return;
        }

        Ricorda();
        for (int k = scelti.Count - 1; k >= 0; k--)
            if (scelti[k] < passi.Count) passi.RemoveAt(scelti[k]);

        RiempiPassi(Math.Min(scelti[0], passi.Count - 1));
        RinfrescaVoce();
        MostraControllo(PulsanteScelto());
        Modificato();
    }

    private void SuPasso_Click(object sender, RoutedEventArgs e) => SpostaPassi(-1);

    private void GiuPasso_Click(object sender, RoutedEventArgs e) => SpostaPassi(+1);

    /// <summary>
    /// Sposta di un posto i passi scelti, tenendoli insieme. Tre passi scelti
    /// che si spostano uno per volta si sparpaglierebbero.
    /// </summary>
    private void SpostaPassi(int verso)
    {
        var scelti = IndiciPassi();
        if (scelti.Count == 0) return;

        var passi = Passi();
        if (verso < 0 && scelti[0] == 0) return;
        if (verso > 0 && scelti[^1] >= passi.Count - 1) return;

        Ricorda();
        var presi = scelti.Select(i => passi[i]).ToList();
        for (int k = scelti.Count - 1; k >= 0; k--) passi.RemoveAt(scelti[k]);

        int dove = Math.Clamp(scelti[0] + verso, 0, passi.Count);
        passi.InsertRange(dove, presi);

        RiempiPassi(Enumerable.Range(dove, presi.Count).ToList());
        RinfrescaVoce();
        Modificato();
    }

    private void SfogliaPasso_Click(object sender, RoutedEventArgs e)
    {
        string? scelto = ScegliProgramma();
        if (scelto is null) return;
        Ricorda();
        CampoValorePasso.Text = scelto;
    }

    // ---- griglia e anteprima ----

    private void ColonneSu_Click(object sender, RoutedEventArgs e) => CambiaGriglia(+1, 0);

    private void ColonneGiu_Click(object sender, RoutedEventArgs e) => CambiaGriglia(-1, 0);

    private void RigheSu_Click(object sender, RoutedEventArgs e) => CambiaGriglia(0, +1);

    private void RigheGiu_Click(object sender, RoutedEventArgs e) => CambiaGriglia(0, -1);

    private void CambiaGriglia(int colonne, int righe)
    {
        int c = Math.Clamp(deck.Cols + colonne, 1, 12);
        int r = Math.Clamp(deck.Rows + righe, 1, 8);
        if (c == deck.Cols && r == deck.Rows) return;

        Ricorda("griglia");
        deck.Cols = c;
        deck.Rows = r;
        AggiornaGriglia();
        Modificato();
    }

    private void AggiornaGriglia()
    {
        TestoColonne.Text = deck.Cols.ToString();
        TestoRighe.Text = deck.Rows.ToString();
        TestoSfondo.Text = deck.Sfondo.Length > 0 && Icone.Esiste(deck.Sfondo) ? "immagine" : "grafite";

        int perPagina = Math.Max(1, deck.Cols * deck.Rows);
        int quanti = Correnti.Count;
        int pagine = Math.Max(1, (quanti + perPagina - 1) / perPagina);
        string qui = percorso.Count == 0 ? "Nel deck" : "Qui dentro";
        string celle = $"{deck.Cols} × {deck.Rows} = {perPagina} celle";

        TestoCapienza.Text = quanti switch
        {
            0 => $"{celle}. {qui} non c'e' ancora niente.",
            1 => $"{celle}. {qui} c'e' un pulsante solo.",
            _ when pagine == 1 =>
                $"{celle}. {qui} ci sono {quanti} pulsanti: stanno in una pagina sola.",
            _ => $"{celle} per pagina. {qui} i {quanti} pulsanti occupano {pagine} pagine, "
                 + "e sul tablet si passa dall'una all'altra scorrendo di lato.",
        };

        // La griglia vale per tutte le cartelle: e' una sola, ed e' quella del
        // pannello. Cambiarla qui la cambia dappertutto, e va detto.
        if (percorso.Count > 0)
            TestoCapienza.Text += " La griglia e' la stessa in tutte le cartelle.";

        AggiornaTotale();
        AggiornaAnteprima();
    }

    /// <summary>Quanti pulsanti ci sono in tutto: dentro le cartelle non si vedono.</summary>
    private void AggiornaTotale()
    {
        int quanti = deck.Ovunque().Count(b => !b.Cartella);
        int cartelle = deck.Ovunque().Count(b => b.Cartella);

        TestoTotale.Text = cartelle == 0
            ? $"{quanti} pulsanti in tutto"
            : $"{quanti} pulsanti in tutto, in {(cartelle == 1 ? "una cartella" : cartelle + " cartelle")}";
    }

    private void AggiornaAnteprima()
    {
        Anteprima.Mostra(deck, percorso, Scelto);
        TestoPagina.Text = $"Pagina {Anteprima.Pagina + 1} di {Anteprima.Pagine}";

        // Le altre due non hanno un pulsante scelto: non si sta montando
        // niente, si sta guardando com'e' adesso.
        AnteprimaViva.Mostra(deck, -1);
        DeckVivo.Mostra(deck, percorsoVivo, -1);
        AggiornaPagineDeck();
    }

    /// <summary>
    /// Le frecce del deck grande. Compaiono solo se c'e' dove andare: con una
    /// pagina sola sarebbero due pulsanti che non fanno niente.
    /// </summary>
    private void AggiornaPagineDeck()
    {
        RigaPagineDeck.Visibility = DeckVivo.Pagine > 1 ? Visibility.Visible : Visibility.Collapsed;
        TestoPaginaDeck.Text = $"Pagina {DeckVivo.Pagina + 1} di {DeckVivo.Pagine}";
    }

    private void PaginaDeckSu_Click(object sender, RoutedEventArgs e)
    {
        DeckVivo.Pagina++;
        AggiornaPagineDeck();
    }

    private void PaginaDeckGiu_Click(object sender, RoutedEventArgs e)
    {
        DeckVivo.Pagina--;
        AggiornaPagineDeck();
    }

    private void PaginaSu_Click(object sender, RoutedEventArgs e)
    {
        Anteprima.Pagina++;
        TestoPagina.Text = $"Pagina {Anteprima.Pagina + 1} di {Anteprima.Pagine}";
    }

    private void PaginaGiu_Click(object sender, RoutedEventArgs e)
    {
        Anteprima.Pagina--;
        TestoPagina.Text = $"Pagina {Anteprima.Pagina + 1} di {Anteprima.Pagine}";
    }

    // ---- passi indietro ----

    /// <summary>
    /// Mette da parte com'e' il deck adesso, prima di cambiarlo.
    ///
    /// Il motivo serve a non prendere cinquanta fotografie mentre si scrive un
    /// nome: due modifiche dello stesso genere, a meno di un secondo e mezzo
    /// l'una dall'altra, sono un gesto solo. Senza motivo — le cose che si
    /// fanno una per volta, aggiungere, togliere, spostare — la fotografia si
    /// prende sempre.
    /// </summary>
    private void Ricorda(string motivo = "")
    {
        long adesso = Environment.TickCount64;
        if (motivo.Length > 0 && motivo == motivoPasso && adesso - quandoPasso < 1500)
        {
            quandoPasso = adesso;
            return;
        }

        motivoPasso = motivo;
        quandoPasso = adesso;

        passato.Add(ConfigFile.Clona(deck));
        if (passato.Count > 50) passato.RemoveAt(0);
        futuro.Clear();
        AggiornaPassi();
    }

    private void Annulla_Click(object sender, RoutedEventArgs e) => Passo(passato, futuro, "Annullato");

    private void Rifai_Click(object sender, RoutedEventArgs e) => Passo(futuro, passato, "Rifatto");

    /// <summary>
    /// Un passo indietro o in avanti: il deck di adesso finisce nella pila
    /// opposta, e quello in cima alla pila scelta prende il suo posto.
    /// </summary>
    private void Passo(List<DeckConfig> da, List<DeckConfig> a, string detto)
    {
        if (da.Count == 0) return;

        a.Add(ConfigFile.Clona(deck));
        deck = da[^1];
        da.RemoveAt(da.Count - 1);

        // La fotografia e' un deck nuovo di zecca, non quello che sta dentro
        // deckFile: senza rimetterlo al suo posto, il salvataggio scriverebbe
        // il profilo di prima e il passo indietro sparirebbe su disco.
        int quale = deckFile.Profili.FindIndex(x =>
            string.Equals(x.Nome, deck.Nome, StringComparison.OrdinalIgnoreCase));
        if (quale >= 0) deckFile.Profili[quale] = deck;

        // La fotografia e' di un albero intero: la cartella che si stava
        // guardando puo' non esserci piu'.
        var dove = deck.PercorsoValido(percorso).ToList();
        percorso.Clear();
        percorso.AddRange(dove);
        percorsoVivo.Clear();

        motivoPasso = "";
        RiempiElencoPulsanti(Correnti.Count > 0 ? 0 : -1);
        AggiornaGriglia();
        Modificato();
        AggiornaPassi();
        Registra($"{detto}. Il deck com'era prima dell'ultima modifica.");
    }

    private void AggiornaPassi()
    {
        BtnAnnulla.IsEnabled = passato.Count > 0;
        BtnRifai.IsEnabled = futuro.Count > 0;
    }

    // ---- provare ----

    private void ProvaPulsante_Click(object sender, RoutedEventArgs e)
    {
        var b = PulsanteScelto();
        if (b is null || b.Cartella || b.Action is null) return;

        // Il conto alla rovescia si scrive dentro il pulsante che e' stato
        // premuto: « Prova » sta in due posti, e vederlo contare nell'altro
        // sarebbe come non vederlo.
        Prova(b.Action, Nome(b), sender as Button ?? BtnProva);
    }

    private void ProvaPasso_Click(object sender, RoutedEventArgs e)
    {
        var passo = PassoScelto();
        if (passo is null) return;
        Prova(passo, $"passo {ElencoPassi.SelectedIndex + 1}", sender as Button);
    }

    /// <summary>
    /// Prova l'azione dopo tre secondi, non subito.
    ///
    /// Una combinazione di tasti finisce nella finestra che ha il fuoco, e nel
    /// momento in cui si preme « Prova » quella finestra e' TabDeck: si
    /// proverebbe sempre e solo su se' stessi. Il conto alla rovescia da' il
    /// tempo di andare sulla finestra a cui la combinazione e' destinata.
    /// </summary>
    private void Prova(ActionSpec azione, string nome, Button? pulsante)
    {
        if (conto is not null)
        {
            AnnullaConto();
            return;
        }

        string male = Azioni.Controlla(azione);
        if (male.Length > 0 && !male.StartsWith("Non trovo", StringComparison.Ordinal))
        {
            Registra($"Non la provo: {male}", true);
            return;
        }

        // OBS e Twitch non guardano quale finestra ha il fuoco: niente attesa.
        if (Azioni.Di(azione.Type).Subito)
        {
            engine.Prova(azione, nome);
            return;
        }

        contoPulsante = pulsante;
        contoTesto = pulsante?.Content as string ?? "";
        contoRimasto = 3;

        conto = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        conto.Tick += (_, _) =>
        {
            contoRimasto--;
            if (contoRimasto > 0)
            {
                if (contoPulsante is not null) contoPulsante.Content = $"Fra {contoRimasto}…";
                return;
            }

            AnnullaConto();
            engine.Prova(azione, nome);
        };

        if (contoPulsante is not null) contoPulsante.Content = $"Fra {contoRimasto}…";
        conto.Start();
        Registra("Vai sulla finestra a cui serve: fra tre secondi l'azione parte.");
    }

    private void AnnullaConto()
    {
        if (conto is null) return;
        conto.Stop();
        conto = null;
        if (contoPulsante is not null) contoPulsante.Content = contoTesto;
        contoPulsante = null;
    }

    // ---- salvare ----

    private void Modificato() => StatoSalvataggio(true);

    private void Salvato() => StatoSalvataggio(false);

    /// <summary>
    /// Lo stesso avviso in due posti: nella pagina Pulsanti e nella riga del
    /// deck grande, dove si dispone. Chi trascina di la' deve sapere che quel
    /// che ha appena spostato non e' ancora andato da nessuna parte.
    /// </summary>
    private void StatoSalvataggio(bool modificato)
    {
        deckModificato = modificato;
        SchedaDeck.Header = modificato ? "Pulsanti ·" : "Pulsanti";

        // Due cose diverse, e la seconda non si vedeva: che il deck sia
        // scritto su disco, e che il tablet ne abbia una copia. Da quando il
        // collegamento non lo rimanda piu' da solo, « salvato » non vuol piu'
        // dire « di la' c'e' questo ».
        string detto = modificato
            ? "Modificato, non ancora salvato."
            : settings.DeckDaMandare
                ? "Salvato, non ancora sul tablet."
                : "Salvato.";
        var tinta = (Brush)FindResource(modificato || settings.DeckDaMandare ? "Attenzione" : "TestoTenue");

        TestoDeckSalvato.Text = detto;
        TestoDeckSalvato.Foreground = tinta;
        TestoDeckVivoSalvato.Text = detto;
        TestoDeckVivoSalvato.Foreground = tinta;
    }

    private void SalvaDeck_Click(object sender, RoutedEventArgs e) => SalvaDeck();

    /// <summary>
    /// Manda al tablet il deck gia' salvato, senza toccare il file.
    ///
    /// Esiste perche' il collegamento non manda piu' niente da solo: ci si
    /// collega, e questo e' il pulsante che dice « adesso prendi questo ». Se
    /// c'e' qualcosa di non salvato si passa dal salvataggio, che manda
    /// comunque: mandare una griglia diversa da quella scritta su disco
    /// vorrebbe dire due deck diversi senza modo di sapere quale sia quello
    /// buono.
    /// </summary>
    private void MandaDeck_Click(object sender, RoutedEventArgs e)
    {
        if (deckModificato)
        {
            SalvaDeck();
            return;
        }

        if (!engine.SendDeck())
        {
            Registra("Non sei collegato al tablet: collegalo e ripremi.", true);
            return;
        }

        DeckDaMandare(false);
        Registra($"Profilo « {deck.Nome} » mandato al tablet.");
    }

    /// <summary>
    /// Segna se quello che sta sul tablet e' ancora questo. Va nel file delle
    /// impostazioni: la domanda sopravvive alla chiusura della finestra, e al
    /// tablet non si puo' chiedere cosa ha.
    /// </summary>
    private void DeckDaMandare(bool si)
    {
        settings.DeckDaMandare = si;
        SalvaImpostazioni();
        StatoSalvataggio(deckModificato);
    }

    /// <param name="manda">
    /// Falso solo mentre si chiude: li' il tablet sta per perdere il
    /// collegamento comunque, e mandargli una griglia sarebbe un saluto.
    /// </param>
    private void SalvaDeck(bool manda = true)
    {
        var pulito = PerIlFile();

        // Si controllano tutti i profili, non solo quello davanti: un pulsante
        // rotto in un altro profilo si scoprirebbe altrimenti soltanto
        // passando di la', che e' il momento peggiore.
        var guai = pulito.Profili
            .SelectMany(profilo => profilo.Ovunque()
                .Select(b => (profilo, b, male: Male(b))))
            .Where(x => x.male.Length > 0)
            .ToList();

        try
        {
            ConfigFile.Save(deckPath, pulito);
        }
        catch (IOException ex)
        {
            Registra($"Deck non salvato: {ex.Message}", true);
            return;
        }

        // Le immagini di pulsanti che non ci sono piu' si buttano solo adesso:
        // finche' il deck non era salvato, un pulsante appena cancellato poteva
        // ancora tornare indietro, e la sua icona doveva essere ancora li'.
        // Contano i pulsanti di tutti i profili: un'icona che qui non serve
        // piu' puo' essere la faccia di un pulsante in un altro deck.
        Icone.Pulisci(pulito.Profili.SelectMany(x => x.Ovunque()).Select(b => b.Icon)
            .Concat(pulito.Profili.Select(x => x.Sfondo)));

        engine.UseDeck(pulito.Attuale());

        bool andato = manda && engine.SendDeck();
        Salvato();
        DeckDaMandare(!andato);

        Registra(andato
            ? "Deck salvato e mandato al tablet."
            : "Deck salvato. Al tablet ci va quando premi « Manda al tablet ».");

        // Il salvataggio non si rifiuta per un'azione incompleta — il deck e'
        // roba di chi lo scrive — ma non lo si lascia neanche scoprire dopo, a
        // dito premuto sul tablet.
        if (guai.Count > 0)
        {
            string dove = deckFile.Profili.Count > 1 ? $" (in « {guai[0].profilo.Nome} »)" : "";
            Registra(guai.Count == 1
                ? $"Attenzione, '{Nome(guai[0].b)}'{dove}: {guai[0].male}"
                : $"Attenzione: {guai.Count} pulsanti hanno qualcosa che non va. Il primo e' "
                  + $"'{Nome(guai[0].b)}'{dove}: {guai[0].male}",
                true);
        }
    }

    private static string Nome(DeckButton b) => b.Label.Length > 0 ? b.Label : b.Id;

    /// <summary>
    /// Cosa non va in un pulsante. E' il controllo di Azioni piu' l'unica cosa
    /// che li' non si puo' sapere: se il profilo che un pulsante nomina esiste
    /// ancora. Un profilo rinominato o buttato via lascia dietro pulsanti che
    /// non portano piu' da nessuna parte, e premendoli non succede niente.
    /// </summary>
    private string Male(DeckButton b)
    {
        string male = Azioni.Controlla(b);
        if (male.Length > 0) return male;

        if (!string.Equals(b.Action?.Type, "profilo", StringComparison.OrdinalIgnoreCase)) return "";

        string nome = b.Action!.Value.Trim();
        return deckFile.Cerca(nome) is null
            ? $"non c'e' nessun profilo che si chiama '{nome}'."
            : "";
    }

    /// <summary>
    /// La copia che finisce su disco: tutti i profili, non solo quello davanti,
    /// perche' il file e' uno solo e li contiene tutti.
    ///
    /// I passi si scrivono solo per le sequenze, il contenuto solo per le
    /// cartelle. In memoria restano anche dopo aver cambiato tipo, cosi' chi
    /// cambia idea due volte ritrova la sua sequenza o la sua cartella; nel
    /// file sarebbero solo confusione.
    /// </summary>
    private DeckFile PerIlFile()
    {
        var copia = new DeckFile { Attivo = deckFile.Attivo };
        foreach (var profilo in deckFile.Profili)
        {
            var pulito = new DeckConfig
            {
                Nome = profilo.Nome,
                Cols = profilo.Cols,
                Rows = profilo.Rows,
            };
            pulito.Buttons.AddRange(profilo.Buttons.Select(Ripulisci));
            copia.Profili.Add(pulito);
        }
        return copia;
    }

    private static DeckButton Ripulisci(DeckButton b)
    {
        var pulito = b.Copia();
        var tipo = Azioni.Di(pulito.Action?.Type);

        RipulisciAzione(pulito.Action);

        if (tipo.Codice == "cartella")
        {
            pulito.Buttons = b.Dentro.Select(Ripulisci).ToList();
        }
        else
        {
            pulito.Buttons = null;
        }

        return pulito;
    }

    /// <summary>
    /// Toglie da un'azione quel che il suo tipo non usa, scendendo dentro le
    /// sequenze annidate. In memoria i passi restano anche dopo aver cambiato
    /// tipo, cosi' chi cambia idea due volte ritrova la sua sequenza; nel file
    /// sarebbero solo confusione.
    /// </summary>
    private static void RipulisciAzione(ActionSpec? azione)
    {
        if (azione is null) return;

        if (Azioni.Di(azione.Type).Codice != "seq")
        {
            azione.Steps = null;
            azione.Ripeti = 1;   // fuori dalle sequenze non vuol dire niente
            return;
        }

        foreach (var passo in azione.Steps ?? new List<ActionSpec>()) RipulisciAzione(passo);
    }
}
