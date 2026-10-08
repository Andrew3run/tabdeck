package dev.tabdeck;

import android.os.SystemClock;

/**
 * Le animazioni dell'orologio, senza oggetti che si animano.
 *
 * Dall'altro tablet serve un pezzo solo: {@link Inseguito}, il valore che
 * insegue un bersaglio, con cui scorre la pastiglia accesa delle due schede.
 * Un {@code ValueAnimator} per ogni cosa che si muove vorrebbe dire un oggetto,
 * un listener e una callback per fotogramma ciascuno; qui il tempo e' un numero
 * solo, e la View smette da sola di chiedere fotogrammi quando ha finito.
 */
public final class Anima {

    private Anima() {}

    /** Adesso, in una base di tempo monotona: il cambio d'ora non la sposta. */
    public static long ora() {
        return SystemClock.uptimeMillis();
    }

    /**
     * Un valore che insegue un bersaglio.
     *
     * Serve alle cose senza un inizio e una fine, dove un'animazione con una
     * durata sarebbe sempre quella sbagliata perche' il bersaglio cambia mentre
     * ci si sta andando. Qui si va sempre verso l'ultimo bersaglio, e cambiarlo
     * a meta' strada non fa saltare niente. Non alloca e non tiene handler.
     */
    public static final class Inseguito {

        private float valore, bersaglio;
        private long ultimo;
        /** Costante di tempo, in millisecondi. */
        private final float tempo;

        public Inseguito(float partenza, float millisecondi) {
            valore = bersaglio = partenza;
            tempo = Math.max(1f, millisecondi);
        }

        public void vaiA(float b) {
            bersaglio = b;
        }

        /** Portalo avanti fino ad adesso. @return true se serve un altro fotogramma. */
        public boolean passo() {
            long adesso = ora();
            if (ultimo == 0L) ultimo = adesso;
            // Un salto lungo non teletrasporta: dopo una pausa si riparte piano.
            float dt = Math.min(64f, adesso - ultimo);
            ultimo = adesso;
            if (Math.abs(bersaglio - valore) < 0.002f) {
                valore = bersaglio;
                return false;
            }
            valore += (bersaglio - valore) * (1f - (float) Math.exp(-dt / tempo));
            return true;
        }

        public float valore() {
            return valore;
        }

        /** Mettilo li' senza animazione: fuori scena, animare vorrebbe dire far
         *  vedere un movimento gia' finito prima che qualcuno guardasse. */
        public void subito(float v) {
            valore = bersaglio = v;
            ultimo = 0L;
        }
    }
}
