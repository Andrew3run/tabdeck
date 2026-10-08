package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.BitmapShader;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Shader;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.Handler;
import android.view.MotionEvent;
import android.view.View;

import java.io.File;
import java.text.SimpleDateFormat;
import java.util.Arrays;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/**
 * La Dashboard: la prima cosa che il tablet mostra, e quella a cui si torna.
 *
 * <h3>Com'e' fatta</h3>
 *
 * Con le regole del deck. In cima una fascia, come quella del deck: la data a
 * sinistra, a destra la prossima sveglia e il timer che corre, che si premono.
 * Sotto, a sinistra l'ora in grande; a destra la casa, fatta di tasti - le
 * scene, le routine e le luci, ognuna col suo colore - a pagine di sei come le
 * pagine del deck, che si sfogliano col dito. In fondo una fila di tasti bassi:
 * le sezioni, le estensioni e il PC.
 *
 * Le pagine sono la risposta a « e se le scene sono dieci? »: restano tutte qui,
 * della stessa misura, invece di rimpicciolirsi o di finire in un'altra sezione.
 *
 * <h3>Cosa non c'e'</h3>
 *
 * Niente che si configuri qui: le luci e le scene si montano sul PC. Una sezione
 * spenta dal PC non compare nemmeno qui.
 *
 * Batte al secondo solo mentre si vede, e solo se un timer corre; altrimenti al
 * cambio del minuto.
 */
public final class DashboardView extends View implements Luci.Ascolto, Ora.Ascolto, Meteo.Ascolto {

    public interface Azioni {
        void apriDeck();
        void apriCasa();
        void apriOrologio(boolean sveglie);
        void apriSchermo();
        void connetti();
        void apriImpostazioni();
        void apriEstensione(String id);
    }

    /** Un'estensione con la sua voce nella barra. */
    public static final class Voce {
        final String id;
        final String nome;
        final Bitmap icona;

        public Voce(String id, String nome, Bitmap icona) {
            this.id = id;
            this.nome = nome;
            this.icona = icona;
        }
    }

    /** L'azzurro dell'azione: il tasto « Connetti al PC ». */
    private static final int AZIONE = Tinte.AZIONE;

    /** Tasti per riga e righe della casa: sei per pagina. */
    private static final int COLONNE = 3, RIGHE = 2;

    private final Misure m;
    private final Luci luci;
    private final Ora ora;
    private final Azioni azioni;
    private final Tasti tasti;
    private Vetro vetro;

    private final Paint pOra = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pTitolo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pOcchio = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pPunto = new Paint(Paint.ANTI_ALIAS_FLAG);

    // ---- la foto dietro l'ora ----
    /** Ogni quanto cambia la foto, e quanto dura la dissolvenza fra due. */
    private static final long OGNI_FOTO_MS = 5 * 60000L;
    private static final long DISSOLVENZA_MS = 1600L;

    /** Una foto pronta da disegnare, con il suo momento d'arrivo e il verso dello scorrimento. */
    private static final class Strato {
        final String nome, didascalia;
        final Bitmap foto;
        final BitmapShader shader;
        /** La stessa foto smerigliata, piccola: la si stira, e la stiratura la sfoca ancora. */
        final Bitmap smerigliata;
        final BitmapShader shaderVetro;
        final long inizio = System.currentTimeMillis();
        final int verso;

        Strato(String nome, String didascalia, Bitmap foto, Bitmap smerigliata, int verso) {
            this.nome = nome;
            this.didascalia = didascalia;
            this.foto = foto;
            this.shader = new BitmapShader(foto, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP);
            this.smerigliata = smerigliata;
            this.shaderVetro = smerigliata == null ? null
                    : new BitmapShader(smerigliata, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP);
            this.verso = verso;
        }
    }

    /** Il vetro dietro il meteo: pieno in cima, svanito a meta' del riquadro. */
    private LinearGradient maschera;
    private final Paint pVetroFoto = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final Matrix mVetro = new Matrix();

    private Strato foto, fotoPrima;
    private int verso = 1;
    private boolean caricando;
    private final Paint pFoto = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final Paint pVelo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pSegnoMeteo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pGradi = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pRiga = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Matrix mFoto = new Matrix();
    /** Il velo sopra la foto: chiaro in alto dietro il meteo, scuro in fondo dietro l'ora. */
    private LinearGradient velo;
    private final Meteo meteo;
    /** La luce azzurra della Home, in alto. */
    private final Paint pAloneSezione = new Paint();

    private final SimpleDateFormat formatoOra = new SimpleDateFormat("HH:mm", Locale.ITALIAN);
    private final SimpleDateFormat formatoData = new SimpleDateFormat("EEEE d MMMM", Locale.ITALIAN);

    // ---- stato che arriva da fuori ----
    private boolean collegato;
    private String trasporto = "";
    private boolean sezDeck = true, sezCasa = true, sezOrologio = true, sezSchermo = true;
    private final List<Voce> estensioni = new ArrayList<Voce>();

