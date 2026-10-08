using System.Diagnostics;

namespace TabDeck;

/// <summary>
/// Il motore, in due parti che si accendono separatamente.
///
///   Collegamento  apre il canale col tablet, via cavo o via rete, e gli manda
///                 la griglia del deck. Costa niente: nessuna cattura, nessuna
///                 compressione, nessuno schermo in piu' in Windows.
///   Schermo       crea il monitor virtuale e comincia a mandarlo.
///
/// Sono separate perche' il deck si usa quasi sempre da solo: tenere acceso uno
/// schermo che nessuno guarda vuol dire un monitor fantasma in Windows che si
/// mangia le finestre, e un tablet che scalda per niente.
///
/// Che cosa mandare lo si sceglie: lo schermo virtuale, che e' quello di tutti
/// i giorni, oppure un monitor vero fra quelli attaccati. Quello che non si fa
/// e' **ripiegare**: se la cosa scelta non c'e' — il driver che manca, il
/// monitor staccato — non parte niente e si dice perche'. Prima il ripiego
/// c'era, e mandava il desktop grande rimpicciolito su sette pollici: un
/// risultato inutile che per giunta nascondeva il guasto vero.
/// </summary>
public sealed class Engine : IDisposable
{
    /// <summary>
    /// Quanto si aspetta la conferma del tablet prima di mandare comunque il
    /// frame dopo. Serve solo a non restare bloccati se l'app dall'altra parte
    /// muore fra un PRESENT e il suo ACK.
    /// </summary>
    private const int AckTimeoutMs = 1000;

    private readonly Settings settings;
    private readonly Link link = new();
    private readonly InputInjector input = new();
    private readonly ActionRunner runner;

    private DeckConfig deck;
    private LuciConfig luci = new();

    /// <summary>
    /// Le icone gia' consegnate al tablet in questa sessione. Si svuota a ogni
    /// nuovo collegamento: dall'altra parte potrebbe esserci un tablet che non
    /// le ha mai viste.
    /// </summary>
    private readonly HashSet<string> iconeInviate = new(StringComparer.OrdinalIgnoreCase);

    private ScreenCapture? capture;

    private CancellationTokenSource? linkLife;
    private CancellationTokenSource? screenLife;
    private Thread? reader;
    private Thread? pump;

    /// <summary>Frame mandati e non ancora confermati: vedi il ciclo in Pump().</summary>
    private int inFlight;
    private long frameSentAt;
    private int skippedFrames;
    private volatile bool geometryDirty = true;

    /// <summary>Falso mentre sul tablet e' aperto il deck: non si cattura.</summary>
    private volatile bool tabletWatching;

    private int panelWidth, panelHeight;
    private Rect source;

    /// <summary>
    /// Adattatore acceso da noi, da rispegnere. Se resta null vuol dire che lo
    /// schermo virtuale c'era gia': non e' roba nostra e non lo si tocca.
    /// </summary>
    private VirtualAdapter? adapterWeTurnedOn;

    /// <summary>Riga di stato: fps effettivi e banda, aggiornata ogni secondo.</summary>
    public string Throughput { get; private set; } = "";

    public event Action<string>? Log;
    /// <summary>Vero appena il collegamento e' aperto, falso quando cade.</summary>
    public event Action<bool>? ConnectionChanged;
    /// <summary>Vero mentre lo schermo viaggia verso il tablet.</summary>
    public event Action<bool>? ScreenChanged;

    public bool Connected => link.IsConnected;
    public bool ScreenOn => pump?.IsAlive == true;
    public string Transport => link.Transport;
    public string Peer => link.Peer;

    public Engine(Settings settings, DeckConfig deck, LuciConfig luci)
    {
        this.settings = settings;
        this.deck = deck;
        this.luci = luci;
        runner = new ActionRunner(input, Say);
        // Un passo « Cambia profilo » in mezzo a una sequenza: il motore non
        // sa cambiarlo, la finestra si'. Arriva dal thread dell'azione, e chi
        // ascolta si passa il Dispatcher come per la pressione dal tablet.
        runner.Profilo = nome => ProfiloChiesto?.Invoke(nome);
        InputInjector.Log = Say;

        link.OnReady += OnReady;
        link.OnTouch += OnTouch;
        link.OnWheel += delta => input.Wheel(delta);
        link.OnPress += OnPress;
        link.OnAck += () => Interlocked.Decrement(ref inFlight);
        link.OnPlugin += (tipo, dati, lunghezza) => FramePlugin?.Invoke(tipo, dati, lunghezza);
        link.OnSalvaschermo += json => FrameSalvaschermo?.Invoke(json);
        link.OnConfig += json => ConfigDalTablet?.Invoke(json);
    }

