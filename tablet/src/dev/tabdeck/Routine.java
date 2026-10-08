package dev.tabdeck;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * Una routine: un pulsante solo che tocca piu' lampade, una dopo l'altra.
 *
 * « Buonanotte » spegne tutto tranne il comodino e lo porta al venti per cento;
 * « Film » spegne la plafoniera e mette il comodino sull'arancione. Scriverla
 * richiede una tastiera, quindi si scrive sul PC; premerla richiede solo un
 * dito, quindi si preme sul tablet - anche a PC spento.
 *
 * I passi vanno in ordine e uno per volta: due comandi in parallelo verso la
 * stessa lampada finirebbero uno sopra l'altro, perche' una lampada Tuya
 * accetta una connessione sola alla volta.
 */
public final class Routine {

    public final String nome;
    public final String glifo;
    public final int colore;
    public final List<Passo> passi;

    /**
     * Vero per una scena: com'erano le luci in un momento, da ritrovare con un tocco.
     * Falso per una routine vera, una sequenza con le sue attese. Sul tablet stanno
     * in due posti diversi: le scene si scelgono, le routine si fanno partire.
     */
    public final boolean scena;

    /** Vero mentre la routine sta girando: il pulsante si disegna in attesa. */
    public boolean inCorso;

    public Routine(String nome, String glifo, int colore, List<Passo> passi, boolean scena) {
        this.nome = nome;
        this.glifo = glifo;
        this.colore = colore;
        this.passi = passi;
        this.scena = scena;
    }

    /** L'azione di un passo che non tocca lampade: aspetta e basta. */
    public static final String ATTESA = "attesa";

    /** Dieci minuti: oltre, non e' piu' una scena, e' una sveglia. */
    private static final int ATTESA_MAX_S = 600;

    /** I secondi di un passo « attesa », tenuti fra uno e dieci minuti. */
    public static int secondiAttesa(String valore) {
        int s;
        try {
            s = Integer.parseInt(valore.trim());
        } catch (Exception e) {
            s = 5;
        }
        return Math.max(1, Math.min(ATTESA_MAX_S, s));
    }

    /** Un passo: a chi, cosa, con che valore. */
    public static final class Passo {
        /** Identificativo della lampada, oppure vuoto per dire « tutte ». Ignorato da « attesa ». */
        public final String luce;
        /** on | off | inverti | luce | colore | bianco | bianchezza | attesa */
        public final String azione;
        /** La percentuale per « luce », il colore RRGGBB per « colore », i secondi per « attesa ». */
        public final String valore;

        public Passo(String luce, String azione, String valore) {
            this.luce = luce;
            this.azione = azione;
            this.valore = valore;
        }
    }

    public static Routine da(JSONObject o) {
        List<Passo> passi = new ArrayList<Passo>();
        JSONArray a = o.optJSONArray("passi");
        if (a != null) {
            for (int i = 0; i < a.length(); i++) {
                JSONObject p = a.optJSONObject(i);
                if (p == null) continue;
                passi.add(new Passo(
                        p.optString("luce", ""),
                        p.optString("azione", "off"),
                        p.optString("valore", "")));
            }
        }
        return new Routine(
                o.optString("nome", ""),
                o.optString("glifo", "◉"),
                Tinta.leggi(o.optString("colore", ""), 0xFF1F6F4A),
                passi,
                o.optBoolean("scena", false));
    }

    public JSONObject json() throws org.json.JSONException {
        JSONArray a = new JSONArray();
        for (Passo p : passi) {
            JSONObject o = new JSONObject();
            o.put("luce", p.luce);
            o.put("azione", p.azione);
            o.put("valore", p.valore);
            a.put(o);
        }
        JSONObject o = new JSONObject();
        o.put("nome", nome);
        o.put("glifo", glifo);
        o.put("colore", Tinta.scrivi(colore));
        o.put("passi", a);
        o.put("scena", scena);
        return o;
    }
}