    // ---- aree ----
    private final RectF tempo = new RectF();
    private final RectF casa = new RectF();
    private final RectF titoloCasa = new RectF();
    private final RectF tutteSpente = new RectF();
    private final RectF chipSveglia = new RectF();
    private final RectF chipTimer = new RectF();
    private final RectF[] celle = new RectF[COLONNE * RIGHE];
    private final List<RectF> areeFondo = new ArrayList<RectF>();
    /** Cosa apre ogni tasto in fondo: "deck", "timer", "sveglie", "pc", oppure l'id di un'estensione. */
    private final List<String> quale = new ArrayList<String>();
    private final RectF pill = new RectF();

    // ---- testi composti fuori da onDraw ----
    private String testoOra = "", testoData = "", testoSveglia = "", testoTimer = null;

    private static final int P_TEMPO = 0, P_SVEGLIA = 1, P_TIMER = 2, P_TITOLO_CASA = 3, P_TUTTE = 4,
            P_CELLA = 5, P_FONDO = 6;
    private int premuto = -1, indice = -1;

    /** La pagina della casa, e lo sfoglio col dito. */
    private int pagina;
    private float daX;
    private boolean sfoglia;

    private boolean davanti;
    private final Handler battito = new Handler();
    private final Runnable tic = new Runnable() {
        @Override public void run() {
            if (!davanti) return;
            meteo.aggiorna();
            componi();
            invalidate();
            long adesso = System.currentTimeMillis();
            long passo = ora.conti().isEmpty() ? 60000 - (adesso % 60000) : 1000 - (adesso % 1000);
            battito.postDelayed(this, passo);
        }
    };

    public DashboardView(Context c, Misure misure, Luci luci, Ora ora, Azioni azioni) {
        super(c);
        m = misure;
        this.luci = luci;
        this.ora = ora;
        this.azioni = azioni;
        tasti = new Tasti(misure);
        meteo = new Meteo(c);
        meteo.setAscolto(this);
        pGradi.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
        pGradi.setColor(0xFFFFFFFF);
        pRiga.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        setClickable(true);
        for (int i = 0; i < celle.length; i++) celle[i] = new RectF();

        pOra.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
        pOra.setColor(Tinte.TESTO);
        pTitolo.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pTitolo.setColor(Tinte.TESTO);
        pOcchio.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pOcchio.setColor(Tinte.TESTO_TENUE);

        ora.aggiungiAscolto(this);
        Vetro.chiedi(m, new Vetro.Pronto() {
            @Override public void vetroPronto(Vetro v) {
                vetro = v;
                v.adattaA(getWidth(), getHeight());
                invalidate();
            }
        });
        componi();
    }

    /** Collegamento, sezioni accese ed estensioni: li decide l'activity. */
    public void stato(boolean collegato, String trasporto, boolean deck, boolean casa, boolean orologio,
                      boolean schermo, List<Voce> voci) {
        this.collegato = collegato;
        this.trasporto = trasporto == null ? "" : trasporto;
        sezDeck = deck;
        sezCasa = casa;
        sezOrologio = orologio;
        sezSchermo = schermo;
        estensioni.clear();
        estensioni.addAll(voci);
        disponi();
        invalidate();
    }

    public void attivo(boolean adesso) {
        if (davanti == adesso) return;
        davanti = adesso;
        battito.removeCallbacks(tic);
        if (davanti) {
            // Le luci accese si contano sullo stato vero: una lettura entrando, non un giro continuo.
            if (sezCasa && !luci.vuoto()) luci.aggiorna();
            meteo.aggiorna();
            battito.post(tic);
            invalidate();
        }
    }

    @Override
    public void luceCambiata(Lampada l) {
        if (davanti) invalidate();
    }

    @Override
    public void elencoCambiato() {
        pagina = Math.min(pagina, pagine() - 1);
        if (davanti) invalidate();
    }

    @Override
    public void oraCambiata() {
        componi();
        if (davanti) invalidate();
    }

    @Override
    public boolean hasOverlappingRendering() {
        return false;
    }

    @Override
    protected void onDetachedFromWindow() {
        super.onDetachedFromWindow();
        battito.removeCallbacks(tic);
    }

    // ---- misure ----

    @Override
    protected void onSizeChanged(int w, int h, int vw, int vh) {
        super.onSizeChanged(w, h, vw, vh);
        if (vetro != null) vetro.adattaA(w, h);
        pAloneSezione.setShader(Vetro.aloneSezione(w, h, Tinte.AZIONE));
        disponi();
        maschera = new LinearGradient(0, tempo.top, 0, tempo.top + tempo.height() * 0.5f,
                new int[] { 0xFF000000, 0xE0000000, 0x00000000 }, new float[] { 0f, 0.35f, 1f },
                Shader.TileMode.CLAMP);
        velo = new LinearGradient(0, tempo.top, 0, tempo.bottom,
                new int[] { 0x40000000, 0x08000000, 0x28000000, 0xC0000000 },
                new float[] { 0f, 0.28f, 0.55f, 1f }, Shader.TileMode.CLAMP);
        scegliFoto();
    }

