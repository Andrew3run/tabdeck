package dev.tabdeck;

import android.graphics.Paint;

/**
 * Il testo che sta nel suo spazio.
 *
 * Contare i caratteri non e' misurare: una M e una i non sono larghe uguale.
 * Qui si misura con lo <b>stesso Paint con cui si disegna</b>, che e' l'unica
 * cosa che sa quanto e' larga davvero una stringa - il nome di una routine,
 * il titolo della sveglia che suona.
 *
 * Misurare dentro onDraw sarebbe la cosa da non fare: {@code measureText}
 * costa e {@code substring} alloca. Per questo non ci sono funzioni da chiamare
 * a ogni fotogramma ma una {@link Riga} che si ricorda l'ultima risposta, e
 * finche' testo, spazio e corpo non cambiano torna la stringa di prima.
 */
public final class Testo {

    private Testo() {}

    /** Un carattere solo, non tre punti: tre punti sono tre larghezze tolte al testo. */
    private static final String PUNTINI = "…";

    /** Taglia con i puntini quel che non ci sta. Il Paint dev'essere gia' al corpo giusto. */
    public static String taglia(Paint p, String s, float spazio) {
        if (s == null || s.length() == 0 || spazio <= 0f) return "";
        if (p.measureText(s) <= spazio) return s;

        float segno = p.measureText(PUNTINI);
        if (spazio <= segno) return "";

        int quanti = p.breakText(s, true, spazio - segno, null);
        // Lo spazio prima dei puntini si legge come un buco nella riga.
        while (quanti > 0 && s.charAt(quanti - 1) == ' ') quanti--;
        return quanti <= 0 ? PUNTINI : s.substring(0, quanti) + PUNTINI;
    }

    /**
     * Una scritta su una riga sola, che si ricorda l'ultimo taglio. Lascia il
     * Paint al corpo che ha usato, cosi' si disegna subito dopo.
     */
    public static final class Riga {

        private String sorgente;
        private float spazio = -1f, massimo = -1f, minimo = -1f;
        private String reso = "";
        private float corpo;

        /**
         * Prima si rimpicciolisce fino a {@code minimo}, e solo se nemmeno li'
         * ci sta si taglia: leggere un nome un po' piu' piccolo e' meglio che
         * leggerlo a meta'.
         */
        public String adatta(Paint p, String s, float spazio, float massimo, float minimo) {
            if (s == null) s = "";
            if (spazio == this.spazio && massimo == this.massimo && minimo == this.minimo
                    && s.equals(sorgente)) {
                p.setTextSize(corpo);
                return reso;
            }
            this.sorgente = s;
            this.spazio = spazio;
            this.massimo = massimo;
            this.minimo = minimo;

            // A passi dell'otto per cento: sotto non si vede la differenza.
            float dim = massimo;
            p.setTextSize(dim);
            while (dim > minimo && p.measureText(s) > spazio) {
                dim = Math.max(minimo, dim * 0.92f);
                p.setTextSize(dim);
            }
            corpo = dim;
            reso = taglia(p, s, spazio);
            return reso;
        }
    }
}
