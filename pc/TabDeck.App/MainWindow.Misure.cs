using System.Windows;
using System.Windows.Controls;

namespace TabDeck;

/// <summary>
/// Quanto spazio si prende la finestra, secondo quanto spazio le hanno dato.
///
/// Non e' una questione di eleganza: le pagine sono fatte di colonne con una
/// larghezza minima, e su uno schermo piccolo la somma di quei minimi non ci
/// sta. Senza qualcuno che se ne accorga, la terza colonna finisce oltre il
/// bordo e i pulsanti che ci stavano dentro diventano irraggiungibili.
///
/// Tre misure, e si sceglie guardando la finestra:
///
///   AMPIO    la finestra com'e' pensata: barra coi nomi, tre colonne nella
///            pagina dei pulsanti, due nella dashboard.
///   MEDIO    gli stessi pezzi, piu' stretti e con meno aria attorno.
///   STRETTO  la barra si riduce ai glifi, la dashboard mette una colonna
///            sotto l'altra, e l'anteprima del deck lascia la sua colonna e
///            passa sopra i campi.
///
/// Chi legge queste misure non lo sa: stanno in App.xaml come risorse, e le
/// pagine le prendono con DynamicResource. Cambiarle qui le cambia dappertutto
/// senza che nessuna pagina debba avere un'opinione.
/// </summary>
public partial class MainWindow
{
    private enum Formato { Ampio, Medio, Stretto }

    private Formato formato = Formato.Ampio;
    private bool basso;
    private bool misurato;

    /// <summary>
    /// La finestra non si apre piu' grande dello schermo su cui si apre.
    ///
    /// Le misure scritte nel XAML sono quelle di un monitor da scrivania: su un
    /// portatile da 1366x768 una finestra alta 820 nasce mezza sotto la barra
    /// delle applicazioni, e la prima cosa da fare sarebbe rimetterla a posto.
    /// </summary>
    private void PreparaMisure()
    {
        // Clamp e non Min/Max annidati: Math.Min con un NaN restituisce NaN, e
        // una finestra a cui si assegna NaN passa alla misura automatica —
        // cioe' diventa alta quanto il suo contenuto, che qui vuol dire piu'
        // dello schermo.
        var area = SystemParameters.WorkArea;
        Width = Stretta(Width, 1340, MinWidth, area.Width - 40);
        Height = Stretta(Height, 820, MinHeight, area.Height - 40);

        SizeChanged += (_, _) => AdattaMisure();
        AdattaMisure();
    }

    /// <summary>Una misura dentro i suoi limiti, con un valore di scorta se manca.</summary>
    private static double Stretta(double quanto, double scorta, double minimo, double massimo)
    {
        if (double.IsNaN(quanto) || quanto <= 0) quanto = scorta;
        return Math.Clamp(quanto, minimo, Math.Max(minimo, massimo));
    }

    /// <summary>
    /// Guarda quanto e' grande la finestra e, se e' cambiata misura, rimette a
    /// posto tutto quanto. Il confronto con la misura di prima non e' un
    /// risparmio da poco: SizeChanged arriva a ogni pixel mentre si trascina il
    /// bordo, e rifare le colonne di tre pagine a ogni pixel si vede.
    /// </summary>
    private void AdattaMisure()
    {
        // Prima che la finestra sia sullo schermo ActualWidth vale zero, e
        // zero vorrebbe dire « strettissima »: finche' non c'e', valgono le
        // misure con cui e' stata chiesta.
        double larghezza = ActualWidth > 0 ? ActualWidth : Width;
        double altezza = ActualHeight > 0 ? ActualHeight : Height;

        var adesso = larghezza switch
        {
            >= 1280 => Formato.Ampio,
            >= 1080 => Formato.Medio,
            _ => Formato.Stretto,
        };

        // Sotto i settecento pixel di altezza il margine sopra e sotto e' la
        // prima cosa da restituire: e' spazio che non mostra niente.
        bool bassa = altezza < 700;

        if (misurato && adesso == formato && bassa == basso) return;
        formato = adesso;
        basso = bassa;
        misurato = true;

        Spazi();
        Pagine();
    }