    /** L'altezza della fascia in cima: la stessa del deck. */
    private float fascia() {
        return m.dp(52);
    }

    private void disponi() {
        int w = getWidth(), h = getHeight();
        if (w <= 0 || h <= 0) return;
        float mg = m.margine + m.s1;
        float spazio = m.s4;
        float testa = fascia();

        // In fondo i tasti bassi, a tutta larghezza: si raggiungono col pollice.
        quale.clear();
        // Timer e sveglie sono una sezione sola, con le sue due schede: un tasto.
        // Al posto del secondo, le Impostazioni, che sulla Home non c'erano.
        if (sezDeck) quale.add("deck");
        if (sezOrologio) quale.add("orologio");
        quale.add("impostazioni");
        for (Voce v : estensioni) quale.add(v.id);
        if (!collegato || sezSchermo) quale.add("pc");
        areeFondo.clear();
        float altaFondo = Math.max(m.bersaglio * 1.5f, h * 0.125f);
        float fondo = h - mg;
        if (!quale.isEmpty()) {
            int n = quale.size();
            float larga = (w - mg * 2f - spazio * (n - 1)) / n;
            for (int i = 0; i < n; i++) {
                float x = mg + (larga + spazio) * i;
                areeFondo.add(new RectF(x, fondo - altaFondo, x + larga, fondo));
            }
            fondo -= altaFondo + spazio;
        }

        // L'ora a sinistra, la casa a destra; senza casa l'ora prende tutto.
        float taglio = sezCasa ? mg + (w - mg * 2f - spazio) * 0.5f : w - mg;
        tempo.set(mg, testa, taglio, fondo);
        if (sezCasa) casa.set(taglio + spazio, testa, w - mg, fondo);
        else casa.setEmpty();

        // La testa della casa: il nome, i pallini e « Tutte spente ».
        float alta = Math.max(m.dp(34), m.bersaglio * 0.8f);
        float largaTutte = tasti.larghezzaChip("Tutte spente", alta);
        tutteSpente.set(casa.right - m.s3 - largaTutte, casa.top + m.s3,
                casa.right - m.s3, casa.top + m.s3 + alta);
        titoloCasa.set(casa.left, casa.top, tutteSpente.left - m.s2, tutteSpente.bottom + m.s1);

        // La griglia: sei tasti, il lato il piu' grande che ci sta.
        float gTop = tutteSpente.bottom + m.s3 + 3f;
        float gap = m.s3;
        float largaC = (casa.width() - m.s3 * 2f - gap * (COLONNE - 1)) / COLONNE;
        float altaC = (casa.bottom - m.s3 - 3f - gTop - gap * (RIGHE - 1)) / RIGHE;
        for (int i = 0; i < celle.length; i++) {
            float x = casa.left + m.s3 + (largaC + gap) * (i % COLONNE);
            float y = gTop + (altaC + gap) * (i / COLONNE);
            celle[i].set(x, y, x + largaC, y + altaC);
        }
        disponiChip();
    }

    /** I due chip della fascia, da destra: la sveglia, e prima il timer se corre. */
    private void disponiChip() {
        int w = getWidth();
        if (w <= 0) return;
        float mg = m.margine + m.s1;
        float alta = Math.max(m.dp(34), m.bersaglio * 0.8f);
        float y = (fascia() - alta) / 2f;
        float x = w - mg;
        if (sezOrologio) {
            float l = tasti.larghezzaChip(testoSveglia, alta);
            chipSveglia.set(x - l, y, x, y + alta);
            x -= l + m.s2;
            if (testoTimer != null) {
                float lt = tasti.larghezzaChip(testoTimer, alta);
                chipTimer.set(x - lt, y, x, y + alta);
            } else {
                chipTimer.setEmpty();
            }
        } else {
            chipSveglia.setEmpty();
            chipTimer.setEmpty();
        }
    }

    private void componi() {
        scegliFoto();
        Date adesso = new Date();
        testoOra = formatoOra.format(adesso);
        String giorno = formatoData.format(adesso);
        testoData = Character.toUpperCase(giorno.charAt(0)) + giorno.substring(1);
        String prossima = ora.prossimaSveglia();
        testoSveglia = prossima != null ? prossima : "Nessuna sveglia";
        List<Ora.Conto> conti = ora.conti();
        testoTimer = conti.isEmpty() ? null : Ora.scorrere(conti.get(0).restano())
                + (conti.size() > 1 ? "  +" + (conti.size() - 1) : "");
        disponiChip();
    }

    // ---- la casa ----

    /** Il segnaposto del tasto « Tutto », che apre la sezione Casa. */
    private static final Object TUTTO = new Object();

