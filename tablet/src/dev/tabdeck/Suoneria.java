package dev.tabdeck;

import android.content.Context;
import android.media.AudioManager;
import android.media.MediaPlayer;
import android.media.RingtoneManager;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.os.Vibrator;
import android.util.Log;

/**
 * Il suono di quando scade qualcosa.
 *
 * Stava dentro {@link Ora}, e finche' la sveglia era una e partiva a volume
 * pieno erano venti righe. Con la rampa, la vibrazione e lo spegnimento da solo
 * diventa un mestiere a parte, e {@link Ora} torna a occuparsi di <i>quando</i>
 * invece che di <i>come</i>.
 *
 * <h3>Sale piano</h3>
 *
 * Una sveglia che parte al massimo in una stanza silenziosa non sveglia: fa
 * saltare. Si parte a un decimo e si sale fino al pieno in mezzo minuto, un
 * gradino al secondo; chi si sveglia al primo gradino non sente mai gli altri.
 *
 * La rampa e' sul {@link MediaPlayer}, non sul volume di sistema: alzare
 * {@code STREAM_ALARM} vorrebbe dire lasciarlo alzato, e la mattina dopo si
 * partirebbe al massimo, che e' proprio quello che si sta evitando.
 *
 * <h3>Il timer suona diverso</h3>
 *
 * Un timer che scade e' un avviso, non una levataccia: parte a poco piu' di
 * meta', non sale e non vibra. Chi l'ha messo e' sveglio e nella stanza
 * accanto.
 *
 * <h3>La vibrazione</h3>
 *
 * Il Tab 3 7.0 in versione Wi-Fi non ha il motorino: si chiede prima con
 * {@code hasVibrator()}, e senza non si prova nemmeno.
 */
public final class Suoneria {

    private static final String TAG = "TabDeck.Suoneria";

    /** Da qui parte la rampa della sveglia. */
    private static final float VOLUME_INIZIALE = 0.10f;
    /** E qui sta fermo il timer. */
    private static final float VOLUME_TIMER = 0.55f;

    /** Un gradino al secondo per mezzo minuto. */
    private static final int PASSI = 30;
    private static final int PASSO_MS = 1000;

    /**
     * Dopo tanto smette da sola. Una sveglia che suona all'infinito in una casa
     * vuota e' rumore e basta, e a batteria costa piu' di quanto sia servita.
     */
    private static final long SMETTE_DA_SOLA_MS = 3 * 60 * 1000L;

    /** Mezzo secondo si', mezzo no. */
    private static final long[] BATTITO = { 0, 500, 500 };

    private final Context context;
    private final Handler ui = new Handler(Looper.getMainLooper());
    /** Chi va avvisato quando la suoneria si spegne da sola. */
    private final Runnable quandoTace;

    private MediaPlayer lettore;
    private Vibrator vibrazione;
    private int passo;

    public Suoneria(Context context, Runnable quandoTace) {
        this.context = context.getApplicationContext();
        this.quandoTace = quandoTace;
    }

    /** Comincia a suonare. {@code sveglia} falso vuol dire timer. */
    public void suona(boolean sveglia) {
        taci();
        Uri uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM);
        // Non tutti i sistemi hanno una suoneria di sveglia impostata: su un
        // tablet ripulito puo' mancare. Meglio ripiegare che tacere.
        if (uri == null) uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE);
        if (uri == null) uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION);
        if (uri != null) {
            try {
                lettore = new MediaPlayer();
                // Sul canale della sveglia e non su quello dei contenuti: e'
                // l'unico che resta udibile col resto abbassato.
                lettore.setAudioStreamType(AudioManager.STREAM_ALARM);
                lettore.setDataSource(context, uri);
                lettore.setLooping(true);
                lettore.prepare();
                float partenza = sveglia ? VOLUME_INIZIALE : VOLUME_TIMER;
                lettore.setVolume(partenza, partenza);
                lettore.start();
                if (sveglia) {
                    passo = 0;
                    ui.postDelayed(rampa, PASSO_MS);
                }
            } catch (Exception e) {
                Log.w(TAG, "la suoneria non parte: " + e.getMessage());
                rilascia();
            }
        } else {
            Log.w(TAG, "nessuna suoneria di sistema da usare");
        }
        if (sveglia) vibra();
        // Il ritardo si arma comunque, anche se il suono non e' partito: la
        // schermata che suona deve smettere di occupare il pannello lo stesso.
        ui.postDelayed(basta, SMETTE_DA_SOLA_MS);
    }

    private final Runnable rampa = new Runnable() {
        @Override public void run() {
            if (lettore == null) return;
            passo++;
            float v = VOLUME_INIZIALE + (1f - VOLUME_INIZIALE) * (passo / (float) PASSI);
            if (v > 1f) v = 1f;
            try {
                lettore.setVolume(v, v);
            } catch (Exception morto) {
                return;
            }
            if (passo < PASSI) ui.postDelayed(this, PASSO_MS);
        }
    };

    private final Runnable basta = new Runnable() {
        @Override public void run() {
            Log.i(TAG, "nessuno ha risposto: smetto da sola");
            taci();
            if (quandoTace != null) quandoTace.run();
        }
    };

    private void vibra() {
        try {
            Vibrator v = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
            if (v != null && v.hasVibrator()) {
                v.vibrate(BATTITO, 0);
                vibrazione = v;
            }
        } catch (Exception e) {
            vibrazione = null;
        }
    }

    /**
     * Zitta, senza dire niente a nessuno. Chi sta per far partire un suono la
     * chiama prima per sicurezza, e avvisare anche li' farebbe ridisegnare le
     * pagine due volte di fila.
     */
    public void taci() {
        ui.removeCallbacks(rampa);
        ui.removeCallbacks(basta);
        rilascia();
        if (vibrazione != null) {
            try {
                vibrazione.cancel();
            } catch (Exception ignorata) {
                // Il servizio se n'e' andato: non vibra piu' comunque.
            }
            vibrazione = null;
        }
    }

    private void rilascia() {
        if (lettore == null) return;
        try {
            lettore.stop();
        } catch (Exception ignorata) {
            // Gia' fermo: capita se non era mai partito.
        }
        try {
            lettore.release();
        } catch (Exception ignorata) {
            // Niente da liberare.
        }
        lettore = null;
    }

    // ---- volume di sistema ----

    /** Vero se il canale della sveglia e' a zero: suonerebbe in silenzio. */
    public boolean muta() {
        AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        return am != null && am.getStreamVolume(AudioManager.STREAM_ALARM) == 0;
    }

    /**
     * Riporta il canale della sveglia a meta' corsa. Questo si' tocca il volume
     * di sistema, ma solo perche' qualcuno ha premuto « Alza »: la rampa non lo
     * fa mai da se'.
     */
    public void alzaVolume() {
        AudioManager am = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (am == null) return;
        int max = am.getStreamMaxVolume(AudioManager.STREAM_ALARM);
        am.setStreamVolume(AudioManager.STREAM_ALARM, Math.max(1, max / 2), 0);
    }
}