    /// <summary>Le risorse che tutte le pagine leggono: margini e barra laterale.</summary>
    private void Spazi()
    {
        bool stretto = formato == Formato.Stretto;
        var risorse = Application.Current.Resources;

        double lati = formato switch
        {
            Formato.Ampio => 24,
            Formato.Medio => 18,
            _ => 12,
        };
        double sopra = basso ? lati - 8 : lati - 2;
        double sotto = basso ? lati - 10 : lati - 8;

        risorse["SpazioPagina"] = new Thickness(lati, Math.Max(8, sopra), lati, Math.Max(6, sotto));

        risorse["LarghezzaBarra"] = stretto ? 52.0 : 196.0;
        risorse["SpazioBarra"] = stretto
            ? new Thickness(5, 8, 5, 12)
            : new Thickness(11, 10, 11, 16);
        // Con la barra stretta il glifo va messo in mezzo a mano: e' largo 21,
        // la barra 52, e sedici per parte lo centrano.
        risorse["SpazioVoceBarra"] = stretto
            ? new Thickness(16, 0, 0, 0)
            : new Thickness(14, 0, 10, 0);

        risorse["NomiBarra"] = stretto ? Visibility.Collapsed : Visibility.Visible;
        risorse["SegniBarra"] = stretto ? Visibility.Visible : Visibility.Collapsed;

        // Il sottotitolo dell'intestazione e' la prima cosa che si puo' togliere
        // senza perdere niente: dice quel che la finestra e', non quel che sta
        // facendo.
        TestoSottotitolo.Visibility = stretto ? Visibility.Collapsed : Visibility.Visible;

        // Nel piede della pagina dei pulsanti il conto dei pulsanti sta in
        // mezzo fra « Annulla » e « Salva »: stretti, sono i due pulsanti a
        // doverci stare, non il conto.
        TestoTotale.Visibility = stretto ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Le colonne delle tre pagine che ne hanno piu' di una.</summary>
    private void Pagine()
    {
        bool stretto = formato == Formato.Stretto;

        // ---- dashboard ----
        // Larga, i cinque riquadri stanno su due colonne e il deck si prende
        // quel che avanza in basso. Stretta, vanno tutti in colonna e la
        // pagina scorre: due colonne larghe un dito non sono due colonne.
        DashDestra.Width = stretto ? new GridLength(0) : new GridLength(360);

        SistemaDashboard();

        // ---- luci ----
        LuciElenco.Width = new GridLength(stretto ? 210 : 264);

        // ---- pulsanti ----
        DeckElenco.Width = new GridLength(formato switch
        {
            Formato.Ampio => 330,
            Formato.Medio => 296,
            _ => 240,
        });
        DeckDettaglio.MinWidth = stretto ? 330 : 380;

        // La terza colonna si chiude e l'anteprima si trasferisce sopra i
        // campi. Il trasferimento e' vero — lo stesso controllo cambia padre —
        // perche' due anteprime che cercano di restare d'accordo sono due
        // anteprime che prima o poi litigano.
        DeckAnteprima.MinWidth = stretto ? 0 : 320;
        DeckAnteprima.Width = stretto ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        AnteprimaAlLato(!stretto);
    }

    /// <summary>
    /// I cinque riquadri della dashboard nelle loro celle.
    ///
    /// Sta in un metodo suo e non dentro <see cref="Pagine"/> perche' non
    /// dipende solo dalla larghezza: senza routine di casa il riquadro delle
    /// routine non c'e', e il deck si prende anche la sua colonna invece di
    /// lasciare mezza pagina vuota. Quel « senza routine » cambia quando si
    /// salvano le luci, non quando si tira il bordo della finestra.
    /// </summary>
    private void SistemaDashboard()
    {
        bool stretto = formato == Formato.Stretto;
        bool routine = RiquadroRoutineVive.Visibility == Visibility.Visible;

        Cella(RiquadroCollegamento, 0, 0, stretto ? 2 : 1, new Thickness(0, 0, stretto ? 0 : 16, 0));
        Cella(DashTablet, stretto ? 1 : 0, stretto ? 0 : 1, stretto ? 2 : 1,
              new Thickness(0, stretto ? 16 : 0, 0, 0));
        Cella(DashMisure, stretto ? 2 : 1, 0, 2, new Thickness(0, 16, 0, 0));
        Cella(DashDeck, stretto ? 3 : 2, 0, stretto || !routine ? 2 : 1,
              new Thickness(0, 16, stretto || !routine ? 0 : 16, 0));
        Cella(RiquadroRoutineVive, stretto ? 4 : 2, stretto ? 0 : 1, stretto ? 2 : 1,
              new Thickness(0, 16, 0, 0));

        // In colonna nessuna riga puo' « prendere quel che avanza »: la pagina
        // e' piu' alta della finestra, scorre, e una riga elastica dentro una
        // pagina che scorre non vuol dire niente.
        DashRiga2.Height = stretto ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        DashRiga3.Height = stretto ? GridLength.Auto : new GridLength(0);
        DashRiga4.Height = stretto ? GridLength.Auto : new GridLength(0);

        ScorriDashboard.VerticalScrollBarVisibility = stretto
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    /// <summary>Mette un riquadro in una cella della griglia, col suo margine.</summary>
    private static void Cella(FrameworkElement pezzo, int riga, int colonna, int quante, Thickness bordo)
    {
        Grid.SetRow(pezzo, riga);
        Grid.SetColumn(pezzo, colonna);
        Grid.SetColumnSpan(pezzo, quante);
        pezzo.Margin = bordo;
    }

    /// <summary>
    /// Sposta l'anteprima del deck fra la sua colonna e la cima dei campi. Il
    /// controllo e' uno solo: prima si stacca da dove sta, poi si attacca dove
    /// deve andare — un elemento con due padri in WPF non esiste.
    /// </summary>
    private void AnteprimaAlLato(bool lato)
    {
        if (lato && ReferenceEquals(OspiteAnteprimaLato.Content, ColonnaAnteprima)) return;
        if (!lato && ReferenceEquals(OspiteAnteprimaSopra.Content, ColonnaAnteprima)) return;

        OspiteAnteprimaLato.Content = null;
        OspiteAnteprimaSopra.Content = null;

        if (lato) OspiteAnteprimaLato.Content = ColonnaAnteprima;
        else OspiteAnteprimaSopra.Content = ColonnaAnteprima;

        OspiteAnteprimaSopra.Visibility = lato ? Visibility.Collapsed : Visibility.Visible;
        OspiteAnteprimaLato.Visibility = lato ? Visibility.Visible : Visibility.Collapsed;

        // Sopra i campi l'altezza e' infinita — si scorre — e senza un tetto
        // l'anteprima si prenderebbe tutta la larghezza della colonna,
        // spingendo i campi sotto il bordo della finestra. Nella sua colonna
        // invece il tetto e' l'altezza, e non serve.
        Anteprima.MaxWidth = lato ? double.PositiveInfinity : 460;
    }
}
