package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.view.View;

/**
 * Il marchio in cima alla barra: lo stesso quadratino dell'app sul PC, azzurro
 * che sfuma nel viola con la griglia del deck dentro. E' l'unica cosa colorata
 * della barra, e dice di che app si tratta senza scriverlo.
 */
public final class Marchio extends View {

    private final Paint fondo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint segno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF quadro = new RectF();
    private final float densita;

    public Marchio(Context c) {
        super(c);
        densita = c.getResources().getDisplayMetrics().density;
        segno.setColor(0xFFFFFFFF);
    }

    @Override
    protected void onSizeChanged(int w, int h, int vw, int vh) {
        float lato = 34f * densita;
        quadro.set((w - lato) / 2f, 0, (w + lato) / 2f, lato);
        fondo.setShader(new LinearGradient(quadro.left, quadro.top, quadro.right, quadro.bottom,
                0xFF5B8CFF, 0xFF8B5BFF, Shader.TileMode.CLAMP));
    }

    @Override
    protected void onDraw(Canvas c) {
        float r = 10f * densita;
        c.drawRoundRect(quadro, r, r, fondo);
        Pittogrammi.disegna(c, "layout-grid", quadro.centerX(), quadro.centerY(), 18f * densita, segno);
    }

    @Override
    public boolean hasOverlappingRendering() {
        return false;
    }
}