    /**
     * Tutto quel che la casa sa fare con un tocco: prima le scene, poi le
     * routine, poi le luci, e in fondo « Tutto », che porta alla sezione Casa
     * dove ci sono le stanze, la luminosita' e i colori.
     */
    private List<Object> scelte() {
        List<Object> fuori = new ArrayList<Object>();
        for (Routine r : luci.routine()) if (r.scena) fuori.add(r);
        for (Routine r : luci.routine()) if (!r.scena) fuori.add(r);
        fuori.addAll(luci.elenco());
        if (!fuori.isEmpty()) fuori.add(TUTTO);
        return fuori;
    }

    private int pagine() {
        int n = scelte().size();
        return Math.max(1, (n + celle.length - 1) / celle.length);
    }

    // ---- disegno ----

    @Override
    protected void onDraw(Canvas c) {
        if (vetro == null || !vetro.vivo()) {
            c.drawColor(Tinte.FONDO);
            return;
        }
        vetro.disegnaSfondo(c, getWidth(), getHeight());
        c.drawRect(0, 0, getWidth(), getHeight(), pAloneSezione);
        disegnaFascia(c);
        disegnaTempo(c);
        if (sezCasa) disegnaCasa(c);
        disegnaFondo(c);
    }

    private void disegnaFascia(Canvas c) {
        float mg = m.margine + m.s1;
        pTitolo.setTextSize(m.corpo);
        float limite = (chipTimer.isEmpty() ? chipSveglia.left : chipTimer.left) - m.s3;
        if (chipSveglia.isEmpty()) limite = getWidth() - mg;
        c.drawText(Testo.taglia(pTitolo, testoData, limite - mg), mg,
                fascia() / 2f - (pTitolo.descent() + pTitolo.ascent()) / 2f, pTitolo);
        if (!chipSveglia.isEmpty()) {
            boolean viva = ora.prossimaSveglia() != null;
            tasti.chip(c, vetro, chipSveglia, -1, viva ? "alarm-clock" : "alarm-clock-off", testoSveglia,
                    Tinte.OROLOGIO, viva ? 0.55f : 0f, premuto == P_SVEGLIA);
        }
        if (!chipTimer.isEmpty() && testoTimer != null) {
            tasti.chip(c, vetro, chipTimer, -1, "clock", testoTimer, Tinte.OROLOGIO, 0.55f, premuto == P_TIMER);
        }
    }

    /**
     * L'ora sopra una foto del salvaschermo, come quando il tablet riposa.
     *
     * La foto si muove: un lento avvicinarsi con un po' di scorrimento, che
     * cambia verso a ogni foto, e quando arriva la prossima la vecchia sfuma
     * via sotto. Sopra, un velo che scende dal basso tiene leggibili l'ora e la
     * didascalia; in alto a sinistra il meteo del posto. Senza foto resta il
     * pannello, con l'ora e il meteo.
     */
    private void disegnaTempo(Canvas c) {
        long adesso = System.currentTimeMillis();
        boolean muove = false;

        if (foto == null) {
            vetro.pannello(c, tempo, m.raggio, Tinte.OROLOGIO, 0x10);
        } else {
            vetro.incavo(c, tempo, m.raggio, 2.5f);
            float dissolvenza = Math.min(1f, (adesso - foto.inizio) / (float) DISSOLVENZA_MS);
            if (fotoPrima != null && dissolvenza < 1f) {
                disegnaStrato(c, fotoPrima, adesso, 255);
                disegnaStrato(c, foto, adesso, (int) (255 * dissolvenza));
            } else {
                fotoPrima = null;
                disegnaStrato(c, foto, adesso, 255);
            }
            muove = true;

            pVelo.setShader(velo);
            c.drawRoundRect(tempo, m.raggio, m.raggio, pVelo);
        }

        // Il meteo, in alto a sinistra.
        Meteo.Dati d = meteo.dati();
        float x = tempo.left + m.dp(24);
        if (d != null) {
            float y = tempo.top + m.dp(22);
            float lato = m.dp(30);
            pSegnoMeteo.setColor(0xFFFFFFFF);
            pSegnoMeteo.setShadowLayer(m.dp(6), 0, m.dp(1), 0x66000000);
            Pittogrammi.disegna(c, d.icona(), x + lato / 2f, y + lato / 2f, lato, pSegnoMeteo);
            pGradi.setTextSize(m.dp(32));
            pGradi.setShadowLayer(m.dp(8), 0, m.dp(1), 0x80000000);
            String gradi = d.temperatura + "°";
            float base = y + lato / 2f - (pGradi.descent() + pGradi.ascent()) / 2f;
            c.drawText(gradi, x + lato + m.dp(10), base, pGradi);
            float dopo = x + lato + m.dp(10) + pGradi.measureText(gradi) + m.dp(14);
            pRiga.setTextSize(m.dp(14));
            pRiga.setColor(0xF2FFFFFF);
            pRiga.setShadowLayer(m.dp(6), 0, m.dp(1), 0x80000000);
            String sopra = d.citta.length() > 0 ? d.citta : d.detto();
            float largo = tempo.right - m.dp(20) - dopo;
            c.drawText(Testo.taglia(pRiga, sopra, largo), dopo, y + lato / 2f - m.dp(3), pRiga);
            pRiga.setTextSize(m.dp(12));
            pRiga.setColor(0xCCFFFFFF);
            String sotto = (d.citta.length() > 0 ? d.detto() + " · " : "") + "max " + d.massima + "° · min " + d.minima + "°";
            c.drawText(Testo.taglia(pRiga, sotto, largo), dopo, y + lato / 2f + m.dp(13), pRiga);
        }

        // L'ora, e sotto la didascalia della foto: in basso a sinistra.
        float corpo = Math.min(tempo.height() * 0.30f, tempo.width() / 3.4f);
        pOra.setTextSize(corpo);
        pOra.setColor(premuto == P_TEMPO ? Tinte.TESTO_MEDIO : Tinte.TESTO);
        String dida = foto != null ? foto.didascalia : "";
        if (foto == null) {
            pOra.setShadowLayer(0, 0, 0, 0);
            c.drawText(testoOra, x, tempo.centerY() - (pOra.descent() + pOra.ascent()) / 2f + m.dp(14), pOra);
        } else {
            float fondo = tempo.bottom - m.dp(24);
            if (dida.length() > 0) {
                pRiga.setTextSize(m.dp(12));
                pRiga.setColor(0xB3FFFFFF);
                pRiga.setShadowLayer(m.dp(6), 0, m.dp(1), 0x80000000);
                c.drawText(Testo.taglia(pRiga, dida, tempo.width() - m.dp(48)), x, fondo, pRiga);
                fondo -= m.dp(12) + m.dp(10);
            }
            pOra.setShadowLayer(m.dp(10), 0, m.dp(2), 0x80000000);
            c.drawText(testoOra, x - corpo * 0.03f, fondo - pOra.descent() * 0.3f, pOra);
            pOra.setShadowLayer(0, 0, 0, 0);
        }

        // Il movimento: quindici fotogrammi al secondo bastano a uno zoom lento,
        // e solo mentre la Home si vede.
        if (muove && davanti) postInvalidateDelayed(66);
    }

