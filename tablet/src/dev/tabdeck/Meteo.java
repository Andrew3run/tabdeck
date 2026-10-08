package dev.tabdeck;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.Locale;

/**
 * Il meteo del posto, per il riquadro dell'ora nella Home.
 *
 * Due domande, e nessuna chiave: dove si e' lo chiede a ip-api.com, dall'indirizzo
 * di rete, una volta al giorno; che tempo fa lo chiede a Open-Meteo, ogni mezz'ora
 * e solo mentre la Home si guarda. Tutte e due in chiaro, http: KitKat ha i
 * certificati del 2013 e TLS 1.2 spento, e un https moderno qui non si apre.
 *
 * L'ultima risposta resta nelle preferenze: all'accensione, o senza rete, si vede
 * il meteo di prima invece di un buco. Si spegne nelle Impostazioni del tablet, e
 * spento non chiede piu' niente a nessuno.
 */
public final class Meteo {

    private static final String TAG = "TabDeck.Meteo";
    public static final String PREFS = "meteo";
    public static final String ACCESO = "acceso";

    private static final long OGNI_MS = 30 * 60000L;
    private static final long POSIZIONE_OGNI_MS = 24 * 3600000L;

    /** Quello che si disegna. Immutabile: arriva dal thread di rete intero. */
    public static final class Dati {
        public final String citta;
        public final int temperatura, massima, minima;
        public final int codice;
        public final boolean giorno;

        Dati(String citta, int temperatura, int massima, int minima, int codice, boolean giorno) {
            this.citta = citta;
            this.temperatura = temperatura;
            this.massima = massima;
            this.minima = minima;
            this.codice = codice;
            this.giorno = giorno;
        }

        /** L'icona Lucide del tempo, dal codice WMO. */
        public String icona() {
            if (codice == 0) return giorno ? "sun" : "moon";
            if (codice <= 2) return giorno ? "cloud-sun" : "cloud-moon";
            if (codice == 3) return "cloud";
            if (codice == 45 || codice == 48) return "cloud-fog";
            if (codice >= 51 && codice <= 57) return "cloud-drizzle";
            if ((codice >= 61 && codice <= 67) || (codice >= 80 && codice <= 82)) return "cloud-rain";
            if ((codice >= 71 && codice <= 77) || codice == 85 || codice == 86) return "cloud-snow";
            if (codice >= 95) return "cloud-lightning";
            return "cloud";
        }

        /** Il tempo in una parola. */
        public String detto() {
            if (codice == 0) return "Sereno";
            if (codice <= 2) return "Poco nuvoloso";
            if (codice == 3) return "Nuvoloso";
            if (codice == 45 || codice == 48) return "Nebbia";
            if (codice >= 51 && codice <= 57) return "Pioviggine";
            if ((codice >= 61 && codice <= 67) || (codice >= 80 && codice <= 82)) return "Pioggia";
            if ((codice >= 71 && codice <= 77) || codice == 85 || codice == 86) return "Neve";
            if (codice >= 95) return "Temporale";
            return "Variabile";
        }
    }

    public interface Ascolto {
        /** Thread UI: ci sono dati nuovi. */
        void meteoCambiato(Dati d);
    }

    private final Context context;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private Ascolto ascolto;
    private Dati dati;
    private boolean inCorso;

    public Meteo(Context c) {
        context = c.getApplicationContext();
        dati = leggiSalvati();
    }

    public void setAscolto(Ascolto a) {
        ascolto = a;
    }

    /** L'ultimo meteo noto, o null. */
    public Dati dati() {
        return acceso(context) ? dati : null;
    }

    public static boolean acceso(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(ACCESO, true);
    }

