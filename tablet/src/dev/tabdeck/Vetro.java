package dev.tabdeck;

import android.graphics.Bitmap;
import android.graphics.BitmapShader;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.util.ArrayList;
import java.util.List;

/**
 * Il vetro smerigliato dei pannelli dell'orologio.
 *
 * Il vetro costoso e' quello che sfoca quello che si muove dietro. Qui dietro
 * non si muove niente: lo sfondo e' un'immagine ferma che si genera da soli.
 * Quindi la sfocatura si fa <b>una volta sola</b>, e per il resto della vita
 * del processo un pannello di vetro costa un rettangolo con una texture da
 * pochi kilobyte.
 *
 * La catena: {@link Sfondo#componi} a piena misura (2,4 MB, temporanei), ridotta
 * a un ottavo, tre passate di media mobile, e la grande si ricicla subito. Si
 * disegna ingrandita con filtro bilineare, che aggiunge morbidezza gratis. Per
 * dare l'idea dello spessore, dentro un pannello la stessa immagine e'
 * sfalsata di pochi pixel.
 *
 * <h3>Uno per processo, fatto in disparte</h3>
 *
 * Lo usano due schermate - l'orologio e quella che suona - e l'activity, che e'
 * la Home, viene rifatta piu' volte al giorno: si compone una volta e si tiene.
 * La composizione gira su un thread suo che finisce, perche' in onCreate un
 * paio di decimi di secondo si vedrebbero come una Home che tarda. Chi lo
 * chiede prima che sia pronto viene richiamato; se la memoria non basta si
 * ripiega su un vetro a tinta unita, e le schermate restano usabili.
 */
public final class Vetro {

    private static final String TAG = "TabDeck.Vetro";

    /** Un ottavo. Sotto si vedono i quadrati, sopra si spreca. */
    private static final int RIDUZIONE = 8;
    private static final int RAGGIO_SFOCATURA = 16;

    /** Di quanto il vetro di un pannello e' sfalsato rispetto allo sfondo. */
    private static final float SFALSAMENTO = 7f;

    /** Il nero appena accennato dentro un incavo, il bianco sotto un comando. */
    private static final int FONDO_COMANDO = 0x16FFFFFF;

    public interface Pronto {
        /** Thread UI: il vetro c'e'. */
        void vetroPronto(Vetro v);
    }

    private static Vetro condiviso;
    private static boolean inCostruzione;
    private static final List<Pronto> attese = new ArrayList<Pronto>();

    private final Bitmap sfocato;
    private final BitmapShader shader;

    private final Matrix mFondo = new Matrix();
    private final Matrix mPannello = new Matrix();

    private final Paint pFondo = new Paint(Paint.FILTER_BITMAP_FLAG);
    private final Paint pVetro = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final Paint pVelo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pBordo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pBagliore = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final RectF appoggio = new RectF();
    private final RectF incavo = new RectF();
    private final Paint pLucido = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pTinta = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final android.util.SparseArray<LinearGradient> sfumature = new android.util.SparseArray<LinearGradient>();
    private final Matrix mLucido = new Matrix();
    /** Il filo di luce in cima e d'ombra in fondo dei tasti del deck, nel quadrato da 0 a 1. */
    private final LinearGradient lucido = new LinearGradient(0, 0, 0, 1,
            new int[] { 0x30FFFFFF, 0x00FFFFFF, 0x00000000, 0x4D000000 },
            new float[] { 0f, 0.035f, 0.965f, 1f }, Shader.TileMode.CLAMP);

    private Vetro(Bitmap sfocato) {
        this.sfocato = sfocato;
        shader = new BitmapShader(sfocato, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP);
        pFondo.setShader(shader);
        pVetro.setShader(shader);
        pBordo.setStyle(Paint.Style.STROKE);
        pBordo.setStrokeWidth(1f);
        pLucido.setShader(lucido);
    }

