package dev.tabdeck;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.Shader;

import java.util.Calendar;

/**
 * Lo sfondo dietro i pannelli di vetro dell'orologio.
 *
 * Arriva dall'altro tablet senza la meta' che la' conta di piu' - prendersi il
 * wallpaper di sistema, che su KitKat non si fa da un'app che non vuole il
 * permesso - ed e' rimasta solo la funzione che compone l'immagine. Non c'e'
 * nessun file: sono tre aloni larghi su un fondo scuro, e un JPEG nell'APK
 * darebbe lo stesso risultato pesando mezzo megabyte, contro i 29 KB di tutta
 * TabDeck.
 *
 * L'immagine grande vive pochi millisecondi: {@link Vetro} la riduce, la
 * sfoca e la ricicla subito.
 */
public final class Sfondo {

    private Sfondo() {}

    public static final int ALBA = 0, GIORNO = 1, SERA = 2, NOTTE = 3;

    /** I tre aloni di ogni ora del giorno. */
    private static final int[][] ALONI = {
        { 0xFF3B6FD4, 0xFFD98A45, 0xFF7A4E9E },   // alba
        { 0xFF2F7BE8, 0xFF23A37A, 0xFF3E5FA8 },   // giorno
        { 0xFF8E3F86, 0xFFD07A32, 0xFF2B3F7A },   // sera
        { 0xFF1E3E7A, 0xFF2A2F6B, 0xFF14284A },   // notte: niente che chiami l'occhio al buio
    };

    /** Che ora e' per lo sfondo. */
    public static int variante(long quando) {
        Calendar c = Calendar.getInstance();
        c.setTimeInMillis(quando);
        int ora = c.get(Calendar.HOUR_OF_DAY);
        if (ora >= 5 && ora < 9) return ALBA;
        if (ora >= 9 && ora < 17) return GIORNO;
        if (ora >= 17 && ora < 22) return SERA;
        return NOTTE;
    }

    /**
     * Compone l'immagine: lo stesso fondo del deck, grafite con una luce appena
     * accennata al centro. Gli aloni colorati di prima facevano di ogni sezione
     * un'altra app; adesso il fondo e' uno, e il colore resta agli accenti.
     * Da un thread di sfondo; la bitmap va riciclata appena usata.
     */
    public static Bitmap componi(int larghezza, int altezza, int variante) {
        Bitmap b = Bitmap.createBitmap(larghezza, altezza, Bitmap.Config.ARGB_8888);
        Canvas c = new Canvas(b);
        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        p.setShader(new RadialGradient(larghezza / 2f, altezza * 0.4f,
                Math.max(1, Math.max(larghezza, altezza) * 0.75f),
                Tinte.FONDO_ALTO, Tinte.FONDO_BASSO, Shader.TileMode.CLAMP));
        c.drawRect(0, 0, larghezza, altezza, p);
        p.setShader(null);
        return b;
    }

    private static void alone(Canvas c, Paint p, float cx, float cy, float raggio, int colore) {
        p.setShader(new RadialGradient(cx, cy, raggio,
                new int[] { Tinte.con(colore, 0xAA), Tinte.con(colore, 0x4C), Tinte.con(colore, 0x00) },
                new float[] { 0f, 0.38f, 1f },
                Shader.TileMode.CLAMP));
        c.drawCircle(cx, cy, raggio, p);
    }
}
