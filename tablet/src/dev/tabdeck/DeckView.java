package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.SystemClock;
import android.util.Log;
import android.util.SparseArray;
import android.view.MotionEvent;
import android.view.VelocityTracker;
import android.view.ViewConfiguration;
import android.view.SoundEffectConstants;
import android.view.View;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * La griglia dello stream deck: e' questa la schermata di casa del tablet.
 *
 * Disegnata a mano su una sola View invece che con un GridLayout pieno di
 * Button: su 1GB di RAM e con la Vivante GC1000, quindici viste con sfondo,
 * testo e stato premuto costano piu' di quindici rettangoli. Qui il disegno
 * completo e' un centinaio di primitive e non alloca niente mentre gira.
 *
 * I pulsanti oltre {@code cols x rows} non vengono buttati via: finiscono nelle
 * pagine successive. Lo scorrimento segue il dito e poi si assesta da solo —
 * senza il movimento non ci sarebbe modo di capire da dove arriva la pagina
 * nuova, e i pallini in fondo dicono quante sono.
 *
 * Un pulsante puo' essere una **cartella**: dentro ha altri pulsanti, e
 * premendolo si apre il suo elenco con una fascia in cima che dice dove si e' e
 * riporta indietro. E' l'unica cosa del deck, insieme alle pagine, che il
 * tablet decide da solo: il PC manda l'albero intero una volta, e da li' in poi
 * sfogliarlo non costa un pacchetto. Serve proprio perche' il deck si sfoglia
 * anche a computer spento.
 *
 * Il tablet non sa cosa faccia un pulsante e non deve saperlo: riceve
 * etichetta, glifo, colore e il nome di un'icona, e quando lo si preme rimanda
 * l'identificativo. Le azioni vivono tutte sul PC, dove si cambiano senza
 * reinstallare nulla.
 *
 * L'icona e' un'immagine scelta sul PC e mandata prima della griglia; quando
 * c'e', prende il posto del glifo. I glifi restano per quel che sono buoni —
 * una freccia, un quadrato — ma il font di KitKat ne conosce ventinove, e un
 * pulsante « Photoshop » con dentro un cerchietto non dice niente a nessuno.
 *
 * L'ultima griglia ricevuta resta in memoria persistente: all'accensione il
 * deck e' gia' disegnato, prima ancora che il PC risponda. Spento il computer,
 * i pulsanti si vedono comunque — semplicemente non fanno niente, e si vede che
 * non lo fanno.
 */
public final class DeckView extends View {

    private static final String TAG = "TabDeck.Deck";
    private static final String PREFS = "tabdeck";
    private static final String KEY_DECK = "ultimoDeck";

    /** Nessun pulsante premuto. */
    private static final int NONE = -1;

    /** Posto premuto che vale la fascia della cartella aperta, non una cella. */
    private static final int FASCIA = -2;

    /** Durata dell'assestamento dopo lo swipe. */
    private static final long SLIDE_MS = 210;

    /** Durata del piccolo salto d'ingrandimento quando si entra o si esce. */
    private static final long ZOOM_MS = 140;

    /**
     * Un pulsante come arriva dal PC. Immutabile: la griglia si sostituisce
     * intera, e cosi' il disegno non puo' trovarsela cambiata a meta'.
     *
     * {@code figli} distingue una cartella da un pulsante: null vuol dire che
     * premendolo si manda l'identificativo al PC, non null vuol dire che si
     * apre l'elenco che porta con se'.
     */
    private static final class Nodo {
        final String id;
        final String label;
        final String glyph;
        final String icon;
        final int color;
        final Nodo[] figli;

        Nodo(String id, String label, String glyph, String icon, int color, Nodo[] figli) {
            this.id = id;
            this.label = label;
            this.glyph = glyph;
            this.icon = icon;
            this.color = color;
            this.figli = figli;
        }

        boolean cartella() {
            return figli != null;
        }
    }

    private static final Nodo[] NIENTE = new Nodo[0];

    // ---- i colori del deck: grafite, come l'app sul PC ----