    /**
     * Il vetro, adesso o appena e' pronto. Dal thread UI.
     */
    public static void chiedi(final Misure m, Pronto chi) {
        if (condiviso != null && condiviso.vivo()) {
            chi.vetroPronto(condiviso);
            return;
        }
        attese.add(chi);
        if (inCostruzione) return;
        inCostruzione = true;

        final Handler ui = new Handler(Looper.getMainLooper());
        new Thread(new Runnable() {
            @Override public void run() {
                Vetro v = null;
                try {
                    v = crea(m);
                } catch (Throwable t) {
                    Log.w(TAG, "vetro non composto, ripiego sulla tinta unita: " + t);
                }
                if (v == null) {
                    try {
                        Bitmap tinta = Bitmap.createBitmap(4, 4, Bitmap.Config.ARGB_8888);
                        tinta.eraseColor(Tinte.FONDO);
                        v = new Vetro(tinta);
                    } catch (Throwable t) {
                        Log.w(TAG, "nemmeno il ripiego: " + t);
                    }
                }
                final Vetro fatto = v;
                ui.post(new Runnable() {
                    @Override public void run() {
                        inCostruzione = false;
                        condiviso = fatto;
                        List<Pronto> chi = new ArrayList<Pronto>(attese);
                        attese.clear();
                        if (fatto == null) return;
                        for (int i = 0; i < chi.size(); i++) chi.get(i).vetroPronto(fatto);
                    }
                });
            }
        }, "TabDeck-vetro").start();
    }

    private static Vetro crea(Misure m) {
        Bitmap grande = Sfondo.componi(m.larghezza, m.altezza,
                Sfondo.variante(System.currentTimeMillis()));
        Bitmap piccolo;
        try {
            piccolo = Bitmap.createScaledBitmap(grande,
                    Math.max(1, m.larghezza / RIDUZIONE),
                    Math.max(1, m.altezza / RIDUZIONE), true);
        } finally {
            grande.recycle();
        }
        long inizio = System.currentTimeMillis();
        sfoca(piccolo, RAGGIO_SFOCATURA);
        Log.i(TAG, "sfocatura " + piccolo.getWidth() + "x" + piccolo.getHeight()
                + " in " + (System.currentTimeMillis() - inizio) + " ms");
        return new Vetro(piccolo);
    }

    /** Tre passate di media mobile: una gaussiana senza calcolare una gaussiana. */
    private static void sfoca(Bitmap b, int raggio) {
        int w = b.getWidth(), h = b.getHeight();
        int[] px = new int[w * h];
        b.getPixels(px, 0, w, 0, 0, w, h);
        int[] appoggio = new int[w * h];
        for (int passata = 0; passata < 3; passata++) {
            media(px, appoggio, w, h, raggio, true);
            media(appoggio, px, w, h, raggio, false);
        }
        b.setPixels(px, 0, w, 0, 0, w, h);
    }

    private static void media(int[] da, int[] a, int w, int h, int raggio, boolean orizzontale) {
        int lungo = orizzontale ? w : h;
        int corto = orizzontale ? h : w;
        for (int riga = 0; riga < corto; riga++) {
            for (int i = 0; i < lungo; i++) {
                int r = 0, g = 0, bl = 0, quanti = 0;
                for (int k = -raggio; k <= raggio; k++) {
                    int j = i + k;
                    if (j < 0 || j >= lungo) continue;
                    int c = orizzontale ? da[riga * w + j] : da[j * w + riga];
                    r += (c >> 16) & 0xFF;
                    g += (c >> 8) & 0xFF;
                    bl += c & 0xFF;
                    quanti++;
                }
                int c = 0xFF000000 | ((r / quanti) << 16) | ((g / quanti) << 8) | (bl / quanti);
                if (orizzontale) a[riga * w + i] = c;
                else a[i * w + riga] = c;
            }
        }
    }

    // ---- disegno ----

