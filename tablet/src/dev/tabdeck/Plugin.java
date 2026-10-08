package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.view.View;

/**
 * La parte del tablet di un'estensione: il nucleo la conosce solo da qui.
 *
 * Non sta nell'APK di TabDeck. La manda il PC e la carica {@link Estensioni};
 * senza nessuna estensione deck, schermo, luci e orologio sono esattamente gli
 * stessi. Ogni estensione ha la sua voce nella barra laterale, fra le sezioni e
 * il gruppo di servizio.
 */
public interface Plugin {

    /** La sezione dell'estensione. Una per activity, costruita la prima volta che serve. */
    View vista(Context context, Link link);

    /** L'icona della sua voce nella barra laterale, o null per quella di serie. */
    Bitmap icona(Context context);

    /** Thread UI: un frame da 0x51 a 0x5F arrivato dal PC. */
    void suFrame(int tipo, String json);

    /** La sezione e' davanti, o non lo e' piu': chi anima si ferma quando non si vede. */
    void mostrato(boolean davanti);

    /**
     * Il tasto indietro del tablet, premuto mentre la sezione e' davanti.
     * @return true se il plugin ha fatto un passo indietro, false se era gia' all'inizio.
     */
    boolean indietro();
}
