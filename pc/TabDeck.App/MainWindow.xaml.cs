using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TabDeck;

/// <summary>
/// La finestra. Tutto quello che succede parte da un pulsante premuto qui, o
/// da una spunta messa in Gestione › Impostazioni — collegarsi all'apertura,
/// al cavo attaccato, riprovare; vedi MainWindow.Impostazioni.cs. Niente resta
/// acceso dopo la chiusura.
///
/// La parte che monta e modifica il deck sta in MainWindow.Deck.cs: e' meta'
/// della finestra, ed e' l'unica che ha una sua struttura invece di limitarsi
/// a leggere e scrivere un'impostazione.
/// </summary>
public partial class MainWindow : Window
{
    private readonly string configDir;
    private readonly string settingsPath;
    private readonly string deckPath;
    private readonly string adbPath;

    private readonly Settings settings;
    private readonly Engine engine;

    /// <summary>Aspetta che sia il tablet a farsi vivo. Vedi <see cref="Richiamo"/>.</summary>
    private readonly Richiamo richiamo = new();

    /// <summary>L'icona nell'area di notifica: e' li' che la finestra si ritira.</summary>
    private Vassoio? vassoio;

    /// <summary>
    /// Vero solo quando si sta uscendo davvero, cioe' da « Esci » nel menu
    /// dell'icona. La croce della finestra non lo accende: nasconde e basta.
    /// </summary>
    private bool uscita;

    private List<MonitorInfo> schermi = new();

    /// <summary>
    /// I nomi Windows delle voci in <c>ElencoSorgenti</c>, nello stesso ordine.
    /// La prima e' vuota, ed e' lo schermo virtuale: non ha un nome stabile da
    /// salvare, e non deve averlo.
    /// </summary>
    private readonly List<string> sorgenti = new();
    private List<VirtualAdapter> adattatori = new();
    private List<FoundTablet> trovati = new();

    /// <summary>Vero mentre si riempiono i campi: impedisce di risalvare quel che si sta leggendo.</summary>
    private bool caricamento;

    public MainWindow()
    {
        InitializeComponent();

        configDir = ConfigFile.Directory();
        settingsPath = Path.Combine(configDir, "tabdeck.json");
        deckPath = Path.Combine(configDir, "deck.json");
        luciPath = Path.Combine(configDir, "luci.json");

        settings = ConfigFile.Load<Settings>(settingsPath);
        deckFile = DeckFile.Carica(deckPath);
        deck = deckFile.Attuale();
        luci = ConfigFile.Load<LuciConfig>(luciPath);
        adbPath = TrovaAdb();

        engine = new Engine(settings, deck, luci);
        engine.Log += Registra;
        engine.ProfiloChiesto += ProfiloDalTablet;
        engine.ConnectionChanged += CollegamentoCambiato;
        engine.ScreenChanged += SchermoCambiato;
        engine.ConfigDalTablet += ConfigDalTablet;

        richiamo.Log += messaggio => Registra(messaggio);
        richiamo.Annuncio += TabletSiAnnuncia;
        richiamo.Ascolta();

        CaricaImpostazioni();
        AggiornaSchermi();
        CercaDriver();
        PreparaDeck();
        PreparaCasa();
        PreparaMisure();

        // La prima voce della barra e' l'intestazione del gruppo, che e'
        // spenta: senza dirlo qui, il TabControl aprirebbe una pagina vuota.
        Schede.SelectedItem = SchedaDashboard;

        // La banda si legge dal motore una volta al secondo: farla arrivare a
        // eventi vorrebbe dire attraversare i thread venti volte al secondo per
        // aggiornare una riga di testo.
        var battito = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        battito.Tick += (_, _) =>
        {
            TestoBanda.Text = engine.ScreenOn ? engine.Throughput : "";
            DashboardBanda.Text = engine.ScreenOn ? engine.Throughput : "—";
        };
        battito.Start();

        PreparaVassoio();
        PreparaImpostazioni();
        PreparaSalvaschermo();
        PreparaEstensioni();
        PreparaArrivi();
        PreparaAggiornamento();
        PreparaPostazioni();
        PreparaInternet();
        MostraAppTablet();
        Schede.SelectionChanged += SchedaTablet_Aperta;

        Registra($"Configurazione in {configDir}");
        Closing += Chiusura;
    }

    // ---- l'area di notifica ----