    /** Ricalcola l'ingrandimento per la View che disegna. Nessuna allocazione. */
    public void adattaA(int larghezza, int altezza) {
        if (larghezza <= 0 || altezza <= 0 || !vivo()) return;
        float scalaX = larghezza / (float) sfocato.getWidth();
        float scalaY = altezza / (float) sfocato.getHeight();
        mFondo.setScale(scalaX, scalaY);
        mPannello.setScale(scalaX, scalaY);
        mPannello.postTranslate(-SFALSAMENTO, -SFALSAMENTO);
    }

    /** Lo sfondo, a pagina intera. */
    public void disegnaSfondo(Canvas c, int larghezza, int altezza) {
        shader.setLocalMatrix(mFondo);
        c.drawRect(0, 0, larghezza, altezza, pFondo);
    }

    /**
     * Un pannello: il piano grafite dei riquadri del deck e dell'app sul PC, con
     * un filo di bordo. Il colore della sezione e' appena un'ombra: la tinta
     * piena resta agli accenti e ai tasti.
     *
     * Era vetro smerigliato su aloni colorati, e ogni sezione sembrava un'app
     * diversa dal deck che le sta accanto.
     */
    public void pannello(Canvas c, RectF area, float raggio, int colore, int opacitaVelo) {
        // Le pillole diventano rettangoli smussati come i tasti del deck.
        float r = Math.min(raggio, area.height() * 0.24f);
        if (opacitaVelo >= 0x1A) {
            // Quello che si preme - una scena, una luce, una durata - e' un
            // tasto, con la sua tinta piena quando e' acceso o premuto.
            // Sotto 0x5E la tinta e' un accenno - il tasto a riposo resta
            // grafite - e da li' in su e' piena, com'e' un tasto acceso.
            tasto(c, area, r, colore, (opacitaVelo - 0x1A) / (float) (0x5E - 0x1A));
            return;
        }
        // Il piano e' neutro come i riquadri del deck: il colore della sezione
        // sta negli accenti e nei tasti, non nel fondo.
        pVelo.setColor(Tinte.PIANO);
        c.drawRoundRect(area, r, r, pVelo);
        pBordo.setColor(Tinte.PIANO_BORDO);
        c.drawRoundRect(area, r, r, pBordo);
    }

    /** Un tasto con la tinta data per {@code quanto} sopra la grafite: 1 e' la tinta piena. */
    public void tasto(Canvas c, RectF area, float raggio, int colore, float quanto) {
        // L'azzurro d'azione resta pieno: e' il tasto da premere, e il testo sopra si legge.
        int pieno = colore | 0xFF000000;
        int tinta = pieno == Tinte.AZIONE ? 0xFF4A78E6 : pieno == Tinte.OROLOGIO ? 0xFFD9963F : scura(pieno);
        int fondo = Tinte.fondi(Tinte.TASTO, tinta, Math.max(0f, Math.min(1f, quanto)));
        // Oltre la tinta piena e' la pressione: il tasto si schiarisce, come nel deck.
        if (quanto > 1f) fondo = Tinte.fondi(fondo, 0xFFFFFFFF, Math.min(0.3f, (quanto - 1f) * 0.6f));
        // L'incavo prima del tasto, come sotto ogni cella del deck.
        incavo(c, area, raggio, 2.5f);
        // La sfumatura del deck: un decimo piu' chiara in cima, un quinto piu'
        // scura in fondo. Una per tinta, portata al tasto con una matrice.
        LinearGradient sfumatura = sfumature.get(fondo);
        if (sfumatura == null) {
            sfumatura = new LinearGradient(0, 0, 0, 1, Tinte.fondi(fondo, 0xFFFFFFFF, 0.12f),
                    Tinte.fondi(fondo, 0xFF000000, 0.22f), Shader.TileMode.CLAMP);
            if (sfumature.size() > 64) sfumature.clear();
            sfumature.put(fondo, sfumatura);
        }
        mLucido.setScale(1f, Math.max(1f, area.height()));
        mLucido.postTranslate(0, area.top);
        sfumatura.setLocalMatrix(mLucido);
        pTinta.setShader(sfumatura);
        c.drawRoundRect(area, raggio, raggio, pTinta);
        mLucido.setScale(1f, Math.max(1f, area.height()));
        mLucido.postTranslate(0, area.top);
        lucido.setLocalMatrix(mLucido);
        c.drawRoundRect(area, raggio, raggio, pLucido);
    }