    private static final int FONDO_CENTRO = 0xFF1B1D22;
    private static final int FONDO_BORDO = 0xFF0E0F12;
    /** L'incavo in cui sta ogni tasto, come nei deck veri. */
    private static final int POZZO = 0xFF07080A;
    private static final int VUOTO = 0xFF17181C;
    private static final int TESTO = 0xFFE6E7EA;
    private static final int TENUE = 0xFF9A9DA5;
    private static final int CHIP = 0xFF26282E;
    private static final int CHIP_PREMUTO = 0xFF34363D;
    private static final int PUNTO_SPENTO = 0xFF3A3C42;
    private static final int PUNTO_ACCESO = 0xFFD8D9DC;
    private static final int VERDE = 0xFF3DDC84;
    /**
     * Il colore dei pulsanti senza colore. Il vecchio #212734, blu notte, si
     * legge come grafite: i pulsanti fatti prima restano uguali agli altri.
     */
    private static final int PREDEFINITO = 0xFF33363E;
    private static final int VECCHIO_PREDEFINITO = 0xFF212734;

    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint lustro = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pozzo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint fondo = new Paint();
    private final Paint glyphPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint segnoPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint labelPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint hintPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint dotPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint titlePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint tenuePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint velo = new Paint(Paint.ANTI_ALIAS_FLAG);
    /** Filtro attivo: le icone si disegnano scalate, e senza si vedono i gradini. */
    private final Paint iconPaint = new Paint(Paint.FILTER_BITMAP_FLAG);
    private final RectF cell = new RectF();
    private final RectF iconRect = new RectF();
    private final RectF band = new RectF();
    private final RectF pill = new RectF();
    private final Matrix locale = new Matrix();

    /**
     * La sfumatura di ogni tinta, nel quadrato da zero a uno: si porta al tasto
     * con una matrice, cosi' disegnare non alloca niente. Si rifa' a ogni griglia.
     */
    private final SparseArray<LinearGradient> sfumature = new SparseArray<LinearGradient>();
    private final LinearGradient lucido;

    private final float density;
    private final float headHeight;
    private final float margine;
    /** Il lato massimo di un tasto: oltre, un tasto diventa un cartello. */
    private final float latoMassimo;

    // ---- misure della griglia, rifatte da misura() ----
    private float lato, passo, gridLeft, gridTop, raggio;

    private Link link;

    private int cols = 5;
    private int rows = 3;

    /** Il nome del profilo, scritto nella fascia in cima. */
    private String nomeDeck = "Deck";

    /**
     * L'immagine dietro i tasti, scelta sul PC per il profilo: arriva come le
     * icone e il tablet se la tiene. Vuoto per il fondo grafite.
     */
    private String sfondo = "";
    private final Paint pSfondo = new Paint(Paint.FILTER_BITMAP_FLAG);
    private final Paint pVeloSfondo = new Paint();
    private final RectF dove = new RectF();

    /** La radice del deck: quello che si vede quando non si e' entrati da nessuna parte. */
    private Nodo[] radice = NIENTE;

    /** Le cartelle aperte, una per piano. Vuoto vuol dire che si e' alla radice. */
    private final List<Nodo> percorso = new ArrayList<Nodo>();

    /** L'elenco disegnato adesso: la radice, oppure il contenuto dell'ultima cartella aperta. */
    private Nodo[] correnti = NIENTE;

    /** Posto premuto dentro la pagina corrente, non indice assoluto. */
    private int pressed = NONE;
    private boolean connected;

    private int page;
    /** Ascissa da cui e' partito il dito, per distinguere swipe da tocco. */
    private float dragFrom;
    private boolean dragging;
    /**
     * Misura la velocita' del dito. Senza, un colpetto rapido che non arriva a
     * un terzo di pagina veniva annullato: il gesto sembrava non funzionare
     * proprio quando lo si faceva con naturalezza.
     */
    private VelocityTracker velocity;
    private final float flickSpeed;

    /**
     * Scorrimento in corso, in frazioni di larghezza: 0 = pagina ferma al suo
     * posto, -1 = completamente scivolata a sinistra. Vale sia mentre il dito
     * trascina sia durante l'assestamento.
     */
    private float slide;
    private float slideFrom, slideTo;
    private long slideStart;
    private boolean sliding;
    /** Pagina verso cui si sta assestando; -1 se si torna indietro. */
    private int slideTarget = NONE;

    /**
     * Il salto d'ingrandimento con cui una cartella si apre e si chiude.
     *
     * Senza, cambiare piano e' un lampo: la griglia diventa un'altra griglia
     * uguale e non si capisce se si e' entrati o se si e' cambiata pagina. Un
     * settimo di secondo di scala basta a dirlo, e non costa un livello fuori
     * schermo — e' una sola trasformazione sul canvas.
     */
    private float zoomFrom = 1f;
    private long zoomStart;
    private boolean zooming;