    /// <summary>
    /// L'icona accanto all'orologio, e la finestra che si ritira li'.
    ///
    /// La croce chiudeva tutto: il collegamento cadeva, lo schermo virtuale
    /// spariva e il tablet restava con una griglia che non comanda niente.
    /// E' il gesto piu' facile della finestra, e faceva la cosa piu' cara.
    /// Adesso la croce nasconde, e a spegnere davvero e' « Esci » nel menu
    /// dell'icona — un gesto in piu', per quello che costa di piu'.
    /// </summary>
    private void PreparaVassoio()
    {
        vassoio = new Vassoio("TabDeck — scollegato");
        vassoio.Apri += MostraFinestra;
        vassoio.Esci += EsciDavvero;
        // Impostazioni e cavo attaccato li aggancia PreparaImpostazioni.

        if (!vassoio.Acceso)
            Registra("L'area di notifica non ha accettato l'icona: la croce chiudera' come prima.", true);
    }

    /// <summary>
    /// Rimette in vista la finestra. La chiamano l'icona nell'area di notifica
    /// e la copia di troppo che si e' appena accorta di essere di troppo.
    /// </summary>
    private void MostraFinestra()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();

        // Windows non lascia che un programma qualunque si porti davanti: il
        // diritto ce l'ha quello che l'utente ha toccato per ultimo, che in un
        // caso e' la copia di troppo, gia' in chiusura, e nell'altro e'
        // Esplora risorse. Il giro da Topmost e ritorno ottiene lo stesso
        // risultato senza restare davvero sempre in primo piano.
        Topmost = true;
        Topmost = false;
    }

    private void NascondiNelVassoio()
    {
        Hide();

        // Il fumetto una volta sola: chi lo ha letto sa dov'e' finita la
        // finestra, e ripeterglielo a ogni chiusura sarebbe una seccatura.
        if (!settings.AvvisoVassoio)
        {
            settings.AvvisoVassoio = true;
            SalvaImpostazioni();
            vassoio?.Avviso("TabDeck resta acceso",
                "La finestra si e' ritirata qui: il tablet resta collegato. "
                + "Per spegnere davvero, tasto destro sull'icona e « Esci ».");
        }
        Registra("Finestra ritirata nell'area di notifica. Il collegamento resta aperto.");
    }

    /// <summary>Spegne davvero. E' l'unica strada, ed e' voluta.</summary>
    private void EsciDavvero()
    {
        uscita = true;
        // Se la finestra e' nascosta, Close() la chiude senza mostrarla: il
        // MessageBox del deck non salvato comparirebbe pero' da solo in mezzo
        // allo schermo, senza niente dietro che spieghi da dove viene.
        if (!IsVisible) MostraFinestra();
        Close();
    }

    /// <summary>adb sta dentro tools/platform-tools, accanto alla cartella config.</summary>
    private string TrovaAdb()
    {
        if (!string.IsNullOrWhiteSpace(settings.AdbPath) && File.Exists(settings.AdbPath))
            return settings.AdbPath;

        var dir = new DirectoryInfo(configDir).Parent;
        for (int i = 0; i < 4 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "platform-tools", "adb.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return "adb";   // ultima spiaggia: quello nel PATH
    }

    /// <summary>
    /// Chiudere butta via le modifiche al deck che non sono state salvate, e
    /// buttarle via in silenzio sarebbe scortese: si chiede, una volta sola.
    ///
    /// Ma solo se si sta uscendo davvero. La croce della finestra vuol dire
    /// « togliti di mezzo », non « stacca il tablet »: quella ritira la
    /// finestra nell'area di notifica e non chiede niente, perche' non c'e'
    /// niente da perdere.
    /// </summary>
    private void Chiusura(object? sender, CancelEventArgs e)
    {
        // Senza icona non ci si nasconde: la finestra sparirebbe senza lasciare
        // niente da premere per riaprirla, e l'unico modo di riprenderla sarebbe
        // il Gestione attivita'.
        if (!uscita && vassoio is { Acceso: true })
        {
            e.Cancel = true;
            NascondiNelVassoio();
            return;
        }

        if (deckModificato)
        {
            var scelta = MessageBox.Show(this,
                "Il deck e' stato modificato e non e' stato salvato.\nSalvarlo prima di chiudere?",
                "TabDeck", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (scelta == MessageBoxResult.Cancel)
            {
                // Ci si ripensa: la finestra resta, e la croce torna a voler
                // dire « nascondi ». Senza questa riga, la volta dopo la croce
                // spegnerebbe tutto senza avvisare.
                uscita = false;
                e.Cancel = true;
                return;
            }
            if (scelta == MessageBoxResult.Yes) SalvaDeck(false);
        }

        attesa?.Stop();
        controlloCavo?.Stop();
        richiamo.Dispose();
        estensioni?.Dispose();
        salvaschermo?.Dispose();
        engine.Dispose();
        SalvaImpostazioni();
        vassoio?.Dispose();
    }

    /// <summary>
    /// Il tablet ha premuto « Connetti al PC » e ha detto dove si trova.
    ///
    /// Ci si collega e basta, senza chiedere conferma: la conferma e'
    /// esattamente il pulsante che e' appena stato premuto, e chiederla di
    /// nuovo qui vorrebbe dire farsi trovare davanti al PC per rispondere - il
    /// contrario di quello a cui serve.
    ///
    /// Arriva dal thread di rete del richiamo, quindi si passa dal Dispatcher:
    /// Collega() tocca i campi della finestra.
    /// </summary>
    private void TabletSiAnnuncia(FoundTablet tablet)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => TabletSiAnnuncia(tablet));
            return;
        }

        if (engine.Connected)
        {
            // Gia' collegati: l'annuncio ha fatto il suo lavoro un'altra volta.
            return;
        }

        if (!settings.Comportamento.AccettaChiamata)
        {
            Registra($"{tablet.Model} chiama da {tablet.Address}, ma collegarsi alla chiamata e' spento "
                     + "in Gestione › Impostazioni: premi « Collega in rete ».");
            return;
        }

        Registra($"{tablet.Model} chiama da {tablet.Address}.");
        caricamento = true;
        CampoIndirizzo.Text = tablet.Address;
        caricamento = false;
        Collega("wifi");
    }

    // ---- registro e stato ----

    private void Registra(string message) => Registra(message, false);

    /// <param name="guaio">Colora il messaggio in fondo: qualcosa non e' andato.</param>
    private void Registra(string message, bool guaio)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Registra(message, guaio));
            return;
        }

        TestoOra.Text = DateTime.Now.ToString("HH:mm:ss");
        TestoMessaggio.Text = message;
        TestoMessaggio.Foreground = (Brush)FindResource(guaio ? "Attenzione" : "TestoTenue");
        SpiaMessaggio.Fill = (Brush)FindResource(guaio ? "Attenzione" : "TestoTenue");

        CampoRegistro.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        CampoRegistro.ScrollToEnd();
    }

    private void CollegamentoCambiato(bool connected)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => CollegamentoCambiato(connected));
            return;
        }

        SpiaStato.Fill = (Brush)FindResource(connected ? "Acceso" : "Spento");
        TestoStato.Text = connected ? $"{engine.Transport} · {engine.Peer}" : "Scollegato";

        // Lo stesso stato sull'icona: a finestra nascosta e' l'unico posto dove
        // si puo' ancora leggere, ed e' li' che si va a guardare.
        string dice = connected ? $"Collegato via {engine.Transport} a {engine.Peer}" : "Scollegato";
        if (vassoio is not null)
        {
            vassoio.Stato = dice;
            vassoio.Suggerimento("TabDeck — " + dice);
        }

        BtnUsb.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        BtnWifi.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        BtnScollega.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        BtnScollega.Content = "Scollega";
        BtnSchermo.IsEnabled = connected;
        MostraStatoTablet();

        // Lo stesso stato, in grande, sulla dashboard: la pastiglia in alto dice
        // se c'e' o non c'e', qui si dice anche per che strada e cosa fare.
        DashboardSpia.Fill = (Brush)FindResource(connected ? "Acceso" : "Spento");
        RiquadroCollegamento.BorderBrush = (Brush)FindResource(connected ? "AzioneSopra" : "Bordo");
        DashboardStato.Text = connected
            ? (engine.Transport.Equals("usb", StringComparison.OrdinalIgnoreCase)
                ? "Collegato col cavo"
                : "Collegato in rete")
            : "Scollegato";
        DashboardDettaglio.Text = connected
            ? $"Il tablet risponde a {engine.Peer}. Manda lo schermo, oppure scegli cosa fargli vedere qui sotto."
            : "Attacca il cavo e premi « Collega col cavo », oppure accendi il tablet sulla "
              + "rete di casa e premi « Collega in rete »: l'indirizzo lo trova da solo.";

        // Collegati, i tentativi automatici non servono piu'; scollegati, se
        // stanno andando la dashboard lo deve dire al posto del testo di sopra.
        if (connected)
        {
            FermaAttesa();
            Notifica(settings.Comportamento.NotificaCollegato, "Tablet collegato", dice);
            AggiornaInternet();
            // Col cavo l'app del tablet si puo' confrontare con TabDeck.apk: solo lettura.
            if (engine.Transport.Equals("usb", StringComparison.OrdinalIgnoreCase)) ControllaAppTablet();
        }
        else
        {
            AggiornaAttesa();
            AggiornaInternet();
        }

        if (!connected)
        {
            TestoBanda.Text = "";
            DashboardBanda.Text = "—";
            SchermoCambiato(false);
            return;
        }

        // Collegarsi non riscrive piu' il tablet: quello che ha in mano se lo
        // tiene lui. Se di qua c'e' qualcosa di piu' nuovo lo si dice adesso,
        // che e' il momento in cui si puo' fare qualcosa.
        if (settings.DeckDaMandare)
            Registra("Il deck qui e' cambiato dall'ultimo invio: premi « Manda al tablet ».");
        if (settings.LuciDaMandare)
            Registra("Le luci qui sono cambiate dall'ultimo invio: Gestione › Luci, « Manda al tablet ».");
    }

    private void SchermoCambiato(bool on)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SchermoCambiato(on));
            return;
        }

        BtnSchermo.Content = on ? "Ferma lo schermo" : "Manda lo schermo";
        if (!on)
        {
            TestoBanda.Text = "";
            DashboardBanda.Text = "—";
        }
        AggiornaDashboardSchermo();
    }

    /// <summary>
    /// Il riquadro dello schermo sulla dashboard. Sono due cose diverse, e vanno
    /// dette insieme: che ci sia un monitor virtuale acceso, e che ci stia
    /// viaggiando sopra l'immagine.
    /// </summary>
    private void AggiornaDashboardSchermo()
    {
        if (settings.Screen.Length > 0)
        {
            var vero = schermi.FirstOrDefault(
                m => string.Equals(m.DeviceName, settings.Screen, StringComparison.OrdinalIgnoreCase));

            DashboardSchermoTitolo.Text = "SCHERMO " + Corto(settings.Screen);
            DashboardSchermo.Text = vero is null
                ? "Non c'e'"
                : $"{vero.Bounds.W}x{vero.Bounds.H}";
            return;
        }

        DashboardSchermoTitolo.Text = "SCHERMO VIRTUALE";
        var virtuale = schermi.FirstOrDefault(m => m.IsVirtual);

        if (virtuale is null)
        {
            DashboardSchermo.Text = "Spento";
            return;
        }

        DashboardSchermo.Text = $"{virtuale.Bounds.W}x{virtuale.Bounds.H}";
    }

    /// <summary>La pastiglia dello stato riporta alla dashboard da dove si e'.</summary>
    private void Stato_Click(object sender, RoutedEventArgs e) => Schede.SelectedItem = SchedaDashboard;

    private void CollegaUsb_Click(object sender, RoutedEventArgs e) => Collega("usb");

    private void CollegaWifi_Click(object sender, RoutedEventArgs e) => Collega("wifi");

    private void Collega(string trasporto)
    {
        SalvaImpostazioni();
        Mouse.OverrideCursor = Cursors.Wait;
        string error;
        try
        {
            error = engine.Connect(trasporto, adbPath);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (error.Length > 0)
        {
            Registra("Non si collega: " + error, true);
            return;
        }

        // Collegandosi in rete senza indirizzo, il motore lo cerca e lo trova:
        // va rimesso nel campo, altrimenti si resta a guardare una casella
        // vuota mentre il collegamento e' aperto.
        AllineaIndirizzo();
    }

    /// <summary>
    /// A collegamento aperto scollega. A collegamento chiuso il pulsante c'e'
    /// solo mentre si riprova, e allora vuol dire « smetti ».
    /// </summary>
    private void Scollega_Click(object sender, RoutedEventArgs e)
    {
        bool riprovava = InAttesa;
        FermaAttesa();

        if (engine.Connected) engine.Disconnect();
        else if (riprovava) Registra("Smesso di riprovare.");
    }

    private void Schermo_Click(object sender, RoutedEventArgs e)
    {
        if (engine.ScreenOn)
        {
            engine.StopScreen();
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        string error;
        try
        {
            error = engine.StartScreen();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (error.Length > 0) Registra("Schermo non partito: " + error, true);
        AggiornaSchermi();
        CercaDriver();
    }

    // ---- impostazioni ----

    private void CaricaImpostazioni()
    {
        MostraImpostazioni();
        caricamento = true;

        foreach (var spunta in new[] { SpuntaSezDashboard, SpuntaSezDeck, SpuntaSezSchermo, SpuntaSezCasa, SpuntaSezOrologio,
                                       SpuntaSenzaDashboard, SpuntaSenzaDeck, SpuntaSenzaCasa, SpuntaSenzaOrologio })
            spunta.Click += (_, _) =>
            {
                settings.Tablet.Sezioni.Cambiate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SalvaImpostazioni();
            };

        CursoreFps.ValueChanged += (_, _) =>
        {
            TestoFps.Text = ((int)CursoreFps.Value).ToString();
            if (!caricamento) SalvaImpostazioni();
        };
        CursoreQualita.ValueChanged += (_, _) =>
        {
            TestoQualita.Text = ((int)CursoreQualita.Value).ToString();
            if (!caricamento) SalvaImpostazioni();
        };
        SpuntaCursore.Click += (_, _) => SalvaImpostazioni();
        ElencoAcceso.SelectionChanged += (_, _) => { if (!caricamento) SalvaImpostazioni(); };
        ElencoInternet.SelectionChanged += (_, _) => { if (!caricamento) SalvaImpostazioni(); };
        SpuntaLuceSistema.Click += (_, _) =>
        {
            CursoreLuce.IsEnabled = SpuntaLuceSistema.IsChecked != true;
            SalvaImpostazioni();
        };
        CursoreLuce.ValueChanged += (_, _) =>
        {
            TestoLuce.Text = SpuntaLuceSistema.IsChecked == true ? "auto" : $"{(int)CursoreLuce.Value}%";
            if (!caricamento) SalvaImpostazioni();
        };
        ElencoGesti.SelectionChanged += (_, _) => { if (!caricamento) SalvaImpostazioni(); };
        CampoIndirizzo.LostFocus += (_, _) => SalvaImpostazioni();
        CampoPorta.LostFocus += (_, _) => SalvaImpostazioni();

        caricamento = false;
        AggiornaDashboardImmagine();
        CollegamentoCambiato(false);
    }

    /// <summary>
    /// Mette nei campi quel che c'e' in <see cref="settings"/>. A parte dagli eventi
    /// perche' serve di nuovo cambiando postazione, e gli eventi vanno attaccati una volta.
    /// </summary>
    private void MostraImpostazioni()
    {
        bool prima = caricamento;
        caricamento = true;

        CampoIndirizzo.Text = settings.Host;
        CampoPorta.Text = settings.Port.ToString();

        CursoreFps.Value = settings.Fps;
        CursoreQualita.Value = settings.Quality;
        SpuntaCursore.IsChecked = settings.DrawCursor;
        ElencoGesti.SelectedIndex = settings.Touch.Mode.Equals("pointer", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        ElencoAcceso.SelectedIndex = settings.Tablet.KeepAwake switch
        {
            "never" => 0,
            "always" => 2,
            _ => 1,
        };
        SpuntaLuceSistema.IsChecked = settings.Tablet.Brightness < 0;
        ElencoInternet.SelectedIndex = settings.Tablet.Internet switch { "dati" => 1, "tutto" => 2, _ => 0 };
        CursoreLuce.Value = settings.Tablet.Brightness < 0 ? 60 : settings.Tablet.Brightness;
        CursoreLuce.IsEnabled = settings.Tablet.Brightness >= 0;
        TestoLuce.Text = settings.Tablet.Brightness < 0 ? "auto" : settings.Tablet.Brightness + "%";

        var sezioni = settings.Tablet.Sezioni;
        SpuntaSezDashboard.IsChecked = sezioni.Dashboard;
        SpuntaSezDeck.IsChecked = sezioni.Deck;
        SpuntaSezSchermo.IsChecked = sezioni.Schermo;
        SpuntaSezCasa.IsChecked = sezioni.Casa;
        SpuntaSezOrologio.IsChecked = sezioni.Orologio;
        var senza = settings.Tablet.SenzaPc;
        SpuntaSenzaDashboard.IsChecked = senza.Dashboard;
        SpuntaSenzaDeck.IsChecked = senza.Deck;
        SpuntaSenzaCasa.IsChecked = senza.Casa;
        SpuntaSenzaOrologio.IsChecked = senza.Orologio;

        TestoFps.Text = settings.Fps.ToString();
        TestoQualita.Text = settings.Quality.ToString();

        ElencoApertura.SelectedIndex = settings.Comportamento.CollegaAllApertura switch
        {
            "usb" => 1,
            "wifi" => 2,
            "auto" => 3,
            _ => 0,
        };

        caricamento = prima;
    }

    private void AggiornaDashboardImmagine()
    {
        DashboardImmagine.Text = $"{(int)CursoreFps.Value} fps";
        DashboardQualita.Text = $"Qualita' JPEG {(int)CursoreQualita.Value}.";
    }

    private void SalvaImpostazioni()
    {
        if (caricamento) return;

        settings.Host = CampoIndirizzo.Text.Trim();
        if (int.TryParse(CampoPorta.Text, out int porta) && porta is > 0 and < 65536)
            settings.Port = porta;
        settings.Fps = (int)CursoreFps.Value;
        settings.Quality = (int)CursoreQualita.Value;
        settings.DrawCursor = SpuntaCursore.IsChecked == true;
        settings.Touch.Mode = ElencoGesti.SelectedIndex == 1 ? "pointer" : "touch";

        settings.Tablet.KeepAwake = ElencoAcceso.SelectedIndex switch
        {
            0 => "never",
            2 => "always",
            _ => "screen",
        };
        settings.Tablet.Brightness = SpuntaLuceSistema.IsChecked == true ? -1 : (int)CursoreLuce.Value;
        settings.Tablet.Internet = ElencoInternet.SelectedIndex switch { 1 => "dati", 2 => "tutto", _ => "no" };
        settings.Tablet.Sezioni.Dashboard = SpuntaSezDashboard.IsChecked == true;
        settings.Tablet.Sezioni.Deck = SpuntaSezDeck.IsChecked == true;
        settings.Tablet.Sezioni.Schermo = SpuntaSezSchermo.IsChecked == true;
        settings.Tablet.Sezioni.Casa = SpuntaSezCasa.IsChecked == true;
        settings.Tablet.Sezioni.Orologio = SpuntaSezOrologio.IsChecked == true;
        settings.Tablet.SenzaPc.Dashboard = SpuntaSenzaDashboard.IsChecked == true;
        settings.Tablet.SenzaPc.Deck = SpuntaSenzaDeck.IsChecked == true;
        settings.Tablet.SenzaPc.Casa = SpuntaSenzaCasa.IsChecked == true;
        settings.Tablet.SenzaPc.Orologio = SpuntaSenzaOrologio.IsChecked == true;

        ScriviImpostazioni();

        AggiornaDashboardImmagine();

        engine.SettingsChanged();
        AggiornaInternet();
        // Le preferenze del tablet contano solo se il tablet le riceve: si
        // rimandano a ogni salvataggio, che e' economico e evita di doversi
        // ricordare quali cambiano cosa.
        if (engine.Connected) engine.SendTabletConfig();
    }

    // ---- schermi ----

    private void AggiornaSchermi_Click(object sender, RoutedEventArgs e) => AggiornaSchermi();

    private void AggiornaSchermi()
    {
        schermi = Displays.List();
        var virtuale = schermi.FirstOrDefault(m => m.IsVirtual);

        RiempiSorgenti();

        TestoSchermi.Text = virtuale is null
            ? "Nessuno schermo virtuale acceso. Compare quando premi « Manda lo schermo »."
            : $"Schermo virtuale acceso: {virtuale.Bounds.W}x{virtuale.Bounds.H} "
              + $"in ({virtuale.Bounds.X},{virtuale.Bounds.Y}) — {virtuale.DeviceName}";

        AggiornaRisoluzioni(virtuale);
        AggiornaDashboardSchermo();
    }

    /// <summary>
    /// L'elenco di quel che si puo' mandare: lo schermo virtuale in cima,
    /// perche' e' quello che serve quasi sempre, e sotto i monitor veri.
    ///
    /// Quello virtuale sta nell'elenco anche quando non e' acceso: non e' una
    /// cosa che c'e' o non c'e', e' una cosa che nasce quando si preme
    /// « Manda lo schermo ». I monitor veri invece si elencano solo se ci sono.
    /// </summary>
    private void RiempiSorgenti()
    {
        bool prima = caricamento;
        caricamento = true;

        sorgenti.Clear();
        ElencoSorgenti.Items.Clear();

        sorgenti.Add("");
        ElencoSorgenti.Items.Add("Schermo virtuale — creato quando serve");

        foreach (var m in schermi.Where(m => !m.IsVirtual))
        {
            sorgenti.Add(m.DeviceName);
            ElencoSorgenti.Items.Add(
                $"{Corto(m.DeviceName)} — {m.Bounds.W}x{m.Bounds.H}"
                + (m.IsPrimary ? ", principale" : "") + $" — {m.Adapter}");
        }

        int scelto = sorgenti.FindIndex(
            n => string.Equals(n, settings.Screen, StringComparison.OrdinalIgnoreCase));

        // Lo schermo salvato puo' non esserci piu': staccato, spento. Non lo si
        // cambia di nascosto — cambiarlo vorrebbe dire mandare al tablet un
        // altro monitor senza dirlo — ma lo si dice, e l'elenco resta sul
        // virtuale finche' non si sceglie.
        if (scelto < 0)
        {
            ElencoSorgenti.SelectedIndex = 0;
            TestoSorgente.Text = $"Lo schermo scelto ({settings.Screen}) adesso non c'e'. "
                                 + "Finche' non ne scegli un altro non parte niente.";
            TestoSorgente.Foreground = (Brush)FindResource("Attenzione");
        }
        else
        {
            ElencoSorgenti.SelectedIndex = scelto;
            DiciSorgente();
        }

        caricamento = prima;
    }

    /// <summary>
    /// Il nome che Windows da' a uno schermo e' « \\.\DISPLAY2 »: nell'elenco
    /// ne basta la coda, il resto e' rumore uguale per tutti. Quello salvato
    /// resta pero' il nome intero, che e' l'unico che Windows riconosce.
    /// </summary>
    private static string Corto(string deviceName)
    {
        int taglio = deviceName.LastIndexOf('\\');
        return taglio >= 0 ? deviceName[(taglio + 1)..] : deviceName;
    }

    /// <summary>La riga sotto l'elenco: cosa comporta la scelta fatta.</summary>
    private void DiciSorgente()
    {
        bool virtuale = settings.Screen.Length == 0;
        TestoSorgente.Text = virtuale
            ? "Nasce quando premi « Manda lo schermo » e sparisce quando fermi, chiudi o "
              + "stacchi il tablet. Windows lo vede come un monitor in piu': ci trascini "
              + "dentro le finestre, e viene portato da solo ai 1024x600 del pannello."
            : "Va al tablet quello che vedi tu su quel monitor, ridotto per stare nel "
              + "pannello: si legge quel che e' grande, non il testo piccolo. La sua "
              + "risoluzione non viene toccata, e se lo stacchi non parte niente.";
        TestoSorgente.Foreground = (Brush)FindResource("TestoTenue");
    }

    /// <summary>
    /// Cambiare sorgente mentre si trasmette non ha senso a meta': si ferma, si
    /// cambia, e si riparte quando lo chiedi. Fermare da soli e' meno peggio
    /// che continuare a mandare il monitor di prima dopo averne scelto un
    /// altro.
    /// </summary>
    private void Sorgente_Changed(object sender, SelectionChangedEventArgs e)
    {
        int i = ElencoSorgenti.SelectedIndex;
        if (caricamento || i < 0 || i >= sorgenti.Count) return;
        if (string.Equals(sorgenti[i], settings.Screen, StringComparison.Ordinal)) return;

        bool andava = engine.ScreenOn;
        if (andava) engine.StopScreen();

        settings.Screen = sorgenti[i];
        SalvaImpostazioni();
        DiciSorgente();
        AggiornaDashboardSchermo();

        Registra(settings.Screen.Length == 0
            ? "Al tablet andra' lo schermo virtuale."
            : $"Al tablet andra' {settings.Screen}."
              + (andava ? " Ho fermato la trasmissione: premi « Manda lo schermo » per ripartire." : ""));
    }

    private void AggiornaRisoluzioni(MonitorInfo? virtuale)
    {
        ElencoRisoluzioni.Items.Clear();
        if (virtuale is null) return;

        foreach (var (w, h) in Displays.AvailableModes(virtuale.DeviceName))
        {
            ElencoRisoluzioni.Items.Add($"{w}x{h}");
            if (w == virtuale.Bounds.W && h == virtuale.Bounds.H)
                ElencoRisoluzioni.SelectedIndex = ElencoRisoluzioni.Items.Count - 1;
        }
    }

    private void ApplicaRisoluzione_Click(object sender, RoutedEventArgs e)
    {
        if (ElencoRisoluzioni.SelectedItem is not string mode) return;

        var parts = mode.Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h))
            return;

        PortaA(w, h);
    }

    /// <summary>I pixel del pannello: e' la risoluzione buona, a un pulsante di distanza.</summary>
    private void RisoluzionePannello_Click(object sender, RoutedEventArgs e) => PortaA(1024, 600);

    private void PortaA(int w, int h)
    {
        var virtuale = Displays.List().FirstOrDefault(m => m.IsVirtual);
        if (virtuale is null)
        {
            Registra("Nessuno schermo virtuale acceso.", true);
            return;
        }

        string error = Displays.SetResolution(virtuale.DeviceName, w, h);
        Registra(error.Length == 0
            ? $"Schermo virtuale portato a {w}x{h}."
            : $"Risoluzione non cambiata: {error}", error.Length > 0);

        AggiornaSchermi();
        engine.SettingsChanged();
    }

    // ---- schermo virtuale ----

    private void CercaDriver_Click(object sender, RoutedEventArgs e) => CercaDriver();

    private void CercaDriver()
    {
        adattatori = Displays.FindVirtualAdapters();

        if (adattatori.Count == 0)
        {
            TestoDriver.Text = "Nessun driver di schermo virtuale installato.";
            TestoDriverAiuto.Visibility = Visibility.Visible;
            BtnAccendiSchermo.IsEnabled = false;
            BtnSpegniSchermo.IsEnabled = false;
            DashboardDriver.Text = "Nessun driver installato.";
            return;
        }

        var acceso = adattatori.FirstOrDefault(a => a.Enabled);
        TestoDriver.Text = string.Join(Environment.NewLine,
            adattatori.Select(a => $"{a.Name} — {(a.Enabled ? "acceso" : "spento")}"));
        TestoDriverAiuto.Visibility = Visibility.Collapsed;
        BtnAccendiSchermo.IsEnabled = acceso is null;
        BtnSpegniSchermo.IsEnabled = acceso is not null;
        DashboardDriver.Text = acceso is null ? "Il driver e' spento." : "Il driver e' acceso.";
    }

    private void AccendiSchermo_Click(object sender, RoutedEventArgs e) => CambiaSchermoVirtuale(true);

    private void SpegniSchermo_Click(object sender, RoutedEventArgs e) => CambiaSchermoVirtuale(false);

    private void CambiaSchermoVirtuale(bool acceso)
    {
        var target = adattatori.FirstOrDefault(a => a.Enabled != acceso);
        if (target is null)
        {
            Registra("Nessun adattatore virtuale da cambiare.", true);
            return;
        }

        Registra($"{(acceso ? "Accendo" : "Spengo")} {target.Name}: Windows chiedera' il consenso.");
        string error = Displays.SetAdapterEnabled(target.InstanceId, acceso);
        Registra(error.Length == 0
            ? $"{target.Name} ora e' {(acceso ? "acceso" : "spento")}."
            : $"Non riuscito: {error}", error.Length > 0);

        CercaDriver();
        AggiornaSchermi();
    }

    // ---- collegamento ----

    private void CercaUsb_Click(object sender, RoutedEventArgs e)
    {
        var devices = Link.UsbDevices(adbPath);
        TestoUsb.Text = devices.Count == 0
            ? "Nessun tablet visto da adb."
            : $"Trovato: {string.Join(", ", devices)}";
        Registra(devices.Count == 0
            ? "adb non vede nessun dispositivo: cavo, debug USB, autorizzazione sul tablet."
            : $"adb vede {devices.Count} dispositivo/i.", devices.Count == 0);
    }

    private void CercaWifi_Click(object sender, RoutedEventArgs e)
    {
        Registra("Cerco il tablet in rete locale...");
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            CercaWifi();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void CercaWifi()
    {
        trovati = Link.Discover();
        ElencoTrovati.Items.Clear();
        foreach (var t in trovati) ElencoTrovati.Items.Add($"{t.Address} — {t.Model}");

        TestoTrovatiVuoto.Visibility = trovati.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (trovati.Count == 0)
        {
            TestoTrovatiVuoto.Text = "Nessuna risposta: il tablet e' spento, su un'altra rete, o senza TabDeck aperto.";
            Registra("Nessuna risposta. Il tablet deve essere acceso, con TabDeck aperto e sulla stessa rete.", true);
            return;
        }

        ElencoTrovati.SelectedIndex = 0;
        Registra(trovati.Count == 1
            ? $"Trovato {trovati[0].Model} a {trovati[0].Address}."
            : $"Trovati {trovati.Count} tablet.");
    }

    private void ElencoTrovati_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = ElencoTrovati.SelectedIndex;
        if (index < 0 || index >= trovati.Count) return;
        CampoIndirizzo.Text = trovati[index].Address;
        SalvaImpostazioni();
    }

    // ---- comandi al tablet ----

    private void MostraDeck_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.ShowOnTablet(false, false, vai: true));

    private void MostraSchermo_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.ShowOnTablet(true, true));

    private void BarraApri_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.SendCommand("railShow"));

    private void BarraChiudi_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.SendCommand("railHide"));

    private void TabletImpostazioni_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.SendCommand("settings"));

    private void TabletWifi_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.SendCommand("wifi"));

    private void TabletRiavvia_Click(object sender, RoutedEventArgs e) => AlTablet(() => engine.SendCommand("restart"));

    /// <summary>Un comando ha senso solo se c'e' qualcuno che lo riceve.</summary>
    private void AlTablet(Action comando)
    {
        if (!engine.Connected)
        {
            Registra("Non sei collegato al tablet.", true);
            return;
        }
        comando();
    }

    // ---- registro ----

    private void CopiaRegistro_Click(object sender, RoutedEventArgs e)
    {
        if (CampoRegistro.Text.Length == 0) return;
        try
        {
            Clipboard.SetText(CampoRegistro.Text);
            Registra("Registro copiato negli appunti.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Gli appunti sono una risorsa condivisa: capita che un altro
            // programma li tenga aperti proprio in quel momento.
            Registra("Gli appunti erano occupati: riprova.", true);
        }
    }

    private void SvuotaRegistro_Click(object sender, RoutedEventArgs e) => CampoRegistro.Clear();
}
