package dev.tabdeck;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.PowerManager;

/**
 * Dove arriva la sveglia di Android.
 *
 * Non suona qui e non disegna niente: accende il pannello e passa la mano
 * all'activity, che e' la Home e quindi c'e' sempre. Tutto il resto — cosa
 * suonare, per quanto, cosa riprogrammare — sta in {@link Ora}, in un posto
 * solo. Da qui passano due cose: che cosa e' scattato, e quale fra i tanti
 * timer e sveglie, perche' e' il numero che dice a {@link Ora} chi spegnere e
 * chi riprogrammare.
 *
 * Il blocco di risveglio serve per il caso che conta davvero: le sette del
 * mattino, pannello spento, processore addormentato. Senza, Android consegna il
 * messaggio e torna a dormire prima che l'activity abbia finito di aprirsi.
 * Si prende con una scadenza e non si rilascia a mano: se qualcosa piu' avanti
 * sbaglia strada, scade da solo invece di restare appeso a mangiare batteria —
 * che su questo tablet e' un errore che si paga caro.
 */
public final class Allarme extends BroadcastReceiver {

    /** Tanto basta ad aprire l'activity e a far partire la suoneria. */
    private static final long RESPIRO_MS = 20_000L;

    // FULL_WAKE_LOCK e i suoi due compagni sono deprecati da Android 5, dove al
    // loro posto c'e' una finestra che si dichiara capace di accendere il
    // pannello. Qui si compila contro KitKat, dove quella strada non esiste e
    // questa e' l'unica: la nota di javac va zittita qui, perche' con
    // $ErrorActionPreference = 'Stop' basta lei a far fallire la build.
    @SuppressWarnings("deprecation")
    @Override
    public void onReceive(Context ctx, Intent intent) {
        String cosa = intent.getStringExtra(Ora.EXTRA_COSA);
        int id = intent.getIntExtra(Ora.EXTRA_ID, -1);
        if (cosa == null || id < 0) return;

        PowerManager pm = (PowerManager) ctx.getSystemService(Context.POWER_SERVICE);
        if (pm != null) {
            PowerManager.WakeLock sveglio = pm.newWakeLock(
                    PowerManager.FULL_WAKE_LOCK
                            | PowerManager.ACQUIRE_CAUSES_WAKEUP
                            | PowerManager.ON_AFTER_RELEASE,
                    "TabDeck.Allarme");
            sveglio.acquire(RESPIRO_MS);
        }

        Intent apri = new Intent(ctx, MainActivity.class);
        // SINGLE_TOP perche' l'activity e' singleTask e sta gia' li' davanti:
        // non se ne vuole una seconda, si vuole che quella aperta riceva la
        // notizia in onNewIntent.
        apri.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        apri.putExtra(Ora.EXTRA_COSA, cosa);
        apri.putExtra(Ora.EXTRA_ID, id);
        ctx.startActivity(apri);
    }
}
