package dev.tabdeck;

import android.content.Context;
import android.util.Log;

import org.json.JSONObject;

import java.io.Closeable;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;

/**
 * Legge e scrive i piccoli file JSON dell'app in getFilesDir().
 *
 * Esiste per le sveglie. Finche' ce n'era una sola stava bene nelle
 * preferenze, una chiave per campo; con un elenco di sveglie e di timer le
 * chiavi diventano un file, e un file va scritto con un'attenzione che le
 * preferenze nascondevano.
 *
 * <b>La scrittura e' atomica.</b> Il tablet sta sul comodino attaccato a un
 * alimentatore che qualcuno puo' staccare: se la corrente se ne va a meta'
 * scrittura, un file troncato al riavvio vuol dire <i>tutte le sveglie
 * cancellate</i>, in silenzio, e il primo che se ne accorge e' chi non si e'
 * svegliato. Si scrive su un file temporaneo, lo si forza sul disco, e solo
 * allora lo si rinomina sopra il vecchio: il rename e' l'unica operazione che
 * il filesystem promette indivisibile.
 *
 * <b>org.json e non una libreria:</b> e' gia' nel framework, e per un file da
 * mezzo kilobyte un parser in piu' sarebbe tutto peso.
 */
public final class Archivio {

    private static final String TAG = "TabDeck.Archivio";

    private Archivio() {}

    /** Il contenuto, oppure null se il file non c'e' o non si legge. */
    public static JSONObject leggi(Context c, String nome) {
        File f = new File(c.getFilesDir(), nome);
        if (!f.exists()) return null;
        FileInputStream in = null;
        try {
            in = new FileInputStream(f);
            byte[] tutto = new byte[(int) f.length()];
            int letti = 0;
            while (letti < tutto.length) {
                int n = in.read(tutto, letti, tutto.length - letti);
                if (n < 0) break;
                letti += n;
            }
            return new JSONObject(new String(tutto, 0, letti, "UTF-8"));
        } catch (Exception e) {
            // Un file rotto non deve impedire all'app di partire: chi chiama
            // riparte da vuoto. Meglio un elenco di sveglie vuoto, che si vede,
            // di una Home che non si apre.
            Log.w(TAG, nome + " illeggibile, riparto da zero: " + e.getMessage());
            return null;
        } finally {
            chiudi(in);
        }
    }

    /** Scrive, o lascia sul disco quello che c'era prima. Mai una via di mezzo. */
    public static boolean scrivi(Context c, String nome, JSONObject dati) {
        File definitivo = new File(c.getFilesDir(), nome);
        File temporaneo = new File(c.getFilesDir(), nome + ".tmp");
        FileOutputStream out = null;
        try {
            out = new FileOutputStream(temporaneo);
            out.write(dati.toString().getBytes("UTF-8"));
            out.flush();
            // Senza questo i byte stanno ancora nella cache del kernel: il
            // rename riuscirebbe e il contenuto no.
            out.getFD().sync();
            out.close();
            out = null;

            // Prima si prova a rinominare sopra il vecchio, che su Linux
            // sostituisce in un colpo solo. Cancellare prima e rinominare dopo
            // lascerebbe un attimo senza nessun file, e la corrente che va via
            // proprio li' farebbe ripartire l'app da un elenco vuoto.
            if (temporaneo.renameTo(definitivo)) return true;
            if (definitivo.exists() && !definitivo.delete()) {
                Log.w(TAG, "non riesco a togliere il vecchio " + nome);
            }
            if (!temporaneo.renameTo(definitivo)) {
                Log.w(TAG, "rename fallito per " + nome);
                return false;
            }
            return true;
        } catch (Exception e) {
            Log.w(TAG, "non ho potuto scrivere " + nome + ": " + e.getMessage());
            return false;
        } finally {
            chiudi(out);
            if (temporaneo.exists()) temporaneo.delete();
        }
    }

    /** Il file esiste? Serve a capire se c'e' ancora da migrare. */
    public static boolean esiste(Context c, String nome) {
        return new File(c.getFilesDir(), nome).exists();
    }

    private static void chiudi(Closeable qualcosa) {
        if (qualcosa == null) return;
        try {
            qualcosa.close();
        } catch (Exception ignorata) {
            // Gia' chiuso o mai aperto: non c'e' niente da salvare qui.
        }
    }
}
