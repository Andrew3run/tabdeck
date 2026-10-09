package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.view.MotionEvent;
import android.view.View;

import java.util.ArrayList;
import java.util.List;

/**
 * La pagina App: disegnata come il deck, con la sua stessa fascia in cima e un tasto
 * quadrato nel suo incavo per ogni estensione accesa. Un tocco apre l'estensione.
 *
 * Nella barra c'e' una voce sola, questa; prima ogni estensione ne aveva una sua e
 * con due o tre la barra non bastava piu'.
 */
public final class AppView extends View {

    /** Cosa fare quando si tocca un tasto. */
    public interface Azione {
        void apri(String id);
    }

    private static final int FONDO_CENTRO = 0xFF1B1D22;
    private static final int FONDO_BORDO = 0xFF0E0F12;
    private static final int POZZO = 0xFF07080A;
    private static final int TESTO = 0xFFE6E7EA;
    private static final int TENUE = 0xFF9A9DA5;
    /** Il grigio dei pulsanti senza colore nel deck. */
    private static final int TASTO = 0xFF33363E;
    private static final int COLONNE = 5;

    private final Azione azione;
    private final float density;
    private final Paint fondo = new Paint();
    private final Paint pozzo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint riempi = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint lustro = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint velo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint titolo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint suggerimento = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint nome = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint segno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint immagine = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final LinearGradient sfumatura;
    private final Matrix locale = new Matrix();
    private final LinearGradient lucido;
    private final RectF cella = new RectF();
    private final RectF incavo = new RectF();
    private final RectF bitmapRect = new RectF();

    private final List<DashboardView.Voce> voci = new ArrayList<DashboardView.Voce>();
    private final List<RectF> aree = new ArrayList<RectF>();
    private int premuto = -1;

    public AppView(Context context, Azione azione) {
        super(context);
        this.azione = azione;
        density = getResources().getDisplayMetrics().density;

        titolo.setColor(TESTO);
        titolo.setTextSize(sp(15));
        titolo.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        suggerimento.setColor(TENUE);
        suggerimento.setTextSize(sp(14));
        suggerimento.setTextAlign(Paint.Align.CENTER);
        suggerimento.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
        nome.setColor(0xE0FFFFFF);
        nome.setTextAlign(Paint.Align.CENTER);
        nome.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        segno.setColor(0xF2FFFFFF);
        pozzo.setColor(POZZO);
        velo.setColor(0x33FFFFFF);

        // Lo stesso filo di luce in cima e d'ombra in fondo dei tasti del deck.
        lucido = new LinearGradient(0, 0, 0, 1,
                new int[] { 0x30FFFFFF, 0x00FFFFFF, 0x00000000, 0x4D000000 },
                new float[] { 0f, 0.035f, 0.965f, 1f }, Shader.TileMode.CLAMP);
        lustro.setShader(lucido);
        sfumatura = new LinearGradient(0, 0, 0, 1,
                Tinte.fondi(TASTO, 0xFFFFFFFF, 0.10f), Tinte.fondi(TASTO, 0xFF000000, 0.22f),
                Shader.TileMode.CLAMP);
    }

    /** Rifa' i tasti: sono pochi, e cosi' non restano indietro rispetto alle estensioni vere. */
    public void elenco(List<DashboardView.Voce> nuove) {
        voci.clear();
        voci.addAll(nuove);
        premuto = -1;
        invalidate();
    }

    @Override
    protected void onSizeChanged(int w, int h, int ow, int oh) {
        super.onSizeChanged(w, h, ow, oh);
        fondo.setShader(new RadialGradient(w / 2f, h * 0.4f, Math.max(1, Math.max(w, h) * 0.75f),
                FONDO_CENTRO, FONDO_BORDO, Shader.TileMode.CLAMP));
    }