    /** Una foto col suo movimento: si avvicina dell'otto per cento e scivola di lato. */
    private void disegnaStrato(Canvas c, Strato s, long adesso, int alfa) {
        float t = Math.min(1f, (adesso - s.inizio) / (float) (OGNI_FOTO_MS + DISSOLVENZA_MS));
        // Una curva morbida: parte e arriva piano, non a scatti.
        float mor = t * t * (3f - 2f * t);
        float base = Math.max(tempo.width() / s.foto.getWidth(), tempo.height() / s.foto.getHeight());
        float k = base * (1.04f + 0.08f * mor);
        float largo = s.foto.getWidth() * k, alto = s.foto.getHeight() * k;
        float scivola = (largo - tempo.width()) * 0.5f * (mor - 0.5f) * s.verso;
        float sale = (alto - tempo.height()) * 0.3f * (mor - 0.5f);
        mFoto.setScale(k, k);
        mFoto.postTranslate(tempo.centerX() - largo / 2f + scivola, tempo.centerY() - alto / 2f - sale);
        s.shader.setLocalMatrix(mFoto);
        pFoto.setShader(s.shader);
        pFoto.setAlpha(alfa);
        c.drawRoundRect(tempo, m.raggio, m.raggio, pFoto);

        // Il vetro: la copia smerigliata con la stessa matrice, ingrandita di
        // quanto e' piu' piccola, e una maschera che la fa svanire scendendo.
        if (s.shaderVetro != null && maschera != null) {
            float r = s.foto.getWidth() / (float) s.smerigliata.getWidth();
            mVetro.set(mFoto);
            mVetro.preScale(r, r);
            s.shaderVetro.setLocalMatrix(mVetro);
            pVetroFoto.setShader(new android.graphics.ComposeShader(s.shaderVetro, maschera,
                    android.graphics.PorterDuff.Mode.DST_IN));
            pVetroFoto.setAlpha(alfa);
            c.drawRoundRect(tempo, m.raggio, m.raggio, pVetroFoto);
        }
    }

    /**
     * La copia smerigliata di una foto: ridotta a un dodicesimo e passata tre
     * volte per una media mobile. Piccola com'e', costa pochi kilobyte e pochi
     * millisecondi, ed e' gia' sul thread delle foto.
     */
    private static Bitmap smeriglia(Bitmap b) {
        int w = Math.max(8, b.getWidth() / 12), h = Math.max(8, b.getHeight() / 12);
        Bitmap piccola = Bitmap.createScaledBitmap(b, w, h, true).copy(Bitmap.Config.ARGB_8888, true);
        int[] px = new int[w * h];
        int[] tmp = new int[w * h];
        piccola.getPixels(px, 0, w, 0, 0, w, h);
        for (int passata = 0; passata < 3; passata++) {
            media(px, tmp, w, h, 2, true);
            media(tmp, px, w, h, 2, false);
        }
        // Un velo chiaro appena accennato: il vetro e' un po' piu' luminoso di quel che copre.
        for (int i = 0; i < px.length; i++) {
            int c = px[i];
            int r = Math.min(255, ((c >> 16) & 0xFF) + 14);
            int g = Math.min(255, ((c >> 8) & 0xFF) + 14);
            int bl = Math.min(255, (c & 0xFF) + 16);
            px[i] = 0xFF000000 | (r << 16) | (g << 8) | bl;
        }
        piccola.setPixels(px, 0, w, 0, 0, w, h);
        return piccola;
    }

