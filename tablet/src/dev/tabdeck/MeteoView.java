package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.view.MotionEvent;
import android.view.View;

/**
 * La pagina Meteo: quel che la Home mostra in piccolo, a tutto schermo. Stessa fascia
 * del deck, con la freccia per tornare alla Home.
 */
public final class MeteoView extends View {

    public interface Azione {
        void indietro();
    }

    private static final int FONDO_CENTRO = 0xFF1B1D22;
    private static final int FONDO_BORDO = 0xFF0E0F12;
    private static final int TESTO = 0xFFE6E7EA;
    private static final int TENUE = 0xFF9A9DA5;
    private static final int CHIP = 0xFF26282E;
    private static final int CHIP_PREMUTO = 0xFF34363D;

    private final Azione azione;
    private final float density;
    private final Paint fondo = new Paint();
    private final Paint riempi = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint testo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint segno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF freccia = new RectF();
    private boolean premuto;
    private Meteo.Dati dati;

    public MeteoView(Context context, Azione azione) {
        super(context);
        this.azione = azione;
        density = getResources().getDisplayMetrics().density;
        segno.setColor(0xF2FFFFFF);
    }

    public void dati(Meteo.Dati d) {
        dati = d;
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

        float margine = dp(18), testa = dp(44), mezzo = testa / 2f;
        float l = dp(30);
        freccia.set(margine, mezzo - l / 2f, margine + l, mezzo + l / 2f);
        riempi.setColor(premuto ? CHIP_PREMUTO : CHIP);
        c.drawRoundRect(freccia, dp(9), dp(9), riempi);
        Pittogrammi.disegna(c, "chevron-left", freccia.centerX(), mezzo, dp(18), segno);
        testo.setTextAlign(Paint.Align.LEFT);
        testo.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        testo.setColor(TESTO);
        testo.setTextSize(sp(15));
        c.drawText("Meteo", freccia.right + dp(10), mezzo + testo.getTextSize() * 0.35f, testo);

        testo.setTextAlign(Paint.Align.CENTER);
        testo.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
        float cx = w / 2f, cy = testa + (h - testa) / 2f;
        if (dati == null) {
            testo.setColor(TENUE);
            testo.setTextSize(sp(14));
            c.drawText("Meteo non disponibile", cx, cy, testo);
            return;
        }

        Pittogrammi.disegna(c, dati.icona(), cx, cy - dp(90), dp(96), segno);
        testo.setColor(TESTO);
        testo.setTextSize(sp(72));
        c.drawText(dati.temperatura + "°", cx, cy + dp(26), testo);
        testo.setTextSize(sp(22));
        String luogo = dati.citta.length() > 0 ? dati.citta + " · " + dati.detto() : dati.detto();
        c.drawText(luogo, cx, cy + dp(66), testo);
        testo.setColor(TENUE);
        testo.setTextSize(sp(16));
        c.drawText("max " + dati.massima + "° · min " + dati.minima + "°", cx, cy + dp(96), testo);
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        boolean su = freccia.contains(e.getX(), e.getY());
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                premuto = su;
                invalidate();
                return true;
            case MotionEvent.ACTION_UP:
                boolean vale = premuto && su;
                premuto = false;
                invalidate();
                if (vale) azione.indietro();
                return true;
            case MotionEvent.ACTION_CANCEL:
                premuto = false;
                invalidate();
                return true;
            default:
                return true;
        }
    }

    private float dp(float v) {
        return v * density;
    }

    private float sp(float v) {
        return v * getResources().getDisplayMetrics().scaledDensity;
    }
}
