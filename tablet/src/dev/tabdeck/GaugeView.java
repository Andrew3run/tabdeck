package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.View;

/**
 * Come sta il tablet — CPU, memoria, batteria — in fondo alla barra laterale.
 *
 * Tutte e tre le misure sono del tablet e le legge il tablet: nessuna passa
 * dal cavo, quindi ci sono sempre, anche a PC spento. Prima CPU e RAM erano
 * quelle del PC e arrivavano nel frame STATS: si vedeva "RAM 83%" e sembrava
 * la memoria del tablet, che era un'altra cosa, e per giunta comparivano solo
 * mentre il PC trasmetteva lo schermo.
 *
 * Prima erano due righe di testo da 10sp: c'erano, ma a mezzo metro di distanza
 * non si leggevano. Due barrette si leggono con la coda dell'occhio, che e'
 * l'unico modo in cui si guarda davvero questa parte dello schermo — e la
 * percentuale resta scritta sotto per quando serve il numero preciso.
 *
 * La barra e' larga 56dp: qui dentro non ci sta niente di piu' elaborato, ed e'
 * un bene, perche' questo non deve rubare attenzione al deck.
 */
public final class GaugeView extends View {

    private static final int COLOR_LABEL = 0xFF8A8D95;
    private static final int COLOR_TRACK = 0xFF1E2024;
    private static final int COLOR_CALMO = 0xFF3DDC84;
    private static final int COLOR_MEDIO = 0xFFD8A657;
    private static final int COLOR_CARICO = 0xFFE06C75;

    private final Paint text = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint bar = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF rect = new RectF();
    private final float density;

    /** -1 = ancora nessuna misura. */
    private int cpu = -1;
    private int ram = -1;
    private int battery = -1;
    private boolean charging;

    public GaugeView(Context context) {
        super(context);
        density = getResources().getDisplayMetrics().density;
        text.setColor(COLOR_LABEL);
        text.setTextAlign(Paint.Align.LEFT);
        text.setTextSize(sp(9.5f));
    }

    /** Carico del tablet; qualsiasi valore fuori da 0-100 vale "sconosciuto". */
    public void set(int cpuPercent, int ramPercent) {
        if (cpu == cpuPercent && ram == ramPercent) return;
        cpu = valid(cpuPercent) ? cpuPercent : -1;
        ram = valid(ramPercent) ? ramPercent : -1;
        invalidate();
    }

    private static boolean valid(int v) {
        return v >= 0 && v <= 100;
    }

    /**
     * Carica della batteria, con l'indicazione se e' sotto carica.
     * Il segno + accanto alla percentuale evita di dover indovinare se il cavo
     * sta caricando o sta solo portando dati — su questo tablet capita che
     * porti i dati e basta, e lo si scopre quando si spegne.
     */
    public void setBattery(int percent, boolean charging) {
        if (battery == percent && this.charging == charging) return;
        battery = valid(percent) ? percent : -1;
        this.charging = charging;
        invalidate();
    }

    /** Il PC: il punto acceso e come, oppure spento. */
    public void setPc(boolean collegato, String come) {
        String nuovo = come == null ? "" : come;
        if (pc == collegato && nuovo.equals(viaPc)) return;
        pc = collegato;
        viaPc = nuovo;
        invalidate();
    }

    private boolean pc;
    private String viaPc = "";
    private final RectF corpo = new RectF();

    @Override
    protected void onMeasure(int widthSpec, int heightSpec) {
        setMeasuredDimension(resolveSize((int) dp(72), widthSpec), (int) dp(82));
    }