    private static void media(int[] da, int[] a, int w, int h, int raggio, boolean orizzontale) {
        int lungo = orizzontale ? w : h, corto = orizzontale ? h : w;
        for (int riga = 0; riga < corto; riga++) {
            for (int i = 0; i < lungo; i++) {
                int r = 0, g = 0, b = 0, n = 0;
                for (int k = -raggio; k <= raggio; k++) {
                    int j = i + k;
                    if (j < 0 || j >= lungo) continue;
                    int c = orizzontale ? da[riga * w + j] : da[j * w + riga];
                    r += (c >> 16) & 0xFF;
                    g += (c >> 8) & 0xFF;
                    b += c & 0xFF;
                    n++;
                }
                int c = 0xFF000000 | ((r / n) << 16) | ((g / n) << 8) | (b / n);
                if (orizzontale) a[riga * w + i] = c;
                else a[i * w + riga] = c;
            }
        }
    }

    /**
     * La foto di adesso: una di quelle del salvaschermo, che il tablet tiene
     * anche a PC spento, e cambia ogni cinque minuti. Si decodifica fuori dal
     * thread dell'interfaccia e gia' ridotta, in 565: una foto intera in 8888
     * su questo tablet pesa piu' dell'app intera.
     */
    private void scegliFoto() {
        // Prima delle misure non si sa quanto grande decodificarla.
        if (tempo.width() < 2 || tempo.height() < 2) return;
        File[] tutte = getContext().getDir("salvaschermo", Context.MODE_PRIVATE).listFiles();
        java.util.List<File> buone = new ArrayList<File>();
        if (tutte != null) {
            Arrays.sort(tutte);
            for (File f : tutte) if (!f.getName().endsWith(".arrivo")) buone.add(f);
        }
        if (buone.isEmpty()) {
            if (foto != null) {
                foto = null;
                fotoPrima = null;
                invalidate();
            }
            return;
        }
        final File scelta = buone.get((int) ((System.currentTimeMillis() / OGNI_FOTO_MS) % buone.size()));
        if ((foto != null && scelta.getName().equals(foto.nome)) || caricando) return;
        caricando = true;
        // Un po' piu' grande del riquadro: lo zoom arriva al dodici per cento.
        final int largo = Math.max(1, (int) (tempo.width() * 1.15f)), alto = Math.max(1, (int) (tempo.height() * 1.15f));
        final String dida = didascalia(scelta.getName());
        new Thread(new Runnable() {
            @Override public void run() {
                Bitmap b = null;
                try {
                    BitmapFactory.Options o = new BitmapFactory.Options();
                    o.inJustDecodeBounds = true;
                    BitmapFactory.decodeFile(scelta.getPath(), o);
                    int campione = 1;
                    while (o.outWidth / (campione * 2) >= largo && o.outHeight / (campione * 2) >= alto) campione *= 2;
                    o = new BitmapFactory.Options();
                    o.inSampleSize = campione;
                    o.inPreferredConfig = Bitmap.Config.RGB_565;
                    b = BitmapFactory.decodeFile(scelta.getPath(), o);
                } catch (Throwable t) {
                    b = null;
                }
                Bitmap s = null;
                try {
                    if (b != null) s = smeriglia(b);
                } catch (Throwable t) {
                    s = null;
                }
                final Bitmap fatta = b, smerigliata = s;
                post(new Runnable() {
                    @Override public void run() {
                        caricando = false;
                        if (fatta == null) return;
                        fotoPrima = foto;
                        foto = new Strato(scelta.getName(), dida, fatta, smerigliata, verso = -verso);
                        invalidate();
                    }
                });
            }
        }, "TabDeck-foto").start();
    }

    /** La didascalia di una foto, come il PC l'ha mandata al salvaschermo. */
    private String didascalia(String nome) {
        try {
            String s = getContext().getSharedPreferences("salvaschermo", Context.MODE_PRIVATE).getString("config", null);
            if (s == null) return "";
            org.json.JSONArray elenco = new org.json.JSONObject(s).optJSONArray("foto");
            if (elenco == null) return "";
            for (int i = 0; i < elenco.length(); i++) {
                org.json.JSONObject f = elenco.optJSONObject(i);
                if (f != null && nome.equals(f.optString("nome"))) return f.optString("didascalia", "");
            }
        } catch (Exception ignorato) {
            // senza didascalia
        }
        return "";
    }

