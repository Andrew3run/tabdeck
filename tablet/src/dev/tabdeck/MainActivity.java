package dev.tabdeck;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.graphics.drawable.GradientDrawable;
import android.os.BatteryManager;
import android.os.Bundle;
import android.os.Handler;
import android.provider.Settings;
import android.util.DisplayMetrics;
import android.util.Log;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;

/**
 * La schermata di casa del tablet. L'app e' registrata come launcher:
 * all'accensione il dispositivo entra direttamente qui, senza TouchWiz, senza
 * cassetto delle applicazioni e senza barre di sistema. Non c'e' modo di
 * arrivare a nient'altro, ed e' voluto: il tablet fa due cose.
 *
 *   Deck     la griglia di pulsanti, sempre disponibile, disegnata anche a PC
 *            spento. E' il modo predefinito perche' e' quello che si usa
 *            entrando e uscendo dalla scrivania.
 *   Schermo  il monitor in piu' del PC. Mentre e' aperto arriva l'immagine e i
 *            tocchi tornano indietro come mouse.
 *
 * Un modo alla volta: passando al deck il PC smette di catturare, cosi' non si
 * comprime e non si trasmette niente per uno schermo che nessuno sta guardando.
 */
public final class MainActivity extends Activity implements Link.Listener, Ora.Ascolto {

    private static final String TAG = "TabDeck.Main";

    private static final int UI_FLAGS =
            View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
            | View.SYSTEM_UI_FLAG_FULLSCREEN
            | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY;

    private static final String PREFS = "tabdeck";
    private static final String KEY_MODE = "modo";
    private static final String KEY_RAIL = "railCollapsed";

    private static final int MODE_DECK = 0;
    private static final int MODE_SCREEN = 1;
    private static final int MODE_SETTINGS = 2;
    private static final int MODE_LUCI = 3;
    private static final int MODE_TIMER = 4;
    private static final int MODE_SVEGLIA = 5;
    private static final int MODE_ESTENSIONE = 6;
    private static final int MODE_DASHBOARD = 7;
    private static final int MODE_APP = 8;
    private static final int MODE_METEO = 9;
    private static final String KEY_ESTENSIONE = "estensione";

    /**
     * Gli annunci delle estensioni accese, per processo e non per activity:
     * essendo la Home, l'activity viene rifatta mentre il collegamento resta
     * aperto, e il PC non ha motivo di riannunciarle.
     */
    private static final LinkedHashMap<String, JSONObject> annunciate = new LinkedHashMap<String, JSONObject>();

    /**
     * Lo schermo del PC sta viaggiando: il PC ha mandato « screen » e non ancora
     * « deck ». Senza, la voce Schermo portava a un rettangolo nero. Per processo,
     * come gli annunci: la Home si rifa' mentre lo schermo continua a arrivare.
     */
    private static boolean schermoAcceso;

    private static final String KEY_AWAKE = "keepAwake";
    private static final String KEY_BRIGHT = "brightness";

    // Una sola scala di grigi fredda per tutta l'interfaccia: il contenuto che
    // conta e' il deck e lo schermo remoto, la barra deve sparire dietro.
    // La grafite del deck e dell'app sul PC: tre schermi, una tavolozza.
    private static final int COLOR_SFONDO   = 0xFF131418;
    private static final int COLOR_BARRA    = 0xFF15161A;
    private static final int COLOR_SEPARA   = 0xFF25272C;
    private static final int COLOR_ICONA    = 0xFF80838B;
    private static final int COLOR_ICONA_ON = 0xFFFFFFFF;
    private static final int COLOR_ATTIVO   = 0xFF1C1E23;
    private static final int COLOR_ACCENTO  = 0xFF3DDC84;
    private static final int COLOR_SPENTO   = 0xFF5C5F66;
    /** Le voci di servizio: piu' quiete delle sezioni, perche' contano meno. */
    private static final int COLOR_SERVIZIO = 0xFF6B6E76;

    /**
     * Il colore di ogni sezione, nella barra: l'azzurro della Home, il viola del
     * marchio per il deck, il verde della casa, l'ambra dell'orologio. A riposo
     * l'icona ne ha un velo, aperta la voce diventa un tasto di quel colore: si
     * capisce dove si e' con la coda dell'occhio, prima ancora di leggere.
     */
    private static final int TINTA_HOME = 0xFF5B8CFF;
    private static final int TINTA_DECK = 0xFF8B6BFF;
    private static final int TINTA_CASA = 0xFF4FD08F;
    private static final int TINTA_OROLOGIO = 0xFFFFB454;
    private static final int TINTA_SCHERMO = 0xFF4FC3E8;
    private static final int TINTA_APP = 0xFFFF7A9C;

    /** Il fondo della barra: lo tinge la sezione aperta, vedi {@link #tingiBarra}. */
    private GradientDrawable fondoBarra;

    /** La barra: abbastanza larga per il nome sotto l'icona. */
    private static final int LARGHEZZA_BARRA = 72;

    private Link link;
    private ScreenView screen;
    private DeckView deck;
    private GaugeView gauges;
    private SettingsView options;
    private CasaView casaView;
    private DashboardView dashboardView;
    private RailTab tabDashboard;
    private Luci luci;
    /** Timer e Sveglia: due voci della barra, una schermata sola con due schede. */
    private OrologioView orologioView;
    private Misure misure;
    private SuoneriaView suoneriaView;
    private Ora ora;
    private TextView statusDot;
    private View rail;
    private FrameLayout railHandle;
    private RailTab tabDeck;
    private RailTab tabScreen;
    private RailTab tabSettings;
    private RailTab tabLuci;
    private RailTab tabTimer;
    /** La scheda dell'orologio aperta per ultima: MODE_TIMER o MODE_SVEGLIA. */
    private int ultimoOrologio = MODE_TIMER;
    /** Le estensioni caricate, nell'ordine in cui sono arrivate. */
    private final LinkedHashMap<String, Voce> voci = new LinkedHashMap<String, Voce>();
    /** La voce App nella barra, fra le sezioni e il gruppo di servizio. */
    private RailTab tabApp;
    /** La griglia delle estensioni accese. */
    private AppView appView;
    private MeteoView meteoView;
    /** Quella aperta in MODE_ESTENSIONE. */
    private String estensioneDavanti = "";
    private FrameLayout content;
    private float density;

    /** Le sezioni che il PC ha scelto di mostrare. Impostazioni c'e' sempre: e' la via d'uscita. */
    private static final String KEY_SEZIONI = "sezioni";
    /**
     * Quando le sezioni sono state scelte l'ultima volta, qui o sul PC, in millisecondi.
     * Al collegamento vince la scelta piu' recente dei due lati: prima vinceva sempre
     * quella fatta sul tablet a PC staccato, e una scelta fatta dopo sul PC si perdeva.
     */
    private static final String KEY_SEZIONI_QUANDO = "sezioniQuando";
    /**
     * Due scelte, nell'ordine di {@link #SEZIONI}: quali sezioni col PC collegato e quali
     * senza. Il deck o lo schermo servono col PC; la casa e l'orologio anche senza.
     */
    private final boolean[] sezConPc = { true, true, true, true, true };
    private final boolean[] sezSenzaPc = { true, true, true, true, true };
    /** Le sezioni di adesso, prese da una delle due scelte secondo il collegamento. */
    private boolean sezDashboard = true;
    private boolean sezDeck = true;
    private boolean sezSchermo = true;
    private boolean sezCasa = true;
    private boolean sezOrologio = true;

    private Salvaschermo salvaschermo;
    private SalvaschermoView salvaschermoView;
    /** Il salvaschermo si programma solo con l'activity davanti: a pannello spento non c'e' niente da coprire. */
    private boolean inPrimoPiano;

    private int mode = MODE_DECK;

    /** "never", "screen" o "always". Si cambia dal tablet o dal PC. */
    private String keepAwakePolicy = "screen";
    /** 0-100, oppure -1 per lasciare la luminosita' di Android. */
    private int brightness = -1;

    private Sonda sonda;
    private final Handler campionatore = new Handler();

    /** Ogni due secondi: e' il passo con cui una barretta si legge senza agitarsi. */
    private static final long PASSO_CARICO = 2000;

    /**
     * Rilegge CPU e memoria del tablet e le porta alle barrette. Gira solo
     * mentre l'activity e' in primo piano — cioe' praticamente sempre, visto
     * che questa e' la Home — e si ferma con onPause, perche' un timer che
     * misura un'interfaccia che nessuno guarda e' solo batteria buttata.
     */
    private final Runnable carico = new Runnable() {
        @Override public void run() {
            gauges.set(sonda.cpu(), sonda.ram());
            campionatore.postDelayed(this, PASSO_CARICO);
        }
    };