    // ---- plugin ----

    /// <summary>
    /// Un frame di plugin dal tablet. Thread di rete, e il buffer si riusa: chi
    /// ascolta copia prima di tornare. Il motore non sa cosa c'e' dentro, ed e'
    /// questo che fa di un plugin una cosa che si puo' togliere.
    /// </summary>
    public event Action<byte, byte[], int>? FramePlugin;

    /// <summary>Un frame JSON di plugin verso il tablet. Scollegati, non parte niente.</summary>
    public void MandaPlugin(byte tipo, string json)
    {
        if (link.IsConnected) link.SendJson(tipo, json);
    }

    /// <summary>Un frame binario di plugin verso il tablet: il pacchetto di un'estensione.</summary>
    public void MandaPluginBytes(byte tipo, byte[] dati)
    {
        if (link.IsConnected) link.SendBytes(tipo, dati);
    }

    /// <summary>
    /// Porta il tablet sul pannello di un plugin. Mai mentre sta mostrando lo
    /// schermo: chi sta lavorando sul monitor remoto non deve vederselo portar
    /// via da un cambio di scheda sul PC.
    /// </summary>
    public void MostraPlugin(string id)
    {
        if (!link.IsConnected || ScreenOn) return;
        link.SendJson(Proto.Mode, ConfigFile.Wire(new { mode = "plugin:" + id, full = false }));
    }

    /// <summary>Scelte fatte sul tablet: oggi le sezioni della barra. Thread di rete.</summary>
    public event Action<string>? ConfigDalTablet;

    // ---- salvaschermo ----

    /// <summary>{"mancano":[nomi]} dal tablet. Thread di rete.</summary>
    public event Action<string>? FrameSalvaschermo;

    public void MandaSalvaschermo(string json)
    {
        if (link.IsConnected) link.SendJson(Proto.Salvaschermo, json);
    }