    public static void accendi(Context c, boolean si) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(ACCESO, si).apply();
    }

    /** Chiede il meteo se l'ultima risposta ha piu' di mezz'ora. Thread UI. */
    public void aggiorna() {
        if (inCorso || !acceso(context)) return;
        final SharedPreferences p = prefs();
        if (System.currentTimeMillis() - p.getLong("quando", 0) < OGNI_MS && dati != null) return;
        inCorso = true;
        new Thread(new Runnable() {
            @Override public void run() {
                Dati nuovi = null;
                try {
                    nuovi = chiedi(p);
                } catch (Throwable t) {
                    Log.w(TAG, "meteo non letto: " + t);
                }
                final Dati fatti = nuovi;
                ui.post(new Runnable() {
                    @Override public void run() {
                        inCorso = false;
                        if (fatti == null) return;
                        dati = fatti;
                        if (ascolto != null) ascolto.meteoCambiato(fatti);
                    }
                });
            }
        }, "TabDeck-meteo").start();
    }

    private Dati chiedi(SharedPreferences p) throws Exception {
        // Dove: una volta al giorno basta, il tablet non si sposta.
        float lat = p.getFloat("lat", Float.NaN), lon = p.getFloat("lon", Float.NaN);
        String citta = p.getString("citta", "");
        // Una citta' scelta a mano vince sempre: l'indirizzo di rete dice dov'e'
        // il fornitore, che puo' stare a cento chilometri.
        boolean manuale = p.getBoolean("manuale", false);
        if (!manuale && (Float.isNaN(lat) || System.currentTimeMillis() - p.getLong("posizioneQuando", 0) > POSIZIONE_OGNI_MS)) {
            JSONObject o = new JSONObject(scarica("http://ip-api.com/json/?fields=status,city,lat,lon&lang=it"));
            if ("success".equals(o.optString("status"))) {
                lat = (float) o.optDouble("lat");
                lon = (float) o.optDouble("lon");
                citta = o.optString("city", "");
                p.edit().putFloat("lat", lat).putFloat("lon", lon).putString("citta", citta)
                        .putLong("posizioneQuando", System.currentTimeMillis()).apply();
            }
        }
        if (Float.isNaN(lat)) return null;

        String url = String.format(Locale.US, "http://api.open-meteo.com/v1/forecast?latitude=%.4f&longitude=%.4f"
                + "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min"
                + "&timezone=auto&forecast_days=1", lat, lon);
        JSONObject o = new JSONObject(scarica(url));
        JSONObject ora = o.getJSONObject("current");
        JSONObject giorno = o.getJSONObject("daily");
        JSONArray max = giorno.getJSONArray("temperature_2m_max");
        JSONArray min = giorno.getJSONArray("temperature_2m_min");
        Dati d = new Dati(citta,
                (int) Math.round(ora.getDouble("temperature_2m")),
                (int) Math.round(max.getDouble(0)),
                (int) Math.round(min.getDouble(0)),
                ora.optInt("weather_code", 3),
                ora.optInt("is_day", 1) == 1);
        p.edit().putLong("quando", System.currentTimeMillis())
                .putString("dati", new JSONObject().put("citta", d.citta).put("t", d.temperatura)
                        .put("max", d.massima).put("min", d.minima).put("codice", d.codice)
                        .put("giorno", d.giorno).toString())
                .apply();
        return d;
    }

    // ---- la localita' scelta a mano ----

    public interface Trovata {
        /** Thread UI: il nome trovato, o null se non c'e' una citta' con quel nome. */
        void trovata(String nome);
    }

    /** La citta' del meteo adesso: quella scelta, o l'ultima dedotta dalla rete. */
    public static String citta(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString("citta", "");
    }

    public static boolean manuale(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean("manuale", false);
    }

    /**
     * Cerca la citta' su Open-Meteo e la fissa come localita' del meteo. Il
     * meteo si rilegge al prossimo giro della Home, cioe' entro un minuto.
     */
    public static void scegliCitta(Context c, final String nome, final Trovata esito) {
        final SharedPreferences p = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        final Handler ui = new Handler(Looper.getMainLooper());
        new Thread(new Runnable() {
            @Override public void run() {
                String trovato = null;
                try {
                    String url = "http://geocoding-api.open-meteo.com/v1/search?count=1&language=it&format=json&name="
                            + java.net.URLEncoder.encode(nome.trim(), "UTF-8");
                    JSONArray r = new JSONObject(scarica(url)).optJSONArray("results");
                    if (r != null && r.length() > 0) {
                        JSONObject o = r.getJSONObject(0);
                        trovato = o.optString("name", nome.trim());
                        p.edit().putBoolean("manuale", true)
                                .putFloat("lat", (float) o.getDouble("latitude"))
                                .putFloat("lon", (float) o.getDouble("longitude"))
                                .putString("citta", trovato)
                                .putLong("quando", 0)
                                .apply();
                    }
                } catch (Throwable t) {
                    Log.w(TAG, "citta' non trovata: " + t);
                }
                final String fatto = trovato;
                ui.post(new Runnable() {
                    @Override public void run() { esito.trovata(fatto); }
                });
            }
        }, "TabDeck-citta").start();
    }

    /** Torna alla localita' dedotta dalla rete. */
    public static void automatica(Context c) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putBoolean("manuale", false).putLong("posizioneQuando", 0).putLong("quando", 0).apply();
    }

    private Dati leggiSalvati() {
        try {
            String s = prefs().getString("dati", null);
            if (s == null) return null;
            JSONObject o = new JSONObject(s);
            return new Dati(o.optString("citta", ""), o.optInt("t"), o.optInt("max"), o.optInt("min"),
                    o.optInt("codice", 3), o.optBoolean("giorno", true));
        } catch (Exception e) {
            return null;
        }
    }

    private SharedPreferences prefs() {
        return context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private static String scarica(String indirizzo) throws Exception {
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