    /**
     * Lo stato in fondo alla barra, in due righe sole: il PC, con il suo punto,
     * e la batteria disegnata come una batteria. Le tre righe CPU, RAM e BAT di
     * prima erano numeri da officina in mezzo a una barra di icone; il carico
     * compare solo quando e' alto, perche' e' li' che spiega un tablet lento.
     */
    @Override
    protected void onDraw(Canvas canvas) {
        float cx = getWidth() / 2f;
        // Il riquadro che tiene insieme le due righe: sono lo stato, non comandi.
        rect.set(dp(7), 0, getWidth() - dp(7), getHeight() - dp(2));
        bar.setColor(0xFF1E2025);
        canvas.drawRoundRect(rect, dp(12), dp(12), bar);
        bar.setStyle(Paint.Style.STROKE);
        bar.setStrokeWidth(Math.max(1f, dp(1)));
        bar.setColor(0xFF2A2C32);
        canvas.drawRoundRect(rect, dp(12), dp(12), bar);
        bar.setStyle(Paint.Style.FILL);
        text.setTextAlign(Paint.Align.LEFT);
        text.setTextSize(sp(10f));

        // Il PC: punto e parola, centrati insieme.
        String parola = !pc ? "PC spento" : "USB".equalsIgnoreCase(viaPc) ? "cavo" : viaPc.length() > 0 ? "rete" : "PC";
        float r = dp(3f);
        float largo = r * 2 + dp(5) + text.measureText(parola);
        float x = cx - largo / 2f;
        float y = dp(15);
        if (pc) {
            bar.setColor(0x403DDC84);
            canvas.drawCircle(x + r, y, r * 1.9f, bar);
        }
        bar.setColor(pc ? COLOR_CALMO : 0xFF55575D);
        canvas.drawCircle(x + r, y, r, bar);
        text.setColor(pc ? 0xFFB4B7BE : COLOR_LABEL);
        canvas.drawText(parola, x + r * 2 + dp(5), y - (text.descent() + text.ascent()) / 2f, text);

        // La batteria: il corpo, il polo, il riempimento.
        float bw = dp(44), bh = dp(20);
        float top = dp(30);
        corpo.set(cx - bw / 2f - dp(2), top, cx + bw / 2f - dp(2), top + bh);
        bar.setStyle(Paint.Style.STROKE);
        bar.setStrokeWidth(dp(1.5f));
        bar.setColor(0xFF3A3C42);
        canvas.drawRoundRect(corpo, dp(4), dp(4), bar);
        bar.setStyle(Paint.Style.FILL);
        rect.set(corpo.right + dp(1.5f), corpo.centerY() - dp(3), corpo.right + dp(3.5f), corpo.centerY() + dp(3));
        canvas.drawRoundRect(rect, dp(1), dp(1), bar);
        if (battery > 0) {
            float in = dp(3);
            rect.set(corpo.left + in, corpo.top + in, corpo.left + in + (corpo.width() - in * 2) * battery / 100f,
                    corpo.bottom - in);
            bar.setColor(batteryColor(battery));
            canvas.drawRoundRect(rect, dp(2), dp(2), bar);
        }
        text.setTextAlign(Paint.Align.CENTER);
        text.setColor(COLOR_LABEL);
        String quanto = battery < 0 ? "--" : battery + "%" + (charging ? " +" : "");
        if (cpu >= 85) {
            text.setColor(COLOR_MEDIO);
            quanto = "CPU " + cpu + "%";
        }
        canvas.drawText(quanto, cx, corpo.bottom + dp(15), text);
    }

    /**
     * Il colore dice se vale la pena guardare: verde finche' tutto e' tranquillo,
     * giallo quando comincia a scaldarsi, rosso quando il tablet e' occupato —
     * che e' anche il momento in cui i tocchi rispondono in ritardo e lo
     * schermo remoto scatta, quindi spiega da solo perche' va a rilento.
     */
    private static int colorFor(int value) {
        if (value >= 85) return COLOR_CARICO;
        if (value >= 60) return COLOR_MEDIO;
        return COLOR_CALMO;
    }

    /** Sulla batteria la scala e' rovesciata: e' il poco che preoccupa. */
    private int batteryColor(int value) {
        if (charging) return COLOR_CALMO;
        if (value <= 15) return COLOR_CARICO;
        if (value <= 35) return COLOR_MEDIO;
        return COLOR_CALMO;
    }

    private float dp(float v) {
        return v * density;
    }

    private float sp(float v) {
        return v * getResources().getDisplayMetrics().scaledDensity;
    }
}