    public DeckView(Context context) {
        super(context);
        density = getResources().getDisplayMetrics().density;
        headHeight = dp(44);
        margine = dp(18);
        latoMassimo = dp(150);

        Typeface normale = Typeface.create("sans-serif", Typeface.NORMAL);
        Typeface leggero = Typeface.create("sans-serif-light", Typeface.NORMAL);

        glyphPaint.setColor(0xFFFFFFFF);
        glyphPaint.setTextAlign(Paint.Align.CENTER);
        segnoPaint.setColor(0xF2FFFFFF);
        labelPaint.setColor(0xE0FFFFFF);
        labelPaint.setTextAlign(Paint.Align.CENTER);
        labelPaint.setTypeface(normale);
        hintPaint.setColor(TENUE);
        hintPaint.setTextAlign(Paint.Align.CENTER);
        hintPaint.setTextSize(sp(14));
        hintPaint.setTypeface(leggero);
        titlePaint.setColor(TESTO);
        titlePaint.setTextSize(sp(15));
        titlePaint.setTypeface(normale);
        tenuePaint.setColor(TENUE);
        tenuePaint.setTextSize(sp(15));
        tenuePaint.setTypeface(leggero);
        pozzo.setColor(POZZO);

        // Un filo di luce in cima e un filo d'ombra in fondo: e' quel che fa di
        // un rettangolo colorato un tasto, senza ombre vere che su questa GPU
        // costerebbero un livello fuori schermo.
        lucido = new LinearGradient(0, 0, 0, 1,
                new int[] { 0x30FFFFFF, 0x00FFFFFF, 0x00000000, 0x4D000000 },
                new float[] { 0f, 0.035f, 0.965f, 1f }, Shader.TileMode.CLAMP);
        lustro.setShader(lucido);

        // Meta' della soglia di sistema per un lancio: qui basta un accenno,
        // non c'e' niente da scorrere per sbaglio.
        flickSpeed = ViewConfiguration.get(context).getScaledMinimumFlingVelocity() * 0.5f;

        restore(context);
    }

    @Override
    protected void onSizeChanged(int w, int h, int ow, int oh) {
        super.onSizeChanged(w, h, ow, oh);
        // Una luce appena accennata dietro la griglia, che fa da piano del deck
        // invece di un nero piatto.
        fondo.setShader(new RadialGradient(w / 2f, h * 0.4f, Math.max(1, Math.max(w, h) * 0.75f),
                FONDO_CENTRO, FONDO_BORDO, Shader.TileMode.CLAMP));
        // Il velo sopra lo sfondo: piu' scuro in cima, dietro la fascia. Gli
        // stessi numeri li usa l'anteprima sul PC.
        pVeloSfondo.setShader(new LinearGradient(0, 0, 0, h, 0x9908090A, 0x7308090A, Shader.TileMode.CLAMP));
    }

    public void setLink(Link link) {
        this.link = link;
    }

    /** L'indicatore serve solo a spiegare perche' un pulsante non fa niente. */
    public void setConnected(boolean connected) {
        if (this.connected == connected) return;
        this.connected = connected;
        invalidate();
    }

    // ---- configurazione ----

    /**
     * Accetta la griglia mandata dal PC e la ricorda. Se il JSON e' rotto si
     * tiene quella di prima: meglio un deck vecchio che uno schermo vuoto.
     */
    public void configure(String json) {
        if (apply(json)) {
            getContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                    .edit().putString(KEY_DECK, json).apply();
        }
    }

    private void restore(Context context) {
        String saved = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getString(KEY_DECK, null);
        if (saved != null) apply(saved);
    }

    private boolean apply(String json) {
        try {
            JSONObject o = new JSONObject(json);
            Set<String> usate = new HashSet<String>();
            Nodo[] nuovi = leggi(o.optJSONArray("buttons"), usate, 0);

            cols = Math.max(1, o.optInt("cols", 5));
            rows = Math.max(1, o.optInt("rows", 3));
            String nome = o.optString("nome", "");
            nomeDeck = nome.length() > 0 ? nome : "Deck";
            sfondo = o.optString("sfondo", "");
            if (sfondo.length() > 0) usate.add(sfondo);
            radice = nuovi;
            sfumature.clear();

            // Una griglia nuova e' l'unico momento in cui si sa con certezza
            // quali immagini servono ancora: senza questa riga ogni icona mai
            // provata resterebbe sul tablet per sempre.
            Icone.tieniSolo(getContext(), usate);

            // La cartella che si stava guardando puo' non esistere piu': si
            // riparte dalla radice, che c'e' sempre.
            percorso.clear();
            correnti = radice;

            pressed = NONE;
            sliding = false;
            zooming = false;
            slide = 0;
            page = 0;
            invalidate();
            return true;
        } catch (JSONException e) {
            Log.w(TAG, "griglia non valida, tengo la precedente: " + e.getMessage());
            return false;
        }
    }