    /// <summary>Una foto gia' pronta per il tablet: [u16 lunghezza nome][nome][JPEG].</summary>
    public void MandaFotoSalvaschermo(string nome, byte[] jpeg)
    {
        if (!link.IsConnected) return;
        byte[] n = System.Text.Encoding.ASCII.GetBytes(nome);
        var frame = new byte[2 + n.Length + jpeg.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)n.Length);
        n.CopyTo(frame, 2);
        jpeg.CopyTo(frame, 2 + n.Length);
        link.SendBytes(Proto.SalvaschermoFoto, frame);
    }

    private void Say(string message)
    {
        if (!zitto) Log?.Invoke(message);
    }

    /// <summary>
    /// Vero mentre un tentativo automatico cerca il tablet: « non risponde,
    /// cerco in rete » ogni quindici secondi riempirebbe il registro di righe
    /// tutte uguali. L'esito lo scrive chi ha chiesto il tentativo.
    /// </summary>
    private volatile bool zitto;

    /// <summary>
    /// Un collegamento alla volta. Adesso i tentativi possono partire anche da
    /// un thread a parte — all'apertura, al cavo attaccato, dopo una caduta —
    /// e due Connect intrecciati aprirebbero due socket verso lo stesso tablet.
    /// </summary>
    private readonly object connectLock = new();

    /// <summary>
    /// Il collegamento e' caduto da solo: il tablet si e' spento, il cavo e'
    /// stato staccato, la rete e' sparita. Non arriva per « Scollega », che e'
    /// una scelta e non un guasto. Thread di rete.
    /// </summary>
    public event Action? Caduto;

    // ---- collegamento ----

    /// <summary>
    /// Apre il canale col tablet. <paramref name="transport"/> vale "usb" o
    /// "wifi": lo decide chi preme il pulsante, non un'impostazione salvata,
    /// perche' e' una scelta che cambia da un momento all'altro — il cavo
    /// quando si lavora alla scrivania, la rete quando si e' in poltrona.
    /// </summary>
    /// <param name="silenzioso">Non scrivere i passi intermedi: vedi <see cref="zitto"/>.</param>
    public string Connect(string transport, string adbPath, bool silenzioso = false)
    {
        lock (connectLock)
        {
            if (Connected) return "";

            zitto = silenzioso;
            try
            {
                return ConnectLocked(transport, adbPath);
            }
            finally
            {
                zitto = false;
            }
        }
    }

    private string ConnectLocked(string transport, string adbPath)
    {
        bool wifi = transport.Equals("wifi", StringComparison.OrdinalIgnoreCase);
        string error = wifi ? ConnectOverWifi() : link.ConnectUsb(adbPath, settings.UsbPort);
        if (error.Length > 0) return error;
        zitto = false;

        settings.Transport = wifi ? "wifi" : "usb";
        tabletWatching = false;
        // Un tablet nuovo, o lo stesso dopo un riavvio: quel che gli si era
        // mandato in una sessione precedente non e' piu' una garanzia.
        iconeInviate.Clear();
        linkLife = new CancellationTokenSource();

        reader = new Thread(ReadLoop) { IsBackground = true, Name = "tabdeck-read" };
        reader.Start();

        // Il tablet resta nella sezione in cui era: ha una Dashboard sua, e riportarlo
        // al deck a ogni collegamento lo toglieva da li'. Lo schermo si accende
        // quando lo si chiede, e con lui la sua voce nella barra.
        SendTabletConfig();

        Say($"Collegato via {link.Transport} a {link.Peer}.");
        ConnectionChanged?.Invoke(true);
        return "";
    }

    /// <summary>
    /// Collegamento in rete. Se l'indirizzo non c'e' — o non risponde piu',
    /// perche' il router ne ha dato un altro — il tablet si cerca da soli
    /// invece di rimandare indietro un errore che chiede all'utente di sapere
    /// una cosa che il programma puo' scoprire in un secondo.
    /// </summary>
    private string ConnectOverWifi()
    {
        if (settings.Host.Length > 0)
        {
            string firstTry = link.ConnectWifi(settings.Host, settings.Port);
            if (firstTry.Length == 0) return "";
            Say($"{settings.Host} non risponde, cerco il tablet in rete.");
        }
        else
        {
            Say("Nessun indirizzo salvato, cerco il tablet in rete.");
        }

        var found = Link.Discover();
        if (found.Count == 0)
        {
            return "nessun tablet trovato in rete: controlla che sia acceso, "
                 + "con TabDeck aperto e sulla stessa rete del PC";
        }

        Say($"Trovato {found[0].Model} a {found[0].Address}.");
        string error = link.ConnectWifi(found[0].Address, settings.Port);
        // L'indirizzo che ha funzionato si tiene: il giro dopo si parte da li'.
        if (error.Length == 0) settings.Host = found[0].Address;
        return error;
    }

    /// <summary>Chiude tutto: prima lo schermo, poi il canale.</summary>
    public void Disconnect()
    {
        StopScreen();
        linkLife?.Cancel();
        link.Disconnect();
        reader = null;
        Say("Scollegato.");
        ConnectionChanged?.Invoke(false);
    }

    private void ReadLoop()
    {
        try
        {
            link.ReadLoop(linkLife?.Token ?? CancellationToken.None);
        }
        finally
        {
            if (linkLife is { IsCancellationRequested: false })
            {
                Say("Il tablet si e' scollegato.");
                linkLife.Cancel();
                // Il socket va chiuso anche da qui. Senza, restava in CloseWait e
                // Link.IsConnected continuava a dire di si': il tablet riaperto
                // premeva « Connetti al PC » e la chiamata veniva scartata come
                // « gia' collegati », per sempre, finche' non si riavviava TabDeck.
                link.Disconnect();
                // Staccato il tablet, lo schermo virtuale non ha piu' ragione
                // di esistere: lasciarlo li' vorrebbe dire finestre che
                // scompaiono in un monitor che non guarda nessuno.
                StopScreen();
                ConnectionChanged?.Invoke(false);
                Caduto?.Invoke();
            }
        }
    }

    // ---- schermo ----

    /// <summary>
    /// Accende il monitor virtuale e comincia a mandarlo. Restituisce stringa
    /// vuota se e' andata, altrimenti il motivo gia' scritto per la finestra.
    /// </summary>
    public string StartScreen()
    {
        if (!Connected) return "collegati prima al tablet";
        if (ScreenOn) return "";

        var screen = ApriSorgente();
        if (screen is null)
        {
            return Virtuale
                ? "schermo virtuale non disponibile: senza quello non c'e' niente da mandare"
                : "lo schermo scelto non c'e' piu': scegline un altro in Gestione > Schermo";
        }

        source = screen.Bounds;
        capture = new ScreenCapture(settings.Quality, settings.TileCols, settings.TileRows);

        Volatile.Write(ref inFlight, 0);
        geometryDirty = true;
        screenLife = new CancellationTokenSource();

        pump = new Thread(Pump) { IsBackground = true, Name = "tabdeck-pump" };
        pump.Start();

        // Il tablet passa allo schermo: e' quello che si vuole vedere dopo aver
        // premuto questo pulsante. A piena larghezza, cosi' i pixel combaciano.
        ShowOnTablet(true, true);

        Say($"Mando {source.W}x{source.H} da {screen.DeviceName}.");
        ScreenChanged?.Invoke(true);
        return "";
    }

    /// <summary>Ferma la trasmissione e toglie il monitor virtuale.</summary>
    public void StopScreen()
    {
        bool wasOn = ScreenOn;

        screenLife?.Cancel();
        pump?.Join(500);
        pump = null;
        capture?.Dispose();
        capture = null;
        Throughput = "";

        if (wasOn && Connected) ShowOnTablet(false, false);
        CloseVirtualScreen();

        if (wasOn)
        {
            Say("Schermo fermato.");
            ScreenChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Vero quando quel che si manda e' lo schermo virtuale, che e' il caso
    /// normale. Uno schermo vero si sceglie a mano e si riconosce dal nome.
    /// </summary>
    private bool Virtuale => settings.Screen.Length == 0;

    /// <summary>
    /// Lo schermo da mandare, acceso se serve.
    ///
    /// Sono due mestieri diversi: uno schermo virtuale **si crea**, e se il
    /// driver non c'e' non si va da nessuna parte; uno schermo vero **c'e' gia'
    /// oppure non c'e'**, e in quel caso non si ripiega su un altro. Il ripiego
    /// silenzioso e' esattamente il guaio che questo codice aveva prima: al
    /// tablet finiva il desktop grande rimpicciolito, illeggibile, senza che
    /// nessuno l'avesse chiesto.
    /// </summary>
    private MonitorInfo? ApriSorgente() => Virtuale ? OpenVirtualScreen() : ApriSchermoVero();

    private MonitorInfo? ApriSchermoVero()
    {
        var voluto = Displays.List().FirstOrDefault(
            m => string.Equals(m.DeviceName, settings.Screen, StringComparison.OrdinalIgnoreCase));

        if (voluto is null)
        {
            Say($"Lo schermo {settings.Screen} non c'e' piu': staccato, spento, o "
                + "rinominato da Windows. Scegline un altro in Gestione > Schermo.");
            return null;
        }

        // La sua risoluzione non si tocca: e' un monitor che sta davanti a
        // qualcuno. A far stare l'immagine nel pannello ci pensa ApplyGeometry,
        // riducendola in trasmissione.
        Say($"Mando {voluto.DeviceName}: {voluto.Bounds.W}x{voluto.Bounds.H}, {voluto.Adapter}.");
        return voluto;
    }

    /// <summary>
    /// Accende l'adattatore virtuale se serve e aspetta che il monitor compaia.
    /// Se era gia' acceso lo si usa e basta, senza prendersene la proprieta'.
    /// </summary>
    private MonitorInfo? OpenVirtualScreen()
    {
        var existing = Displays.List().FirstOrDefault(m => m.IsVirtual);
        if (existing is not null) return existing;

        var adapter = Displays.FindVirtualAdapters().FirstOrDefault();
        if (adapter is null)
        {
            Say("Nessun driver di schermo virtuale installato.");
            return null;
        }

        Say($"Accendo lo schermo virtuale ({adapter.Name}).");
        string error = Displays.SetAdapterEnabled(adapter.InstanceId, true);
        if (error.Length > 0)
        {
            Say($"Non riesco ad accenderlo: {error}");
            return null;
        }

        adapterWeTurnedOn = adapter;
        var screen = Displays.WaitForVirtualScreen(true);
        if (screen is null)
        {
            Say("L'adattatore e' acceso ma il monitor non e' comparso.");
            return null;
        }

        Say($"Schermo virtuale pronto: {screen.Bounds.W}x{screen.Bounds.H}.");
        return screen;
    }

    /// <summary>Toglie lo schermo virtuale, ma solo se e' stato acceso da qui.</summary>
    private void CloseVirtualScreen()
    {
        var adapter = adapterWeTurnedOn;
        adapterWeTurnedOn = null;
        if (adapter is null) return;

        Say("Tolgo lo schermo virtuale.");
        string error = Displays.SetAdapterEnabled(adapter.InstanceId, false);
        if (error.Length > 0) Say($"Non riesco a toglierlo: {error}");
    }

    /// <summary>
    /// Lo schermo che si sta mandando, riletto adesso: puo' essersi spostato o
    /// aver cambiato risoluzione mentre trasmettevamo.
    ///
    /// Quello virtuale si cerca per « e' virtuale » e non per nome salvato: il
    /// nome cambia a ogni ricreazione del monitor, e cercarlo per nome faceva
    /// ricadere la cattura sullo schermo principale — al tablet arrivava il
    /// desktop grande rimpicciolito, illeggibile e inutile, invece del monitor
    /// appena acceso. Uno schermo vero invece **e'** il suo nome: se non c'e'
    /// piu' si torna a mani vuote, non su un altro.
    /// </summary>
    private MonitorInfo? CurrentScreen()
    {
        var elenco = Displays.List();
        return Virtuale
            ? elenco.FirstOrDefault(m => m.IsVirtual)
            : elenco.FirstOrDefault(m => string.Equals(m.DeviceName, settings.Screen,
                                                       StringComparison.OrdinalIgnoreCase));
    }

    // ---- ciclo di trasmissione ----

    private void Pump()
    {
        var token = screenLife!.Token;
        int frameInterval = Math.Max(20, 1000 / Math.Max(1, settings.Fps));
        var sw = Stopwatch.StartNew();
        long lastFullRefresh = 0, lastPace = 0;
        int framesSent = 0;
        long bytesSent = 0;

        while (!token.IsCancellationRequested)
        {
            long started = sw.ElapsedMilliseconds;

            if (geometryDirty) ApplyGeometry();

            // Controllo di flusso. Il tablet conferma ogni frame disegnato, e
            // finche' la conferma non arriva non se ne manda un altro: a 20 fps
            // il PXA986 non sta dietro, e senza freno i frame si accodavano nel
            // socket. La latenza cresceva a ogni giro e lo schermo sembrava
            // bloccato, mentre stava solo mostrando immagini di secondi prima.
            // Meglio saltare un frame che accumularlo: chi guarda vuole
            // l'ultimo, non tutti.
            //
            // Conferma mai arrivata: l'app dall'altra parte e' morta fra un
            // PRESENT e il suo ACK. Si riparte da zero invece di restare fermi.
            if (Volatile.Read(ref inFlight) > 0 && started - frameSentAt >= AckTimeoutMs)
                Volatile.Write(ref inFlight, 0);

            if (!tabletWatching)
            {
                // Il tablet sta guardando il deck: alla ripresa servira' un
                // frame intero, perche' quello che ha in memoria e' vecchio.
                lastFullRefresh = 0;
            }
            else if (Volatile.Read(ref inFlight) >= settings.MaxInFlight)
            {
                skippedFrames++;
            }
            else
            {
                bool full = started - lastFullRefresh >= settings.FullRefreshSeconds * 1000L;
                if (full) lastFullRefresh = started;

                try
                {
                    var tiles = capture!.Grab(full, settings.DrawCursor);
                    if (tiles.Count > 0)
                    {
                        foreach (var t in tiles)
                        {
                            link.SendTile(t.X, t.Y, t.W, t.H, t.Jpeg.AsSpan(0, t.Length));
                            bytesSent += t.Length;
                        }
                        link.SendPresent();
                        framesSent++;
                        Interlocked.Increment(ref inFlight);
                        frameSentAt = started;
                    }
                }
                catch (Exception e)
                {
                    Say($"Cattura non riuscita: {e.Message}");
                    Thread.Sleep(500);
                }
            }

            if (started - lastPace >= 1000)
            {
                long span = Math.Max(1, started - lastPace);
                Throughput = $"{framesSent * 1000.0 / span:F0} fps · "
                           + $"{bytesSent * 1000.0 / span / 1024:F0} KB/s"
                           + (skippedFrames > 0 ? $" · {skippedFrames} saltati" : "");
                framesSent = 0;
                bytesSent = 0;
                skippedFrames = 0;
                lastPace = started;
            }

            int spent = (int)(sw.ElapsedMilliseconds - started);
            if (spent < frameInterval) Thread.Sleep(frameInterval - spent);
        }
    }

    // ---- geometria ----

    private void OnReady(int w, int h, bool watching)
    {
        if (w > 0 && h > 0)
        {
            panelWidth = w;
            panelHeight = h;
            Say($"Pannello del tablet: {w}x{h}.");
        }

        if (tabletWatching != watching)
        {
            tabletWatching = watching;
            if (ScreenOn)
            {
                Say(watching
                    ? "Il tablet mostra lo schermo: riprendo a trasmettere."
                    : "Il tablet mostra il deck: smetto di catturare.");
            }
        }
        geometryDirty = true;

        // READY arriva anche quando l'app sul tablet si riaggancia dopo essere
        // stata ricreata: quel che aveva in volo e' perso, si riparte puliti.
        Volatile.Write(ref inFlight, 0);

        // Qui, una volta, partivano il deck e le luci. Non partono piu':
        // collegarsi non e' chiedere di riscrivere il tablet. Il tablet il suo
        // deck e le sue luci se li tiene — e' cosi' che funziona a PC spento —
        // e quello che sta qui puo' essere un montaggio a meta' lasciato ieri
        // sera. Vanno quando lo si chiede, con « Manda al tablet ».
    }

    private void ApplyGeometry()
    {
        geometryDirty = false;
        input.RefreshScreenGeometry();

        // Lo schermo puo' essersi spostato o aver cambiato risoluzione mentre
        // trasmettevamo: si rilegge invece di fidarsi di quello di prima.
        var screen = CurrentScreen();
        if (screen is not null) source = screen.Bounds;

        int outW = source.W, outH = source.H;

        // Con lo schermo virtuale la risoluzione la decidiamo noi: portarlo
        // agli stessi pixel del pannello e' l'unico modo di avere il testo
        // nitido, e costa un cambio di modalita' una volta sola.
        //
        // Con uno schermo vero no, mai: e' un monitor che sta davanti a
        // qualcuno, e rimpicciolirglielo sotto il naso per far comodo a un
        // pannello da sette pollici sarebbe il contrario di un servizio. Li'
        // l'immagine si riduce in trasmissione, nel blocco qui sotto.
        if (Virtuale && panelWidth > 0 && screen is not null
            && (source.W != panelWidth || source.H != panelHeight))
        {
            if (Displays.SetResolution(screen.DeviceName, panelWidth, panelHeight).Length == 0)
            {
                var updated = Displays.List().FirstOrDefault(m => m.IsVirtual);
                if (updated is not null)
                {
                    source = updated.Bounds;
                    outW = source.W;
                    outH = source.H;
                    Say($"Schermo virtuale portato a {outW}x{outH}.");
                }
            }
        }

        if (panelWidth > 0 && (source.W > panelWidth || source.H > panelHeight))
        {
            // Riduzione che conserva le proporzioni: il tablet mette le bande
            // nere, meglio di un'immagine schiacciata.
            double scale = Math.Min((double)panelWidth / source.W, (double)panelHeight / source.H);
            outW = Math.Max(1, (int)Math.Round(source.W * scale));
            outH = Math.Max(1, (int)Math.Round(source.H * scale));
        }

        capture!.SetSource(source, outW, outH);
        input.Source = source;
        input.OutputWidth = outW;
        input.OutputHeight = outH;

        // I gesti viaggiano con la geometria: sono le due cose che il tablet non
        // puo' sapere da solo, e cambiano insieme.
        link.SendJson(Proto.Hello, ConfigFile.Wire(new
        {
            w = outW,
            h = outH,
            touchMode = settings.Touch.Mode,
            longPressMs = settings.Touch.LongPressMs,
            wheelNotchPx = settings.Touch.WheelNotchPx,
            invertScroll = settings.Touch.InvertScroll,
        }));
    }

    /// <summary>Da chiamare quando in finestra si cambia schermo, gesti o qualita'.</summary>
    public void SettingsChanged() => geometryDirty = true;

    /// <summary>
    /// Dice al tablet cosa mostrare. Con <paramref name="full"/> la barra
    /// laterale si ritira e restano i 1024 pixel pieni del pannello: e' la
    /// differenza fra un'immagine ridotta e una nitida.
    /// </summary>
    /// <param name="vai">Con « deck »: il deck e' chiesto apposta, e il tablet ci va da qualunque sezione.
    /// Senza, « deck » dice solo che lo schermo e' finito.</param>
    public void ShowOnTablet(bool screen, bool full, bool vai = false)
    {
        link.SendJson(Proto.Mode, ConfigFile.Wire(new
        {
            mode = screen ? "screen" : "deck",
            full,
            vai,
        }));
    }

    /// <summary>Manda al tablet come deve comportarsi: schermo acceso, luminosita'.</summary>
    public void SendTabletConfig()
    {
        link.SendJson(Proto.Config, ConfigFile.Wire(new
        {
            keepAwake = settings.Tablet.KeepAwake,
            brightness = settings.Tablet.Brightness,
            // Due scelte: il tablet usa quella del momento, e passa all'altra da solo
            // quando si collega o si stacca.
            sezioni = new
            {
                collegato = new
                {
                    dashboard = settings.Tablet.Sezioni.Dashboard,
                    deck = settings.Tablet.Sezioni.Deck,
                    schermo = settings.Tablet.Sezioni.Schermo,
                    casa = settings.Tablet.Sezioni.Casa,
                    orologio = settings.Tablet.Sezioni.Orologio,
                },
                scollegato = new
                {
                    dashboard = settings.Tablet.SenzaPc.Dashboard,
                    deck = settings.Tablet.SenzaPc.Deck,
                    schermo = false,
                    casa = settings.Tablet.SenzaPc.Casa,
                    orologio = settings.Tablet.SenzaPc.Orologio,
                },
                cambiate = settings.Tablet.Sezioni.Cambiate,
            },
        }));
    }

    /// <summary>
    /// Un comando singolo al tablet: "settings", "wifi", "railShow", "railHide",
    /// "restart", "salvaschermo".
    /// </summary>
    public void SendCommand(string what)
    {
        link.SendJson(Proto.Cmd, ConfigFile.Wire(new { @do = what }));
    }

    // ---- deck ----

    /// <summary>
    /// Manda al tablet solo quel che serve a disegnare: etichetta, glifo,
    /// colore, icona. Le azioni restano qui — cosi' cambiarle non richiede di
    /// reinstallare niente sul tablet, e il tablet non puo' eseguire nulla.
    ///
    /// Le icone partono prima della griglia, altrimenti il tablet disegnerebbe
    /// dei pulsanti che nominano immagini che non ha ancora.
    /// </summary>
    /// <returns>Vero se e' partito davvero: senza collegamento non parte niente.</returns>
    public bool SendDeck()
    {
        if (!link.IsConnected) return false;

        SendIcone();
        link.SendJson(Proto.Deck, ConfigFile.Wire(new
        {
            // Il nome del profilo sta nella fascia in cima del deck.
            nome = deck.Nome,
            sfondo = deck.Sfondo,
            cols = deck.Cols,
            rows = deck.Rows,
            buttons = PerIlTablet(deck.Buttons),
        }));
        return true;
    }

    /// <summary>
    /// La griglia come la disegnera' il tablet. Le cartelle portano con se' il
    /// loro contenuto: aprirne una non deve chiedere niente al PC, che potrebbe
    /// benissimo essere spento.
    /// </summary>
    private static List<object> PerIlTablet(List<DeckButton> lista) =>
        lista.Select(b => (object)new
        {
            id = b.Id,
            label = b.Label,
            glyph = b.Glyph,
            color = b.Color,
            icon = b.Icon,
            // Assente per un pulsante normale: e' cosi' che il tablet distingue
            // una cartella, senza dover sapere niente delle azioni.
            buttons = b.Cartella ? PerIlTablet(b.Dentro) : null,
        }).ToList();

    /// <summary>
    /// Le icone che il deck usa, una volta sola per collegamento.
    ///
    /// Il tablet se le tiene su disco, quindi rimandarle a ogni griglia sarebbe
    /// banda buttata; rimandarle a ogni collegamento invece serve, perche' non
    /// c'e' modo di sapere se dall'altra parte c'e' lo stesso tablet di prima o
    /// uno appena reinstallato. Sono immagini da 128 pixel: quaranta pulsanti
    /// stanno in qualche centinaio di chilobyte, meno di un frame di schermo.
    /// </summary>
    private void SendIcone()
    {
        foreach (var nome in deck.Ovunque().Select(b => b.Icon).Append(deck.Sfondo)
                     .Where(n => n.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!iconeInviate.Add(nome)) continue;

            byte[]? png = Icone.Bytes(nome);
            if (png is null)
            {
                iconeInviate.Remove(nome);
                Say($"L'icona '{nome}' non e' piu' in config/icone: quel pulsante mostrera' il glifo.");
                continue;
            }
            link.SendIcon(nome, png);
        }
    }

    /// <summary>
    /// Un pulsante chiede di passare a un altro profilo. Non lo puo' fare il
    /// motore: qui c'e' un deck solo, e i profili li tiene la finestra. Arriva
    /// dal thread di rete, quindi chi ascolta si passi il Dispatcher.
    /// </summary>
    public event Action<string>? ProfiloChiesto;

    /// <summary>
    /// Le azioni che non esegue il PC ma la finestra. Vero se l'azione era una
    /// di quelle ed e' gia' stata smistata.
    /// </summary>
    private bool Smista(ActionSpec? azione)
    {
        if (azione is null || !string.Equals(azione.Type, "profilo", StringComparison.OrdinalIgnoreCase))
            return false;

        ProfiloChiesto?.Invoke(azione.Value.Trim());
        return true;
    }

    /// <summary>
    /// Il deck su cui il motore lavora d'ora in poi. Non lo manda: mandarlo e'
    /// un gesto a parte, e chi salva non sta per forza chiedendo di riscrivere
    /// il tablet.
    /// </summary>
    public void UseDeck(DeckConfig fresh) => deck = fresh;

    // ---- luci di casa ----

    /// <summary>
    /// Manda al tablet l'elenco delle luci: nome, indirizzo, chiave, versione.
    ///
    /// Qui, al contrario del deck, viaggia tutto il necessario per agire —
    /// chiave compresa — perche' il punto e' proprio che il tablet possa
    /// comandare le lampade da solo, a PC spento e a cavo staccato. Il tablet
    /// se lo salva; questo invio serve solo a portarglielo la prima volta e a
    /// ogni cambiamento.
    ///
    /// Le righe senza chiave non partono: sul tablet diventerebbero pulsanti
    /// che non possono funzionare.
    /// </summary>
    /// <returns>Vero se e' partito davvero: senza collegamento non parte niente.</returns>
    public bool SendLuci()
    {
        if (!link.IsConnected) return false;
        link.SendJson(Proto.Luci, ConfigFile.Wire(new
        {
            luci = luci.Luci.Where(l => l.Completa).Select(l => new
            {
                nome = l.Nome,
                id = l.Id,
                ip = l.Ip,
                chiave = l.Chiave,
                versione = l.Versione,
                glifo = l.Glifo,
                colore = l.Colore,
                stanza = l.Stanza,
            }).ToList(),
            routine = luci.Routine.Where(r => r.Passi.Count > 0).Select(r => new
            {
                nome = r.Nome,
                glifo = r.Glifo,
                colore = r.Colore,
                scena = r.Scena,
                passi = r.Passi.Select(p => new
                {
                    luce = p.Luce,
                    azione = p.Azione,
                    valore = p.Valore,
                }).ToList(),
            }).ToList(),
        }));
        return true;
    }

    /// <summary>Come <see cref="UseDeck"/>: si ricorda, non si manda.</summary>
    public void UseLuci(LuciConfig fresh) => luci = fresh;

    /// <summary>
    /// Esegue subito un'azione, come se il pulsante fosse stato premuto sul
    /// tablet. Serve alla finestra: un'azione si scrive alla cieca, e provarla
    /// sul posto e' l'unico modo di sapere se fa quel che si voleva.
    /// </summary>
    public void Prova(ActionSpec azione, string nome)
    {
        Say($"Prova: {nome}");
        if (Smista(azione)) return;
        runner.Run(azione, nome);
    }

    // ---- eventi dal tablet ----

    private void OnTouch(byte action, int x, int y)
    {
        // Senza schermo in viaggio le coordinate non vogliono dire niente: il
        // tablet non dovrebbe mandarne, ma un evento in ritardo puo' arrivare
        // dopo lo stop, e finirebbe per muovere il mouse a caso.
        if (!ScreenOn) return;

        switch (action)
        {
            case Proto.TouchMove: input.MoveTo(x, y); break;
            case Proto.TouchDown: input.MoveTo(x, y); input.LeftDown(); break;
            case Proto.TouchUp: input.LeftUp(); break;
            case Proto.TouchRight: input.MoveTo(x, y); input.RightClick(); break;
        }
    }

    private void OnPress(string id)
    {
        // Cercato in tutto l'albero: il tablet manda l'identificativo e basta,
        // e un pulsante dentro una cartella e' un pulsante come gli altri.
        var button = deck.Ovunque().FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));
        if (button is null)
        {
            Say($"Pulsante '{id}' non presente nel deck.");
            return;
        }

        // Le cartelle le apre il tablet: se ne arriva la pressione, quel tablet
        // ha una griglia piu' vecchia di questa.
        if (button.Cartella)
        {
            Say($"'{button.Label}' e' una cartella: la apre il tablet, non il PC.");
            return;
        }

        Say($"Deck: {button.Label}");
        if (Smista(button.Action)) return;
        runner.Run(button);
    }

    public void Dispose()
    {
        Disconnect();
        link.Dispose();
    }
}