    @Override
    protected void onDraw(Canvas c) {
        int w = getWidth(), h = getHeight();
        c.drawRect(0, 0, w, h, fondo);

        float margine = dp(18), testa = dp(44);
        c.drawText("App", margine, testa / 2f + titolo.getTextSize() * 0.35f, titolo);

        aree.clear();
        if (voci.isEmpty()) {
            c.drawText("Nessun add-on installato", w / 2f, (h + testa) / 2f, suggerimento);
            return;
        }

        // Tasti quadrati come nel deck: distanza del sedici per cento del lato, lato al
        // piu' 150, la griglia al centro dello spazio sotto la fascia.
        float k = 0.16f;
        int righe = (voci.size() + COLONNE - 1) / COLONNE;
        int colonne = Math.min(COLONNE, voci.size());
        float perL = (w - margine * 2f) / (colonne + (colonne - 1) * k);
        float perH = (h - testa - margine) / (righe + (righe - 1) * k);
        float lato = Math.max(0f, Math.min(dp(150), Math.min(perL, perH)));
        float passo = lato * (1f + k);
        float raggio = lato * 0.17f;
        float larga = colonne * lato + (colonne - 1) * lato * k;
        float alta = righe * lato + (righe - 1) * lato * k;
        float sinistra = (w - larga) / 2f;
        float sopra = testa + (h - testa - alta) / 2f;

        nome.setTextSize(Math.min(sp(13), lato * 0.115f));
        for (int i = 0; i < voci.size(); i++) {
            float x = sinistra + (i % COLONNE) * passo;
            float y = sopra + (i / COLONNE) * passo;
            cella.set(x, y, x + lato, y + lato);
            aree.add(new RectF(cella));

            float bordo = dp(3);
            incavo.set(cella.left - bordo, cella.top - bordo, cella.right + bordo, cella.bottom + bordo);
            c.drawRoundRect(incavo, raggio + bordo, raggio + bordo, pozzo);

            boolean giu = i == premuto;
            int salvato = -1;
            if (giu) {
                salvato = c.save();
                c.scale(0.93f, 0.93f, cella.centerX(), cella.centerY());
            }
            locale.setScale(1f, lato);
            locale.postTranslate(0, cella.top);
            sfumatura.setLocalMatrix(locale);
            riempi.setShader(sfumatura);
            c.drawRoundRect(cella, raggio, raggio, riempi);
            riempi.setShader(null);
            lucido.setLocalMatrix(locale);
            c.drawRoundRect(cella, raggio, raggio, lustro);

            DashboardView.Voce v = voci.get(i);
            float cy = cella.centerY() - lato * 0.1f;
            float latoIcona = lato * 0.44f;
            if (v.icona != null) {
                Bitmap b = v.icona;
                float s = Math.min(latoIcona / b.getWidth(), latoIcona / b.getHeight());
                float iw = b.getWidth() * s, ih = b.getHeight() * s;
                bitmapRect.set(cella.centerX() - iw / 2f, cy - ih / 2f, cella.centerX() + iw / 2f, cy + ih / 2f);
                c.drawBitmap(b, null, bitmapRect, immagine);
            } else {
                Tratti.disegna(c, Tratti.ESTENSIONE, cella.centerX(), cy, lato * 0.36f, segno);
            }
            c.drawText(Testo.taglia(nome, v.nome, lato - dp(12)), cella.centerX(), cella.bottom - lato * 0.115f, nome);

            if (giu) {
                c.drawRoundRect(cella, raggio, raggio, velo);
                c.restoreToCount(salvato);
            }
        }
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                premuto = trova(e.getX(), e.getY());
                invalidate();
                return true;
            case MotionEvent.ACTION_UP: {
                int i = premuto;
                premuto = -1;
                invalidate();
                // Vale solo se il dito si alza sul tasto dove era sceso.
                if (i >= 0 && i < voci.size() && trova(e.getX(), e.getY()) == i) azione.apri(voci.get(i).id);
                return true;
            }
            case MotionEvent.ACTION_CANCEL:
                premuto = -1;
                invalidate();
                return true;
            default:
                return true;
        }
    }

    private int trova(float x, float y) {
        for (int i = 0; i < aree.size(); i++) if (aree.get(i).contains(x, y)) return i;
        return -1;
    }

    private float dp(float v) {
        return v * density;
    }

    private float sp(float v) {
        return v * getResources().getDisplayMetrics().scaledDensity;
    }
}