    /**
     * Legge un elenco di pulsanti, e per ogni cartella l'elenco che ha dentro.
     * Il limite di profondita' non e' una regola di gusto: e' quel che impedisce
     * a un file girato male di far ricorrere questo metodo all'infinito.
     */
    private Nodo[] leggi(JSONArray array, Set<String> usate, int piano) throws JSONException {
        int n = array == null ? 0 : array.length();
        if (n == 0 || piano > 8) return NIENTE;

        Nodo[] nodi = new Nodo[n];
        for (int i = 0; i < n; i++) {
            JSONObject b = array.getJSONObject(i);
            String icona = b.optString("icon", "");
            if (icona.length() > 0) usate.add(icona);

            JSONArray figli = b.optJSONArray("buttons");
            nodi[i] = new Nodo(
                    b.optString("id", ""),
                    b.optString("label", ""),
                    b.optString("glyph", ""),
                    icona,
                    tinta(b.optString("color", "")),
                    figli == null ? null : leggi(figli, usate, piano + 1));
        }
        return nodi;
    }

    private static int tinta(String value) {
        int c = Tinta.leggi(value, PREDEFINITO);
        return c == VECCHIO_PREDEFINITO ? PREDEFINITO : c;
    }

    // ---- cartelle ----

    /** Vero quando si sta guardando dentro una cartella. */
    private boolean dentro() {
        return !percorso.isEmpty();
    }

    /** Entra nella cartella, se e' una cartella. */
    private void apri(Nodo nodo) {
        if (nodo == null || !nodo.cartella()) return;
        percorso.add(nodo);
        correnti = nodo.figli;
        cambiaPiano(0.92f);
    }

    /** Torna alla cartella che contiene questa. Vero se c'era dove tornare. */
    public boolean risali() {
        if (percorso.isEmpty()) return false;
        percorso.remove(percorso.size() - 1);
        correnti = percorso.isEmpty() ? radice : percorso.get(percorso.size() - 1).figli;
        cambiaPiano(1.07f);
        return true;
    }

    /** Torna alla radice da qualunque profondita': serve al tasto del deck nella barra. */
    public boolean tornaAllaRadice() {
        if (percorso.isEmpty()) return false;
        percorso.clear();
        correnti = radice;
        cambiaPiano(1.07f);
        return true;
    }

    private void cambiaPiano(float da) {
        page = 0;
        pressed = NONE;
        sliding = false;
        slide = 0;
        zoomFrom = da;
        zoomStart = SystemClock.uptimeMillis();
        zooming = true;
        invalidate();
    }

    // ---- pagine ----

    /** Quanti pulsanti stanno in una pagina. */
    private int perPage() {
        return Math.max(1, cols * rows);
    }

    /** Quante pagine servono ai pulsanti della cartella aperta: almeno una. */
    public int pageCount() {
        return Math.max(1, (correnti.length + perPage() - 1) / perPage());
    }

    /**
     * Avvia l'assestamento: o la pagina nuova entra fino in fondo, o quella di
     * prima torna al suo posto. In tutti e due i casi si parte da dove il dito
     * ha lasciato, cosi' il movimento non ha scatti.
     */
    private void settle(int target) {
        slideTarget = target;
        slideFrom = slide;
        slideTo = target == NONE ? 0 : (target > page ? -1 : 1);
        slideStart = SystemClock.uptimeMillis();
        sliding = true;
        postInvalidateOnAnimation();
    }

    /** Avanza l'animazione di un fotogramma; torna false quando e' finita. */
    private boolean stepSlide() {
        long elapsed = SystemClock.uptimeMillis() - slideStart;
        if (elapsed >= SLIDE_MS) {
            if (slideTarget != NONE) page = slideTarget;
            slideTarget = NONE;
            sliding = false;
            slide = 0;
            return false;
        }
        // Decelerazione: parte veloce e si posa piano, che e' come ci si aspetta
        // che si comporti una cosa lanciata con il dito.
        float t = elapsed / (float) SLIDE_MS;
        float eased = 1 - (1 - t) * (1 - t) * (1 - t);
        slide = slideFrom + (slideTo - slideFrom) * eased;
        return true;
    }

    // ---- misure ----

    /**
     * Le misure della griglia, da una regola sola: tasti quadrati, la distanza
     * fra due tasti e' il sedici per cento del lato, e il lato e' il piu' grande
     * che ci sta - ma mai oltre {@link #latoMassimo}. La griglia sta al centro
     * dello spazio sotto la fascia, come i tasti in un deck vero: e' questo che
     * la fa sembrare un oggetto invece di un foglio pieno di rettangoli.
     */
    private void misura() {
        float w = getWidth();
        float h = getHeight();
        float k = 0.16f;
        float perL = (w - margine * 2f) / (cols + (cols - 1) * k);
        float perH = (h - headHeight - margine) / (rows + (rows - 1) * k);
        lato = Math.max(0f, Math.min(latoMassimo, Math.min(perL, perH)));
        passo = lato * (1f + k);
        raggio = lato * 0.17f;
        float larga = cols * lato + (cols - 1) * lato * k;
        float alta = rows * lato + (rows - 1) * lato * k;
        gridLeft = (w - larga) / 2f;
        gridTop = headHeight + (h - headHeight - alta) / 2f;
    }

