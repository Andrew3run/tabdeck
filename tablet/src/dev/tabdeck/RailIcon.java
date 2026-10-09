package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

/**
 * Le icone della barra laterale.
 *
 * Sono le stesse icone dell'app sul PC — Lucide, a tratto, in un quadrato di 24 —
 * lette da {@link Lucide}. Prima erano una dozzina di primitive disegnate a mano
 * per voce: si leggevano, ma ognuna aveva il suo peso e le sue proporzioni, e
 * accanto all'app del PC sembravano di un'altra famiglia. La voce di
 * un'estensione porta invece l'immagine che l'estensione da', a colori: e' un
 * altro programma, non un attrezzo del tablet.
 */
public final class RailIcon extends View {

    public static final int DECK = 0;
    public static final int SCREEN = 1;
    public static final int COLLAPSE = 2;
    public static final int EXPAND = 3;
    public static final int RESTART = 4;
    public static final int WIFI = 5;
    public static final int SETTINGS = 6;
    public static final int CASA = 7;
    public static final int CONNETTI = 8;
    public static final int TIMER = 9;
    /** Un'estensione che non ha dato un'immagine sua. */
    public static final int ESTENSIONE = 10;
    public static final int DASHBOARD = 11;

    /** Il colore della voce accesa: e' quello che MainActivity passa a setColor. */
    private static final int COLORE_ACCESO = 0xFFF0F1F3;

    private static final String[] TRACCIATI = new String[12];
    static {
        // layout-dashboard
        TRACCIATI[DASHBOARD] = "M4,3 h5 a1,1 0 0 1 1,1 v7 a1,1 0 0 1 -1,1 h-5 a1,1 0 0 1 -1,-1 v-7 a1,1 0 0 1 1,-1 Z"
                + " M15,3 h5 a1,1 0 0 1 1,1 v3 a1,1 0 0 1 -1,1 h-5 a1,1 0 0 1 -1,-1 v-3 a1,1 0 0 1 1,-1 Z"
                + " M15,12 h5 a1,1 0 0 1 1,1 v7 a1,1 0 0 1 -1,1 h-5 a1,1 0 0 1 -1,-1 v-7 a1,1 0 0 1 1,-1 Z"
                + " M4,16 h5 a1,1 0 0 1 1,1 v3 a1,1 0 0 1 -1,1 h-5 a1,1 0 0 1 -1,-1 v-3 a1,1 0 0 1 1,-1 Z";
        // keyboard: una tastiera e non una griglia, che accanto alla Dashboard si confondeva
        // layout-grid: la griglia del deck, la stessa icona dell'app sul PC
        TRACCIATI[DECK] = "M4 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z"
                + " M15 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z"
                + " M15 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z"
                + " M4 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z";
        // monitor
        TRACCIATI[SCREEN] = "M4,3 h16 a2,2 0 0 1 2,2 v10 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-10 a2,2 0 0 1 2,-2 Z"
                + " M8,21 L16,21 M12,17 L12,21";
        // panel-left-close, panel-left-open: un pannello che si chiude o si apre, non una
        // freccia qualunque che in mezzo alle voci non si capiva cosa facesse
        TRACCIATI[COLLAPSE] = "M5,3 h14 a2,2 0 0 1 2,2 v14 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-14 a2,2 0 0 1 2,-2 Z"
                + " M9 3v18 M16 15 l-3-3 3-3";
        TRACCIATI[EXPAND] = "M5,3 h14 a2,2 0 0 1 2,2 v14 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-14 a2,2 0 0 1 2,-2 Z"
                + " M9 3v18 M14 9 l3 3-3 3";
        // rotate-cw
        TRACCIATI[RESTART] = "M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8 M21 3v5h-5";
        // wifi
        TRACCIATI[WIFI] = "M12 20h.01 M2 8.82a15 15 0 0 1 20 0 M5 12.859a10 10 0 0 1 14 0 M8.5 16.429a5 5 0 0 1 7 0";
        // sliders-horizontal
        // settings: l'ingranaggio, come nel modello e nell'app sul PC
        TRACCIATI[SETTINGS] = "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0Z";
        // home
        TRACCIATI[CASA] = "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8"
                + " M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
        // monitor con una freccia che entra: e' il tablet che chiama il PC
        TRACCIATI[CONNETTI] = "M4,3 h16 a2,2 0 0 1 2,2 v10 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-10 a2,2 0 0 1 2,-2 Z"
                + " M8,21 L16,21 M12,17 L12,21 M7 10h8 M12 7l3 3-3 3";
        // clock
        // timer: il cronometro
        TRACCIATI[TIMER] = "M10 2L14 2 M12 14L15 11 M4 14a8 8 0 1 0 16 0a8 8 0 1 0 -16 0Z";
        // puzzle
        TRACCIATI[ESTENSIONE] = "M15.39 4.39a1 1 0 0 0 1.68-.474 2.5 2.5 0 1 1 3.014 3.015 1 1 0 0 0-.474 1.68l1.683 1.682"
                + "a2.414 2.414 0 0 1 0 3.414L19.61 15.39a1 1 0 0 1-1.68-.474 2.5 2.5 0 1 0-3.014 3.015 1 1 0 0 1 .474 1.68"
                + "l-1.683 1.682a2.414 2.414 0 0 1-3.414 0L8.61 19.61a1 1 0 0 0-1.68.474 2.5 2.5 0 1 1-3.014-3.015"
                + " 1 1 0 0 0 .474-1.68l-1.683-1.682a2.414 2.414 0 0 1 0-3.414L4.39 8.61a1 1 0 0 1 1.68.474"
                + " 2.5 2.5 0 1 0 3.014-3.015 1 1 0 0 1-.474-1.68l1.683-1.682a2.414 2.414 0 0 1 3.414 0z";
    }