    /**
     * Un comando: un tasto del deck. Grafite, oppure la tinta data con la sua
     * opacita' sopra la grafite; poi il filo di luce in cima e d'ombra in fondo.
     * Due comandi uguali restano uguali ovunque, come due tasti dello stesso deck.
     */
    public void controllo(Canvas c, RectF area, float raggio, int colore, int opacita) {
        tasto(c, area, Math.min(raggio, area.height() * 0.24f), colore,
                opacita > 0 ? Math.min(1f, opacita / 255f * 1.6f) : 0f);
    }

    /**
     * Una tinta portata alla luminosita' dei tasti del deck. Le lampade hanno
     * colori vivi - il giallo di una luce calda - e il testo bianco sopra non si
     * leggerebbe: sopra una certa luce la tinta si scurisce, e resta riconoscibile.
     */
    private static int scura(int colore) {
        float l = (0.299f * Color.red(colore) + 0.587f * Color.green(colore) + 0.114f * Color.blue(colore)) / 255f;
        float massima = 0.36f;
        return l <= massima ? colore : Tinte.fondi(colore, 0xFF000000, 1f - massima / l);
    }

    /** L'incavo scuro attorno a un tasto, come le celle del deck. */
    public void incavo(Canvas c, RectF tasto, float raggio, float bordo) {
        incavo.set(tasto.left - bordo, tasto.top - bordo, tasto.right + bordo, tasto.bottom + bordo);
        pVelo.setColor(Tinte.POZZO);
        c.drawRoundRect(incavo, raggio + bordo, raggio + bordo, pVelo);
    }

    /** Il posto vuoto della griglia: l'incavo con dentro la cella spenta, come nel deck. */
    public void vuoto(Canvas c, RectF tasto, float raggio) {
        incavo(c, tasto, raggio, 2.5f);
        pVelo.setColor(0xFF17181C);
        c.drawRoundRect(tasto, raggio, raggio, pVelo);
    }

    /** Un alone morbido dietro l'icona che suona. */
    public void bagliore(Canvas c, float cx, float cy, float raggio, int colore) {
        pBagliore.setShader(new RadialGradient(cx, cy, raggio,
                Tinte.con(colore, 0x66), Tinte.con(colore, 0x00), Shader.TileMode.CLAMP));
        c.drawCircle(cx, cy, raggio, pBagliore);
        pBagliore.setShader(null);
    }

    /**
     * La luce di una sezione: un alone del suo colore che scende dall'angolo in
     * alto a sinistra e si spegne prima di meta' pagina. E' cio' che distingue
     * la Casa verde dall'Orologio ambra senza tingere niente di quel che si legge.
     */
    public static Shader aloneSezione(int larghezza, int altezza, int colore) {
        return new RadialGradient(larghezza * 0.22f, -altezza * 0.15f, Math.max(1, altezza * 0.95f),
                new int[] { Tinte.con(colore, 0x3A), Tinte.con(colore, 0x14), Tinte.con(colore, 0x00) },
                new float[] { 0f, 0.5f, 1f }, Shader.TileMode.CLAMP);
    }

    /** Il rettangolo d'appoggio, per non allocarne uno a ogni onDraw. */
    public RectF area(float sinistra, float alto, float destra, float basso) {
        appoggio.set(sinistra, alto, destra, basso);
        return appoggio;
    }

    public boolean vivo() {
        return sfocato != null && !sfocato.isRecycled();
    }
}