    @Override
    public void meteoCambiato(Meteo.Dati d) {
        if (davanti) invalidate();
    }

    private void disegnaCasa(Canvas c) {
        vetro.pannello(c, casa, m.raggio, CasaView.TINTA, 0x10);

        // La testa: « CASA » e quante accese, i pallini delle pagine, « Tutte spente ».
        List<Lampada> elenco = luci.elenco();
        int accese = 0;
        for (Lampada l : elenco) if (accesa(l)) accese++;
        float y = tutteSpente.centerY();
        float x = casa.left + m.s3 + m.s1;
        pOcchio.setTextSize(m.micro);
        pOcchio.setColor(premuto == P_TITOLO_CASA ? Tinte.TESTO : Tinte.TESTO_TENUE);
        float base = y - (pOcchio.descent() + pOcchio.ascent()) / 2f;
        c.drawText("CASA", x, base, pOcchio);
        float dopo = x + pOcchio.measureText("CASA") + m.s2;
        if (!elenco.isEmpty()) {
            tasti.chip(c, vetro, tutteSpente, Tratti.ACCENSIONE, null, "Tutte spente",
                    CasaView.TINTA, 0f, premuto == P_TUTTE);
        }
        disegnaPallini(c, tutteSpente.left - m.s3, y);

        List<Object> tutte = scelte();
        if (tutte.isEmpty()) {
            pOcchio.setTextSize(m.nota);
            pOcchio.setColor(Tinte.SPENTO);
            c.drawText(Testo.taglia(pOcchio, "Nessuna luce", casa.width() - m.s3 * 2f),
                    x, celle[0].top + m.nota, pOcchio);
            return;
        }
        int primo = pagina * celle.length;
        for (int i = 0; i < celle.length; i++) {
            RectF b = celle[i];
            if (primo + i >= tutte.size()) {
                // Il posto c'e' anche vuoto, come nel deck: la griglia ha la sua forma.
                vetro.vuoto(c, b, tasti.raggio(b));
                continue;
            }
            Object o = tutte.get(primo + i);
            boolean giu = premuto == P_CELLA && indice == i;
            if (o == TUTTO) {
                tasti.fondo(c, vetro, b, 0, 0f, giu);
                tasti.contenuto(c, b, -1, "ellipsis", null, "Tutto", null, false);
            } else if (o instanceof Routine) {
                Routine r = (Routine) o;
                tasti.fondo(c, vetro, b, r.colore, r.inCorso ? 0.45f : 1f, giu);
                tasti.contenuto(c, b, CasaView.icona(r), null, null, r.nome, r.inCorso ? "in corso" : null, true);
            } else {
                Lampada l = (Lampada) o;
                boolean acc = accesa(l);
                tasti.fondo(c, vetro, b, l.colore, acc ? 1f : 0f, giu);
                if (acc) tasti.alone(c, b, 0xFFFFE8B0);
                // Finche' la lampada non ha risposto non si scrive niente: dei
                // puntini sotto il nome sembravano un nome tagliato.
                String nota = l.stato == null ? null : !l.stato.raggiunta ? "non risponde"
                        : acc ? (l.stato.luminosita > 0 ? l.stato.luminosita + "%" : "accesa") : "spenta";
                tasti.contenuto(c, b, Tratti.LAMPADINA, null, null,
                        l.nome.length() > 0 ? l.nome : l.ip, nota, acc);
            }
        }
    }

    private static boolean accesa(Lampada l) {
        return l.stato != null && l.stato.raggiunta && l.stato.accesa;
    }

    /** I pallini delle pagine della casa, finiti a {@code destra}: come nel deck. */
    private void disegnaPallini(Canvas c, float destra, float y) {
        int n = pagine();
        if (n < 2) return;
        float h = m.dp(6), lungo = m.dp(18), spazio = m.dp(6);
        float x = destra - ((n - 1) * (h + spazio) + lungo);
        for (int i = 0; i < n; i++) {
            float l = i == pagina ? lungo : h;
            pill.set(x, y - h / 2f, x + l, y + h / 2f);
            pPunto.setColor(i == pagina ? 0xFFD8D9DC : 0xFF3A3C42);
            c.drawRoundRect(pill, h / 2f, h / 2f, pPunto);
            x += l + spazio;
        }
    }