    private static final Path[] LETTI = new Path[TRACCIATI.length];

    private final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint immagine = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final Path disegno = new Path();
    private final Matrix scala = new Matrix();
    private final RectF box = new RectF();

    private final float density;
    private final int kind;
    private final Bitmap marchio;
    private int color = 0xFF6B6E76;

    public RailIcon(Context context, int kind) {
        this(context, kind, null);
    }

    /** La voce di un'estensione: la sua immagine, o il pezzo di puzzle se non ne ha. */
    public RailIcon(Context context, Bitmap marchio) {
        this(context, ESTENSIONE, marchio);
    }

    private RailIcon(Context context, int kind, Bitmap marchio) {
        super(context);
        this.kind = kind;
        this.marchio = marchio;
        density = context.getResources().getDisplayMetrics().density;
        stroke.setStyle(Paint.Style.STROKE);
        stroke.setStrokeCap(Paint.Cap.ROUND);
        stroke.setStrokeJoin(Paint.Join.ROUND);
    }

    public void setColor(int color) {
        if (this.color == color) return;
        this.color = color;
        invalidate();
    }

    @Override
    protected void onSizeChanged(int w, int h, int vecchiaW, int vecchiaH) {
        // Stesso quadrato per tutte: stesso peso ottico anche con forme diverse.
        float lato = Math.min(w, h) * 0.52f;
        float cx = w / 2f, cy = h / 2f;
        box.set(cx - lato * 0.62f, cy - lato * 0.62f, cx + lato * 0.62f, cy + lato * 0.62f);

        Path base = letto(kind);
        disegno.reset();
        if (base != null) {
            scala.setScale(lato / 24f, lato / 24f);
            scala.postTranslate(cx - lato / 2f, cy - lato / 2f);
            base.transform(scala, disegno);
        }
        // Il tratto di Lucide e' 2 su 24: a questa misura sarebbe sotto i due pixel, e
        // sul pannello del Tab 3 un pixel e mezzo di grigio si perde.
        stroke.setStrokeWidth(Math.max(1.8f * density, 2f * lato / 24f));
    }

    @Override
    protected void onDraw(Canvas canvas) {
        if (marchio != null) {
            // Spento si vede lo stesso, un po' piu' quieto: e' il marchio di un'app, non un grigio.
            immagine.setAlpha(color == COLORE_ACCESO ? 255 : 190);
            canvas.drawBitmap(marchio, null, box, immagine);
            return;
        }
        stroke.setColor(color);
        canvas.drawPath(disegno, stroke);
    }

    private static Path letto(int kind) {
        if (kind < 0 || kind >= TRACCIATI.length || TRACCIATI[kind] == null) return null;
        if (LETTI[kind] == null) LETTI[kind] = Lucide.percorso(TRACCIATI[kind]);
        return LETTI[kind];
    }
}
