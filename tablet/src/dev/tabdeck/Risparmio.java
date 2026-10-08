package dev.tabdeck;

import android.content.Context;
import android.provider.Settings;

/**
 * Le impostazioni di Android da cui dipende davvero quanta corrente beve il
 * tablet. Non sono nell'app: sono nel sistema, e il tablet esce di fabbrica
 * con i valori pensati per stare in mano dieci minuti, non appoggiato alla
 * scrivania tutto il giorno.
 *
 * Misurate su questo SM-T210 prima di scrivere questa classe:
 *
 *   screen_off_timeout        1800000 ms — mezz'ora accesa dopo ogni tocco
 *   stay_on_while_plugged_in  3 — col cavo attaccato lo schermo non si spegne mai
 *   screen_brightness         188/255 — il 74 per cento
 *   wifi_sleep_policy         2 — la radio non dorme mai
 *
 * Con quei valori il tablet a 87 per cento, dichiarato "in carica" su una porta
 * USB, segnava -145 mA: si stava scaricando mentre era attaccato alla corrente.
 * E' lo stesso sintomo annotato in {@code MainActivity.applyAwake()}, ma la
 * causa non era li': {@code stay_on_while_plugged_in} vince sulla nostra
 * scelta, perche' quando il cavo c'e' il timeout di Android non scade mai e il
 * ramo "lascia fare ad Android" non fa niente.
 *
 * <h3>Quello che l'app puo' cambiare e quello che no</h3>
 *
 * Le due voci in {@code Settings.System} — durata e luminosita' — le scriviamo
 * noi: su Android 4.4 {@code WRITE_SETTINGS} e' un permesso normale, concesso
 * al momento dell'installazione, senza schermate da attraversare.
 *
 * Le due in {@code Settings.Global} — il cavo e la radio — vogliono
 * {@code WRITE_SECURE_SETTINGS}, che si da' solo alle app di sistema. Restano
 * in sola lettura: la pagina le mostra com'e' messo e apre la schermata di
 * Android giusta. Da PC si cambiano una volta e basta, con
 *
 *   adb shell settings put global stay_on_while_plugged_in 0
 *   adb shell settings put global wifi_sleep_policy 0
 */
public final class Risparmio {

    /** Le scelte offerte nella pagina, in millisecondi. L'ultima e' "mai". */
    public static final int[] DURATE = {
        15000, 30000, 60000, 120000, 600000, Integer.MAX_VALUE
    };

    public static final String[] DURATE_ETICHETTE = {
        "15 s", "30 s", "1 min", "2 min", "10 min", "mai"
    };

    /**
     * Quanto sta acceso il pannello dopo l'ultimo tocco. Sopra questa soglia
     * non e' piu' un timeout, e' uno schermo sempre acceso: un'ora ha lo stesso
     * effetto pratico di "mai".
     */
    public static final int DURATA_ECCESSIVA = 600000;

    private Risparmio() {}

    // ---- quello che scriviamo noi ----

    /** Millisecondi di pannello acceso dopo l'ultimo tocco. */
    public static int durata(Context c) {
        return Settings.System.getInt(c.getContentResolver(),
                Settings.System.SCREEN_OFF_TIMEOUT, 60000);
    }

    /** {@code false} se Android ha negato la scrittura. */
    public static boolean setDurata(Context c, int ms) {
        try {
            return Settings.System.putInt(c.getContentResolver(),
                    Settings.System.SCREEN_OFF_TIMEOUT, ms);
        } catch (SecurityException e) {
            return false;
        }
    }

    /**
     * Porta la luminosita' di sistema allo stesso valore che la finestra si e'
     * gia' imposta.
     *
     * La finestra si regola da se' con {@code lp.screenBrightness} e per il
     * deck basterebbe. Non basta pero' per quello che sta sopra di noi: la
     * schermata di blocco al risveglio e le impostazioni di Android sono altre
     * finestre, e restano alla luminosita' di sistema. Se quella e' 188 su 255,
     * ogni risveglio accende il pannello al massimo per qualche secondo.
     *
     * @param percento 0-100, oppure negativo per non toccare niente
     */
    public static boolean setLuminosita(Context c, int percento) {
        if (percento < 0) return true;
        int valore = Math.max(10, Math.min(255, Math.round(percento * 255f / 100f)));
        try {
            // Se e' rimasta l'automatica, il valore scritto viene subito
            // scavalcato dal sensore: si spegne prima.
            Settings.System.putInt(c.getContentResolver(),
                    Settings.System.SCREEN_BRIGHTNESS_MODE,
                    Settings.System.SCREEN_BRIGHTNESS_MODE_MANUAL);
            return Settings.System.putInt(c.getContentResolver(),
                    Settings.System.SCREEN_BRIGHTNESS, valore);
        } catch (SecurityException e) {
            return false;
        }
    }

    // ---- quello che possiamo solo guardare ----

    /** Vero se il cavo attaccato tiene il pannello acceso comunque. */
    public static boolean cavoTieneAcceso(Context c) {
        return Settings.Global.getInt(c.getContentResolver(),
                Settings.Global.STAY_ON_WHILE_PLUGGED_IN, 0) != 0;
    }

    /** 0 dorme con lo schermo, 1 dorme solo a batteria, 2 non dorme mai. */
    public static int radio(Context c) {
        return Settings.Global.getInt(c.getContentResolver(),
                Settings.Global.WIFI_SLEEP_POLICY,
                Settings.Global.WIFI_SLEEP_POLICY_DEFAULT);
    }

    public static String radioDetta(int politica) {
        switch (politica) {
            case Settings.Global.WIFI_SLEEP_POLICY_NEVER:            return "sempre sveglia";
            case Settings.Global.WIFI_SLEEP_POLICY_NEVER_WHILE_PLUGGED: return "dorme a batteria";
            default:                                                 return "dorme col pannello";
        }
    }

    public static String durataDetta(int ms) {
        if (ms >= Integer.MAX_VALUE || ms <= 0) return "mai";
        if (ms < 60000) return (ms / 1000) + " s";
        return (ms / 60000) + " min";
    }
}