    private void disegnaFondo(Canvas c) {
        for (int i = 0; i < areeFondo.size(); i++) {
            RectF b = areeFondo.get(i);
            String cosa = quale.get(i);
            boolean giu = premuto == P_FONDO && indice == i;
            if ("pc".equals(cosa)) {
                // Il PC e' l'unico tasto colorato in fondo: e' quel che manca, o quel che si guarda.
                tasti.fondo(c, vetro, b, AZIONE, collegato ? 0f : 1f, giu);
                tasti.inRiga(c, b, -1, collegato ? "monitor" : "plug", null,
                        collegato ? ("USB".equalsIgnoreCase(trasporto) ? "Schermo · cavo" : "Schermo · rete")
                                : "Connetti al PC", !collegato);
                continue;
            }
            // Ognuno col colore della sua sezione, lo stesso della barra.
            if ("deck".equals(cosa)) {
                tasti.fondo(c, vetro, b, 0xFF8B6BFF, 0.55f, giu);
                tasti.inRiga(c, b, -1, "layout-grid", null, "Deck", true);
            } else if ("orologio".equals(cosa)) {
                tasti.fondo(c, vetro, b, Tinte.OROLOGIO, 0.5f, giu);
                tasti.inRiga(c, b, -1, "timer", null, "Timer e sveglie", true);
            } else if ("impostazioni".equals(cosa)) {
                tasti.fondo(c, vetro, b, 0xFF7D8CA8, 0.45f, giu);
                tasti.inRiga(c, b, -1, "settings", null, "Impostazioni", true);
            } else {
                tasti.fondo(c, vetro, b, 0, 0f, giu);
                Voce voce = null;
                for (Voce v : estensioni) if (v.id.equals(cosa)) voce = v;
                tasti.inRiga(c, b, voce != null && voce.icona != null ? -1 : Tratti.ESTENSIONE, null,
                        voce != null ? voce.icona : null, voce != null ? voce.nome : cosa, false);
            }
        }
    }

    // ---- tocco ----

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        float x = e.getX(), y = e.getY();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                daX = x;
                sfoglia = false;
                trova(x, y);
                invalidate();
                return true;
            case MotionEvent.ACTION_MOVE:
                // Sulla casa un trascinamento di lato sfoglia le pagine, e non preme niente.
                if (!sfoglia && casa.contains(daX, e.getY()) && Math.abs(x - daX) > m.dp(24) && pagine() > 1) {
                    sfoglia = true;
                    premuto = indice = -1;
                    invalidate();
                }
                return true;
            case MotionEvent.ACTION_UP: {
                if (sfoglia) {
                    int verso = x < daX ? 1 : -1;
                    pagina = Math.max(0, Math.min(pagine() - 1, pagina + verso));
                    sfoglia = false;
                    invalidate();
                    return true;
                }
                int cosa = premuto, i = indice;
                trova(x, y);
                boolean stessoPosto = cosa >= 0 && cosa == premuto && i == indice;
                premuto = indice = -1;
                if (stessoPosto) agisci(cosa, i);
                invalidate();
                return true;
            }
            case MotionEvent.ACTION_CANCEL:
                premuto = indice = -1;
                sfoglia = false;
                invalidate();
                return true;
        }
        return super.onTouchEvent(e);
    }

    private void trova(float x, float y) {
        premuto = indice = -1;
        for (int i = 0; i < areeFondo.size(); i++) {
            if (areeFondo.get(i).contains(x, y)) {
                premuto = P_FONDO;
                indice = i;
                return;
            }
        }
        if (chipSveglia.contains(x, y)) { premuto = P_SVEGLIA; return; }
        if (chipTimer.contains(x, y)) { premuto = P_TIMER; return; }
        if (sezCasa) {
            if (!luci.vuoto() && tutteSpente.contains(x, y)) { premuto = P_TUTTE; return; }
            if (titoloCasa.contains(x, y)) { premuto = P_TITOLO_CASA; return; }
            List<Object> tutte = scelte();
            int primo = pagina * celle.length;
            for (int i = 0; i < celle.length; i++) {
                if (primo + i < tutte.size() && celle[i].contains(x, y)) {
                    premuto = P_CELLA;
                    indice = i;
                    return;
                }
            }
        }
        if (sezOrologio && tempo.contains(x, y)) premuto = P_TEMPO;
    }

    private void agisci(int cosa, int i) {
        switch (cosa) {
            case P_TEMPO:
            case P_TIMER:
                azioni.apriOrologio(false);
                break;
            case P_SVEGLIA:
                azioni.apriOrologio(true);
                break;
            case P_TITOLO_CASA:
                azioni.apriCasa();
                break;
            case P_TUTTE:
                luci.tutteSpente();
                break;
            case P_CELLA: {
                List<Object> tutte = scelte();
                int n = pagina * celle.length + i;
                if (n >= tutte.size()) break;
                Object o = tutte.get(n);
                if (o == TUTTO) azioni.apriCasa();
                else if (o instanceof Routine) luci.esegui((Routine) o);
                else luci.inverti((Lampada) o);
                break;
            }
            case P_FONDO: {
                String q = quale.get(i);
                if ("deck".equals(q)) azioni.apriDeck();
                else if ("orologio".equals(q)) azioni.apriOrologio(false);
                else if ("impostazioni".equals(q)) azioni.apriImpostazioni();
                else if ("pc".equals(q)) {
                    if (collegato) azioni.apriSchermo();
                    else azioni.connetti();
                } else azioni.apriEstensione(q);
                break;
            }
        }
    }
}
