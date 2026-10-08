package dev.tabdeck;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.Set;

import dalvik.system.DexClassLoader;

/**
 * Le estensioni sul tablet: il codice non sta nell'APK, arriva dal PC.
 *
 * Un'estensione e' un pacchetto Android a parte (dev.tabdeck.&lt;id&gt;) che non
 * si installa nel sistema: il PC lo manda sul collegamento, qui si salva nella
 * cartella privata dell'app e si carica con un {@link DexClassLoader} figlio di
 * quello di TabDeck — cosi' {@link Plugin}, {@link Link} e il resto del nucleo
 * sono le classi di qui, non copie.
 *
 * <h3>Perche' la firma</h3>
 * Il collegamento in rete lo apre chiunque stia sul Wi-Fi. Caricare codice
 * arrivato da li' senza guardarlo vorrebbe dire far eseguire al tablet quello
 * che manda il primo che passa: si accetta solo un pacchetto firmato con la
 * stessa chiave di TabDeck, e dell'estensione che dice di essere.
 */
public final class Estensioni {

    private static final String TAG = "TabDeck.Estensioni";
    private static final String CARTELLA = "estensioni";
    private static final String CARTELLA_DEX = "estensioni-dex";

    /**
     * I classloader gia' fatti, per impronta: la Home si rifa' piu' volte al
     * giorno, e ricaricare il dex a ogni activity costerebbe secondi. Tenerli
     * serve anche a lasciare vivo lo stato statico dell'estensione.
     */
    private static final HashMap<String, ClassLoader> caricate = new HashMap<String, ClassLoader>();

    private Estensioni() {}

    /** Lettere minuscole e cifre: diventa il nome del file e del pacchetto. */
    public static boolean idValido(String id) {
        return id != null && id.matches("[a-z][a-z0-9]{1,31}");
    }

    /** L'impronta del pacchetto salvato per quell'estensione, o "" se non c'e'. */
    public static String impronta(Context c, String id) {
        File f = trova(c, id);
        if (f == null) return "";
        String nome = f.getName();
        return nome.substring(id.length() + 1, nome.length() - ".apk".length());
    }

    /**
     * Thread di rete. Salva il pacchetto arrivato dal PC, dopo averlo verificato.
     * @return null se e' andata, altrimenti il motivo.
     */
    public static synchronized String salva(Context c, String id, byte[] buf, int off, int len) {
        if (!idValido(id)) return "nome non valido";
        File dir = c.getDir(CARTELLA, Context.MODE_PRIVATE);
        File arrivo = new File(dir, id + ".arrivo");
        FileOutputStream out = null;
        try {
            out = new FileOutputStream(arrivo);
            out.write(buf, off, len);
            out.getFD().sync();
        } catch (IOException e) {
            arrivo.delete();
            return "non si salva: " + e.getMessage();
        } finally {
            if (out != null) {
                try { out.close(); } catch (IOException ignored) { }
            }
        }

        String errore = verifica(c, arrivo, id);
        if (errore != null) {
            arrivo.delete();
            return errore;
        }
        String impronta = sha256(buf, off, len);
        rimuovi(c, id);
        if (!arrivo.renameTo(new File(dir, id + "-" + impronta + ".apk"))) {
            arrivo.delete();
            return "non si salva";
        }
        Log.i(TAG, id + " salvata, " + len / 1024 + " KB");
        return null;
    }

    /** Thread UI. L'estensione salvata, costruita. Lancia se il pacchetto non la contiene. */
    public static synchronized Plugin carica(Context c, String id, String classe) throws Exception {
        File f = trova(c, id);
        if (f == null) throw new IOException("pacchetto assente");
        String chiave = f.getName();
        ClassLoader loader = caricate.get(chiave);
        if (loader == null) {
            File dex = c.getDir(CARTELLA_DEX, Context.MODE_PRIVATE);
            loader = new DexClassLoader(f.getPath(), dex.getPath(), null, Estensioni.class.getClassLoader());
            caricate.put(chiave, loader);
        }
        return (Plugin) loader.loadClass(classe).newInstance();
    }

    /** Toglie il pacchetto e il suo dex ottimizzato. */
    public static synchronized void rimuovi(Context c, String id) {
        if (!idValido(id)) return;
        String prefisso = id + "-";
        for (File f : elenca(c.getDir(CARTELLA, Context.MODE_PRIVATE))) {
            if (f.getName().startsWith(prefisso)) f.delete();
        }
        for (File f : elenca(c.getDir(CARTELLA_DEX, Context.MODE_PRIVATE))) {
            if (f.getName().startsWith(prefisso)) f.delete();
        }
        Iterator<String> chiavi = caricate.keySet().iterator();
        while (chiavi.hasNext()) {
            if (chiavi.next().startsWith(prefisso)) chiavi.remove();
        }
    }

    /** Toglie tutte quelle che il PC non ha piu'. */
    public static synchronized void tieniSolo(Context c, Set<String> ids) {
        HashSet<String> salvate = new HashSet<String>();
        for (File f : elenca(c.getDir(CARTELLA, Context.MODE_PRIVATE))) {
            int trattino = f.getName().indexOf('-');
            if (trattino > 0) salvate.add(f.getName().substring(0, trattino));
        }
        for (String id : salvate) {
            if (!ids.contains(id)) {
                Log.i(TAG, id + " non c'e' piu' sul PC: tolta");
                rimuovi(c, id);
            }
        }
    }

    private static File trova(Context c, String id) {
        if (!idValido(id)) return null;
        for (File f : elenca(c.getDir(CARTELLA, Context.MODE_PRIVATE))) {
            String nome = f.getName();
            if (nome.startsWith(id + "-") && nome.endsWith(".apk")) return f;
        }
        return null;
    }

    private static File[] elenca(File dir) {
        File[] files = dir.listFiles();
        return files != null ? files : new File[0];
    }

    private static String verifica(Context c, File f, String id) {
        PackageManager pm = c.getPackageManager();
        PackageInfo arrivato = pm.getPackageArchiveInfo(f.getPath(), PackageManager.GET_SIGNATURES);
        if (arrivato == null) return "non e' un pacchetto Android";
        if (!("dev.tabdeck." + id).equals(arrivato.packageName)) return "il pacchetto e' di un'altra estensione";
        try {
            Signature[] mie = pm.getPackageInfo(c.getPackageName(), PackageManager.GET_SIGNATURES).signatures;
            Signature[] sue = arrivato.signatures;
            // Senza firme vuol dire che una voce del pacchetto non torna con la firma: e' stato toccato.
            if (sue == null || sue.length == 0) return "pacchetto senza firma valida";
            if (!new HashSet<Signature>(Arrays.asList(sue)).equals(new HashSet<Signature>(Arrays.asList(mie)))) {
                return "firmato con una chiave diversa da quella di TabDeck";
            }
        } catch (PackageManager.NameNotFoundException e) {
            return "firma di TabDeck non leggibile";
        }
        return null;
    }

    private static String sha256(byte[] buf, int off, int len) {
        try {
            byte[] d = MessageDigest.getInstance("SHA-256").digest(Arrays.copyOfRange(buf, off, off + len));
            StringBuilder s = new StringBuilder(d.length * 2);
            for (byte b : d) s.append(String.format("%02x", b & 0xFF));
            return s.toString();
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
