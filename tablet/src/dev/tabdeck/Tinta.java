package dev.tabdeck;

/**
 * Colori scritti come "#RRGGBB", che e' il modo in cui viaggiano nei frame e
 * in cui si scrivono nei file di configurazione.
 *
 * Sta in una classe sua perche' serve al deck, alle luci e alle routine, e
 * ognuno se ne era fatta una copia leggermente diversa.
 */
public final class Tinta {
    private Tinta() {}

    /** Il colore scritto in quel testo, o quello di riserva se non si capisce. */
    public static int leggi(String testo, int riserva) {
        if (testo == null) return riserva;
        String pulito = testo.trim();
        if (pulito.length() == 0) return riserva;
        if (pulito.charAt(0) != '#') pulito = "#" + pulito;
        try {
            int c = android.graphics.Color.parseColor(pulito);
            // Senza opacita' piena una tinta scura diventa invisibile sul nero.
            return c | 0xFF000000;
        } catch (IllegalArgumentException e) {
            return riserva;
        }
    }

    public static String scrivi(int colore) {
        return String.format("#%06X", colore & 0xFFFFFF);
    }
}
