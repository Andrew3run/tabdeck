package dev.tabdeck;

import android.app.ActivityManager;
import android.content.Context;

import java.io.IOException;
import java.io.RandomAccessFile;

/**
 * Quanto sta lavorando il tablet: percentuale di CPU e di memoria occupata.
 *
 * Prima queste due misure arrivavano dal PC dentro il frame STATS, ed erano il
 * carico del PC. Era sbagliato due volte: sul tablet interessa il tablet, e
 * quei numeri si vedevano solo mentre il PC trasmetteva lo schermo — a cavo
 * staccato, o col solo deck aperto, le barrette restavano vuote per sempre.
 * Adesso le legge il tablet da se', come gia' faceva con la batteria, e non
 * dipendono da niente che stia dall'altra parte del cavo.
 *
 * Su Android 4.4 /proc/stat e' ancora leggibile da un'app qualsiasi: il divieto
 * e' arrivato con Android 8. Qui si puo'.
 */
public final class Sonda {

    private final ActivityManager am;

    /** Riusato a ogni misura: allocarne uno ogni due secondi non ha senso. */
    private final ActivityManager.MemoryInfo memoria = new ActivityManager.MemoryInfo();

    /** -1 = prima lettura, non c'e' ancora un intervallo su cui calcolare. */
    private long ultimoTotale = -1;
    private long ultimoOccupato = -1;

    public Sonda(Context context) {
        am = (ActivityManager) context.getSystemService(Context.ACTIVITY_SERVICE);
    }

    /**
     * Percentuale di CPU consumata dall'ultima chiamata, oppure -1 finche' non
     * ci sono due letture da confrontare.
     *
     * /proc/stat conta i tick spesi in ciascuno stato da quando il tablet e'
     * acceso: un valore assoluto non dice niente, e' la differenza fra due
     * letture che e' il carico. La riga "cpu" e' la somma dei due core, quindi
     * la scala resta 0-100 e non 0-200.
     */
    public int cpu() {
        String riga = primaRiga();
        if (riga == null) return -1;

        long totale = 0, inattivo = 0;
        int i = 0, len = riga.length();

        // Salta l'etichetta "cpu": da li' in poi sono solo numeri.
        while (i < len && riga.charAt(i) != ' ') i++;

        // Colonne: user nice system idle iowait irq softirq steal. Le prime due
        // dell'elenco che contano come fermo sono idle e iowait, cioe' la 3 e
        // la 4 partendo da zero.
        int colonna = 0;
        while (i < len) {
            while (i < len && riga.charAt(i) == ' ') i++;
            long valore = 0;
            boolean cifre = false;
            while (i < len) {
                char c = riga.charAt(i);
                if (c < '0' || c > '9') break;
                valore = valore * 10 + (c - '0');
                cifre = true;
                i++;
            }
            if (!cifre) break;
            totale += valore;
            if (colonna == 3 || colonna == 4) inattivo += valore;
            colonna++;
        }
        if (totale == 0) return -1;

        long occupato = totale - inattivo;
        long primaTotale = ultimoTotale, primaOccupato = ultimoOccupato;
        ultimoTotale = totale;
        ultimoOccupato = occupato;
        if (primaTotale < 0) return -1;

        long dTotale = totale - primaTotale;
        long dOccupato = occupato - primaOccupato;
        // Un core che si spegne e si riaccende puo' far tornare indietro i
        // contatori: in quel giro non si inventa un numero, si aspetta il dopo.
        if (dTotale <= 0 || dOccupato < 0) return -1;

        int percento = (int) Math.round(dOccupato * 100.0 / dTotale);
        return percento < 0 ? 0 : (percento > 100 ? 100 : percento);
    }

    /**
     * Percentuale di memoria occupata, o -1 se il sistema non la dice.
     *
     * Si usa la memoria "disponibile" di Android, non MemFree del kernel: la
     * cache dei file conta come libera, perche' lo e' — il sistema la cede a
     * chi ne ha bisogno. Contarla come occupata darebbe un tablet all'80%
     * anche appena acceso e con niente aperto, che e' esattamente il genere di
     * numero che spaventa senza dire niente.
     */
    public int ram() {
        am.getMemoryInfo(memoria);
        long totale = memoria.totalMem;
        if (totale <= 0) return -1;
        long occupata = totale - memoria.availMem;
        if (occupata < 0) occupata = 0;
        return (int) Math.round(occupata * 100.0 / totale);
    }

    private static String primaRiga() {
        try (RandomAccessFile f = new RandomAccessFile("/proc/stat", "r")) {
            return f.readLine();
        } catch (IOException e) {
            return null;
        }
    }
}
