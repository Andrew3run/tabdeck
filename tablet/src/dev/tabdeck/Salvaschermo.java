package dev.tabdeck;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.HashSet;

/**
 * Il salvaschermo: come lo si e' scelto sul PC, e le foto che il PC ha mandato.
 *
 * Tutto si salva qui, scelte e immagini: il salvaschermo parte anche a PC spento
 * e a cavo staccato, come l'orologio. Il tablet non scarica niente da internet —
 * su Android 4.4 i certificati e il TLS di oggi sono una lotteria — e non
 * ridimensiona niente: arrivano gia' a 1024x600, pronte da disegnare.
 */
public final class Salvaschermo {

    private static final String TAG = "TabDeck.Salvaschermo";
    private static final String PREFS = "salvaschermo";
    private static final String CHIAVE = "config";
    private static final String CARTELLA = "salvaschermo";

    public static final String FOTO = "foto";
    public static final String AURORA = "aurora";
    public static final String OROLOGIO = "orologio";

    public static final class Foto {
        public final String nome;
        public final String didascalia;

        Foto(String nome, String didascalia) {
            this.nome = nome;
            this.didascalia = didascalia;
        }
    }

    private final Context context;

    public boolean acceso;
    public long dopoMs = 5 * 60000L;
    public String stile = FOTO;
    public long ogniMs = 60000L;
    public boolean ora = true;
    public boolean data = true;
    /** 0-80: quanto si abbassa l'immagine, per non illuminare la stanza di notte. */
    public int attenuazione = 15;
    /** Tiene acceso il pannello mentre scorre: senza, lo spegne il timeout di Android. */
    public boolean pannello;
    public final ArrayList<Foto> foto = new ArrayList<Foto>();

    public Salvaschermo(Context context) {
        this.context = context.getApplicationContext();
        String salvata = preferenze().getString(CHIAVE, null);
        if (salvata == null) return;
        try {
            leggi(new JSONObject(salvata));
        } catch (JSONException e) {
            Log.w(TAG, "configurazione salvata non leggibile");
        }
    }

    /**
     * Thread UI: la configurazione del PC. Toglie le foto che non servono piu' e
     * torna i nomi di quelle che mancano, da chiedere.
     */
    public JSONArray configura(JSONObject o) {
        leggi(o);
        preferenze().edit().putString(CHIAVE, o.toString()).apply();

        HashSet<String> volute = new HashSet<String>();
        JSONArray mancano = new JSONArray();
        for (Foto f : foto) {
            volute.add(f.nome);
            if (!file(f.nome).exists()) mancano.put(f.nome);
        }
        for (File f : elenca()) {
            if (!volute.contains(f.getName())) f.delete();
        }
        return mancano;
    }

    /** Le foto che ci sono davvero, nell'ordine scelto sul PC. */
    public ArrayList<Foto> disponibili() {
        ArrayList<Foto> ci = new ArrayList<Foto>();
        for (Foto f : foto) {
            if (file(f.nome).exists()) ci.add(f);
        }
        return ci;
    }

    public File file(String nome) {
        return new File(context.getDir(CARTELLA, Context.MODE_PRIVATE), nome);
    }

    /** Thread di rete: una foto arrivata dal PC. Il nome lo sceglie il PC, e qui si controlla. */
    public static void salva(Context c, String nome, byte[] buf, int off, int len) {
        if (!nome.matches("[a-z0-9-]{1,64}\\.jpg")) {
            Log.w(TAG, "nome di foto rifiutato: " + nome);
            return;
        }
        File dir = c.getDir(CARTELLA, Context.MODE_PRIVATE);
        File arrivo = new File(dir, nome + ".arrivo");
        FileOutputStream out = null;
        try {
            out = new FileOutputStream(arrivo);
            out.write(buf, off, len);
            out.close();
            out = null;
            // Rinominata solo intera: un collegamento che cade a meta' non lascia
            // una foto tagliata che il salvaschermo proverebbe a disegnare.
            if (!arrivo.renameTo(new File(dir, nome))) arrivo.delete();
        } catch (IOException e) {
            Log.w(TAG, "foto " + nome + " non salvata: " + e.getMessage());
            arrivo.delete();
        } finally {
            if (out != null) {
                try { out.close(); } catch (IOException ignored) { }
            }
        }
    }

    private void leggi(JSONObject o) {
        acceso = o.optBoolean("acceso", false);
        dopoMs = Math.max(1, o.optInt("dopo", 5)) * 60000L;
        stile = o.optString("stile", FOTO);
        ogniMs = Math.max(10, o.optInt("ogni", 60)) * 1000L;
        ora = o.optBoolean("ora", true);
        data = o.optBoolean("data", true);
        attenuazione = Math.max(0, Math.min(80, o.optInt("attenuazione", 15)));
        pannello = o.optBoolean("pannello", false);
        foto.clear();
        JSONArray elenco = o.optJSONArray("foto");
        if (elenco == null) return;
        for (int i = 0; i < elenco.length(); i++) {
            JSONObject f = elenco.optJSONObject(i);
            if (f != null && f.optString("nome", "").length() > 0) {
                foto.add(new Foto(f.optString("nome"), f.optString("didascalia", "")));
            }
        }
    }

    private File[] elenca() {
        File[] files = context.getDir(CARTELLA, Context.MODE_PRIVATE).listFiles();
        return files != null ? files : new File[0];
    }

    private SharedPreferences preferenze() {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }
}
