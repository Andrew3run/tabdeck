package dev.tabdeck;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/**
 * Rete di sicurezza per l'avvio, e per le sveglie.
 *
 * Quando TabDeck e' gia' la Home predefinita Android la lancia da solo; questo
 * ricevitore copre il periodo in cui non lo e' ancora, o il caso in cui
 * l'utente abbia rimesso temporaneamente TouchWiz.
 *
 * <h3>Le volte in cui le sveglie spariscono</h3>
 *
 * AlarmManager tiene le buste in memoria, non su disco. Dopo un
 * <b>riavvio</b> non ne resta nessuna. Dopo una <b>reinstallazione</b> nemmeno,
 * ed e' quella che ci si dimentica: ogni {@code build.ps1 -Install} le
 * porterebbe via in silenzio, e ce ne si accorgerebbe la mattina dopo. E con
 * l'<b>ora o il fuso cambiati</b> restano, ma puntano all'istante sbagliato: la
 * sveglia delle sette, spostato l'orologio di due ore, suonerebbe alle cinque.
 *
 * In tutti questi casi basta rileggere il file e rimetterle, e lo fa
 * {@link Ora#rimetti}. Solo all'avvio si apre anche l'activity: negli altri
 * casi il tablet e' in mano a qualcuno, e vedersi riaprire la Home sotto il
 * dito per un cambio d'ora sarebbe un dispetto.
 */
public final class BootReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(Context ctx, Intent intent) {
        String azione = intent.getAction();
        if (azione == null) return;

        if (Intent.ACTION_BOOT_COMPLETED.equals(azione)) {
            Ora.rimetti(ctx);
            Intent launch = new Intent(ctx, MainActivity.class);
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            ctx.startActivity(launch);
        } else if (Intent.ACTION_MY_PACKAGE_REPLACED.equals(azione)
                || Intent.ACTION_TIME_CHANGED.equals(azione)
                || Intent.ACTION_TIMEZONE_CHANGED.equals(azione)) {
            Ora.rimetti(ctx);
        }
    }
}