    // ---- disegno ----

    @Override
    protected void onDraw(Canvas canvas) {
        canvas.drawRect(0, 0, getWidth(), getHeight(), fondo);
        Bitmap immagine = sfondo.length() > 0 ? Icone.prendi(getContext(), sfondo) : null;
        if (immagine != null) {
            // Ritaglio al centro: la scala che riempie il pannello, il resto fuori.
            float k = Math.max(getWidth() / (float) immagine.getWidth(), getHeight() / (float) immagine.getHeight());
            float w = immagine.getWidth() * k, h = immagine.getHeight() * k;
            dove.set((getWidth() - w) / 2f, (getHeight() - h) / 2f, (getWidth() + w) / 2f, (getHeight() + h) / 2f);
            canvas.drawBitmap(immagine, null, dove, pSfondo);
            canvas.drawRect(0, 0, getWidth(), getHeight(), pVeloSfondo);
        }
        misura();

        boolean animating = sliding && stepSlide();

        drawHead(canvas);

        if (radice.length == 0) {
            canvas.drawText("Nessun pulsante",
                    getWidth() / 2f, (getHeight() + headHeight) / 2f, hintPaint);
            return;
        }

        // Il salto d'ingrandimento: una sola trasformazione, e sotto si disegna
        // esattamente come sempre. La fascia resta ferma: e' il punto fisso da
        // cui si capisce che a cambiare e' il piano.
        float zoom = 1f;
        if (zooming) {
            long elapsed = SystemClock.uptimeMillis() - zoomStart;
            if (elapsed >= ZOOM_MS) {
                zooming = false;
            } else {
                float t = elapsed / (float) ZOOM_MS;
                zoom = zoomFrom + (1f - zoomFrom) * (1 - (1 - t) * (1 - t));
                animating = true;
            }
        }

        int salvato = -1;
        if (zoom != 1f) {
            salvato = canvas.save();
            canvas.scale(zoom, zoom, getWidth() / 2f, (getHeight() + headHeight) / 2f);
        }

        if (correnti.length == 0) {
            canvas.drawText("Cartella vuota",
                    getWidth() / 2f, (getHeight() + headHeight) / 2f, hintPaint);
        } else {
            float w = getWidth();
            float shift = slide * w;
            drawPage(canvas, page, shift);

            // Mentre si scorre si vede anche la pagina che sta arrivando: e' cio'
            // che rende il gesto leggibile invece di un lampo fra due schermate.
            if (slide < -0.001f) drawPage(canvas, page + 1, shift + w);
            else if (slide > 0.001f) drawPage(canvas, page - 1, shift - w);
        }

        if (salvato >= 0) canvas.restoreToCount(salvato);

        if (animating) postInvalidateOnAnimation();
    }

    /**
     * La fascia in cima, sempre alla stessa altezza: a sinistra dove si e' - il
     * profilo, e dentro una cartella la freccia per tornare su e il percorso -, a
     * destra i pallini delle pagine e lo stato del PC.
     *
     * Prima c'era solo dentro le cartelle, e la griglia saltava giu' a ogni
     * ingresso; i pallini stavano in fondo, dove il dito li copriva.
     */
    private void drawHead(Canvas canvas) {
        float mezzo = headHeight / 2f;
        float x = margine;
        float base = mezzo + titlePaint.getTextSize() * 0.35f;

        if (dentro()) {
            float l = dp(30);
            band.set(x, mezzo - l / 2f, x + l, mezzo + l / 2f);
            fill.setShader(null);
            fill.setColor(pressed == FASCIA ? CHIP_PREMUTO : CHIP);
            canvas.drawRoundRect(band, dp(9), dp(9), fill);
            Pittogrammi.disegna(canvas, "chevron-left", band.centerX(), mezzo, dp(18), segnoPaint);
            x = band.right + dp(10);
        }

        // Lo spazio che resta al percorso: tutta la fascia meno pallini e stato.
        float fine = getWidth() - margine - dp(150);
        if (!dentro()) {
            canvas.drawText(Testo.taglia(titlePaint, nomeDeck, fine - x), x, base, titlePaint);
        } else {
            // Radice, poi i puntini se i piani sono piu' di due, poi dove si e'.
            Nodo qui = percorso.get(percorso.size() - 1);
            String nome = qui.label.length() > 0 ? qui.label : qui.id;
            String prima = percorso.size() > 1 ? nomeDeck + "  ›  …  ›  " : nomeDeck + "  ›  ";
            float lp = tenuePaint.measureText(prima);
            if (lp < (fine - x) * 0.5f) {
                canvas.drawText(prima, x, base, tenuePaint);
                x += lp;
            }
            canvas.drawText(Testo.taglia(titlePaint, nome, fine - x), x, base, titlePaint);
        }

        // Lo stato: un punto acceso e « PC », oppure spento e « PC spento ».
        String stato = connected ? "PC" : "PC spento";
        tenuePaint.setTextSize(sp(12));
        float destra = getWidth() - margine;
        float ls = tenuePaint.measureText(stato);
        canvas.drawText(stato, destra - ls, mezzo + tenuePaint.getTextSize() * 0.35f, tenuePaint);
        tenuePaint.setTextSize(sp(15));
        float px = destra - ls - dp(9);
        if (connected) {
            dotPaint.setColor(VERDE);
            dotPaint.setAlpha(60);
            canvas.drawCircle(px, mezzo, dp(6), dotPaint);
        }
        dotPaint.setColor(connected ? VERDE : 0xFF55575D);
        canvas.drawCircle(px, mezzo, dp(3.5f), dotPaint);

        drawPageDots(canvas, px - dp(20), mezzo);
    }

