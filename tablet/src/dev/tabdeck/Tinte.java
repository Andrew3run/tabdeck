package dev.tabdeck;

import android.graphics.Color;

/**
 * I colori della schermata dell'orologio e di quella che suona.
 *
 * Vengono dall'altro tablet, insieme alle due schermate: la ghiera, le schede e
 * i pannelli di vetro sono stati disegnati li' con questa tavolozza, e
 * riportarli qui con i grigi di {@link Pagina} li avrebbe fatti diventare
 * un'altra cosa. Il resto di TabDeck non la usa.
 *
 * La regola che tiene insieme l'aspetto: <b>la sezione ha un colore, e quel
 * colore tinge tutto quello che le sta attorno</b> - il velo del vetro,
 * l'indicatore, le pastiglie accese. Sta forte solo negli accenti piccoli, e
 * altrove e' una velatura che si nota appena.
 */
public final class Tinte {

    private Tinte() {}

    // ---- fondo ----

    /** Il nero-blu su cui si costruisce lo sfondo. Non nero puro: sul pannello
     *  si leggerebbe come una macchia spenta. */
    public static final int FONDO       = 0xFF131418;
    public static final int FONDO_ALTO  = 0xFF1B1D22;
    public static final int FONDO_BASSO = 0xFF0E0F12;

    // ---- testo ----

    public static final int TESTO       = 0xFFF0F1F3;
    public static final int TESTO_MEDIO = 0xFFB4B7BE;
    public static final int TESTO_TENUE = 0xFF9A9DA5;
    /** Cio' che c'e' ma non e' disponibile. */
    public static final int SPENTO      = 0xFF55575D;

    // ---- i piani e i tasti del deck ----

    /** Il piano di un pannello: la grafite dei riquadri dell'app sul PC. */
    public static final int PIANO       = 0xFF1B1D22;
    public static final int PIANO_BORDO = 0xFF2B2D33;
    /** Il tasto senza colore, come il pulsante predefinito del deck. */
    public static final int TASTO       = 0xFF33363E;
    /** L'incavo in cui sta un tasto. */
    public static final int POZZO       = 0xFF07080A;
    /** L'azzurro d'azione dell'app sul PC. */
    public static final int AZIONE      = 0xFF5B8CFF;

    /** L'ambra dell'orologio. */
    public static final int OROLOGIO = 0xFFFFB454;

    /** Il rosso non e' di nessuna sezione: e' degli allarmi, e del cestino. */
    public static final int ALLARME  = 0xFFE06C75;

    // ---- vetro ----

    /** Il filo di luce sul bordo alto del pannello, e l'ombra su quello basso:
     *  e' il dettaglio che separa « un rettangolo grigio » da « vetro ». */
    public static final int BORDO_ALTO  = 0x40FFFFFF;
    public static final int BORDO_BASSO = 0x24000000;

    // ---- aiuti ----

    /** Lo stesso colore con un'altra opacita'. */
    public static int con(int colore, int alpha) {
        return (colore & 0x00FFFFFF) | ((alpha & 0xFF) << 24);
    }

    /** Fonde due colori: serve alla scheda che scorre, per accendere il nome
     *  mentre la pastiglia gli arriva sotto. */
    public static int fondi(int da, int a, float quanto) {
        int alpha = (int) (Color.alpha(da) + (Color.alpha(a) - Color.alpha(da)) * quanto);
        int rosso = (int) (Color.red(da)   + (Color.red(a)   - Color.red(da))   * quanto);
        int verde = (int) (Color.green(da) + (Color.green(a) - Color.green(da)) * quanto);
        int blu   = (int) (Color.blue(da)  + (Color.blue(a)  - Color.blue(da))  * quanto);
        return Color.argb(alpha, rosso, verde, blu);
    }
}
