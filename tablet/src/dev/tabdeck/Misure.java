package dev.tabdeck;

import android.content.Context;
import android.graphics.Point;
import android.util.DisplayMetrics;
import android.view.WindowManager;

/**
 * La scala dell'orologio e della schermata che suona, calcolata una volta sola.
 *
 * Arriva dall'altro tablet con le due schermate, ed e' il pezzo che le fa stare
 * su questo pannello senza ridisegnarle: li' tutto era una frazione
 * dell'altezza di un 1280x800, qui le stesse frazioni si prendono sui 600 pixel
 * del Tab 3. Nessun numero e' stato riscelto a occhio - e' la stessa regola,
 * applicata a un altro schermo.
 *
 * <h3>Una scala sola</h3>
 *
 * <b>Sei corpi e cinque spazi</b>, e tutto il resto ne e' un multiplo. I corpi
 * seguono il lato corto, perche' e' l'altezza la risorsa scarsa, ed e' quella
 * che decide quante righe ci stanno; hanno un tetto in dp perche' su uno
 * schermo piu' grande un titolo non deve diventare un cartellone.
 *
 * I dp restano solo per le cose che devono essere toccabili con un dito:
 * {@link #bersaglio}, dove il dito e' un dito indipendentemente da quanti pixel
 * ci stiano sotto.
 */
public final class Misure {

    /** Larghezza e altezza del pannello in pixel, lato lungo per primo. */
    public final int larghezza, altezza;

    /** Pixel per dp. Sul Tab 3 7.0 vale 1. */
    public final float densita;

    /** Il margine fra i pannelli e il bordo. */
    public final int margine;

    /** Due raggi e non tre: uno per i pannelli, uno per le pastiglie. */
    public final float raggio, raggioPiccolo;

    /** Il bersaglio minimo di un comando. Sotto non si scende: si preme al buio. */
    public final int bersaglio;

    // ---- i sei corpi ----

    /** Etichette in maiuscolo, il « min » accanto al numero. */
    public final float micro;
    /** La seconda riga di una voce. */
    public final float nota;
    /** Il testo normale, e le scritte dentro i pulsanti. */
    public final float corpo;
    /** I nomi: il gradino piu' piccolo che si legge da un metro. */
    public final float voce;
    /** I numeri che contano: l'orario di una sveglia, il conto alla rovescia. */
    public final float titolo;

    // ---- i cinque spazi ----

    public final float s1, s2, s3, s4, s5;

    /** La grandezza normale di un'icona dentro il testo. */
    public final float icona;

    public Misure(Context c) {
        DisplayMetrics dm = c.getResources().getDisplayMetrics();
        densita = dm.density;

        // getRealSize e non getDisplayMetrics: l'app gira a schermo intero
        // senza barre, e le misure vanno prese sul pannello vero.
        Point vero = new Point();
        WindowManager wm = (WindowManager) c.getSystemService(Context.WINDOW_SERVICE);
        if (wm != null) wm.getDefaultDisplay().getRealSize(vero);
        int x = vero.x > 0 ? vero.x : dm.widthPixels;
        int y = vero.y > 0 ? vero.y : dm.heightPixels;

        // Il lato lungo e' la larghezza, sempre: all'avvio del tablet la
        // rotazione puo' non essere ancora applicata, e le misure si prendono
        // una volta sola.
        larghezza = Math.max(x, y);
        altezza   = Math.min(x, y);

        margine       = Math.round(altezza * 0.026f);
        raggio        = altezza * 0.021f;
        raggioPiccolo = altezza * 0.012f;
        bersaglio     = dp(42);

        micro      = corpo(0.0225f, 20f);
        nota       = corpo(0.0280f, 25f);
        this.corpo = corpo(0.0335f, 30f);
        voce       = corpo(0.0430f, 38f);
        titolo     = corpo(0.0560f, 50f);

        s1 = altezza * 0.0075f;
        s2 = altezza * 0.0150f;
        s3 = altezza * 0.0225f;
        s4 = altezza * 0.0325f;
        s5 = altezza * 0.0500f;

        icona = altezza * 0.036f;
    }

    /** Un corpo come frazione dell'altezza, ma non oltre un tetto in dp. */
    private float corpo(float frazione, float tettoDp) {
        return Math.min(altezza * frazione, tettoDp * densita);
    }

    public int dp(float quanti) {
        return Math.round(quanti * densita);
    }
}
