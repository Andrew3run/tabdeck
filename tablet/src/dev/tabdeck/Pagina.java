package dev.tabdeck;

import android.content.Context;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * I mattoni comuni alle pagine disegnate a mano: riquadri, pulsanti, titoletti.
 *
 * La sezione Casa se li e' tenuti in casa propria, e finche' le pagine erano
 * due andava bene. Con il timer, la sveglia e la schermata che suona diventano
 * quattro, e la stessa dozzina di righe copiata quattro volte e' il modo
 * sicuro di ritrovarsi con quattro sfumature diverse dello stesso grigio.
 *
 * Qui non c'e' nessuna logica: solo la forma. Ogni pagina ci mette dentro la
 * sua, e {@link #colonna} e' il posto dove appenderla.
 */
public abstract class Pagina extends ScrollView {

    protected static final int COLOR_SFONDO = 0xFF0A0B0D;
    protected static final int COLOR_TESTO = 0xFFD9DBE0;
    protected static final int COLOR_TENUE = 0xFF8A8D95;
    protected static final int COLOR_BORDO = 0xFF1C1D21;
    protected static final int COLOR_NEUTRO = 0xFF1E2024;
    protected static final int COLOR_ACCENTO = 0xFF3DDC84;
    protected static final int COLOR_AMBRA = 0xFFB4926B;
    protected static final int COLOR_BIANCO = 0xFFFFFFFF;

    /** La tinta di quel che e' armato: il timer che corre, il giorno segnato. */
    protected static final int COLOR_ACCESO = 0xFF33565E;

    protected final LinearLayout colonna;
    private final float density;

    public Pagina(Context context) {
        super(context);
        density = context.getResources().getDisplayMetrics().density;
        setBackgroundColor(COLOR_SFONDO);

        colonna = new LinearLayout(context);
        colonna.setOrientation(LinearLayout.VERTICAL);
        colonna.setPadding(dp(12), dp(10), dp(12), dp(14));
        addView(colonna, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
    }

    // ---- righe e colonne ----

    /** Una riga nuova, gia' appesa alla colonna. */
    protected LinearLayout nuovaRiga() {
        return nuovaRiga(colonna);
    }

    protected LinearLayout nuovaRiga(LinearLayout dove) {
        LinearLayout riga = new LinearLayout(getContext());
        riga.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        p.bottomMargin = dp(6);
        dove.addView(riga, p);
        return riga;
    }

    protected LinearLayout incolonna() {
        LinearLayout c = new LinearLayout(getContext());
        c.setOrientation(LinearLayout.VERTICAL);
        return c;
    }

    protected LinearLayout.LayoutParams peso(int altezza, float quanto) {
        return new LinearLayout.LayoutParams(0, altezza, quanto);
    }

    // ---- pezzi ----

    /** Il riquadro pieno del deck: tinta, angoli tondi, contenuto centrato. */
    protected LinearLayout riquadro(int colore) {
        LinearLayout cella = new LinearLayout(getContext());
        cella.setOrientation(LinearLayout.VERTICAL);
        cella.setGravity(Gravity.CENTER);
        sfondo(cella, colore);
        return cella;
    }

    /** Un riquadro con dentro una scritta sola, e un tocco. */
    protected LinearLayout tasto(String testo, int colore, int colorTesto, OnClickListener azione) {
        LinearLayout cella = riquadro(colore);
        cella.addView(etichettaGrande(testo, colorTesto));
        if (azione != null) cella.setOnClickListener(azione);
        return cella;
    }

    protected TextView numerone(String testo, int colore, int punti) {
        TextView t = new TextView(getContext());
        t.setText(testo);
        t.setTextColor(colore);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, punti);
        t.setGravity(Gravity.CENTER);
        return t;
    }

    protected TextView etichettaGrande(String testo, int colore) {
        TextView t = new TextView(getContext());
        t.setText(testo);
        t.setTextColor(colore);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17);
        t.setGravity(Gravity.CENTER);
        return t;
    }

    protected TextView etichetta(String testo, int colore) {
        TextView t = new TextView(getContext());
        t.setText(testo);
        t.setTextColor(colore);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        t.setGravity(Gravity.CENTER);
        return t;
    }

    /** La scritta piccola in maiuscolo che apre un gruppo. */
    protected TextView titoletto(String testo) {
        TextView t = new TextView(getContext());
        t.setText(testo);
        t.setTextColor(COLOR_TENUE);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        p.topMargin = dp(10);
        p.bottomMargin = dp(5);
        t.setLayoutParams(p);
        return t;
    }

    protected TextView pulsante(String testo, int colore, OnClickListener azione) {
        TextView t = new TextView(getContext());
        t.setText(testo);
        t.setTextColor(COLOR_TESTO);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13);
        t.setGravity(Gravity.CENTER);
        // Alto quanto un polpastrello: sotto i 40 dp su questo pannello si
        // sbaglia bersaglio, e qui si preme al buio.
        t.setMinimumHeight(dp(42));
        t.setPadding(dp(10), dp(10), dp(10), dp(10));
        sfondo(t, colore);
        t.setOnClickListener(azione);
        return t;
    }

    protected void sfondo(View v, int colore) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(colore);
        g.setCornerRadius(dp(10));
        g.setStroke(Math.max(1, dp(1)), colore == COLOR_NEUTRO ? COLOR_BORDO : Color.TRANSPARENT);
        v.setBackground(g);
    }

    protected View spazio(int larghezza) {
        View v = new View(getContext());
        v.setLayoutParams(new LinearLayout.LayoutParams(larghezza, 1));
        return v;
    }

    protected int dp(float v) {
        return Math.round(v * density);
    }
}
