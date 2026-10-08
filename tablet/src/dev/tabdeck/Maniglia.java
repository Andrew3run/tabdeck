package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.View;

/**
 * La linguetta che riapre la barra quando e' nascosta.
 *
 * Era un rettangolo grigio con una freccia, largo quanto un pollice e attaccato al
 * bordo: sopra lo schermo remoto si vedeva come una toppa. Adesso e' una linguetta
 * sottile col bordo arrotondato che esce dal lato, e il dito la prende lo stesso:
 * la parte toccabile e' il riquadro di chi la contiene, piu' largo di quel che si
 * disegna.
 */
public final class Maniglia extends View {

    private final Paint fondo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint bordo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint segno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF forma = new RectF();
    private final float densita;

    public Maniglia(Context c) {
        super(c);
        densita = c.getResources().getDisplayMetrics().density;
        bordo.setStyle(Paint.Style.STROKE);
        bordo.setStrokeWidth(Math.max(1f, densita));
        bordo.setColor(0x1AFFFFFF);
        segno.setColor(0xFFC9CBD0);
    }

    /**
     * Un tasto del deck piccolo, nell'angolo in basso a sinistra, con il segno
     * della barra che si riapre: come nel modello. Era una linguetta a meta'
     * bordo con una freccetta.
     */
    @Override
    protected void onDraw(Canvas c) {
        float lato = 40f * densita;
        float x = 10f * densita, y = getHeight() - 10f * densita - lato;
        View genitore = (View) getParent();
        boolean giu = genitore != null && genitore.isPressed();
        float incavo = 2.5f * densita;
        float r = 12f * densita;
        forma.set(x - incavo, y - incavo, x + lato + incavo, y + lato + incavo);
        fondo.setShader(null);
        fondo.setColor(0xFF07080A);
        c.drawRoundRect(forma, r + incavo, r + incavo, fondo);
        forma.set(x, y, x + lato, y + lato);
        fondo.setShader(new android.graphics.LinearGradient(0, forma.top, 0, forma.bottom,
                giu ? 0xFF4A4D56 : 0xFF3A3D45, giu ? 0xFF33363D : 0xFF26282D,
                android.graphics.Shader.TileMode.CLAMP));
        c.drawRoundRect(forma, r, r, fondo);
        fondo.setShader(null);
        c.drawRoundRect(forma, r, r, bordo);
        Pittogrammi.disegna(c, "panel-left-open", forma.centerX(), forma.centerY(), 19f * densita, segno);
    }

    @Override
    public boolean hasOverlappingRendering() {
        return false;
    }
}