    /**
     * I pallini delle pagine, allineati a destra e finiti in {@code destra}. Quello
     * della pagina che si guarda e' una lineetta: si allunga e si accorcia
     * insieme allo scorrimento, invece di saltare alla fine.
     */
    private void drawPageDots(Canvas canvas, float destra, float y) {
        int pages = pageCount();
        if (pages < 2) return;

        float h = dp(6);
        float corto = h;
        float lungo = dp(18);
        float spazio = dp(6);
        float here = page - slide;

        float totale = 0;
        for (int i = 0; i < pages; i++) {
            float vicino = 1f - Math.min(1f, Math.abs(i - here));
            totale += corto + (lungo - corto) * vicino + (i > 0 ? spazio : 0);
        }
        float x = destra - totale;
        for (int i = 0; i < pages; i++) {
            float vicino = 1f - Math.min(1f, Math.abs(i - here));
            float lw = corto + (lungo - corto) * vicino;
            pill.set(x, y - h / 2f, x + lw, y + h / 2f);
            dotPaint.setColor(fondi(PUNTO_SPENTO, PUNTO_ACCESO, vicino));
            canvas.drawRoundRect(pill, h / 2f, h / 2f, dotPaint);
            x += lw + spazio;
        }
    }

    /** Disegna una pagina traslata di {@code dx}; fuori dai limiti non fa nulla. */
    private void drawPage(Canvas canvas, int index, float dx) {
        if (index < 0 || index >= pageCount()) return;
        if (lato <= 1f) return;

        int first = index * perPage();
        boolean live = index == page && !sliding;

        // Le dimensioni dipendono dal lato del tasto, non dalla densita': la
        // stessa griglia deve restare leggibile con tre righe o con sei.
        float labelSize = Math.min(sp(13), lato * 0.115f);
        labelPaint.setTextSize(labelSize);
        glyphPaint.setTextSize(lato * 0.34f);

        for (int i = 0; i < perPage(); i++) {
            int col = i % cols;
            int row = i / cols;
            float left = dx + gridLeft + col * passo;
            float top = gridTop + row * passo;
            cell.set(left, top, left + lato, top + lato);

            // Fuori schermo durante lo scorrimento: saltarlo fa risparmiare
            // meta' del lavoro proprio nel momento in cui serve fluidita'.
            if (cell.right < -dp(4) || cell.left > getWidth() + dp(4)) continue;

            // L'incavo c'e' sempre, anche per le celle libere: si vede quanto
            // posto resta, e la griglia ha la sua forma anche mezza vuota.
            float bordo = dp(3);
            band.set(cell.left - bordo, cell.top - bordo, cell.right + bordo, cell.bottom + bordo);
            canvas.drawRoundRect(band, raggio + bordo, raggio + bordo, pozzo);

            if (first + i >= correnti.length) {
                fill.setShader(null);
                fill.setColor(VUOTO);
                canvas.drawRoundRect(cell, raggio, raggio, fill);
                continue;
            }

            drawKey(canvas, correnti[first + i], live && i == pressed);
        }
    }