    /**
     * La batteria la legge il tablet da se': farla passare dal PC avrebbe
     * voluto dire non vederla proprio quando serve, cioe' a cavo staccato.
     */
    private final BroadcastReceiver batteryWatcher = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            int level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1);
            int scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1);
            int status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
            int percent = (level < 0 || scale <= 0) ? -1 : Math.round(level * 100f / scale);
            gauges.setBattery(percent,
                    status == BatteryManager.BATTERY_STATUS_CHARGING
                            || status == BatteryManager.BATTERY_STATUS_FULL);
        }
    };

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        density = getResources().getDisplayMetrics().density;
        sonda = new Sonda(this);

        getWindow().addFlags(WindowManager.LayoutParams.FLAG_DISMISS_KEYGUARD);

        // Il collegamento vive nel processo, non nell'activity: se questa viene
        // ricreata (succede quando l'app fa da Home) ci si limita a riagganciarsi.
        link = Link.get();
        setContentView(buildUi());
        screen.setLink(link);
        deck.setLink(link);

        SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        keepAwakePolicy = prefs.getString(KEY_AWAKE, "screen");
        brightness = prefs.getInt(KEY_BRIGHT, -1);
        options.setKeepAwake(keepAwakePolicy);
        options.setBrightness(brightness);
        options.setAddress(localAddress());
        applyBrightness(brightness);

        estensioneDavanti = prefs.getString(KEY_ESTENSIONE, "");
        if (link.isConnected()) {
            for (String id : new ArrayList<String>(annunciate.keySet())) preparaEstensione(id);
        }
        leggiSezioni();
        int modo = prefs.getInt(KEY_MODE, MODE_DASHBOARD);
        setMode(modoVisibile(modo) ? modo : primoModo(), false);
        setRailCollapsed(prefs.getBoolean(KEY_RAIL, false), false);

        link.setListener(this);
        link.start();

        // Il secondo canale del cavo, per internet dal PC. Il consenso alla VPN lo
        // mostra l'activity: il servizio non ha una finestra da cui chiederlo.
        Tunnel.consenso = new Tunnel.Consenso() {
            @Override public void chiedi(Intent richiesta) {
                try {
                    startActivityForResult(richiesta, RICHIESTA_VPN);
                } catch (ActivityNotFoundException e) {
                    Log.w(TAG, "consenso alla VPN non chiedibile: " + e.getMessage());
                }
            }
        };
        Tunnel.avvia(this);

        // Il filtro e' appiccicoso: la registrazione porta subito lo stato
        // attuale, senza aspettare il prossimo cambio di percentuale.
        registerReceiver(batteryWatcher, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));

        // Se siamo qui perche' e' scattata una sveglia mentre il processo era
        // chiuso, l'intento che ci ha aperti lo dice.
        gestisciAllarme(getIntent());
    }

    /**
     * L'activity e' singleTask e sta gia' davanti: quando {@link Allarme} la
     * richiama, la notizia arriva qui e non da un onCreate nuovo.
     */
    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        // Il tasto Home del tablet, con TabDeck gia' davanti: si torna alla Dashboard,
        // come su qualunque schermata di casa. Una sveglia che chiama passa oltre.
        if (intent != null && intent.hasCategory(Intent.CATEGORY_HOME)
                && intent.getStringExtra(Ora.EXTRA_COSA) == null && sezDashboard) {
            if (salvaschermoView.attivo()) chiudiSalvaschermo();
            setRailCollapsed(false, false);
            setMode(MODE_DASHBOARD, true);
        }
        gestisciAllarme(intent);
    }

    /**
     * E' scattato il timer, o la sveglia.
     *
     * Si va in sezione Ora e si comincia a suonare. L'ordine conta: prima il
     * modo, cosi' la vista e' quella davanti e il suo battito e' partito;
     * poi {@link Ora#scattato}, che rimette a posto lo stato, riprogramma il
     * domani e fa partire il suono, e da li' la pagina si ridisegna con la
     * schermata della suoneria.
     *
     * L'extra si toglie subito dall'intento: senza, tornare qui da una
     * ricreazione dell'activity — che essendo la Home capita spesso — farebbe
     * ripartire la suoneria di una sveglia gia' spenta.
     */
    private void gestisciAllarme(Intent intent) {
        if (intent == null) return;
        String cosa = intent.getStringExtra(Ora.EXTRA_COSA);
        int id = intent.getIntExtra(Ora.EXTRA_ID, -1);
        if (cosa == null) return;
        intent.removeExtra(Ora.EXTRA_COSA);
        intent.removeExtra(Ora.EXTRA_ID);
        // Un numero che Ora non conosce piu' — un timer fermato un attimo
        // prima che scadesse, una sveglia tolta — non fa niente, nemmeno
        // cambiare sezione.
        if (!ora.conosce(id)) return;

        // Si va nella sezione a cui la sveglia appartiene: fermata la
        // suoneria si resta li', che e' il posto giusto per rimetterla o per
        // farne partire un'altra.
        setMode(Ora.TIMER.equals(cosa) ? MODE_TIMER : MODE_SVEGLIA, true);
        String routine = ora.scattato(id);
        if (routine != null) eseguiRoutine(routine);
    }

    /**
     * La routine delle luci che la sveglia si porta dietro, cercata per nome.
     *
     * Per nome e non per posizione: le routine si scrivono sul PC e arrivano
     * quando vogliono, e la terza di ieri puo' essere la seconda di oggi. Se
     * quella routine non c'e' piu' la sveglia suona lo stesso, al buio.
     */
    private void eseguiRoutine(String nome) {
        for (Routine r : luci.routine()) {
            if (nome.equals(r.nome)) {
                luci.esegui(r);
                return;
            }
        }
        Log.w(TAG, "la sveglia chiede la routine " + nome + ", che non c'e' piu'");
    }

    /**
     * Qualcosa e' cambiato nel timer o nella sveglia.
     *
     * All'activity ne interessano due sole conseguenze: la schermata che suona
     * va davanti a tutto — sta sopra le sezioni, non dentro una — e finche'
     * suona il pannello non deve spegnersi.
     */
    @Override
    public void oraCambiata() {
        boolean suona = ora.suonando() != null;
        // Una sveglia che squilla va vista, non coperta dalle foto.
        if (suona && salvaschermoView != null && salvaschermoView.attivo()) salvaschermoView.ferma();
        suoneriaView.setVisibility(suona ? View.VISIBLE : View.INVISIBLE);
        applyAwake();
    }

    // ---- costruzione interfaccia ----

    private View buildUi() {
        FrameLayout root = new FrameLayout(this);

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setBackgroundColor(COLOR_SFONDO);

        rail = buildRail();
        row.addView(rail, new LinearLayout.LayoutParams(dp(LARGHEZZA_BARRA), ViewGroup.LayoutParams.MATCH_PARENT));

        // I due modi stanno impilati e occupano lo stesso spazio: cosi' la
        // vista dello schermo conserva le sue misure anche mentre e' nascosta,
        // e il PC non deve rinegoziare la geometria a ogni cambio di modo.
        content = new FrameLayout(this);
        screen = new ScreenView(this);
        deck = new DeckView(this);
        content.addView(screen, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        content.addView(deck, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        luci = new Luci(this);
        ora = new Ora(this);
        misure = new Misure(this);
        casaView = new CasaView(this, misure, luci);
        content.addView(casaView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        dashboardView = new DashboardView(this, misure, luci, ora, azioniDashboard);
        content.addView(dashboardView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        appView = new AppView(this, new AppView.Azione() {
            @Override public void apri(String id) { apriEstensione(id); }
        });
        content.addView(appView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        meteoView = new MeteoView(this, new MeteoView.Azione() {
            @Override public void indietro() { setMode(MODE_DASHBOARD, true); }
        });
        content.addView(meteoView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        // Luci ha un ascoltatore solo, e le luci si vedono in due posti: la Casa e la Dashboard.
        luci.setAscolto(new Luci.Ascolto() {
            @Override public void luceCambiata(Lampada l) {
                casaView.luceCambiata(l);
                dashboardView.luceCambiata(l);
            }
            @Override public void elencoCambiato() {
                casaView.elencoCambiato();
                dashboardView.elencoCambiato();
            }
        });
        orologioView = new OrologioView(this, misure, ora, luci);
        // Una scheda premuta dentro la schermata accende la voce giusta nella
        // barra, come se la si fosse premuta li'.
        orologioView.setSchede(new OrologioView.Schede() {
            @Override public void suScheda(boolean sveglie) {
                setMode(sveglie ? MODE_SVEGLIA : MODE_TIMER, true);
            }
        });
        content.addView(orologioView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        options = new SettingsView(this, impostazioni);
        content.addView(options, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        // Per ultima, quindi sopra tutte: la schermata che suona non appartiene
        // ne' al timer ne' alla sveglia, e deve comparire davanti a qualunque
        // sezione fosse aperta. La sua visibilita' non passa da setMode: la
        // decide soltanto {@link #oraCambiata()}.
        suoneriaView = new SuoneriaView(this, misure, ora);
        suoneriaView.setVisibility(View.INVISIBLE);
        content.addView(suoneriaView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        // Per ultimo anche qui: cosi' le tre pagine si sono gia' ridisegnate
        // quando l'activity va a chiedere se sta suonando.
        ora.aggiungiAscolto(this);

        row.addView(content, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));

        root.addView(row, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        // Maniglia per far ricomparire la barra. Sta sul bordo sinistro a meta'
        // altezza, dove il pollice arriva tenendo il tablet, ed e' larga quanto
        // un polpastrello: la versione piccola in un angolo era comodissima da
        // guardare e impossibile da centrare.
        railHandle = new FrameLayout(this);
        railHandle.setVisibility(View.GONE);
        railHandle.addView(new Maniglia(this), new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        railHandle.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { setRailCollapsed(false, true); }
        });
        // Seconda via d'uscita: a barra nascosta questa maniglia e' l'unica
        // cosa toccabile che non sia lo schermo remoto.
        railHandle.setOnLongClickListener(new View.OnLongClickListener() {
            @Override public boolean onLongClick(View v) {
                openSettings(Settings.ACTION_SETTINGS, "Impostazioni non disponibili");
                return true;
            }
        });
        // In basso a sinistra, come nel modello: il tasto che riapre la barra.
        FrameLayout.LayoutParams handleParams = new FrameLayout.LayoutParams(dp(56), dp(56));
        handleParams.gravity = Gravity.LEFT | Gravity.BOTTOM;
        root.addView(railHandle, handleParams);

        // Sopra tutto, barra compresa: e' un salvaschermo, copre il pannello intero.
        salvaschermo = new Salvaschermo(this);
        salvaschermoView = new SalvaschermoView(this, salvaschermo, new SalvaschermoView.Chiusura() {
            @Override public void chiuso() { chiudiSalvaschermo(); }
        });
        root.addView(salvaschermoView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        return root;
    }

    private View buildRail() {
        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.VERTICAL);
        // Un fondo suo e un filo di luce sul bordo destro: la barra e' un oggetto
        // accanto alla pagina, non un pezzo di pagina piu' scuro.
        fondoBarra = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] { COLOR_BARRA, COLOR_BARRA, COLOR_BARRA });
        GradientDrawable filo = new GradientDrawable();
        filo.setColor(0xFF2A2C32);
        android.graphics.drawable.LayerDrawable sfondoBarra = new android.graphics.drawable.LayerDrawable(
                new android.graphics.drawable.Drawable[] { filo, fondoBarra });
        sfondoBarra.setLayerInset(1, 0, 0, Math.max(1, dp(1)), 0);
        bar.setBackground(sfondoBarra);
        bar.setPadding(0, dp(12), 0, dp(10));

        // Premuto una seconda volta, quando il deck e' gia' davanti, riporta
        // alla radice: e' la via d'uscita corta da una cartella aperta tre
        // piani piu' giu', e non ne serviva una nuova - il tasto della sezione
        // in cui si e' gia' non faceva niente.
        // La Dashboard in cima: e' la schermata di casa, quella a cui si torna.
        tabDashboard = new RailTab(RailIcon.DASHBOARD, "Home", TINTA_HOME, new View.OnClickListener() {
            @Override public void onClick(View v) { setMode(MODE_DASHBOARD, true); }
        });
        tabDeck = new RailTab(RailIcon.DECK, "Deck", TINTA_DECK, new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (mode == MODE_DECK) deck.tornaAllaRadice();
                else setMode(MODE_DECK, true);
            }
        });
        tabScreen = new RailTab(RailIcon.SCREEN, "Schermo", TINTA_SCHERMO, new View.OnClickListener() {
            @Override public void onClick(View v) { setMode(MODE_SCREEN, true); }
        });
        // Senza PC dall'altra parte lo schermo remoto e' un rettangolo nero:
        // la voce c'e' solo mentre c'e' un collegamento. Lo stato iniziale si
        // legge qui, perche' la notizia di onLinkState arriva un giro dopo e
        // per un attimo la voce comparirebbe lo stesso.
        tabScreen.view.setVisibility(link.isConnected() && schermoAcceso ? View.VISIBLE : View.GONE);
        tabLuci = new RailTab(RailIcon.CASA, "Casa", TINTA_CASA, new View.OnClickListener() {
            @Override public void onClick(View v) { setMode(MODE_LUCI, true); }
        });
        // L'orologio sta sotto Casa, e i due stanno insieme apposta: sono le
        // sezioni che funzionano a PC spento e a cavo staccato. Una voce sola
        // per timer e sveglie: la schermata ha le sue due schede in cima, e
        // una seconda voce nella barra faceva la stessa cosa di una scheda.
        // Riapre quella lasciata aperta l'ultima volta.
        tabTimer = new RailTab(RailIcon.TIMER, "Orologio", TINTA_OROLOGIO, new View.OnClickListener() {
            @Override public void onClick(View v) { setMode(ultimoOrologio, true); }
        });
        // Niente marchio in cima: non faceva nulla. Uno spazio tiene la Home dove la mano la cerca.
        bar.addView(new View(this), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(14)));
        bar.addView(tabDashboard.view, rowParams());
        bar.addView(tabDeck.view, rowParams());
        bar.addView(tabScreen.view, rowParams());
        bar.addView(tabLuci.view, rowParams());
        bar.addView(tabTimer.view, rowParams());

        // Le estensioni stanno tutte dietro una voce sola, dopo le sezioni di
        // TabDeck: con due o tre voci una per estensione la barra non bastava.
        // Compare e sparisce col PC, e le sezioni di sempre restano dove la mano
        // le cerca.
        tabApp = new RailTab(RailIcon.ESTENSIONE, "App", TINTA_APP, new View.OnClickListener() {
            @Override public void onClick(View v) { setMode(MODE_APP, true); }
        });
        bar.addView(tabApp.view, rowParams());

        // Sotto le sezioni, dopo una lineetta, Impostazioni e « chiudi »: piccole e
        // grigie, perche' contano meno. Connetti al PC sta nella Home e nelle
        // Impostazioni, il Wi-Fi e il riavvio nelle Impostazioni: nella barra
        // erano tre icone in piu' alla pari delle sezioni.
        bar.addView(separator(), separatorParams());

        tabSettings = new RailTab(RailIcon.SETTINGS, null, new View.OnClickListener() {
            @Override public void onClick(View v) { setMode(MODE_SETTINGS, true); }
        });
        bar.addView(tabSettings.view, rowParams());

        // Chiusa, la barra si riapre trascinando il dito dal bordo sinistro
        // verso l'interno: niente tasto che resti sopra le sezioni.
        bar.addView(new RailTab(RailIcon.COLLAPSE, null, new View.OnClickListener() {
            @Override public void onClick(View v) { setRailCollapsed(true, true); }
        }).view, rowParams());


        // Lo stato sta in fondo.
        View spacer = new View(this);
        bar.addView(spacer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        gauges = new GaugeView(this);
        bar.addView(gauges, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // Il PC lo dice lo stato in fondo, col suo punto: la riga di testo resta
        // per chi la legge ma non si vede.
        statusDot = new TextView(this);
        statusDot.setVisibility(View.GONE);
        statusDot.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
        statusDot.setGravity(Gravity.CENTER);
        statusDot.setPadding(0, dp(2), 0, dp(6));
        bar.addView(statusDot, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        setLinkIndicator(false, "");
        return bar;
    }

    private LinearLayout.LayoutParams rowParams() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private View separator() {
        View v = new View(this);
        v.setBackgroundColor(COLOR_SEPARA);
        return v;
    }

    private LinearLayout.LayoutParams separatorParams() {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Math.max(1, dp(1)));
        p.setMargins(dp(22), dp(8), dp(22), dp(8));
        p.gravity = Gravity.CENTER_HORIZONTAL;
        return p;
    }

    /**
     * Una voce della barra, in due misure.
     *
     * Le sezioni hanno l'icona e il nome sotto, e quella aperta e' un tasto del
     * deck - grafite sfumata nel suo incavo, con l'icona azzurra. Le voci di
     * servizio (connetti, Wi-Fi, riavvia, impostazioni, chiudi) sono piu' basse,
     * grigie e senza nome: si usano di rado, e prima stavano alla pari delle
     * sezioni, in una colonna di icone tutte uguali dove non si capiva cosa
     * fosse cosa.
     */
    private final class RailTab {
        final FrameLayout view;
        final RailIcon icon;
        final View accent;
        final TextView nome;
        private final boolean sezione;

        /** Il colore della sezione: per le voci di servizio non c'e'. */
        private final int tinta;

        RailTab(int kind, String etichetta, View.OnClickListener onClick) {
            this(new RailIcon(MainActivity.this, kind), etichetta, TINTA_HOME, onClick);
        }

        RailTab(int kind, String etichetta, int tinta, View.OnClickListener onClick) {
            this(new RailIcon(MainActivity.this, kind), etichetta, tinta, onClick);
        }

        RailTab(RailIcon icona, String etichetta, View.OnClickListener onClick) {
            this(icona, etichetta, TINTA_HOME, onClick);
        }

        RailTab(RailIcon icona, String etichetta, int tinta, View.OnClickListener onClick) {
            sezione = etichetta != null;
            this.tinta = tinta;
            view = new FrameLayout(MainActivity.this);
            int alta = sezione ? dp(53) : dp(36);

            accent = new View(MainActivity.this);
            GradientDrawable pozzo = new GradientDrawable();
            pozzo.setColor(0xFF07080A);
            pozzo.setCornerRadius(dp(15));
            // Il tasto acceso prende il colore della sezione, spento verso la
            // grafite: si legge come una luce dietro il tasto, non come un bollino.
            GradientDrawable tasto = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
                    new int[] { Tinte.fondi(0xFF3A3D45, tinta, 0.34f), Tinte.fondi(0xFF26282D, tinta, 0.20f) });
            tasto.setCornerRadius(dp(13));
            tasto.setStroke(Math.max(1, dp(1)), Tinte.con(tinta, 0x55));
            android.graphics.drawable.LayerDrawable strati =
                    new android.graphics.drawable.LayerDrawable(new android.graphics.drawable.Drawable[] { pozzo, tasto });
            strati.setLayerInset(1, dp(2), dp(2), dp(2), dp(2));
            if (sezione) {
                accent.setBackground(strati);
            } else {
                GradientDrawable piatto = new GradientDrawable();
                piatto.setColor(0xFF24262B);
                piatto.setCornerRadius(dp(10));
                accent.setBackground(piatto);
            }
            accent.setVisibility(View.INVISIBLE);
            // Altezza esplicita, non MATCH_PARENT: dentro un contenitore alto
            // quanto il contenuto, MATCH_PARENT si misura sullo spazio che
            // resta e la prima voce si prendeva tutta la barra.
            FrameLayout.LayoutParams ap = new FrameLayout.LayoutParams(dp(sezione ? 60 : 40), alta - dp(2));
            ap.gravity = Gravity.CENTER;
            view.addView(accent, ap);

            icon = icona;
            icon.setColor(sezione ? riposo() : COLOR_SERVIZIO);
            FrameLayout.LayoutParams ip = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, sezione ? dp(36) : alta);
            ip.gravity = Gravity.TOP;
            if (sezione) ip.topMargin = dp(2);
            view.addView(icon, ip);

            if (sezione) {
                nome = new TextView(MainActivity.this);
                nome.setText(etichetta);
                nome.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10);
                nome.setTextColor(COLOR_ICONA);
                nome.setGravity(Gravity.CENTER);
                nome.setSingleLine(true);
                FrameLayout.LayoutParams np = new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
                np.gravity = Gravity.BOTTOM;
                np.bottomMargin = dp(5);
                view.addView(nome, np);
            } else {
                nome = null;
            }
            // Tutta la voce si preme, non solo l'icona: il nome sotto e' parte del bersaglio.
            view.setOnClickListener(onClick);
            icon.setOnClickListener(onClick);
            view.setMinimumHeight(alta);
        }

        /** L'icona a riposo: grigia con un velo del colore della sezione. */
        private int riposo() {
            return Tinte.fondi(COLOR_ICONA, tinta, 0.42f);
        }

        void setActive(boolean active) {
            accent.setVisibility(active ? View.VISIBLE : View.INVISIBLE);
            icon.setColor(active ? (sezione ? Tinte.fondi(tinta, 0xFFFFFFFF, 0.25f) : COLOR_ICONA_ON)
                    : (sezione ? riposo() : COLOR_SERVIZIO));
            if (nome != null) nome.setTextColor(active ? COLOR_ICONA_ON : COLOR_ICONA);
        }
    }

    // ---- modi ----

    /**
     * Passa fra deck e schermo. Il PC viene avvisato: mentre si guarda il deck
     * smette di catturare, e la ripresa e' immediata perche' la geometria non
     * e' mai cambiata.
     */
    private void setMode(int next, boolean announce) {
        // L'estensione aperta ieri, oggi non annunciata: si va sulla prima sezione
        // che c'e' invece di aprire una pagina vuota.
        if (next == MODE_ESTENSIONE && voceDavanti() == null) next = primoModo();
        if (next == MODE_APP && !ciSonoApp()) next = primoModo();
        mode = next;
        for (Voce v : voci.values()) {
            boolean davanti = next == MODE_ESTENSIONE && v.id.equals(estensioneDavanti);
            v.vista.setVisibility(davanti ? View.VISIBLE : View.INVISIBLE);
            v.tab.setActive(davanti);
            v.plugin.mostrato(davanti);
        }
        boolean onScreen = next == MODE_SCREEN;

        screen.setVisibility(next == MODE_SCREEN ? View.VISIBLE : View.INVISIBLE);
        deck.setVisibility(next == MODE_DECK ? View.VISIBLE : View.INVISIBLE);
        options.setVisibility(next == MODE_SETTINGS ? View.VISIBLE : View.INVISIBLE);
        casaView.setVisibility(next == MODE_LUCI ? View.VISIBLE : View.INVISIBLE);
        dashboardView.setVisibility(next == MODE_DASHBOARD ? View.VISIBLE : View.INVISIBLE);
        dashboardView.attivo(next == MODE_DASHBOARD);
        appView.setVisibility(next == MODE_APP ? View.VISIBLE : View.INVISIBLE);
        meteoView.setVisibility(next == MODE_METEO ? View.VISIBLE : View.INVISIBLE);
        if (next == MODE_METEO) meteoView.dati(dashboardView.meteoDati());
        // La voce App resta accesa anche dentro un'estensione: e' li' che si e' entrati.
        tabApp.setActive(next == MODE_APP || next == MODE_ESTENSIONE);
        tabDashboard.setActive(next == MODE_DASHBOARD || next == MODE_METEO);
        boolean orologio = next == MODE_TIMER || next == MODE_SVEGLIA;
        orologioView.setVisibility(orologio ? View.VISIBLE : View.INVISIBLE);
        if (orologio) orologioView.mostra(next == MODE_SVEGLIA);

        tabDeck.setActive(next == MODE_DECK);
        tabScreen.setActive(onScreen);
        tabSettings.setActive(next == MODE_SETTINGS);
        tabLuci.setActive(next == MODE_LUCI);
        tabTimer.setActive(orologio);
        tingiBarra(next == MODE_DASHBOARD ? TINTA_HOME : next == MODE_DECK ? TINTA_DECK
                : next == MODE_LUCI ? TINTA_CASA : orologio ? TINTA_OROLOGIO
                : onScreen ? TINTA_SCHERMO : next == MODE_APP || next == MODE_ESTENSIONE ? TINTA_APP
                : next == MODE_SETTINGS ? 0xFF8A8D95 : TINTA_HOME);
        if (orologio) ultimoOrologio = next;
        // Lo stato delle lampade si rilegge entrando nella sezione, non a ciclo:
        // interrogare tre lampade ogni tot secondi vorrebbe dire tenere acceso
        // qualcosa che nessuno guarda, ed e' proprio quello che qui non si fa.
        if (next == MODE_LUCI) casaView.risveglia();
        // E per tutto il tempo che la sezione resta aperta, ogni cinque secondi.
        luci.seguiDaVicino(next == MODE_LUCI);
        // Gli orologi battono solo mentre li si guarda: entrando partono,
        // uscendo si fermano. Il conto alla rovescia resta giusto lo stesso —
        // e' una sottrazione da una scadenza scritta, non un contatore.
        orologioView.attivo(orologio);
        applyAwake();

        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putInt(KEY_MODE, next).apply();
        dicoDavanti(false);
        if (announce) sendReady();
    }

    /**
     * Tiene acceso lo schermo solo mentre si guarda il monitor remoto.
     *
     * Prima il flag era sempre attivo, e siccome TabDeck e' la Home il pannello
     * non si spegneva mai: un 7 pollici acceso assorbe 400-600 mA, una porta
     * USB 2.0 ne da' 500, e il tablet restava fermo al 16 per cento pur essendo
     * "sotto carica". Sul deck si lascia fare al timeout di Android — un tocco
     * lo risveglia, e la griglia e' gia' li' perche' e' la schermata di casa.
     */
    private void applyAwake() {
        // Mentre suona il pannello resta acceso comunque: una sveglia che
        // squilla al buio con lo schermo spento si puo' solo cercare a tastoni,
        // e « Ferma » diventa irraggiungibile.
        boolean awake = (ora != null && ora.suonando() != null)
                || "always".equals(keepAwakePolicy)
                || ("screen".equals(keepAwakePolicy) && mode == MODE_SCREEN)
                || (salvaschermoView != null && salvaschermoView.attivo() && salvaschermo.pannello);
        if (awake) {
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } else {
            getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        }
    }

    /** -1 lascia la luminosita' di sistema; 0-100 la impone a questa finestra. */
    private void applyBrightness(int percent) {
        WindowManager.LayoutParams lp = getWindow().getAttributes();
        lp.screenBrightness = percent < 0
                ? WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
                : Math.max(0.02f, Math.min(1f, percent / 100f));
        getWindow().setAttributes(lp);
    }

    /**
     * Nasconde la barra per dare allo schermo remoto tutti e 1024 i pixel del
     * pannello. Cambiando la larghezza utile cambia anche l'area che il PC deve
     * mandare, quindi si riannuncia la geometria appena il layout e' rifatto.
     */
    private void setRailCollapsed(boolean collapsed, boolean announce) {
        rail.setVisibility(collapsed ? View.GONE : View.VISIBLE);
        // La maniglia non si mostra piu': la barra si riapre col gesto dal bordo.
        railHandle.setVisibility(View.GONE);
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putBoolean(KEY_RAIL, collapsed).apply();
        if (announce) {
            screen.post(new Runnable() {
                @Override public void run() { sendReady(); }
            });
        }
    }

    /**
     * Il fondo della barra prende il colore della sezione aperta: una luce che
     * scende dall'alto, piena dietro il marchio e spenta prima dello stato in
     * fondo. Dice dove si e' anche guardando solo l'angolo dell'occhio.
     */
    private void tingiBarra(int tinta) {
        if (fondoBarra == null) return;
        fondoBarra.setColors(new int[] {
                Tinte.fondi(COLOR_BARRA, tinta, 0.30f),
                Tinte.fondi(COLOR_BARRA, tinta, 0.12f),
                Tinte.fondi(COLOR_BARRA, tinta, 0.04f) });
    }

    /** Applica e ricorda le due preferenze, da qualunque parte arrivino. */
    private void salvaPreferenze(String policy, int brightnessPercent) {
        keepAwakePolicy = policy == null ? "screen" : policy;
        brightness = brightnessPercent;
        applyAwake();
        applyBrightness(brightness);
        options.setKeepAwake(keepAwakePolicy);
        options.setBrightness(brightness);
        getSharedPreferences(PREFS, MODE_PRIVATE).edit()
                .putString(KEY_AWAKE, keepAwakePolicy)
                .putInt(KEY_BRIGHT, brightness)
                .apply();
    }

    /**
     * Dice al PC dove siamo, e lo lascia collegare.
     *
     * Se il collegamento c'e' gia' non si manda niente: l'annuncio serve a
     * farsi trovare, e chi e' gia' trovato non ha bisogno di ripetersi.
     */
    private void annunciaAlPc() {
        if (link.isConnected()) {
            toast("Gia' collegato via " + link.transport());
            return;
        }
        toast("Chiamo il PC...");
        link.annuncia(new Link.Annuncio() {
            @Override public void mandato(int destinazioni, String errore) {
                if (errore != null) {
                    toast("Annuncio non partito: " + errore);
                } else if (destinazioni == 0) {
                    toast("Nessuna rete: controlla il Wi-Fi");
                } else {
                    // Non si promette il collegamento, solo che la chiamata e'
                    // uscita: dall'altra parte la finestra potrebbe essere
                    // chiusa, e dirlo per certo sarebbe una bugia.
                    toast("Chiamata mandata. Se TabDeck e' aperto sul PC, si collega.");
                }
            }
        });
    }

    /**
     * Riporta nella pagina lo stato vero delle impostazioni di risparmio.
     *
     * Non se ne tiene una copia nelle preferenze: la verita' e' nel sistema, e
     * queste voci si cambiano anche da fuori — dalle impostazioni di Android,
     * o da adb. Una copia nostra si sarebbe messa a raccontare il passato.
     */
    private void leggiRisparmio() {
        options.setScreenTimeout(Risparmio.durata(this));
        options.setRisparmioDiSistema(Risparmio.cavoTieneAcceso(this), Risparmio.radio(this));
    }

    /** Indirizzo IPv4 del tablet, per scriverlo sul PC quando serve a mano. */
    private String localAddress() {
        try {
            java.util.Enumeration<java.net.NetworkInterface> nics =
                    java.net.NetworkInterface.getNetworkInterfaces();
            while (nics != null && nics.hasMoreElements()) {
                java.net.NetworkInterface nic = nics.nextElement();
                if (!nic.isUp() || nic.isLoopback()) continue;
                java.util.Enumeration<java.net.InetAddress> addrs = nic.getInetAddresses();
                while (addrs.hasMoreElements()) {
                    java.net.InetAddress a = addrs.nextElement();
                    if (a instanceof java.net.Inet4Address && !a.isLoopbackAddress()) {
                        return a.getHostAddress();
                    }
                }
            }
        } catch (java.net.SocketException ignored) {
        }
        return "—";
    }

    /** La pagina impostazioni del tablet parla con noi da qui. */
    private final SettingsView.Listener impostazioni = new SettingsView.Listener() {
        @Override public void onKeepAwakeChosen(String policy) {
            salvaPreferenze(policy, brightness);
        }
        @Override public void onBrightnessChosen(int percent, boolean finale) {
            salvaPreferenze(keepAwakePolicy, percent);
            // La luminosita' di sistema si scrive solo a dito alzato: durante
            // il trascinamento arrivano decine di chiamate, e ognuna passerebbe
            // per il content provider delle impostazioni.
            if (finale && !Risparmio.setLuminosita(MainActivity.this, percent)) {
                toast("Android non lascia scrivere la luminosita' di sistema");
            }
        }
        @Override public void onScreenTimeoutChosen(int ms) {
            if (Risparmio.setDurata(MainActivity.this, ms)) {
                toast("Pannello acceso per " + Risparmio.durataDetta(ms));
            } else {
                // Sarebbe un permesso mancante nel manifest: WRITE_SETTINGS su
                // Android 4.4 si concede all'installazione, quindi qui non
                // dovrebbe mai capitare — ma se capita, meglio dirlo che
                // lasciare una casella che non si accende senza spiegazioni.
                toast("Android ha rifiutato: manca WRITE_SETTINGS");
            }
            leggiRisparmio();
        }
        @Override public void onConnectToPc() {
            annunciaAlPc();
        }
        @Override public void onOpenAndroidSettings() {
            openSettings(Settings.ACTION_SETTINGS, "Impostazioni non disponibili");
        }
        @Override public void onOpenWifiSettings() {
            openSettings(Settings.ACTION_WIFI_SETTINGS, "Impostazioni Wi-Fi non disponibili");
        }
        @Override public void onOpenDeveloperSettings() {
            openSettings(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS,
                    "Opzioni sviluppatore non disponibili");
        }
        @Override public void onRestart() {
            riavvia("Riavvio TabDeck");
        }
        @Override public void onSezioneScelta(String chiave, boolean visibile) {
            sezioneDalTablet(chiave, visibile);
        }
    };

    /** Chiude il processo: essendo la Home, Android riapre TabDeck da zero. */
    private void riavvia(String avviso) {
        toast(avviso);
        finish();
        android.os.Process.killProcess(android.os.Process.myPid());
    }

    /** Comandi singoli mandati dalla finestra sul PC. */
    private void esegui(String comando) {
        if ("settings".equals(comando)) {
            openSettings(Settings.ACTION_SETTINGS, "Impostazioni non disponibili");
        } else if ("wifi".equals(comando)) {
            openSettings(Settings.ACTION_WIFI_SETTINGS, "Impostazioni Wi-Fi non disponibili");
        } else if ("salvaschermo".equals(comando)) {
            mostraSalvaschermo(true);
        } else if ("railShow".equals(comando)) {
            setRailCollapsed(false, true);
        } else if ("railHide".equals(comando)) {
            setRailCollapsed(true, true);
        } else if ("restart".equals(comando)) {
            riavvia("Riavvio dal PC");
        } else {
            Log.w(TAG, "comando sconosciuto: " + comando);
        }
    }

    private void openSettings(String action, String seNonCe) {
        try {
            startActivity(new Intent(action));
        } catch (ActivityNotFoundException e) {
            toast(seNonCe);
        }
    }

    private int dp(int v) {
        return Math.round(v * density);
    }

    private void toast(String s) {
        Toast.makeText(this, s, Toast.LENGTH_SHORT).show();
    }

    // ---- callback del collegamento ----

    @Override
    public void onLinkState(boolean connected, String transport) {
        setLinkIndicator(connected, transport);
        deck.setConnected(connected);
        options.setLinkState(connected, transport);
        options.setAddress(localAddress());

        // Caduto il collegamento, la voce Schermo sparisce e chi la stava
        // guardando torna al deck, con la barra aperta: a barra chiusa
        // resterebbe solo la maniglia, davanti a una griglia che non ha
        // chiesto nessuno di nascondere.
        if (!connected) schermoAcceso = false;
        // Il PC perde il "davanti" a ogni collegamento: lo si ridice.
        if (connected) dicoDavanti(true);
        // Collegarsi o staccarsi cambia la scelta delle sezioni: quella col PC o quella senza.
        applicaSezioni(mode != MODE_SCREEN);
        if (!connected && mode == MODE_SCREEN) {
            setRailCollapsed(false, false);
            setMode(primoModo(), false);
        }
        // Col PC se ne vanno anche le voci delle estensioni: restano caricate, e
        // sara' il PC a riannunciarle.
        if (!connected) {
            annunciate.clear();
            for (String id : new ArrayList<String>(voci.keySet())) nascondiEstensione(id);
        }

        aggiornaDashboard();

        if (connected) {
            sendReady();
        } else {
            screen.setStatus("Nessun collegamento — apri TabDeck sul PC e premi Avvia");
        }
    }

    // ---- dashboard ----

    private final DashboardView.Azioni azioniDashboard = new DashboardView.Azioni() {
        @Override public void apriDeck() { setMode(MODE_DECK, true); }
        @Override public void apriCasa() { setMode(MODE_LUCI, true); }
        @Override public void apriOrologio(boolean sveglie) { setMode(sveglie ? MODE_SVEGLIA : MODE_TIMER, true); }
        @Override public void apriSchermo() { setMode(MODE_SCREEN, true); }
        @Override public void connetti() { annunciaAlPc(); }
        @Override public void apriImpostazioni() { setMode(MODE_SETTINGS, true); }
        @Override public void apriEstensione(String id) { MainActivity.this.apriEstensione(id); }
        @Override public void apriApp() { setMode(MODE_APP, true); }
        @Override public void apriMeteo() { setMode(MODE_METEO, true); }
    };

    /** Collegamento, sezioni ed estensioni con la voce accesa: la Dashboard mostra quelle. */
    private void aggiornaDashboard() {
        if (dashboardView == null) return;
        ArrayList<DashboardView.Voce> estese = new ArrayList<DashboardView.Voce>();
        for (Voce v : voci.values()) {
            if (v.tab.view.getVisibility() != View.VISIBLE) continue;
            JSONObject annuncio = annunciate.get(v.id);
            String nome = annuncio != null ? annuncio.optString("nome", v.id) : v.id;
            android.graphics.Bitmap icona = null;
            try {
                icona = v.plugin.icona(this);
            } catch (Throwable ignored) {
                // Codice che viene da fuori: senza icona si mostra il pezzo di puzzle.
            }
            estese.add(new DashboardView.Voce(v.id, nome, icona));
        }
        dashboardView.stato(link.isConnected(), link.transport(), sezDeck, sezCasa, sezOrologio,
                sezSchermo && schermoAcceso, estese);
        if (appView == null || tabApp == null) return;
        appView.elenco(estese);
    }

    // ---- sezioni ----

    private void leggiSezioni() {
        SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
        String salvate = p.getString(KEY_SEZIONI, null);
        if (salvate == null) {
            mostraSezioni();
            return;
        }
        try {
            sezioni(new JSONObject(salvate), false);
        } catch (JSONException e) {
            mostraSezioni();
        }
    }

    /** Le cinque sezioni nell'ordine della barra: e' anche l'ordine delle due scelte. */
    private static final String[] SEZIONI = { "dashboard", "deck", "schermo", "casa", "orologio" };

    /**
     * Dal PC o salvate: {"collegato":{...},"scollegato":{...}}. Il formato di prima, con
     * una scelta sola, vale per tutte e due.
     */
    private void sezioni(JSONObject o, boolean salva) {
        JSONObject con = o.optJSONObject("collegato");
        JSONObject senza = o.optJSONObject("scollegato");
        leggiScelta(con != null ? con : o, sezConPc);
        leggiScelta(senza != null ? senza : o, sezSenzaPc);
        if (salva) getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString(KEY_SEZIONI, sezioniAttuali().toString()).apply();
        applicaSezioni(salva);
    }

    private static void leggiScelta(JSONObject o, boolean[] dove) {
        for (int i = 0; i < SEZIONI.length; i++) dove[i] = o.optBoolean(SEZIONI[i], true);
    }

    /** Le sezioni di adesso: quelle col PC o quelle senza, secondo il collegamento. */
    private void applicaSezioni(boolean spostaSeServe) {
        boolean[] scelta = link.isConnected() ? sezConPc : sezSenzaPc;
        sezDashboard = scelta[0];
        sezDeck = scelta[1];
        // Senza PC lo schermo remoto non c'e' comunque, qualunque cosa dica la scelta.
        sezSchermo = link.isConnected() && scelta[2];
        sezCasa = scelta[3];
        sezOrologio = scelta[4];
        mostraSezioni();
        if (spostaSeServe && !modoVisibile(mode)) setMode(primoModo(), link.isConnected());
    }

    private void mostraSezioni() {
        tabDashboard.view.setVisibility(sezDashboard ? View.VISIBLE : View.GONE);
        tabDeck.view.setVisibility(sezDeck ? View.VISIBLE : View.GONE);
        tabScreen.view.setVisibility(sezSchermo && link.isConnected() && schermoAcceso ? View.VISIBLE : View.GONE);
        tabLuci.view.setVisibility(sezCasa ? View.VISIBLE : View.GONE);
        tabTimer.view.setVisibility(sezOrologio ? View.VISIBLE : View.GONE);
        options.setSezioni(sezConPc, sezSenzaPc);
        aggiornaDashboard();
    }

    private JSONObject sezioniAttuali() {
        JSONObject o = new JSONObject();
        try {
            JSONObject con = new JSONObject();
            JSONObject senza = new JSONObject();
            for (int i = 0; i < SEZIONI.length; i++) {
                con.put(SEZIONI[i], sezConPc[i]);
                senza.put(SEZIONI[i], sezSenzaPc[i]);
            }
            o.put("collegato", con);
            o.put("scollegato", senza);
        } catch (JSONException ignored) {
            // Chiavi e valori li scrive questo codice.
        }
        return o;
    }

    /**
     * Dal PC, a ogni CONFIG. Se nel frattempo le si e' cambiate qui — magari a PC
     * spento — vince la scelta di qui, e la si manda al PC invece di perderla.
     */
    private void sezioniDalPc(JSONObject o) {
        SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
        long qui = p.getLong(KEY_SEZIONI_QUANDO, 0L);
        long pc = o.optLong("cambiate", 0L);
        if (qui > pc) {
            mandaSezioni();
            return;
        }
        sezioni(o, true);
        p.edit().putLong(KEY_SEZIONI_QUANDO, pc).apply();
    }

    /** Dalla pagina Impostazioni del tablet, collegato o no: « collegato.deck », « scollegato.casa ». */
    private void sezioneDalTablet(String chiave, boolean visibile) {
        int punto = chiave.indexOf('.');
        if (punto < 0) return;
        boolean[] scelta = chiave.startsWith("collegato") ? sezConPc : sezSenzaPc;
        String nome = chiave.substring(punto + 1);
        for (int i = 0; i < SEZIONI.length; i++) {
            if (SEZIONI[i].equals(nome)) scelta[i] = visibile;
        }
        sezioni(sezioniAttuali(), true);
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putLong(KEY_SEZIONI_QUANDO, System.currentTimeMillis()).apply();
        if (link.isConnected()) mandaSezioni();
    }

    private void mandaSezioni() {
        try {
            JSONObject sez = sezioniAttuali();
            sez.put("cambiate", getSharedPreferences(PREFS, MODE_PRIVATE).getLong(KEY_SEZIONI_QUANDO, 0L));
            link.sendJson(Proto.CONFIG, new JSONObject().put("sezioni", sez).toString());
        } catch (JSONException ignored) {
            // Chiavi e valori li scrive questo codice.
        }
    }

    /**
     * Lo schermo remoto resta sempre visibile quando lo accende il PC: e' un gesto
     * esplicito, e toglierlo da sotto il dito sarebbe peggio della voce nascosta.
     */
    private boolean modoVisibile(int m) {
        switch (m) {
            case MODE_DASHBOARD: return sezDashboard;
            case MODE_DECK: return sezDeck;
            case MODE_LUCI: return sezCasa;
            case MODE_TIMER:
            case MODE_SVEGLIA: return sezOrologio;
            case MODE_ESTENSIONE: return voceDavanti() != null;
            case MODE_APP: return ciSonoApp();
            default: return true;
        }
    }

    private boolean ciSonoApp() {
        // La pagina c'e' sempre: senza estensioni dice che non ce ne sono.
        return true;
    }

    /** Dove si va quando la sezione in cui si era non c'e' piu'. */
    private int primoModo() {
        if (sezDashboard) return MODE_DASHBOARD;
        if (sezDeck) return MODE_DECK;
        if (sezCasa) return MODE_LUCI;
        if (sezOrologio) return ultimoOrologio;
        for (Voce v : voci.values()) {
            if (v.tab.view.getVisibility() == View.VISIBLE) {
                estensioneDavanti = v.id;
                return MODE_ESTENSIONE;
            }
        }
        return MODE_SETTINGS;
    }

    // ---- estensioni ----

    /** Un'estensione caricata: la sua sezione e la sua voce nella barra. */
    private static final class Voce {
        final String id;
        final String impronta;
        final Plugin plugin;
        final View vista;
        final RailTab tab;

        Voce(String id, String impronta, Plugin plugin, View vista, RailTab tab) {
            this.id = id;
            this.impronta = impronta;
            this.plugin = plugin;
            this.vista = vista;
            this.tab = tab;
        }
    }

    /** Un frame PLUGIN dal PC: l'elenco delle installate, oppure un'estensione accesa, spenta o rimossa. */
    private void estensioneDalPc(JSONObject o) {
        JSONArray installate = o.optJSONArray("installate");
        if (installate != null) {
            HashSet<String> ids = new HashSet<String>();
            for (int i = 0; i < installate.length(); i++) ids.add(installate.optString(i));
            Estensioni.tieniSolo(this, ids);
            for (String id : new ArrayList<String>(voci.keySet())) {
                if (!ids.contains(id)) togliEstensione(id);
            }
            annunciate.keySet().retainAll(ids);
            return;
        }
        String id = o.optString("id", "");
        if (!Estensioni.idValido(id)) return;
        if (o.optBoolean("rimossa", false)) {
            annunciate.remove(id);
            togliEstensione(id);
            Estensioni.rimuovi(this, id);
        } else if (o.optBoolean("acceso", false)) {
            annunciate.put(id, o);
            preparaEstensione(id);
        } else {
            annunciate.remove(id);
            nascondiEstensione(id);
        }
    }

    /**
     * Il PC l'ha annunciata accesa. Se il pacchetto che c'e' e' quello annunciato si
     * monta e si dice « pronta »; altrimenti lo si chiede, e si torna qui quando arriva.
     */
    private void preparaEstensione(String id) {
        JSONObject annuncio = annunciate.get(id);
        if (annuncio == null) return;
        String impronta = annuncio.optString("impronta", "");
        Voce v = voci.get(id);
        if (v != null && !v.impronta.equals(impronta)) {
            // Aggiornata sul PC: quella montata e' di prima.
            togliEstensione(id);
            v = null;
        }
        if (v == null) {
            if (!impronta.equals(Estensioni.impronta(this, id))) {
                rispondiEstensione("chiedi", "codice", id);
                return;
            }
            v = montaEstensione(id, impronta, annuncio.optString("classe", ""));
            if (v == null) return;
        }
        v.tab.view.setVisibility(View.VISIBLE);
        aggiornaDashboard();
        try {
            JSONObject o = new JSONObject();
            o.put("pronta", id);
            o.put("impronta", impronta);
            link.sendJson(Proto.PLUGIN, o.toString());
        } catch (JSONException ignored) {
            // Chiavi e valori li scrive questo codice.
        }
    }

    private Voce montaEstensione(final String id, String impronta, String classe) {
        Plugin p;
        View vista;
        android.graphics.Bitmap icona;
        try {
            p = Estensioni.carica(this, id, classe);
            vista = p.vista(this, link);
            icona = p.icona(this);
        } catch (Throwable e) {
            // Codice che viene da fuori: qualunque cosa lanci, la Home resta in piedi.
            Log.w(TAG, "estensione " + id + " non caricata", e);
            rispondiEstensione("errore", e.getMessage() != null ? e.getMessage() : e.getClass().getSimpleName(), id);
            return null;
        }
        vista.setVisibility(View.INVISIBLE);
        // Sotto la schermata che suona: una sveglia resta davanti a tutto.
        content.addView(vista, content.indexOfChild(suoneriaView), new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        String etichetta = id.length() > 0 ? Character.toUpperCase(id.charAt(0)) + id.substring(1) : id;
        RailTab tab = new RailTab(new RailIcon(this, icona), etichetta, new View.OnClickListener() {
            @Override public void onClick(View view) { apriEstensione(id); }
        });
        Voce v = new Voce(id, impronta, p, vista, tab);
        voci.put(id, v);
        return v;
    }

    /**
     * Un frame dal PC: porta in « _e » l'id dell'estensione a cui va. Senza (un PC di prima)
     * lo ricevono tutte quelle visibili.
     */
    private void consegnaAEstensione(int type, String json) {
        String per = "";
        try {
            per = new JSONObject(json).optString("_e", "");
        } catch (JSONException ignored) {
            // Non e' un oggetto: va a tutte, come prima.
        }
        for (Voce v : voci.values()) {
            if (v.tab.view.getVisibility() != View.VISIBLE) continue;
            if (per.length() == 0 || per.equals(v.id)) v.plugin.suFrame(type, json);
        }
    }

    /** L'ultima estensione detta al PC come « davanti »: il PC gli consegna i frame di chi sta usando. */
    private String ultimoDavanti = "";

    /**
     * Dice al PC quale estensione ha davanti (o nessuna): i frame dei pannelli partono tutti
     * dal tablet senza nome, e il PC li consegna a quella.
     */
    private void dicoDavanti(boolean sempre) {
        String chi = mode == MODE_ESTENSIONE && voceDavanti() != null ? estensioneDavanti : "";
        if (!sempre && chi.equals(ultimoDavanti)) return;
        ultimoDavanti = chi;
        try {
            link.sendJson(Proto.PLUGIN, new JSONObject().put("davanti", chi).toString());
        } catch (JSONException ignored) {
            // Chiavi e valori li scrive questo codice.
        }
    }

    private void apriEstensione(String id) {
        estensioneDavanti = id;
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString(KEY_ESTENSIONE, id).apply();
        setMode(MODE_ESTENSIONE, true);
    }

    private Voce voceDavanti() {
        Voce v = voci.get(estensioneDavanti);
        return v != null && v.tab.view.getVisibility() == View.VISIBLE ? v : null;
    }

    private void nascondiEstensione(String id) {
        Voce v = voci.get(id);
        if (v == null) return;
        v.tab.view.setVisibility(View.GONE);
        v.plugin.mostrato(false);
        aggiornaDashboard();
        if (mode == MODE_ESTENSIONE && id.equals(estensioneDavanti)) setMode(primoModo(), link.isConnected());
    }

    private void togliEstensione(String id) {
        nascondiEstensione(id);
        Voce v = voci.remove(id);
        if (v == null) return;
        content.removeView(v.vista);
        aggiornaDashboard();
    }

    private void rispondiEstensione(String chiave, String valore, String id) {
        try {
            JSONObject o = new JSONObject();
            o.put(chiave, valore);
            o.put("id", id);
            link.sendJson(Proto.PLUGIN, o.toString());
        } catch (JSONException ignored) {
            // Chiavi e valori li scrive questo codice.
        }
    }

    /** Thread di rete: si salva qui, perche' il buffer si riusa; si monta sul thread dell'interfaccia. */
    @Override
    public void onCodiceEstensione(final String id, byte[] buf, int off, int len) {
        final String errore = Estensioni.salva(this, id, buf, off, len);
        runOnUiThread(new Runnable() {
            @Override public void run() {
                if (errore == null) {
                    preparaEstensione(id);
                    return;
                }
                Log.w(TAG, "estensione " + id + " rifiutata: " + errore);
                rispondiEstensione("errore", errore, id);
                toast("Estensione rifiutata: " + errore);
            }
        });
    }

    // ---- salvaschermo ----

    private final Runnable avviaSalvaschermo = new Runnable() {
        @Override public void run() { mostraSalvaschermo(false); }
    };

    /** Il dito partito dal bordo sinistro a barra chiusa: forse sta riaprendo la barra. */
    private boolean dalBordo;
    private float dalBordoX;

    /**
     * Ogni tocco, dovunque cada, rimanda il salvaschermo: e' la sola misura di
     * « nessuno lo sta usando ».
     *
     * Qui si riconosce anche il gesto che riapre la barra chiusa: il dito che
     * parte dalla striscia del bordo sinistro e va verso destra. Quel che parte
     * dalla striscia non arriva alle sezioni - ne' allo schermo remoto, che lo
     * manderebbe al PC come un clic - finche' non si alza.
     */
    @Override
    public boolean dispatchTouchEvent(MotionEvent e) {
        if (e.getActionMasked() == MotionEvent.ACTION_DOWN) rimandaSalvaschermo();

        boolean chiusa = rail != null && rail.getVisibility() == View.GONE;
        boolean coperto = salvaschermoView != null && salvaschermoView.attivo();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                dalBordo = chiusa && !coperto && e.getX() < dp(18);
                dalBordoX = e.getX();
                if (dalBordo) return true;
                break;
            case MotionEvent.ACTION_MOVE:
                if (dalBordo) {
                    if (e.getX() - dalBordoX > dp(44)) {
                        dalBordo = false;
                        setRailCollapsed(false, true);
                    }
                    return true;
                }
                break;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                if (dalBordo) {
                    dalBordo = false;
                    return true;
                }
                break;
        }
        return super.dispatchTouchEvent(e);
    }

    private void rimandaSalvaschermo() {
        campionatore.removeCallbacks(avviaSalvaschermo);
        if (salvaschermo.acceso && inPrimoPiano && !salvaschermoView.attivo()) {
            campionatore.postDelayed(avviaSalvaschermo, salvaschermo.dopoMs);
        }
    }

    /**
     * @param subito « Provalo » dal PC: parte anche col salvaschermo spento. Da solo
     *               invece non copre mai lo schermo remoto, che si guarda senza toccarlo.
     */
    private void mostraSalvaschermo(boolean subito) {
        boolean occupato = ora.suonando() != null || (!subito && mode == MODE_SCREEN);
        if (occupato || (!subito && !salvaschermo.acceso)) {
            rimandaSalvaschermo();
            return;
        }
        salvaschermoView.avvia();
        applyAwake();
    }

    private void chiudiSalvaschermo() {
        salvaschermoView.ferma();
        applyAwake();
        rimandaSalvaschermo();
    }

    /** Thread di rete, buffer riusato: la foto si scrive prima di tornare. */
    @Override
    public void onFotoSalvaschermo(String nome, byte[] buf, int off, int len) {
        Salvaschermo.salva(this, nome, buf, off, len);
    }

    private void setLinkIndicator(boolean connected, String transport) {
        if (statusDot == null) return;
        statusDot.setText(connected ? "● " + transport : "○");
        if (gauges != null) gauges.setPc(connected, transport);
        statusDot.setTextColor(connected ? COLOR_ACCENTO : COLOR_SPENTO);
    }

    /**
     * Dice al PC quanto spazio c'e' davvero e se lo si sta guardando. Sono le
     * due cose che dall'altra parte non si possono indovinare.
     */
    private void sendReady() {
        DisplayMetrics m = getResources().getDisplayMetrics();
        try {
            JSONObject o = new JSONObject();
            o.put("panelW", screen.getWidth() > 0 ? screen.getWidth() : m.widthPixels);
            o.put("panelH", screen.getHeight() > 0 ? screen.getHeight() : m.heightPixels);
            o.put("streaming", mode == MODE_SCREEN);
            o.put("device", android.os.Build.MODEL);
            link.sendJson(Proto.READY, o.toString());
        } catch (JSONException ignored) {
        }
    }

    @Override
    public void onTile(int x, int y, int w, int h, byte[] buf, int off, int len) {
        screen.onTile(x, y, w, h, buf, off, len);
    }

    /**
     * Un'icona del deck. Scrittura e decodifica restano qui sul thread di rete:
     * sono decine di millisecondi di flash e di PNG, e sul main looper si
     * vedrebbero come uno scatto del pannello proprio mentre ci si collega.
     * Sull'interfaccia torna solo il ridisegno.
     */
    @Override
    public void onIcon(String nome, byte[] buf, int off, int len) {
        if (!Icone.ricevi(this, nome, buf, off, len)) return;
        deck.post(new Runnable() {
            @Override public void run() { deck.invalidate(); }
        });
    }

    /**
     * Thread di rete. La conferma parte dopo il disegno, non prima: e' proprio
     * il tempo di decodifica e composizione che il PC deve poter misurare per
     * rallentare quando il tablet non sta dietro.
     */
    @Override
    public void onPresent() {
        screen.present();
        link.sendAck();
    }

    @Override
    public void onJson(int type, String json) {
        try {
            switch (type) {
                case Proto.HELLO: {
                    JSONObject o = new JSONObject(json);
                    screen.configure(o.optInt("w"), o.optInt("h"));
                    screen.setTouchConfig(
                            "pointer".equals(o.optString("touchMode", "touch"))
                                    ? ScreenView.TOUCH_POINTER : ScreenView.TOUCH_NATURAL,
                            o.optInt("longPressMs", 550),
                            o.optInt("wheelNotchPx", 48),
                            o.optBoolean("invertScroll", false));
                    screen.setStatus("Collegato, in attesa del primo frame");
                    break;
                }
                case Proto.MODE: {
                    JSONObject o = new JSONObject(json);
                    String modo = o.optString("mode", "");
                    if (modo.startsWith("plugin:")) {
                        // Un'estensione si usa con la barra: e' da li' che si torna al deck.
                        String id = modo.substring("plugin:".length());
                        Voce v = voci.get(id);
                        if (v != null && v.tab.view.getVisibility() == View.VISIBLE) {
                            if (salvaschermoView.attivo()) chiudiSalvaschermo();
                            setRailCollapsed(false, false);
                            apriEstensione(id);
                        }
                        break;
                    }
                    boolean toScreen = "screen".equals(o.optString("mode", "deck"));
                    schermoAcceso = toScreen;
                    mostraSezioni();
                    if (toScreen) {
                        // La barra va via solo quando si chiede lo schermo intero:
                        // e' l'unico modo di avere i 1024 pixel pieni, e quindi un
                        // pixel del PC su un pixel del pannello.
                        if (salvaschermoView.attivo()) chiudiSalvaschermo();
                        setRailCollapsed(o.optBoolean("full", false), false);
                        setMode(MODE_SCREEN, false);
                    } else {
                        // « deck » vuol dire due cose: lo schermo si e' fermato, oppure dal PC si
                        // e' chiesto il deck. Chi guardava lo schermo va al deck come prima; chi
                        // era in un'altra sezione ci resta, a meno che il deck sia chiesto apposta.
                        setRailCollapsed(false, false);
                        if (mode == MODE_SCREEN || o.optBoolean("vai", false)) {
                            setMode(sezDeck ? MODE_DECK : primoModo(), false);
                        }
                    }
                    // La geometria si annuncia dopo il nuovo layout: annunciarla
                    // subito avrebbe mandato la larghezza di prima, e il PC
                    // avrebbe continuato a ridurre l'immagine per niente.
                    screen.post(new Runnable() {
                        @Override public void run() { sendReady(); }
                    });
                    break;
                }
                case Proto.CONFIG: {
                    JSONObject o = new JSONObject(json);
                    // Chi cambia per ultimo vince, e qui il PC e' l'ultimo: si
                    // salva anche in locale, cosi' la pagina impostazioni del
                    // tablet mostra la stessa cosa.
                    salvaPreferenze(o.optString("keepAwake", "screen"), o.optInt("brightness", -1));
                    Web.modo = o.optString("internet", "no");
                    JSONObject sez = o.optJSONObject("sezioni");
                    if (sez != null) sezioniDalPc(sez);
                    break;
                }
                case Proto.SALVASCHERMO: {
                    JSONArray mancano = salvaschermo.configura(new JSONObject(json));
                    if (mancano.length() > 0) {
                        link.sendJson(Proto.SALVASCHERMO, new JSONObject().put("mancano", mancano).toString());
                    }
                    if (!salvaschermo.acceso && salvaschermoView.attivo()) chiudiSalvaschermo();
                    else rimandaSalvaschermo();
                    break;
                }
                case Proto.CMD: {
                    JSONObject o = new JSONObject(json);
                    esegui(o.optString("do", ""));
                    break;
                }
                case Proto.DECK:
                    deck.configure(json);
                    break;
                case Proto.LUCI:
                    // Questo si salva: da qui in poi il tablet comanda le luci
                    // anche senza il PC.
                    luci.configura(json);
                    break;
                case Proto.PLUGIN:
                    estensioneDalPc(new JSONObject(json));
                    break;
                default:
                    if (type > Proto.PLUGIN && type <= Proto.PLUGIN_ULTIMO) {
                        consegnaAEstensione(type, json);
                    } else {
                        Log.w(TAG, "frame di tipo sconosciuto: " + type);
                    }
            }
        } catch (JSONException e) {
            Log.w(TAG, "JSON non valido sul tipo " + type + ": " + e.getMessage());
        }
    }

    // ---- ciclo di vita ----

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) getWindow().getDecorView().setSystemUiVisibility(UI_FLAGS);
    }

    private static final int RICHIESTA_VPN = 41;

    @Override
    protected void onActivityResult(int richiesta, int esito, Intent dati) {
        super.onActivityResult(richiesta, esito, dati);
        if (richiesta == RICHIESTA_VPN && esito == RESULT_OK) Tunnel.consensoDato();
    }

    @Override
    protected void onResume() {
        super.onResume();
        getWindow().getDecorView().setSystemUiVisibility(UI_FLAGS);
        // La prima lettura della CPU non ha un intervallo da confrontare e
        // torna -1: si parte subito lo stesso, cosi' la seconda arriva fra due
        // secondi invece che fra quattro.
        campionatore.removeCallbacks(carico);
        campionatore.post(carico);
        // Si torna qui anche dalle impostazioni di Android, dove quei valori si
        // possono essere mossi: si rilegge invece di fidarsi di quel che si era
        // disegnato prima di uscire.
        leggiRisparmio();
        // Le luci: orecchio sugli annunci e avviso del Wi-Fi che torna. E se
        // si torna qui sulla sezione Casa — il pannello si riaccende, si esce
        // dalle impostazioni di Android — lo stato si rilegge: quello disegnato
        // puo' essere di ore fa.
        luci.avvia();
        if (mode == MODE_LUCI) {
            casaView.risveglia();
            luci.seguiDaVicino(true);
        }
        inPrimoPiano = true;
        rimandaSalvaschermo();
    }

    @Override
    protected void onPause() {
        super.onPause();
        campionatore.removeCallbacks(carico);
        luci.ferma();
        // Pannello spento o altra app davanti: il salvaschermo non ha niente da coprire.
        inPrimoPiano = false;
        campionatore.removeCallbacks(avviaSalvaschermo);
        if (salvaschermoView.attivo()) {
            salvaschermoView.ferma();
            applyAwake();
        }
    }

    /**
     * Da launcher, "indietro" premuto e rilasciato non esce dall'app: non c'e'
     * un "indietro" dove andare. Dentro il plugin invece fa un passo indietro
     * nelle sue pagine ({@link #onKeyUp}). Tenuto premuto apre le impostazioni
     * di Android.
     *
     * E' la via d'uscita che funziona sempre, anche a barra laterale nascosta e
     * anche se l'interfaccia si e' impuntata da qualche parte: il tasto e'
     * fisico e la gestione sta nel framework, non nel nostro disegno.
     */
    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            event.startTracking();
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    public boolean onKeyLongPress(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            toast("Impostazioni di Android");
            openSettings(Settings.ACTION_SETTINGS, "Impostazioni non disponibili");
            return true;
        }
        return super.onKeyLongPress(keyCode, event);
    }

    /**
     * La pressione breve si legge qui, a tasto rilasciato: dopo una pressione
     * lunga l'evento arriva annullato, e le impostazioni di Android non devono
     * portarsi dietro anche un passo indietro.
     */
    @Override
    public boolean onKeyUp(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            // Indietro fa un passo dentro la sezione, e quando non ce ne sono piu'
            // riporta alla Dashboard: da qualunque posto c'e' una strada per tornare.
            if (event.isTracking() && !event.isCanceled()) {
                if (salvaschermoView.attivo()) {
                    chiudiSalvaschermo();
                } else if (mode == MODE_ESTENSIONE && voceDavanti() != null && voceDavanti().plugin.indietro()) {
                    // l'estensione ha fatto il suo passo indietro
                } else if (mode == MODE_LUCI && casaView.indietro()) {
                    // chiuso il dettaglio della lampada
                } else if (mode == MODE_DECK && deck.risali()) {
                    // uscito da una cartella del deck
                } else if (mode == MODE_ESTENSIONE && ciSonoApp()) {
                    // dall'estensione si torna alla pagina App, e da li' alla Dashboard
                    setMode(MODE_APP, true);
                } else if (mode != MODE_DASHBOARD && mode != MODE_SCREEN && sezDashboard) {
                    setMode(MODE_DASHBOARD, true);
                }
            }
            return true;
        }
        return super.onKeyUp(keyCode, event);
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        Tunnel.consenso = null;
        // Prima si stacca l'ascoltatore, poi si liberano i bitmap: invertendo
        // l'ordine il thread di rete potrebbe disegnare su una bitmap gia'
        // riciclata. Il link resta vivo per la prossima activity.
        link.setListener(null);
        campionatore.removeCallbacks(carico);
        screen.release();
        // I thread delle luci muoiono con l'activity: essendo questa la Home,
        // viene ricreata piu' volte in una giornata, e lasciarne uno per volta
        // in giro finirebbe per riempire il poco che c'e'.
        luci.chiudi();
        // Solo la suoneria: le sveglie registrate in Android devono
        // sopravvivere all'activity, e' tutto il loro mestiere.
        ora.chiudi();
        try {
            unregisterReceiver(batteryWatcher);
        } catch (IllegalArgumentException ignored) {
            // Gia' sganciato: succede se onDestroy arriva due volte.
        }
    }
}
