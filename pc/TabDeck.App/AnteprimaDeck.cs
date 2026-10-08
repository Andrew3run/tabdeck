using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WRect = System.Windows.Rect;
using WSize = System.Windows.Size;

namespace TabDeck;

/// <summary>
/// La griglia del deck come la vedra' il tablet, disegnata dentro la finestra.
///
/// Non e' una decorazione: senza, per sapere dove finisce un pulsante bisogna
/// guardare il tablet, e per capire che i pulsanti sono finiti in una seconda
/// pagina bisogna scoprirlo per caso. I numeri — margine, distanza fra le
/// celle, raggio, corpo del glifo e dell'etichetta, l'altezza della fascia
/// della cartella aperta — sono gli stessi di DeckView.java: quello che si vede
/// qui e' quello che esce di la'.
///
/// Il pannello del tablet e' 1024x600, e questa anteprima tiene le stesse
/// proporzioni: una griglia che qui sembra comoda, li' non diventa stretta.
///
/// Dentro le cartelle si entra come sul tablet, premendole; quale sia aperta
/// non lo decide pero' l'anteprima, che si limita a chiederlo. Il percorso —
/// una fila di indici — vive nella finestra, perche' e' la stessa cosa che
/// l'elenco a sinistra deve mostrare: due navigazioni separate che cercano di
/// restare d'accordo sono due navigazioni che prima o poi litigano.
/// </summary>
public sealed class AnteprimaDeck : FrameworkElement
{
    private const double LarghezzaTablet = 1024;
    private const double AltezzaTablet = 600;