    /** Un tasto pieno dentro {@link #cell}. */
    private void drawKey(Canvas canvas, Nodo nodo, boolean down) {
        int salvato = -1;
        if (down) {
            // Premuto, il tasto rientra appena e si accende: la conferma arriva
            // sotto il dito prima ancora che il PC risponda.
            salvato = canvas.save();
            canvas.scale(0.93f, 0.93f, cell.centerX(), cell.centerY());
        }

        locale.setScale(1f, lato);
        locale.postTranslate(0, cell.top);

        LinearGradient sfumatura = sfumatura(nodo.color);
        sfumatura.setLocalMatrix(locale);
        fill.setShader(sfumatura);
        fill.setAlpha(255);
        canvas.drawRoundRect(cell, raggio, raggio, fill);
        fill.setShader(null);

        lucido.setLocalMatrix(locale);
        canvas.drawRoundRect(cell, raggio, raggio, lustro);

        boolean hasLabel = nodo.label.length() > 0;
        float cy = hasLabel ? cell.centerY() - lato * 0.1f : cell.centerY();

        Bitmap icon = Icone.prendi(getContext(), nodo.icon);
        if (icon != null) {
            // Le proporzioni si rispettano: un'icona schiacciata si nota
            // subito, e le stesse misure valgono nell'anteprima sul PC.
            float side = lato * 0.44f;
            float k = Math.min(side / icon.getWidth(), side / icon.getHeight());
            float iw = icon.getWidth() * k;
            float ih = icon.getHeight() * k;
            iconRect.set(cell.centerX() - iw / 2f, cy - ih / 2f,
                    cell.centerX() + iw / 2f, cy + ih / 2f);
            canvas.drawBitmap(icon, null, iconRect, iconPaint);
        } else if (!Pittogrammi.disegna(canvas, nodo.glyph, cell.centerX(), cy, lato * 0.36f, segnoPaint)
                && nodo.glyph.length() > 0) {
            // Un glifo di una volta: va centrato sulla sua altezza reale, non
            // sulla linea di base, altrimenti sembra sempre spostato in basso.
            canvas.drawText(nodo.glyph, cell.centerX(), cy + glyphPaint.getTextSize() * 0.36f, glyphPaint);
        }

        if (hasLabel) {
            canvas.drawText(Testo.taglia(labelPaint, nodo.label, lato - dp(12)),
                    cell.centerX(), cell.bottom - lato * 0.115f, labelPaint);
        }

        // Una cartella si riconosce prima di premerla: i fogli impilati
        // nell'angolo dicono che dietro ce n'e' altri.
        if (nodo.cartella()) {
            segnoPaint.setAlpha(140);
            float l = lato * 0.13f;
            Pittogrammi.disegna(canvas, "layers",
                    cell.right - lato * 0.08f - l / 2f, cell.top + lato * 0.08f + l / 2f, l, segnoPaint);
            segnoPaint.setAlpha(0xF2);
        }

        if (down) {
            velo.setColor(0x33FFFFFF);
            canvas.drawRoundRect(cell, raggio, raggio, velo);
        } else if (!connected && !nodo.cartella()) {
            // A PC spento i pulsanti si vedono ma si spengono: non farebbero
            // niente. Le cartelle no, perche' aprirle funziona lo stesso.
            velo.setColor(0x8C08090A);
            canvas.drawRoundRect(cell, raggio, raggio, velo);
        }

        if (salvato >= 0) canvas.restoreToCount(salvato);
    }

    /**
     * La sfumatura di una tinta: un decimo piu' chiara in cima, un quinto piu'
     * scura in fondo. La stessa formula sta nell'anteprima sul PC.
     */
    private LinearGradient sfumatura(int colore) {
        LinearGradient g = sfumature.get(colore);
        if (g == null) {
            g = new LinearGradient(0, 0, 0, 1,
                    fondi(colore, 0xFFFFFFFF, 0.10f), fondi(colore, 0xFF000000, 0.22f),
                    Shader.TileMode.CLAMP);
            sfumature.put(colore, g);
        }
        return g;
    }

    private static int fondi(int da, int a, float quanto) {
        int r = (int) (Color.red(da) + (Color.red(a) - Color.red(da)) * quanto);
        int g = (int) (Color.green(da) + (Color.green(a) - Color.green(da)) * quanto);
        int b = (int) (Color.blue(da) + (Color.blue(a) - Color.blue(da)) * quanto);
        return Color.argb(255, r, g, b);
    }

    // ---- tocco ----

