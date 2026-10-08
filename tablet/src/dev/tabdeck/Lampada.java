package dev.tabdeck;

import org.json.JSONObject;

/**
 * Una lampada di casa, come la conosce il tablet.
 *
 * Identificativo, indirizzo, chiave locale e versione del protocollo arrivano
 * dal PC una volta sola e restano salvati qui: da quel momento il tablet
 * comanda le luci da solo, a PC spento e anche senza internet.
 *
 * L'indirizzo e' l'unico dato che invecchia — il router puo' riassegnarlo — ed
 * e' per questo che non e' final: {@link Luci} lo riaggiorna ascoltando gli
 * annunci che le lampade mandano in broadcast.
 */
public final class Lampada {

    public final String nome;
    public final String id;
    public final String chiave;
    public final float versione;

    /** Il segno disegnato sulla scheda, scelto dalla finestra sul PC. */
    public final String glifo;

    /** Tinta della scheda, come i pulsanti del deck. */
    public final int colore;

    /** La stanza in cui sta, scritta sul PC. Vuota: la lampada non ne ha una. */
    public final String stanza;

    /** Volatile: lo scrive l'orecchio sugli annunci, lo legge il thread del comando. */
    public volatile String ip;

    /** L'ultimo stato letto, oppure null se non le si e' ancora parlato. */
    public Tuya.Stato stato;

    /** Vero mentre un comando e' in volo: la riga si disegna in attesa. */
    public boolean inCorso;

    // Il resto e' di Luci, e si tocca solo dal thread dell'interfaccia.

    /** L'azione in volo, come ordinale, oppure -1. */
    int inVolo = -1;

    /** Quello che e' stato chiesto mentre era occupata, oppure -1. */
    int dopo = -1;
    int dopoValore;

    /** Quando si e' annunciata l'ultima volta, in uptime. */
    long sentita;

    /** L'ultima rilettura partita da un annuncio, e quante di fila a vuoto. */
    long rincorsa;
    int rincorseAVuoto;

    private volatile Tuya canale;

    public Lampada(String nome, String id, String ip, String chiave, float versione,
                   String glifo, int colore, String stanza) {
        this.stanza = stanza;
        this.nome = nome;
        this.id = id;
        this.ip = ip;
        this.chiave = chiave;
        this.versione = versione;
        this.glifo = glifo;
        this.colore = colore;
    }

    public static Lampada da(JSONObject o) {
        String versione = o.optString("versione", "3.3");
        float v;
        try {
            v = Float.parseFloat(versione);
        } catch (NumberFormatException e) {
            v = 3.3f;
        }
        return new Lampada(
                o.optString("nome", ""),
                o.optString("id", ""),
                o.optString("ip", ""),
                o.optString("chiave", ""),
                v,
                o.optString("glifo", "○"),
                Tinta.leggi(o.optString("colore", ""), 0xFF33565E),
                o.optString("stanza", "").trim());
    }

    public JSONObject json() throws org.json.JSONException {
        JSONObject o = new JSONObject();
        o.put("nome", nome);
        o.put("id", id);
        o.put("ip", ip);
        o.put("chiave", chiave);
        o.put("versione", String.valueOf(versione));
        o.put("glifo", glifo);
        o.put("colore", Tinta.scrivi(colore));
        o.put("stanza", stanza);
        return o;
    }

    /**
     * Il canale verso questa lampada. Vive quanto la lampada e non quanto il
     * comando: dentro si deposita la mappa dei numeri scoperta alla prima
     * lettura, e rifarla a ogni pressione sarebbe un giro in rete buttato.
     */
    public Tuya canale() {
        if (canale == null) {
            canale = new Tuya(id, ip, chiave, versione);
        } else if (!ip.equals(canale.ip())) {
            canale.setIp(ip);
        }
        return canale;
    }

    int inVolo() {
        return inVolo;
    }

    /**
     * Il router l'ha spostata. L'indirizzo va anche al canale subito, e non al
     * prossimo comando: una lettura in corso ribussa a ogni tentativo, e cosi'
     * al tentativo dopo bussa alla porta giusta.
     */
    public void spostaA(String nuovo) {
        ip = nuovo;
        Tuya c = canale;
        if (c != null) c.setIp(nuovo);
    }

    /** Utilizzabile solo se la chiave c'e': senza, non risponde a nessuno. */
    public boolean completa() {
        return id.length() > 0 && ip.length() > 0 && chiave.length() == 16;
    }
}