    private static readonly Typeface Carattere =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface CarattereLeggero =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Light, FontStretches.Normal);

    // I colori di DeckView.java, gli stessi numeri.
    private static readonly SolidColorBrush ColoreTesto = Brush(0xE6, 0xE7, 0xEA);
    private static readonly SolidColorBrush ColoreTenue = Brush(0x9A, 0x9D, 0xA5);
    private static readonly SolidColorBrush ColoreEtichetta = Velato(0xE0);
    private static readonly SolidColorBrush ColoreSegno = Velato(0xF2);
    private static readonly SolidColorBrush ColoreSegnoCartella = Velato(140);
    private static readonly SolidColorBrush ColoreVuoto = Brush(0x17, 0x18, 0x1C);
    private static readonly SolidColorBrush ColorePozzo = Brush(0x07, 0x08, 0x0A);
    private static readonly SolidColorBrush ColoreChip = Brush(0x26, 0x28, 0x2E);
    private static readonly SolidColorBrush ColorePuntoAcceso = Brush(0xD8, 0xD9, 0xDC);
    private static readonly SolidColorBrush ColorePuntoSpento = Brush(0x3A, 0x3C, 0x42);
    private static readonly SolidColorBrush ColoreVerde = Brush(0x3D, 0xDC, 0x84);
    private static readonly SolidColorBrush ColoreScelto = Brush(0x5B, 0x8C, 0xFF);
    private static readonly SolidColorBrush ColorePredefinito = Brush(0x33, 0x36, 0x3E);
    /// <summary>Il velo sopra lo sfondo: i numeri di DeckView.VELO_SFONDO.</summary>
    private static readonly Brush VeloSfondo = Congela(new LinearGradientBrush(
        Color.FromArgb(0x99, 0x08, 0x09, 0x0A), Color.FromArgb(0x73, 0x08, 0x09, 0x0A), 90));

    private static readonly SolidColorBrush VeloSpento = Congela(new SolidColorBrush(Color.FromArgb(0x8C, 0x08, 0x09, 0x0A)));

    /// <summary>La luce dietro la griglia: il RadialGradient del tablet.</summary>
    private static readonly Brush Fondo = Congela(new RadialGradientBrush(
        Color.FromRgb(0x1B, 0x1D, 0x22), Color.FromRgb(0x0E, 0x0F, 0x12))
    {
        Center = new Point(0.5, 0.4), GradientOrigin = new Point(0.5, 0.4), RadiusX = 0.75, RadiusY = 0.75 * 1024 / 600,
    });

    /// <summary>Il filo di luce in cima e d'ombra in fondo: il « lucido » di DeckView.</summary>
    private static readonly Brush Lucido = Congela(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromArgb(0x30, 255, 255, 255), 0), new(Color.FromArgb(0, 255, 255, 255), 0.035),
        new(Color.FromArgb(0, 0, 0, 0), 0.965), new(Color.FromArgb(0x4D, 0, 0, 0), 1),
    }, 90));

    private static SolidColorBrush Velato(byte alfa) => Congela(new SolidColorBrush(Color.FromArgb(alfa, 255, 255, 255)));

    private static T Congela<T>(T pennello) where T : Freezable
    {
        pennello.Freeze();
        return pennello;
    }

    /// <summary>Se il tablet e' collegato: la fascia lo dice, e a PC spento i tasti si spengono.</summary>
    public bool Collegato
    {
        get => collegato;
        set
        {
            if (collegato == value) return;
            collegato = value;
            InvalidateVisual();
        }
    }
    private bool collegato = true;

    private DeckConfig deck = new();
    private int[] percorso = Array.Empty<int>();
    private int scelto = -1;
    private int pagina;
    private int sorvolato = -1;
    private bool sorvolaFascia;

    /// <summary>Il puntatore e' sul pulsantino « togli » della cella sorvolata.</summary>
    private bool sorvolaCroce;

    // ---- trascinamento ----

    /// <summary>Spostamento oltre il quale il gesto e' un trascinamento e non un clic.</summary>
    private const double SogliaTrascinamento = 6;

    /// <summary>Quanto si resta sul bordo prima che la pagina cambi da sola.</summary>
    private static readonly TimeSpan AttesaBordo = TimeSpan.FromMilliseconds(500);

    /// <summary>Quanto si resta su una cartella prima che si apra da sola.</summary>
    private static readonly TimeSpan AttesaCartella = TimeSpan.FromMilliseconds(650);

    private int trascinaDa = -1;
    private int[] trascinaPercorso = Array.Empty<int>();
    private DeckButton? trascinato;
    private bool trascinando;
    private Point premutoIn;
    private Point puntatore;

    /// <summary>Cella di destinazione dentro la pagina mostrata, -1 se non ce n'e'.</summary>
    private int bersaglio = -1;

    /// <summary>Vero quando il bersaglio e' una cartella e lasciare vuol dire « mettici dentro ».</summary>
    private bool bersaglioDentro;

    private DispatcherTimer? bordo;
    private int versoBordo;

    /// <summary>Il fermarsi su una cartella, o sulla fascia, che apre da solo.</summary>
    private DispatcherTimer? sosta;
    private int sostaSu = -2;

    public AnteprimaDeck()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        // Il pulsante appeso al puntatore non deve uscire dal riquadro
        // dell'anteprima: fuori non c'e' nessuna griglia dove posarlo.
        ClipToBounds = true;
    }

    /// <summary>Indice, nell'elenco mostrato, della cella premuta.</summary>
    public event Action<int>? Premuto;

    /// <summary>Cella vuota premuta: l'indice e' la posizione libera nella griglia.</summary>
    public event Action<int>? PremutoVuoto;

    /// <summary>Si chiede di entrare nella cartella che sta a quell'indice.</summary>
    public event Action<int>? Apri;

    /// <summary>Si chiede di tornare alla cartella che contiene questa.</summary>
    public event Action? Esci;

    /// <summary>
    /// Un pulsante e' stato portato da un posto a un altro. I due percorsi
    /// dicono in quale cartella, i due indici in che posizione dentro di essa.
    ///
    /// Sono indici nell'elenco, non celle: la griglia si riempie in ordine, e
    /// spostare una cella vuol dire spostare una riga dell'elenco.
    /// </summary>
    public event Action<int[], int, int[], int>? Portato;

    /// <summary>
    /// Se i pulsanti si possono trascinare. E' acceso sull'anteprima
    /// dell'editor e sul deck grande mentre si dispone; sul deck grande in modo
    /// normale la pressione esegue l'azione, e riordinare mentre si cerca di
    /// premere sarebbe un guaio.
    /// </summary>
    public bool Riordinabile { get; set; }

    /// <summary>
    /// Se premendo una cartella ci si entra. Spento sull'anteprima piccola
    /// della dashboard, che e' un colpo d'occhio e non una tastiera.
    /// </summary>
    public bool Navigabile { get; set; } = true;

    /// <summary>
    /// Se basta un clic per aprire una cartella, com'e' sul tablet. Nell'editor
    /// no: li' il primo clic sceglie il pulsante — anche una cartella ha un
    /// nome e un colore da cambiare — e ad aprirla e' il doppio clic.
    /// </summary>
    public bool ApriAlPrimoClic { get; set; } = true;

    /// <summary>
    /// Se la griglia si comanda anche da qui: un pulsantino « togli » sulla
    /// cella sorvolata e il menu del tasto destro.
    ///
    /// L'anteprima e' il posto dove si guarda il deck: dover andare
    /// nell'elenco a sinistra per buttare via il pulsante che si ha davanti
    /// agli occhi e' un viaggio che non serve a niente. Il pulsantino compare
    /// solo passandoci sopra, e solo se la cella e' abbastanza grande da
    /// contenerlo senza coprire quel che c'e' disegnato.
    /// </summary>
    public bool Scorciatoie { get; set; }

    /// <summary>Il pulsantino « togli » premuto sulla cella a quell'indice.</summary>
    public event Action<int>? Tolto;

    /// <summary>Tasto destro su una cella piena: si chiede il menu di quel pulsante.</summary>
    public event Action<int>? MenuChiesto;

    /// <summary>Il percorso mostrato: vuoto per la radice.</summary>
    public IReadOnlyList<int> Percorso => percorso;

    /// <summary>I pulsanti disegnati adesso, cioe' il contenuto della cartella aperta.</summary>
    public IReadOnlyList<DeckButton> Contenuto => deck.Contenuto(percorso);

    /// <summary>Vero quando si sta guardando dentro una cartella.</summary>
    public bool Dentro => percorso.Length > 0;

    public int Pagina
    {
        get => pagina;
        set
        {
            int nuova = Math.Clamp(value, 0, Pagine - 1);
            if (nuova == pagina) return;
            pagina = nuova;
            InvalidateVisual();
        }
    }

    public int Pagine
    {
        get
        {
            int perPagina = Math.Max(1, deck.Cols * deck.Rows);
            return Math.Max(1, (Contenuto.Count + perPagina - 1) / perPagina);
        }
    }

    /// <summary>
    /// Rilegge il deck e la cartella aperta. La pagina mostrata segue il
    /// pulsante scelto: chi lo sposta in fondo alla seconda pagina vuole
    /// vederlo dov'e' finito.
    /// </summary>
    public void Mostra(DeckConfig config, IReadOnlyList<int> dove, int selezionato)
    {
        deck = config;
        var nuovo = config.PercorsoValido(dove).ToArray();

        // Cambiando cartella si riparte dalla prima pagina: la terza pagina di
        // quella di prima non vuol dire niente qui dentro.
        if (!nuovo.SequenceEqual(percorso)) pagina = 0;
        percorso = nuovo;

        scelto = selezionato;
        int perPagina = Math.Max(1, deck.Cols * deck.Rows);
        pagina = selezionato >= 0
            ? Math.Clamp(selezionato / perPagina, 0, Pagine - 1)
            : Math.Clamp(pagina, 0, Pagine - 1);
        InvalidateVisual();
    }

    /// <summary>Il deck intero, senza entrare in nessuna cartella.</summary>
    public void Mostra(DeckConfig config, int selezionato) =>
        Mostra(config, Array.Empty<int>(), selezionato);

    protected override WSize MeasureOverride(WSize disponibile)
    {
        double larghezza = double.IsInfinity(disponibile.Width) ? LarghezzaTablet : disponibile.Width;
        double altezza = larghezza * AltezzaTablet / LarghezzaTablet;

        // Se in altezza non ci sta, comanda l'altezza. Le proporzioni del
        // pannello non si toccano — sono tutto il senso di questa anteprima —
        // quindi a cedere e' la larghezza: meglio una griglia piu' stretta che
        // una che spinge fuori dalla finestra quel che le sta sotto.
        if (!double.IsInfinity(disponibile.Height) && disponibile.Height > 0 && altezza > disponibile.Height)
        {
            altezza = disponibile.Height;
            larghezza = altezza * LarghezzaTablet / AltezzaTablet;
        }

        return new WSize(larghezza, altezza);
    }

    // ---- mouse ----

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var punto = e.GetPosition(this);

        // Il tasto lasciato altrove — fuori dall'anteprima, dove il rilascio non
        // arriva mai qui — chiude comunque la partita: senza, il movimento
        // successivo farebbe partire un trascinamento che nessuno ha chiesto.
        if (!trascinando && e.LeftButton == MouseButtonState.Released) trascinaDa = -1;

        // Il trascinamento comincia dopo qualche pixel, non al primo movimento:
        // altrimenti scegliere un pulsante con un clic un po' mosso lo
        // sposterebbe, ed e' il genere di cosa che non si capisce mai.
        if (!trascinando && trascinaDa >= 0 && e.LeftButton == MouseButtonState.Pressed
            && (Math.Abs(punto.X - premutoIn.X) > SogliaTrascinamento
                || Math.Abs(punto.Y - premutoIn.Y) > SogliaTrascinamento))
        {
            trascinando = true;
            sorvolato = -1;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
        }

        if (trascinando)
        {
            puntatore = punto;
            MiraTrascinamento(punto);
            GuardaIlBordo(punto);
            InvalidateVisual();
            return;
        }

        bool fascia = SopraLaFascia(punto);
        int sotto = fascia ? -1 : CellaSotto(punto);
        bool croce = SullaCroce(punto, sotto);
        if (sotto == sorvolato && fascia == sorvolaFascia && croce == sorvolaCroce) return;
        sorvolato = sotto;
        sorvolaFascia = fascia;
        sorvolaCroce = croce;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (trascinando) return;   // col mouse catturato non e' un'uscita vera
        sorvolato = -1;
        sorvolaFascia = false;
        sorvolaCroce = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var punto = e.GetPosition(this);

        if (SopraLaFascia(punto))
        {
            if (Navigabile) Esci?.Invoke();
            return;
        }

        int cella = CellaSotto(punto);
        if (cella < 0) return;

        var lista = Contenuto;
        int indice = pagina * Math.Max(1, deck.Cols * deck.Rows) + cella;

        // Il pulsantino « togli » viene prima: sta dentro la cella, e senza
        // questo controllo premerlo sceglierebbe il pulsante e comincerebbe un
        // trascinamento invece di toglierlo.
        if (SullaCroce(punto, cella))
        {
            Tolto?.Invoke(indice);
            return;
        }

        if (indice >= lista.Count)
        {
            PremutoVuoto?.Invoke(indice);
            return;
        }

        bool cartella = lista[indice].Cartella && Navigabile;
        if (cartella && (ApriAlPrimoClic || e.ClickCount >= 2))
        {
            // Sul doppio clic il primo ha gia' scelto il pulsante: aprire e
            // basta e' quel che ci si aspetta, e la scelta resta buona perche'
            // dentro la cartella si riparte da capo.
            Apri?.Invoke(indice);
            return;
        }

        Premuto?.Invoke(indice);
        if (!Riordinabile) return;

        trascinaDa = indice;
        trascinaPercorso = percorso;
        trascinato = lista[indice];
        premutoIn = punto;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!trascinando)
        {
            trascinaDa = -1;
            trascinato = null;
            return;
        }

        var da = trascinaPercorso;
        int daIndice = trascinaDa;
        int posto = bersaglio;
        bool dentro = bersaglioDentro;
        var lista = Contenuto;
        var qui = percorso;

        FineTrascinamento();

        if (daIndice < 0 || posto < 0) return;

        int assoluto = pagina * Math.Max(1, deck.Cols * deck.Rows) + posto;

        if (dentro && assoluto < lista.Count && lista[assoluto].Cartella)
        {
            // Lasciato sopra una cartella: ci finisce dentro, in fondo.
            var giu = qui.Append(assoluto).ToArray();
            Portato?.Invoke(da, daIndice, giu, int.MaxValue);
            return;
        }

        // Una cella libera vuol dire « in fondo »: nell'elenco i buchi non
        // esistono, la griglia si riempie in ordine.
        int a = Math.Min(assoluto, Math.Max(0, lista.Count - (da.SequenceEqual(qui) ? 1 : 0)));
        Portato?.Invoke(da, daIndice, qui, a);
    }

    /// <summary>
    /// Il tasto destro su una cella piena chiede il menu di quel pulsante: gli
    /// stessi comandi dell'elenco a sinistra, li' dove il pulsante si vede.
    /// </summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!Scorciatoie || trascinando) return;

        var punto = e.GetPosition(this);
        if (SopraLaFascia(punto)) return;

        int cella = CellaSotto(punto);
        if (cella < 0) return;

        int indice = pagina * Math.Max(1, deck.Cols * deck.Rows) + cella;
        if (indice >= Contenuto.Count) return;

        MenuChiesto?.Invoke(indice);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Una finestra che ruba il fuoco, o un Esc di sistema: il trascinamento
        // finisce senza spostare niente, invece di restare appeso al puntatore.
        if (trascinando) FineTrascinamento();
    }

    private void FineTrascinamento()
    {
        trascinando = false;
        trascinaDa = -1;
        trascinato = null;
        bersaglio = -1;
        bersaglioDentro = false;
        FermaBordo();
        FermaSosta();
        Cursor = Cursors.Hand;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    /// <summary>
    /// Dove finirebbe il pulsante se lo si lasciasse adesso, e se lasciarlo
    /// vorrebbe dire « qui » oppure « dentro questa cartella ».
    ///
    /// Sopra una cartella il centro e' il dentro e i bordi sono l'accanto: il
    /// dentro si prende la parte larga perche' e' quello che si sta cercando di
    /// fare, e per mettere un pulsante *prima* di una cartella ci sono le
    /// frecce dell'elenco, che non richiedono la mira.
    /// </summary>
    private void MiraTrascinamento(Point punto)
    {
        bersaglio = CellaVicina(punto);
        bersaglioDentro = false;

        var lista = Contenuto;
        int assoluto = pagina * Math.Max(1, deck.Cols * deck.Rows) + bersaglio;
        bool suCartella = bersaglio >= 0 && assoluto < lista.Count && lista[assoluto].Cartella
                          && !(percorso.SequenceEqual(trascinaPercorso) && assoluto == trascinaDa);

        if (suCartella)
        {
            var m = Misure();
            var cella = CellaDi(bersaglio, m);
            var cuore = new WRect(
                cella.Left + cella.Width * 0.16, cella.Top + cella.Height * 0.16,
                cella.Width * 0.68, cella.Height * 0.68);
            bersaglioDentro = cuore.Contains(punto);
        }

        // Fermarsi su una cartella la apre, come fermarsi sul bordo cambia
        // pagina: e' l'unico modo di portare un pulsante due piani piu' giu'
        // senza lasciarlo per strada.
        GuardaLaSosta(bersaglioDentro ? assoluto : SopraLaFascia(punto) ? -1 : -2);
    }

    /// <summary>
    /// Il fermarsi che apre da solo. <paramref name="dove"/> e' l'indice della
    /// cartella sorvolata, -1 per la fascia che riporta su, -2 per niente.
    /// </summary>
    private void GuardaLaSosta(int dove)
    {
        if (dove == -1 && !Dentro) dove = -2;
        if (dove == sostaSu) return;

        FermaSosta();
        sostaSu = dove;
        if (dove == -2) return;

        sosta = new DispatcherTimer { Interval = AttesaCartella };
        sosta.Tick += (_, _) =>
        {
            int quale = sostaSu;
            FermaSosta();
            if (quale == -1) Esci?.Invoke();
            else if (quale >= 0) Apri?.Invoke(quale);
            // Il percorso e' cambiato sotto le mani: la mira va rifatta sul
            // contenuto nuovo, altrimenti resta puntata a una cella di prima.
            MiraTrascinamento(puntatore);
            InvalidateVisual();
        };
        sosta.Start();
    }

    private void FermaSosta()
    {
        sosta?.Stop();
        sosta = null;
        sostaSu = -2;
    }

    /// <summary>
    /// La cella piu' vicina al puntatore, anche quando sta nella fessura fra due
    /// pulsanti. Durante un trascinamento non esiste il « fuori »: si sta
    /// comunque andando da qualche parte, e restare senza destinazione a meta'
    /// strada farebbe lampeggiare l'evidenziazione.
    /// </summary>
    private int CellaVicina(Point punto)
    {
        var m = Misure();
        if (m.Lato <= 0) return -1;

        int col = (int)Math.Round((punto.X - m.Sinistra - m.Lato / 2) / m.Passo);
        int riga = (int)Math.Round((punto.Y - m.Cima - m.Lato / 2) / m.Passo);
        col = Math.Clamp(col, 0, deck.Cols - 1);
        riga = Math.Clamp(riga, 0, deck.Rows - 1);
        return riga * deck.Cols + col;
    }

    /// <summary>
    /// Fermarsi sul bordo cambia pagina. Senza, un pulsante non potrebbe essere
    /// portato dalla prima pagina alla seconda: il trascinamento tiene il mouse,
    /// e le frecce sotto l'anteprima non si possono premere.
    /// </summary>
    private void GuardaIlBordo(Point punto)
    {
        var m = Misure();
        double zona = Math.Max(m.Margine * 2, 18);
        int verso = punto.X < zona ? -1 : punto.X > ActualWidth - zona ? +1 : 0;

        if (verso == 0 || (verso < 0 && pagina == 0) || (verso > 0 && pagina >= Pagine - 1))
        {
            FermaBordo();
            return;
        }
        if (verso == versoBordo && bordo is not null) return;

        FermaBordo();
        versoBordo = verso;
        bordo = new DispatcherTimer { Interval = AttesaBordo };
        bordo.Tick += (_, _) =>
        {
            Pagina += versoBordo;
            MiraTrascinamento(puntatore);
            if ((versoBordo < 0 && pagina == 0) || (versoBordo > 0 && pagina >= Pagine - 1)) FermaBordo();
            InvalidateVisual();
        };
        bordo.Start();
    }

    private void FermaBordo()
    {
        bordo?.Stop();
        bordo = null;
        versoBordo = 0;
    }

    /// <summary>Il puntatore e' sulla fascia della cartella aperta.</summary>
    private bool SopraLaFascia(Point punto) => Dentro && punto.Y < Misure().Testa;

    /// <summary>Indice della cella nella pagina mostrata, -1 fuori dalla griglia.</summary>
    private int CellaSotto(Point punto)
    {
        var m = Misure();
        if (m.Lato <= 0) return -1;

        int col = (int)Math.Floor((punto.X - m.Sinistra) / m.Passo);
        int riga = (int)Math.Floor((punto.Y - m.Cima) / m.Passo);
        if (col < 0 || col >= deck.Cols || riga < 0 || riga >= deck.Rows) return -1;

        // Dentro la fessura fra due celle non c'e' nessun pulsante: il tablet
        // fa lo stesso controllo, e senza si prenderebbe quello a sinistra.
        if (punto.X > m.Sinistra + col * m.Passo + m.Lato || punto.Y > m.Cima + riga * m.Passo + m.Lato) return -1;

        return riga * deck.Cols + col;
    }

    /// <summary>
    /// Il riquadro del pulsantino « togli » dentro una cella, oppure vuoto se
    /// la cella e' troppo piccola perche' ci stia senza coprirla.
    ///
    /// La misura e' in pixel della finestra e non in quelli del tablet: e' un
    /// comando del PC, non una cosa che si vedra' di la', e a densita' un
    /// terzo diventerebbe un puntino da prendere con lo spillo.
    /// </summary>
    private WRect Croce(WRect cella)
    {
        if (!Scorciatoie) return WRect.Empty;
        if (cella.Width < 38 || cella.Height < 28) return WRect.Empty;

        double lato = Math.Min(18, Math.Min(cella.Width, cella.Height) * 0.34);
        if (lato < 12) return WRect.Empty;

        double bordo = Math.Min(5, lato * 0.3);
        return new WRect(cella.Right - lato - bordo, cella.Top + bordo, lato, lato);
    }

    /// <summary>Il punto e' sul pulsantino « togli » di quella cella, e la cella e' piena.</summary>
    private bool SullaCroce(Point punto, int cella)
    {
        if (!Scorciatoie || cella < 0 || trascinando) return false;

        int indice = pagina * Math.Max(1, deck.Cols * deck.Rows) + cella;
        if (indice >= Contenuto.Count) return false;

        var riquadro = Croce(CellaDi(cella, Misure()));
        return !riquadro.IsEmpty && riquadro.Contains(punto);
    }

    private void DisegnaCroce(DrawingContext ctx, WRect cella)
    {
        var riquadro = Croce(cella);
        if (riquadro.IsEmpty) return;

        ctx.DrawRoundedRectangle(
            sorvolaCroce ? Brush(0xB4, 0x3D, 0x3D) : Brush(0x15, 0x1A, 0x21),
            new Pen(Brush(0x39, 0x43, 0x4F), 1),
            riquadro, riquadro.Width / 2, riquadro.Height / 2);

        var segno = Testo("✕", riquadro.Height * 0.52, ColoreTesto);
        ctx.DrawText(segno, new Point(
            riquadro.Left + (riquadro.Width - segno.Width) / 2,
            riquadro.Top + (riquadro.Height - segno.Height) / 2));
    }

    /// <summary>
    /// Le misure della griglia, con la regola di DeckView.misura(): tasti
    /// quadrati, distanza al sedici per cento del lato, lato il piu' grande che
    /// ci sta ma mai oltre 150, e griglia al centro sotto la fascia.
    /// </summary>
    private Misura Misure()
    {
        double k = ActualWidth / LarghezzaTablet;   // il tablet e' a densita' 1
        double margine = 18 * k;
        double testa = 44 * k;
        int cols = Math.Max(1, deck.Cols), rows = Math.Max(1, deck.Rows);
        const double q = 0.16;

        double perL = (ActualWidth - margine * 2) / (cols + (cols - 1) * q);
        double perH = (ActualHeight - testa - margine) / (rows + (rows - 1) * q);
        double lato = Math.Max(0, Math.Min(150 * k, Math.Min(perL, perH)));
        double passo = lato * (1 + q);
        double larga = cols * lato + (cols - 1) * lato * q;
        double alta = rows * lato + (rows - 1) * lato * q;
        return new Misura(margine, testa, lato, passo, lato * 0.17,
            (ActualWidth - larga) / 2, testa + (ActualHeight - testa - alta) / 2, k);
    }

    /// <summary>Le misure della griglia, tutte derivate dalla larghezza.</summary>
    private readonly record struct Misura(
        double Margine, double Testa, double Lato, double Passo, double Raggio,
        double Sinistra, double Cima, double K);

    // ---- disegno ----

    protected override void OnRender(DrawingContext ctx)
    {
        var tutto = new WRect(0, 0, ActualWidth, ActualHeight);
        ctx.DrawRoundedRectangle(Fondo, null, tutto, 6, 6);

        // Lo sfondo del profilo, a pieno pannello, con lo stesso velo del tablet.
        var sfondo = deck.Sfondo.Length > 0 ? Icone.Carica(deck.Sfondo) : null;
        if (sfondo is not null)
        {
            ctx.PushClip(new RectangleGeometry(tutto, 6, 6));
            double k = Math.Max(ActualWidth / sfondo.PixelWidth, ActualHeight / sfondo.PixelHeight);
            double w = sfondo.PixelWidth * k, h = sfondo.PixelHeight * k;
            ctx.DrawImage(sfondo, new WRect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h));
            ctx.DrawRectangle(VeloSfondo, null, tutto);
            ctx.Pop();
        }

        var m = Misure();
        DisegnaTesta(ctx, m);
        if (m.Lato <= 1) return;

        var lista = Contenuto;
        int perPagina = Math.Max(1, deck.Cols * deck.Rows);
        int primo = pagina * perPagina;

        for (int i = 0; i < perPagina; i++)
        {
            int indice = primo + i;
            var cella = CellaDi(i, m);

            // L'incavo c'e' sempre, anche per le celle libere: come sul tablet.
            var incavo = cella;
            incavo.Inflate(3 * m.K, 3 * m.K);
            ctx.DrawRoundedRectangle(ColorePozzo, null, incavo, m.Raggio + 3 * m.K, m.Raggio + 3 * m.K);

            if (indice >= lista.Count)
            {
                // Le celle libere si vedono: e' cosi' che si capisce quanto
                // spazio resta prima che i pulsanti passino a una pagina nuova.
                ctx.DrawRoundedRectangle(
                    sorvolato == i ? Brush(0x20, 0x21, 0x26) : ColoreVuoto,
                    null, cella, m.Raggio, m.Raggio);
                continue;
            }

            var pulsante = lista[indice];

            // Il pulsante che si sta trascinando resta al suo posto come
            // impronta vuota: e' quello che dice da dove si e' partiti, e senza
            // sembrerebbe che la griglia si sia accorciata.
            if (trascinando && ReferenceEquals(pulsante, trascinato))
            {
                ctx.DrawRoundedRectangle(ColoreVuoto, null, cella, m.Raggio, m.Raggio);
                continue;
            }

            DisegnaPulsante(ctx, pulsante, cella, m);

            if (indice == scelto)
            {
                var anello = cella;
                anello.Inflate(1.5, 1.5);
                ctx.DrawRoundedRectangle(null, new Pen(ColoreScelto, 2), anello, m.Raggio + 1.5, m.Raggio + 1.5);
            }
            else if (sorvolato == i && !trascinando)
                ctx.DrawRoundedRectangle(new SolidColorBrush(Colors.White) { Opacity = 0.07 }, null,
                    cella, m.Raggio, m.Raggio);

            // Il pulsantino « togli » solo sulla cella sotto il puntatore: uno
            // su ognuna sarebbe una griglia di croci invece di un deck.
            if (sorvolato == i && !trascinando) DisegnaCroce(ctx, cella);
        }

        DisegnaTrascinato(ctx, m);
    }

    /// <summary>
    /// La fascia in cima, come sul tablet: il profilo, e dentro una cartella la
    /// freccia e il percorso; a destra i pallini delle pagine e lo stato del PC.
    /// Premendola dentro una cartella si risale di un piano.
    /// </summary>
    private void DisegnaTesta(DrawingContext ctx, Misura m)
    {
        double mezzo = m.Testa / 2;
        double x = m.Margine;
        double corpo = Math.Max(6, 15 * m.K);
        double fine = ActualWidth - m.Margine - 150 * m.K;
        string radice = deck.Nome.Length > 0 ? deck.Nome : "Deck";

        if (Dentro)
        {
            double l = 30 * m.K;
            var chip = new WRect(x, mezzo - l / 2, l, l);
            ctx.DrawRoundedRectangle(sorvolaFascia || sostaSu == -1 ? Brush(0x26, 0x28, 0x2E) : ColoreChip,
                null, chip, 9 * m.K, 9 * m.K);
            Pittogrammi.Disegna(ctx, "chevron-left", new Point(chip.X + l / 2, mezzo), 18 * m.K, ColoreSegno);
            x = chip.Right + 10 * m.K;

            var strada = deck.Cartelle(percorso).ToList();
            if (strada.Count > 0)
            {
                var qui = strada[^1];
                string prima = strada.Count > 1 ? radice + "  ›  …  ›  " : radice + "  ›  ";
                var testoPrima = Testo(prima, corpo, ColoreTenue, leggero: true);
                if (testoPrima.WidthIncludingTrailingWhitespace < (fine - x) * 0.5)
                {
                    ctx.DrawText(testoPrima, new Point(x, mezzo - testoPrima.Height / 2));
                    x += testoPrima.WidthIncludingTrailingWhitespace;
                }
                radice = qui.Label.Length > 0 ? qui.Label : qui.Id;
            }
        }

        var titolo = Testo(radice, corpo, ColoreTesto);
        titolo.MaxTextWidth = Math.Max(1, fine - x);
        titolo.MaxLineCount = 1;
        titolo.Trimming = TextTrimming.CharacterEllipsis;
        ctx.DrawText(titolo, new Point(x, mezzo - titolo.Height / 2));

        // Lo stato del collegamento, come sul tablet.
        var stato = Testo(Collegato ? "PC" : "PC spento", Math.Max(5, 12 * m.K), ColoreTenue, leggero: true);
        double destra = ActualWidth - m.Margine;
        ctx.DrawText(stato, new Point(destra - stato.Width, mezzo - stato.Height / 2));
        double px = destra - stato.Width - 9 * m.K;
        if (Collegato)
            ctx.DrawEllipse(new SolidColorBrush(ColoreVerde.Color) { Opacity = 0.24 }, null,
                new Point(px, mezzo), 6 * m.K, 6 * m.K);
        ctx.DrawEllipse(Collegato ? ColoreVerde : Brush(0x55, 0x57, 0x5D), null,
            new Point(px, mezzo), 3.5 * m.K, 3.5 * m.K);

        DisegnaPallini(ctx, m, px - 20 * m.K, mezzo);
    }

    /// <summary>
    /// I pallini delle pagine, finiti in <paramref name="destra"/>: quello della
    /// pagina mostrata e' una lineetta, come sul tablet.
    /// </summary>
    private void DisegnaPallini(DrawingContext ctx, Misura m, double destra, double y)
    {
        int pagine = Pagine;
        if (pagine < 2) return;

        double h = 6 * m.K, lungo = 18 * m.K, spazio = 6 * m.K;
        double x = destra - ((pagine - 1) * (h + spazio) + lungo);
        for (int i = 0; i < pagine; i++)
        {
            bool qui = i == pagina;
            double l = qui ? lungo : h;
            ctx.DrawRoundedRectangle(qui ? ColorePuntoAcceso : ColorePuntoSpento, null,
                new WRect(x, y - h / 2, l, h), h / 2, h / 2);
            x += l + spazio;
        }
    }

    /// <summary>
    /// Un tasto dentro un quadrato qualsiasi: sfumatura della tinta, filo di
    /// luce in cima, icona o glifo, etichetta in fondo. Le misure sono quelle di
    /// DeckView.drawKey, frazioni del lato.
    /// </summary>
    private void DisegnaPulsante(DrawingContext ctx, DeckButton pulsante, WRect cella, Misura m)
    {
        double lato = cella.Width;
        double raggio = lato * 0.17;
        var tinta = Tinta(pulsante.Color);

        var sfumatura = new LinearGradientBrush(Fondi(tinta, Colors.White, 0.10), Fondi(tinta, Colors.Black, 0.22), 90);
        ctx.DrawRoundedRectangle(sfumatura, null, cella, raggio, raggio);
        ctx.DrawRoundedRectangle(Lucido, null, cella, raggio, raggio);

        bool haEtichetta = pulsante.Label.Length > 0;
        double cy = haEtichetta ? cella.Top + lato / 2 - lato * 0.1 : cella.Top + lato / 2;
        var centro = new Point(cella.Left + lato / 2, cy);

        var icona = Icone.Carica(pulsante.Icon);
        if (icona is not null)
        {
            ctx.DrawImage(icona, RiquadroIcona(centro, lato, icona.PixelWidth, icona.PixelHeight));
        }
        else if (!Pittogrammi.Disegna(ctx, pulsante.Glyph, centro, lato * 0.36, ColoreSegno)
                 && pulsante.Glyph.Length > 0)
        {
            var glifo = Testo(pulsante.Glyph, lato * 0.34, Brushes.White);
            ctx.DrawText(glifo, new Point(centro.X - glifo.Width / 2, cy - glifo.Height / 2));
        }

        if (haEtichetta)
        {
            var etichetta = Testo(pulsante.Label, Math.Min(13 * m.K, lato * 0.115), ColoreEtichetta);
            etichetta.MaxTextWidth = Math.Max(1, lato - 12 * m.K);
            etichetta.MaxLineCount = 1;
            etichetta.Trimming = TextTrimming.CharacterEllipsis;
            etichetta.TextAlignment = TextAlignment.Center;
            // La linea di base sta a 0,115 del lato dal fondo, come sul tablet.
            double baseline = cella.Bottom - lato * 0.115;
            ctx.DrawText(etichetta, new Point(cella.Left + 6 * m.K, baseline - etichetta.Baseline));
        }

        if (pulsante.Cartella)
        {
            double l = lato * 0.13;
            Pittogrammi.Disegna(ctx, "layers",
                new Point(cella.Right - lato * 0.08 - l / 2, cella.Top + lato * 0.08 + l / 2), l,
                ColoreSegnoCartella);
        }

        if (!Collegato && !pulsante.Cartella)
            ctx.DrawRoundedRectangle(VeloSpento, null, cella, raggio, raggio);
    }

    /// <summary>
    /// La cella di destinazione e il pulsante appeso al puntatore.
    ///
    /// Il pulsante segue il dito invece di limitarsi a illuminare la cella
    /// sotto: e' l'unico modo di vedere davvero dove sta andando quando le
    /// celle sono quindici e tutte della stessa misura. Sopra una cartella
    /// l'evidenziazione cambia: un anello pieno vuol dire « ci finisce dentro »,
    /// e va detto prima di lasciare il tasto.
    /// </summary>
    private void DisegnaTrascinato(DrawingContext ctx, Misura m)
    {
        if (!trascinando || trascinato is null) return;

        if (bersaglio >= 0)
        {
            var posto = CellaDi(bersaglio, m);
            if (bersaglioDentro)
            {
                var cresciuta = posto;
                cresciuta.Inflate(2, 2);
                ctx.DrawRoundedRectangle(new SolidColorBrush(ColoreScelto.Color) { Opacity = 0.28 },
                    new Pen(ColoreScelto, 2.5), cresciuta, m.Raggio, m.Raggio);
            }
            else
            {
                ctx.DrawRoundedRectangle(new SolidColorBrush(ColoreScelto.Color) { Opacity = 0.16 },
                    new Pen(ColoreScelto, 2), posto, m.Raggio, m.Raggio);
            }
        }

        var sotto = new WRect(puntatore.X - m.Lato / 2, puntatore.Y - m.Lato / 2, m.Lato, m.Lato);
        ctx.PushOpacity(0.9);
        DisegnaPulsante(ctx, trascinato, sotto, m);
        ctx.Pop();
    }

    /// <summary>Il quadrato del tasto numero <paramref name="posto"/> nella pagina mostrata.</summary>
    private WRect CellaDi(int posto, Misura m) =>
        new(m.Sinistra + (posto % Math.Max(1, deck.Cols)) * m.Passo,
            m.Cima + (posto / Math.Max(1, deck.Cols)) * m.Passo,
            m.Lato, m.Lato);

    /// <summary>
    /// Dove va disegnata l'icona: centrata, con le sue proporzioni, dentro un
    /// quadrato di 0,44 del lato - la misura di DeckView.drawKey.
    /// </summary>
    private static WRect RiquadroIcona(Point centro, double lato, double larghezza, double altezza)
    {
        double latoIcona = lato * 0.44;
        double k = larghezza <= 0 || altezza <= 0 ? 1 : Math.Min(latoIcona / larghezza, latoIcona / altezza);
        double w = Math.Max(1, larghezza * k);
        double h = Math.Max(1, altezza * k);
        return new WRect(centro.X - w / 2, centro.Y - h / 2, w, h);
    }

    private FormattedText Testo(string testo, double corpo, Brush colore, bool leggero = false) => new(
        testo, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, leggero ? CarattereLeggero : Carattere,
        Math.Max(1, corpo), colore, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>
    /// Il colore del pulsante, o la grafite se il campo e' vuoto o non valido.
    /// Il vecchio blu notte di partenza si legge come grafite, come sul tablet.
    /// </summary>
    public static Color Tinta(string testo)
    {
        if (string.IsNullOrWhiteSpace(testo)) return ColorePredefinito.Color;
        try
        {
            var convertito = ColorConverter.ConvertFromString(testo.Trim());
            if (convertito is not Color c) return ColorePredefinito.Color;
            return c.R == 0x21 && c.G == 0x27 && c.B == 0x34 ? ColorePredefinito.Color : c;
        }
        catch (FormatException)
        {
            return ColorePredefinito.Color;
        }
    }

    /// <summary>La stessa fusione di DeckView.fondi.</summary>
    private static Color Fondi(Color da, Color a, double quanto) => Color.FromRgb(
        (byte)(da.R + (a.R - da.R) * quanto),
        (byte)(da.G + (a.G - da.G) * quanto),
        (byte)(da.B + (a.B - da.B) * quanto));

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var pennello = new SolidColorBrush(Color.FromRgb(r, g, b));
        pennello.Freeze();
        return pennello;
    }
}