    /** Spostamento oltre il quale il gesto e' uno swipe e non un tocco. */
    private float swipeThreshold() {
        return dp(18);
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                if (velocity == null) velocity = VelocityTracker.obtain();
                velocity.clear();
                velocity.addMovement(event);
                // Un tocco durante l'assestamento lo interrompe: il dito ha
                // sempre la precedenza sull'animazione.
                if (sliding) {
                    if (slideTarget != NONE) page = slideTarget;
                    sliding = false;
                    slide = 0;
                    slideTarget = NONE;
                }
                dragFrom = event.getX();
                dragging = false;
                pressed = cellAt(event.getX(), event.getY());
                invalidate();
                return true;

            case MotionEvent.ACTION_MOVE: {
                if (velocity != null) velocity.addMovement(event);
                float dx = event.getX() - dragFrom;

                // Superata la soglia il gesto diventa uno swipe e il pulsante
                // premuto si annulla: scorrendo fra le pagine non deve partire
                // niente.
                if (!dragging && pressed != FASCIA && pageCount() > 1
                        && Math.abs(dx) > swipeThreshold()) {
                    dragging = true;
                    pressed = NONE;
                }

                if (dragging) {
                    float w = Math.max(1, getWidth());
                    float raw = dx / w;
                    // Ai bordi il movimento si fa pesante invece di fermarsi
                    // secco: si sente che oltre non c'e' niente.
                    boolean atEdge = (raw > 0 && page == 0) || (raw < 0 && page == pageCount() - 1);
                    slide = atEdge ? raw * 0.28f : Math.max(-1f, Math.min(1f, raw));
                    invalidate();
                    return true;
                }

                // Il dito che scivola fuori annulla: e' il comportamento che ci
                // si aspetta da un pulsante, e da' modo di ripensarci.
                int now = cellAt(event.getX(), event.getY());
                if (now != pressed) {
                    pressed = now;
                    invalidate();
                }
                return true;
            }

            case MotionEvent.ACTION_UP: {
                if (dragging) {
                    float vx = 0;
                    if (velocity != null) {
                        velocity.addMovement(event);
                        velocity.computeCurrentVelocity(1000);
                        vx = velocity.getXVelocity();
                    }

                    // Decide o la distanza percorsa o la velocita' con cui si e'
                    // lasciato: un quarto di pagina, oppure un colpetto deciso
                    // anche se corto.
                    boolean avanti = slide <= -0.25f || vx <= -flickSpeed;
                    boolean indietro = slide >= 0.25f || vx >= flickSpeed;

                    int target = NONE;
                    if (avanti && page < pageCount() - 1) target = page + 1;
                    else if (indietro && page > 0) target = page - 1;
                    settle(target);
                } else {
                    int hit = cellAt(event.getX(), event.getY());
                    if (hit != NONE && hit == pressed) premi(hit);
                }
                releaseTracker();
                pressed = NONE;
                dragging = false;
                invalidate();
                return true;
            }

            case MotionEvent.ACTION_CANCEL:
                releaseTracker();
                if (dragging) settle(NONE);
                pressed = NONE;
                dragging = false;
                invalidate();
                return true;

            default:
                return super.onTouchEvent(event);
        }
    }

    /**
     * Quel che succede quando si preme: la fascia riporta su, una cartella si
     * apre qui, tutto il resto parte per il PC.
     */
    private void premi(int hit) {
        playSoundEffect(SoundEffectConstants.CLICK);

        if (hit == FASCIA) {
            risali();
            return;
        }

        int slot = page * perPage() + hit;
        if (slot < 0 || slot >= correnti.length) return;

        Nodo nodo = correnti[slot];
        if (nodo.cartella()) {
            apri(nodo);
            return;
        }
        if (link != null) link.sendPress(nodo.id);
    }

    /**
     * Posto occupato dal dito: {@link #FASCIA} sulla fascia di una cartella
     * aperta, l'indice della cella nella pagina corrente, oppure {@link #NONE}.
     */
    private int cellAt(float x, float y) {
        misura();
        if (y < headHeight) return dentro() ? FASCIA : NONE;

        int first = page * perPage();
        int shown = Math.min(perPage(), correnti.length - first);
        if (shown <= 0 || lato <= 0) return NONE;

        int col = (int) Math.floor((x - gridLeft) / passo);
        int row = (int) Math.floor((y - gridTop) / passo);
        if (col < 0 || col >= cols || row < 0 || row >= rows) return NONE;

        // Dentro la griglia ma nello spazio fra due pulsanti: non e' una
        // pressione, e trattarla come tale farebbe partire azioni per sbaglio.
        if (x > gridLeft + col * passo + lato || y > gridTop + row * passo + lato) return NONE;

        int index = row * cols + col;
        return index < shown ? index : NONE;
    }

    private void releaseTracker() {
        if (velocity == null) return;
        velocity.recycle();
        velocity = null;
    }

    // ---- utilita' ----

    private float dp(float v) {
        return v * density;
    }

    private float sp(float v) {
        return v * getResources().getDisplayMetrics().scaledDensity;
    }
}
