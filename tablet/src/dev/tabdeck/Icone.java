package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;

/**
 * Le immagini dei pulsanti del deck, tenute in casa.
 *
 * Il PC le manda gia' ridotte a 128 pixel e con un nome che e' l'impronta del
 * loro contenuto; qui si scrivono in files/icone e si decodificano una volta
 * sola. Restano su disco per la stessa ragione delle luci: all'accensione il
 * deck deve essere gia' disegnato per intero, e a PC spento i pulsanti devono
 * continuare ad avere una faccia — semplicemente non fanno niente, e si vede
 * dall'indicatore che non lo fanno.
 *
 * La cache in memoria non ha un limite di dimensione e non serve che ce
 * l'abbia: le icone sono al massimo quante sono le celle del deck, e a 128
 * pixel ognuna occupa 64 KB. Quaranta pulsanti stanno in due megabyte e mezzo,
 * su un gigabyte di RAM.
 */
public final class Icone {

    private static final String TAG = "TabDeck.Icone";
    private static final String CARTELLA = "icone";

    /** Il valore nullo e' un « questa non c'e' » ricordato, e vale quanto l'immagine. */
    private static final Map<String, Bitmap> memoria = new HashMap<String, Bitmap>();

    private Icone() {}

    private static File cartella(Context context) {
        File dir = new File(context.getFilesDir(), CARTELLA);
        if (!dir.exists()) dir.mkdirs();
        return dir;
    }

    /**
     * L'icona pronta da disegnare, o null se non c'e'. Chi disegna puo'
     * chiamarla senza controlli: un pulsante senza immagine torna al glifo.
     */
    public static synchronized Bitmap prendi(Context context, String nome) {
        if (nome == null || nome.length() == 0) return null;
        String chiave = pulito(nome);
        if (memoria.containsKey(chiave)) return memoria.get(chiave);

        Bitmap letta = null;
        File file = new File(cartella(context), chiave);
        if (file.exists()) {
            BitmapFactory.Options opzioni = new BitmapFactory.Options();
            // Con l'alfa: le icone stanno su celle colorate, e senza
            // trasparenza si vedrebbero dentro un francobollo nero.
            opzioni.inPreferredConfig = Bitmap.Config.ARGB_8888;
            try {
                letta = BitmapFactory.decodeFile(file.getAbsolutePath(), opzioni);
            } catch (OutOfMemoryError e) {
                Log.w(TAG, "memoria finita mentre leggevo " + nome);
            }
        }

        memoria.put(chiave, letta);
        return letta;
    }

    /**
     * Riceve un'icona dal PC: la scrive e la mette subito in memoria.
     *
     * Torna true se e' cambiato qualcosa da ridisegnare. La stessa icona
     * rimandata al collegamento successivo non e' un cambiamento: il file c'e'
     * gia' identico, e riscriverlo sarebbe solo usura della flash.
     */
    public static synchronized boolean ricevi(Context context, String nome, byte[] dati, int off, int len) {
        if (nome == null || nome.length() == 0 || len <= 0) return false;
        String chiave = pulito(nome);
        File file = new File(cartella(context), chiave);

        // Il nome e' l'impronta del contenuto: se il file c'e' gia' della
        // stessa lunghezza, e' la stessa immagine. Non si decodifica e non si
        // riscrive niente — succede a ogni collegamento, ed e' il caso normale.
        if (memoria.get(chiave) != null && file.length() == len) return false;

        Bitmap immagine;
        try {
            BitmapFactory.Options opzioni = new BitmapFactory.Options();
            opzioni.inPreferredConfig = Bitmap.Config.ARGB_8888;
            immagine = BitmapFactory.decodeByteArray(dati, off, len, opzioni);
        } catch (OutOfMemoryError e) {
            Log.w(TAG, "memoria finita mentre decodificavo " + nome);
            return false;
        }

        if (immagine == null) {
            Log.w(TAG, "icona illeggibile: " + nome);
            return false;
        }

        if (file.length() != len) scrivi(file, dati, off, len);

        // Quella di prima non si ricicla a mano: questo metodo gira sul thread
        // di rete, e il deck potrebbe averla in mano proprio adesso sul thread
        // dell'interfaccia. La lascia andare il garbage collector, che sa
        // aspettare.
        memoria.put(chiave, immagine);
        return true;
    }

    private static void scrivi(File file, byte[] dati, int off, int len) {
        FileOutputStream uscita = null;
        try {
            uscita = new FileOutputStream(file);
            uscita.write(dati, off, len);
        } catch (IOException e) {
            // Senza il file l'icona vive comunque in memoria fino allo
            // spegnimento: si e' persa la persistenza, non l'icona.
            Log.w(TAG, "icona non scritta su disco: " + e.getMessage());
        } finally {
            if (uscita != null) {
                try {
                    uscita.close();
                } catch (IOException ignored) {
                }
            }
        }
    }

    /**
     * Butta le icone che nessun pulsante nomina piu'. Si chiama quando arriva
     * una griglia nuova: e' l'unico momento in cui si sa con certezza quali
     * servono, e senza, ogni immagine mai provata resterebbe li' per sempre.
     */
    public static synchronized void tieniSolo(Context context, Set<String> usate) {
        File[] files = cartella(context).listFiles();
        if (files == null) return;

        Set<String> tenere = new HashSet<String>();
        for (String nome : usate) tenere.add(pulito(nome));

        for (File file : files) {
            String nome = file.getName();
            if (tenere.contains(nome)) continue;

            Bitmap buttata = memoria.remove(nome);
            if (buttata != null) buttata.recycle();
            if (!file.delete()) Log.w(TAG, "icona non cancellata: " + nome);
        }
    }

    /**
     * Il nome arriva dalla rete e finisce in un percorso: qualsiasi cosa somigli
     * a una cartella va via. Il PC manda impronte esadecimali con .png in fondo,
     * ma un nome che risale l'albero delle cartelle non deve poter esistere
     * neanche per sbaglio.
     */
    private static String pulito(String nome) {
        return nome.replace('/', '_').replace('\\', '_').replace("..", "__");
    }
}
