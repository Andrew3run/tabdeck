package dev.tabdeck;

import android.util.Log;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.HashMap;
import java.util.Map;

/**
 * Le pagine che TabDeck chiede a internet (oggi il meteo), dirette o attraverso il PC.
 *
 * Con « Internet col cavo: solo TabDeck » scelto sul PC e il PC collegato, la richiesta
 * non esce dal tablet: va al PC come frame {@link Proto#WEB}, il PC la scarica e
 * rimanda il testo con {@link Proto#WEB_RISPOSTA}. Serve dove il tablet non ha il
 * Wi-Fi ma ha il cavo. In tutti gli altri casi la richiesta parte da qui come sempre;
 * con « tutto il tablet » ci pensa gia' {@link Tunnel}.
 */
public final class Web {

    private static final String TAG = "TabDeck.Web";
    private static final long ATTESA_MS = 15000;

    /** "no", "dati" o "tutto": lo manda il PC nel frame CONFIG. */
    public static volatile String modo = "no";

    private static final Object lock = new Object();
    private static final Map<Integer, String[]> attese = new HashMap<Integer, String[]>();
    private static int prossimo = 1;

    private Web() {}

    /** Thread di lavoro, mai quello della UI: aspetta la risposta. */
    public static String get(String indirizzo) throws Exception {
        Link link = Link.get();
        if ("dati".equals(modo) && link.isConnected()) return dalPc(link, indirizzo);
        return diretto(indirizzo);
    }

    private static String dalPc(Link link, String indirizzo) throws Exception {
        int id;
        String[] posto = new String[2];
        synchronized (lock) {
            id = prossimo++;
            attese.put(id, posto);
        }
        try {
            link.sendJson(Proto.WEB, new JSONObject().put("id", id).put("url", indirizzo).toString());
            long fine = System.currentTimeMillis() + ATTESA_MS;
            synchronized (lock) {
                while (posto[0] == null && posto[1] == null) {
                    long resta = fine - System.currentTimeMillis();
                    if (resta <= 0) throw new Exception("il PC non ha risposto");
                    lock.wait(resta);
                }
            }
            if (posto[1] != null) throw new Exception(posto[1]);
            return posto[0];
        } finally {
            synchronized (lock) {
                attese.remove(id);
            }
        }
    }

    /** Thread di rete: {"id":n,"testo":"..."} oppure {"id":n,"errore":"..."}. */
    static void risposta(String json) {
        try {
            JSONObject o = new JSONObject(json);
            synchronized (lock) {
                String[] posto = attese.get(o.optInt("id"));
                if (posto == null) return;
                if (o.has("errore")) posto[1] = o.optString("errore", "errore");
                else posto[0] = o.optString("testo", "");
                lock.notifyAll();
            }
        } catch (Exception e) {
            Log.w(TAG, "risposta del PC illeggibile: " + e.getMessage());
        }
    }

    private static String diretto(String indirizzo) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(indirizzo).openConnection();
        c.setConnectTimeout(8000);
        c.setReadTimeout(8000);
        c.setRequestProperty("User-Agent", "TabDeck");
        InputStream in = null;
        try {
            in = c.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[4096];
            int n;
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            return out.toString("UTF-8");
        } finally {
            if (in != null) in.close();
            c.disconnect();
        }
    }
}
