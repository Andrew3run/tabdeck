package dev.tabdeck;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.ByteBuffer;
import java.security.SecureRandom;
import java.util.zip.CRC32;

import javax.crypto.Cipher;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

import org.json.JSONObject;

/**
 * Parla alle lampade Tuya (quelle dell'app Smart Life) direttamente sulla rete
 * di casa, senza passare da nessun cloud.
 *
 * Alexa non c'entra e non poteva entrarci: Amazon non espone nessun modo per
 * comandare da fuori i dispositivi collegati a un account. Le lampade Tuya,
 * pero', accettano comandi in locale - e' quello che fa l'app quando il
 * telefono e' in casa. Quindi il tablet le comanda da solo, a PC spento, e
 * anche con internet staccato: basta che sia sulla stessa rete.
 *
 * Tutto quello che serve sta nel framework di Android: socket, AES e HMAC da
 * javax.crypto, CRC32 da java.util.zip, JSON da org.json. Nessuna dipendenza,
 * come per il resto dell'app.
 *
 * Il pacchetto e' sempre fatto cosi':
 *
 *     000055AA | numero | comando | lunghezza | payload | CRC32 | 0000AA55
 *                                               (cifrato)  HMAC in 3.4
 *
 * Le due versioni che girano in casa si comportano diversamente in due punti
 * che e' facile sbagliare:
 *
 *   3.3  si cifra il JSON, POI si mette davanti l'intestazione "3.3"+12 zeri.
 *        In coda un CRC32. Nessuna stretta di mano: si apre e si parla.
 *   3.4  l'intestazione "3.4"+12 zeri si mette PRIMA di cifrare, in coda c'e'
 *        un HMAC-SHA256 al posto del CRC, e prima di qualunque comando si
 *        negozia una chiave di sessione con uno scambio di numeri casuali.
 *
 * Ogni comando apre la sua connessione e la chiude. Sono poche lampade e si
 * premono pochi pulsanti: tenere aperte delle socket verso lampadine che si
 * spengono e cambiano indirizzo costerebbe piu' codice di quanto ne risparmi,
 * e lascerebbe qualcosa acceso a girare - che qui non si vuole.
 *
 * <h3>Perche' si insiste ad aprire</h3>
 *
 * Aprire la connessione e' la parte che sbaglia, e non per colpa di TCP: le
 * lampade Tuya risparmiano corrente spegnendo la radio in ricezione, e mentre
 * dormono <b>non sentono il broadcast</b>. L'ARP, che e' broadcast, e' proprio
 * il primo passo di ogni collegamento: finche' il tablet non ha in tasca il MAC
 * della lampada non puo' mandarle niente, e per averlo deve chiederlo a voce
 * alta a una lampada che non sta ascoltando.
 *
 * E' lo stesso buco descritto nel README fra il PC e il tablet, con i ruoli
 * scambiati. Misurato sul posto, da questo tablet verso una lampada di cui
 * aveva perso l'indirizzo hardware:
 *
 *   4 tentativi in 5 secondi     nessuna risposta, « host irraggiungibile »
 *   20 tentativi in 19 secondi   nessuna risposta
 *   60 tentativi in 60 secondi   silenzio per 21 secondi, poi risponde sempre
 *
 * La lampada, insomma, prima o poi la sente: le tocca svegliarsi comunque ogni
 * tanto, e in una di quelle finestre l'ARP passa. Da quel momento la voce resta
 * in tabella e si rinfresca da sola con richieste dirette - non piu' broadcast -
 * che la lampada riceve senza problemi: il secondo comando e tutti quelli dopo
 * partono in due decimi di secondo.
 *
 * Il PC non ha mai visto questo problema perche' la sua tabella e' sempre calda:
 * e' li' acceso che parla con le lampade. Il tablet ci arriva freddo ogni volta
 * che la rete si riaggancia, e prima si arrendeva dopo quattro secondi - da cui
 * la lampada « che va in timeout », a caso una volta l'una e una volta l'altra.
 *
 * Quindi non si molla al primo no: si riprova per {@link #INSISTENZA_MS}. Si
 * insiste solo sull'<b>apertura</b>, mai su un comando gia' partito, se no un
 * « inverti » ripetuto riporterebbe la luce come stava.
 */
public final class Tuya {

    private static final String TAG = "TabDeck.Tuya";

    private static final int PORTA = 6668;
    private static final int PREFISSO = 0x000055AA;
    private static final int SUFFISSO = 0x0000AA55;

    private static final int CMD_SESS_INIZIO   = 0x03;
    private static final int CMD_SESS_RISPOSTA = 0x04;
    private static final int CMD_SESS_FINE     = 0x05;
    private static final int CMD_CONTROLLO     = 0x07;
    private static final int CMD_INTERROGA     = 0x0A;
    private static final int CMD_CONTROLLO_NEW = 0x0D;
    private static final int CMD_INTERROGA_NEW = 0x10;

    /** Oltre questo il singolo tentativo e' perso e se ne fa un altro. */
    private static final int ATTESA_MS = 4000;

    /**
     * Per quanto si insiste ad aprire prima di dire che la lampada non risponde.
     * Mezzo minuto perche' misurando ne servivano ventuno alla lampada piu'
     * dormigliona, e rinunciare un attimo prima che risponda e' il modo
     * peggiore di sbagliare: costa solo l'attesa di un'icona che gira, e la
     * paga soltanto chi ha davvero una lampada spenta al muro.
     */
    private static final int INSISTENZA_MS = 30000;

    /** Fra un tentativo e l'altro, tanto per non girare a vuoto sulla CPU. */
    private static final int PAUSA_MS = 600;

    private static final byte[] ZERI12 = new byte[12];

    private final String id;
    private final String chiave;
    private final float versione;

    /** Volatile: puo' cambiare mentre {@link #apri()} ribussa, e al giro dopo si usa il nuovo. */
    private volatile String ip;

    // Mappa dei "data point": quale numero e' l'interruttore, quale la
    // luminosita', quale il colore. Cambia col tipo di lampada e si scopre
    // guardando quali numeri arrivano nella risposta, non si puo' sapere prima.
    private char tipo = '?';
    private String dpAcceso = "1";
    private String dpModo;
    private String dpLuminosita;
    private String dpColore;
    private String dpTemperatura;
    private int lumMin = 25, lumMax = 255;
    private int tempMax = 255;
    private boolean mappaFatta;

    /**
     * L'ultimo stato completo conosciuto, aggiornato pezzo per pezzo.
     *
     * Serve perche' dopo un comando la lampada non rimanda tutto: rimanda solo
     * quello che e' cambiato. Ricavare da quella risposta parziale che cosa sa
     * fare la lampada voleva dire concludere che, appena accesa, non regola piu'
     * ne' luce ne' colore - e vedersi sparire i controlli sotto le dita proprio
     * mentre li si usa.
     */
    private final JSONObject noto = new JSONObject();

    public Tuya(String id, String ip, String chiave, float versione) {
        this.id = id;
        this.ip = ip;
        this.chiave = chiave;
        this.versione = versione;
    }

    public void setIp(String ip) {
        this.ip = ip;
    }

    public String ip() {
        return ip;
    }

    /** Com'e' messa una lampada adesso, o perche' non si e' fatta sentire. */
    public static final class Stato {
        public boolean raggiunta;
        public String errore = "";
        public boolean accesa;
        /** Da 1 a 100, oppure -1 se questa lampada non la regola. */
        public int luminosita = -1;
        public boolean haLuminosita;
        public boolean haColore;
        /** Il bianco regolabile da caldo a freddo. */
        public boolean haTemperatura;
    }

    // ---- comandi ----

    /**
     * Interroga la lampada. E' anche il modo con cui si scopre che controlli
     * ha senso disegnare: una lampadina a colori e una plafoniera bianca
     * rispondono con numeri diversi, e l'interfaccia si adegua a quello che
     * torna invece di indovinare.
     */
    public Stato leggi() {
        Sessione s = null;
        try {
            s = apri();
            return interpreta(s.interroga());
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    public Stato accendi(boolean on) {
        Sessione s = null;
        try {
            s = apri();
            interpreta(s.interroga());           // serve la mappa dei numeri
            JSONObject dps = new JSONObject();
            dps.put(dpAcceso, on);
            return interpreta(s.comanda(dps));
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    /** Inverte: accesa diventa spenta e viceversa. */
    public Stato inverti() {
        Sessione s = null;
        try {
            s = apri();
            Stato prima = interpreta(s.interroga());
            JSONObject dps = new JSONObject();
            dps.put(dpAcceso, !prima.accesa);
            return interpreta(s.comanda(dps));
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    /**
     * Luminosita' da 1 a 100. Lo zero non c'e': una lampada al minimo resta
     * accesa, per spegnerla c'e' l'interruttore, e mandare un valore sotto il
     * minimo della lampada la lascia in uno stato da cui poi risponde male.
     */
    public Stato luminosita(int percento) {
        Sessione s = null;
        try {
            s = apri();
            interpreta(s.interroga());
            if (dpLuminosita == null) return errore("questa lampada non regola la luce");
            int chiesto = Math.min(100, Math.max(1, percento));
            int v = lumMin + Math.round((lumMax - lumMin) * (chiesto / 100f));
            JSONObject dps = new JSONObject();
            dps.put(dpAcceso, true);             // regolare una lampada spenta non si vede
            dps.put(dpLuminosita, v);
            return interpreta(s.comanda(dps));
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    /** Colore come 0xRRGGBB. */
    public Stato colore(int rgb) {
        Sessione s = null;
        try {
            s = apri();
            interpreta(s.interroga());
            if (dpColore == null) return errore("questa lampada non fa colori");
            JSONObject dps = new JSONObject();
            dps.put(dpAcceso, true);
            if (dpModo != null) dps.put(dpModo, "colour");
            dps.put(dpColore, esadecimaleColore(rgb));
            return interpreta(s.comanda(dps));
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    /**
     * Il bianco da caldo a freddo, da 0 a 100. Zero e' la luce delle lampadine
     * vecchie, cento quella del giorno.
     */
    public Stato temperatura(int percento) {
        Sessione s = null;
        try {
            s = apri();
            interpreta(s.interroga());
            if (dpTemperatura == null) return errore("questa lampada ha un bianco solo");
            int chiesto = Math.min(100, Math.max(0, percento));
            JSONObject dps = new JSONObject();
            dps.put(dpAcceso, true);
            if (dpModo != null) dps.put(dpModo, "white");
            dps.put(dpTemperatura, Math.round(tempMax * (chiesto / 100f)));
            return interpreta(s.comanda(dps));
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    /** Torna alla luce bianca, uscendo dal modo colore. */
    public Stato bianco() {
        Sessione s = null;
        try {
            s = apri();
            interpreta(s.interroga());
            if (dpModo == null) return errore("questa lampada ha solo il bianco");
            JSONObject dps = new JSONObject();
            dps.put(dpAcceso, true);
            dps.put(dpModo, "white");
            return interpreta(s.comanda(dps));
        } catch (Exception e) {
            return fallita(e);
        } finally {
            chiudi(s);
        }
    }

    // ---- lettura della risposta ----

    private Stato interpreta(JSONObject risposta) {
        Stato stato = new Stato();
        JSONObject arrivati = estraiDps(risposta);
        if (arrivati == null) {
            stato.errore = "risposta senza stato";
            return stato;
        }
        JSONObject dps = unisci(arrivati);
        mappa(dps);
        stato.raggiunta = true;
        stato.accesa = dps.optBoolean(dpAcceso, false);
        stato.haLuminosita = dpLuminosita != null;
        stato.haColore = dpColore != null;
        stato.haTemperatura = dpTemperatura != null;
        if (stato.haLuminosita) {
            int v = dps.optInt(dpLuminosita, lumMin);
            stato.luminosita = Math.max(1, Math.min(100,
                    Math.round((v - lumMin) * 100f / Math.max(1, lumMax - lumMin))));
        }
        return stato;
    }

    /**
     * Mette i valori appena arrivati sopra quelli gia' noti e restituisce il
     * quadro completo. Una risposta parziale aggiorna, non sostituisce.
     */
    private JSONObject unisci(JSONObject arrivati) {
        // names() invece di keys(): quello restituisce un iteratore senza tipo,
        // e javac se ne lamenta con una nota che qui fa fallire la build.
        org.json.JSONArray chiavi = arrivati.names();
        if (chiavi == null) return noto;
        for (int i = 0; i < chiavi.length(); i++) {
            String k = chiavi.optString(i, "");
            if (k.length() == 0) continue;
            try {
                noto.put(k, arrivati.get(k));
            } catch (Exception ignored) {
            }
        }
        return noto;
    }

    /**
     * In 3.3 lo stato arriva come {"dps":{...}}, in 3.4 impacchettato dentro
     * "data". Meglio accettarli entrambi qui che portarsi due strade a monte.
     */
    private static JSONObject estraiDps(JSONObject risposta) {
        if (risposta == null) return null;
        if (risposta.has("dps")) return risposta.optJSONObject("dps");
        JSONObject dati = risposta.optJSONObject("data");
        return dati == null ? null : dati.optJSONObject("dps");
    }

    /**
     * Quale numero fa cosa. Le lampade Tuya si dividono in tre famiglie e non
     * lo dichiarano: si riconoscono da quali numeri compaiono nello stato.
     * Il 20 e' la firma delle piu' recenti, le vecchie partono dall'1.
     */
    private void mappa(JSONObject dps) {
        // Una volta capito che lampada e', non si torna indietro: le risposte
        // successive sono parziali e la farebbero sembrare piu' povera di
        // quello che e'.
        if (mappaFatta) return;
        mappaFatta = true;
        if (dps.has("20")) {
            tipo = 'B';
            dpAcceso = "20"; dpModo = "21"; dpLuminosita = "22"; dpColore = "24";
            dpTemperatura = "23";
            lumMin = 10; lumMax = 1000; tempMax = 1000;
        } else if (dps.has("3") || dps.has("5")) {
            tipo = 'A';
            dpAcceso = "1"; dpModo = "2"; dpLuminosita = "3"; dpColore = "5";
            dpTemperatura = "4";
            lumMin = 25; lumMax = 255; tempMax = 255;
        } else {
            // Interruttore e poco altro: una plafoniera che accende e basta,
            // o una presa. Niente modo, niente colore.
            tipo = 'C';
            dpAcceso = "1"; dpModo = null; dpColore = null; dpTemperatura = null;
            dpLuminosita = dps.has("2") ? "2" : null;
            lumMin = 25; lumMax = 255;
        }
        // Quello che la lampada non ha mandato, non ce l'ha: meglio non
        // disegnare un cursore che poi non muove niente.
        if (dpLuminosita != null && !dps.has(dpLuminosita)) dpLuminosita = null;
        if (dpColore != null && !dps.has(dpColore)) dpColore = null;
        if (dpModo != null && !dps.has(dpModo)) dpModo = null;
        if (dpTemperatura != null && !dps.has(dpTemperatura)) dpTemperatura = null;
    }

    /**
     * Il colore si scrive in due formati diversi a seconda della famiglia: le
     * nuove vogliono tinta, saturazione e valore su quattro cifre esadecimali
     * ciascuno; le vecchie vogliono prima l'RGB e poi gli stessi tre numeri su
     * misure piu' piccole.
     */
    private String esadecimaleColore(int rgb) {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        float[] hsv = new float[3];
        android.graphics.Color.RGBToHSV(r, g, b, hsv);
        int h = Math.round(hsv[0]);
        if (tipo == 'B') {
            return String.format("%04x%04x%04x", h,
                    Math.round(hsv[1] * 1000), Math.round(hsv[2] * 1000));
        }
        return String.format("%02x%02x%02x%04x%02x%02x", r, g, b, h,
                Math.round(hsv[1] * 255), Math.round(hsv[2] * 255));
    }

    private static Stato errore(String testo) {
        Stato s = new Stato();
        s.errore = testo;
        return s;
    }

    /**
     * Perche' non ce l'ha fatta, detto in una riga che stia sotto il nome della
     * lampada. I messaggi di rete di Android sono lunghi, in inglese e pieni di
     * sigle: sulla scheda non ci starebbero, e a chi guarda non direbbero
     * niente in piu' di queste tre righe.
     */
    private static Stato fallita(Exception e) {
        String m = e.getMessage();
        if (m == null) m = e.getClass().getSimpleName();
        if (m.contains("EHOSTUNREACH") || m.contains("unreachable")) {
            // Il caso di gran lunga piu' comune: la lampada dorme e non ha
            // sentito nemmeno la domanda « chi sei ».
            return errore("muta da " + (INSISTENZA_MS / 1000) + " secondi - spenta al muro?");
        }
        if (m.contains("ETIMEDOUT") || m.contains("timed out") || m.contains("timeout")) {
            return errore("scaduto il tempo d'attesa");
        }
        if (m.contains("ECONNREFUSED") || m.contains("refused")) {
            return errore("rifiuta il collegamento");
        }
        if (m.contains("ENETUNREACH") || m.contains("ENONET")) {
            return errore("il tablet non e' in rete");
        }
        return errore(m);
    }

    private static void chiudi(Sessione s) {
        if (s != null) s.chiudi();
    }

    /**
     * Apre la connessione, e se non ci riesce riprova finche' c'e' tempo.
     *
     * E' l'unica cosa che il tablet puo' fare da solo: non gli e' concesso
     * scrivere a mano la voce nella tabella degli indirizzi hardware - ci
     * vorrebbero i permessi di amministratore - e la lampada addormentata la
     * sveglia soltanto chi quell'indirizzo ce l'ha gia'. Resta bussare, e
     * bussare abbastanza a lungo da capitare in una delle finestre in cui la
     * radio della lampada e' accesa.
     *
     * Nel registro finisce quanto e' costato: serve a distinguere « lampada
     * spenta al muro » da « lampada che dormiva », che a occhio si somigliano.
     */
    private Sessione apri() throws Exception {
        long inizio = System.currentTimeMillis();
        Exception ultima = null;
        int tentativi = 0;
        while (true) {
            tentativi++;
            try {
                Sessione s = new Sessione();
                if (tentativi > 1) {
                    android.util.Log.i(TAG, ip + " ha risposto al tentativo " + tentativi
                            + ", dopo " + (System.currentTimeMillis() - inizio) + " ms");
                }
                return s;
            } catch (Exception e) {
                ultima = e;
            }
            if (System.currentTimeMillis() - inizio >= INSISTENZA_MS) {
                android.util.Log.w(TAG, ip + " muta dopo " + tentativi + " tentativi in "
                        + (System.currentTimeMillis() - inizio) + " ms: " + ultima);
                throw ultima;
            }
            Thread.sleep(PAUSA_MS);
        }
    }

    // ---- la connessione ----

    /**
     * Una connessione aperta verso una lampada, per il tempo di un comando.
     * Tiene il numero di sequenza e, in 3.4, la chiave di sessione negoziata.
     */
    private final class Sessione {

        private final Socket socket = new Socket();
        private final InputStream in;
        private final OutputStream out;

        private int sequenza = 1;
        /** In 3.3 e' la chiave della lampada; in 3.4 diventa quella di sessione. */
        private byte[] chiaveCorrente = chiave.getBytes();

        Sessione() throws Exception {
            try {
                socket.connect(new InetSocketAddress(ip, PORTA), ATTESA_MS);
                socket.setSoTimeout(ATTESA_MS);
                socket.setTcpNoDelay(true);
                in = socket.getInputStream();
                out = socket.getOutputStream();
                if (versione >= 3.4f) negozia();
            } catch (Exception e) {
                // Una stretta di mano 3.4 fallita dopo la connect lasciava la
                // socket aperta: la lampada ne accetta una sola, e rifiutava
                // tutti i tentativi dopo, fino a che non scadeva da se'.
                chiudi();
                throw e;
            }
        }

        void chiudi() {
            try { socket.close(); } catch (Exception ignored) { }
        }

        /**
         * La stretta di mano della 3.4: ci si scambiano due numeri casuali, si
         * verifica che l'altro conosca la chiave della lampada, e la chiave con
         * cui si parlera' viene fuori dai due numeri messi in XOR e cifrati.
         * Serve a impedire che qualcuno registri un comando e lo rimandi tale
         * e quale piu' tardi.
         */
        private void negozia() throws Exception {
            byte[] mio = new byte[16];
            new SecureRandom().nextBytes(mio);

            byte[] risposta = scambia(CMD_SESS_INIZIO, cifra(chiaveCorrente, mio, true), CMD_SESS_RISPOSTA);
            if (risposta.length < 16) throw new Exception("stretta di mano rifiutata");

            byte[] suo = new byte[16];
            System.arraycopy(risposta, 0, suo, 0, 16);

            // Si risponde firmando il SUO numero: e' la prova che anche noi
            // abbiamo la chiave, e chiude lo scambio.
            manda(CMD_SESS_FINE, cifra(chiaveCorrente, hmac(chiaveCorrente, suo), true));

            byte[] misto = new byte[16];
            for (int i = 0; i < 16; i++) misto[i] = (byte) (mio[i] ^ suo[i]);
            chiaveCorrente = cifra(chiaveCorrente, misto, false);
        }

        /** Chiede alla lampada come sta. */
        JSONObject interroga() throws Exception {
            if (versione >= 3.4f) {
                manda(CMD_INTERROGA_NEW, corpoSenzaIntestazione(new JSONObject()));
            } else {
                JSONObject c = new JSONObject();
                c.put("gwId", id);
                c.put("devId", id);
                c.put("uid", id);
                c.put("t", String.valueOf(System.currentTimeMillis() / 1000));
                manda(CMD_INTERROGA, corpoSenzaIntestazione(c));
            }
            return attendiStato();
        }

        /** Manda dei valori da cambiare, e riporta lo stato che ne risulta. */
        JSONObject comanda(JSONObject dps) throws Exception {
            if (versione >= 3.4f) {
                JSONObject dati = new JSONObject();
                dati.put("dps", dps);
                JSONObject c = new JSONObject();
                c.put("protocol", 5);
                c.put("t", System.currentTimeMillis() / 1000);
                c.put("data", dati);
                manda(CMD_CONTROLLO_NEW, corpo(c));
            } else {
                JSONObject c = new JSONObject();
                c.put("devId", id);
                c.put("uid", id);
                c.put("t", String.valueOf(System.currentTimeMillis() / 1000));
                c.put("dps", dps);
                manda(CMD_CONTROLLO, corpo(c));
            }
            // La risposta a un comando dice solo "ricevuto", senza stato: quello
            // si richiede a parte, altrimenti l'interfaccia mostrerebbe ancora
            // la situazione di prima.
            try {
                ricevi();
            } catch (Exception nessunaConferma) {
                // Qualche lampada non conferma e passa direttamente allo stato:
                // non e' un motivo per far fallire il comando.
            }
            return interroga();
        }

        /**
         * Legge finche' non arriva qualcosa che contenga davvero uno stato.
         *
         * Dopo un comando le lampade mandano anche aggiornamenti che nessuno ha
         * chiesto, e prendere per buona la prima risposta che passa vuol dire
         * restare disallineati di un messaggio per tutto il resto della
         * conversazione: si legge la conferma al posto dello stato, e lo stato
         * al posto della conferma dopo.
         */
        private JSONObject attendiStato() throws Exception {
            Exception ultima = null;
            for (int i = 0; i < 4; i++) {
                try {
                    JSONObject o = json(ricevi()[1]);
                    if (estraiDps(o) != null) return o;
                } catch (Exception e) {
                    ultima = e;
                }
            }
            throw ultima != null ? ultima : new Exception("nessuno stato nella risposta");
        }

        // ---- pacchetti ----

        /** Corpo di un comando: porta l'intestazione di versione. */
        private byte[] corpo(JSONObject c) throws Exception {
            byte[] chiaro = c.toString().getBytes("UTF-8");
            if (versione >= 3.4f) {
                // Qui l'intestazione di versione va PRIMA di cifrare.
                return cifra(chiaveCorrente, unisci(intestazioneVersione(), chiaro), true);
            }
            // Qui invece DOPO: e' la differenza che fa fallire in silenzio chi
            // scrive un'implementazione sola per tutt'e due.
            return unisci(intestazioneVersione(), cifra(chiaveCorrente, chiaro, true));
        }

        /**
         * Corpo di un'interrogazione. Le richieste di stato e i passi della
         * stretta di mano viaggiano senza intestazione di versione, in tutte e
         * due le versioni: e' un elenco chiuso di comandi, non una regola.
         */
        private byte[] corpoSenzaIntestazione(JSONObject c) throws Exception {
            return cifra(chiaveCorrente, c.toString().getBytes("UTF-8"), true);
        }

        private byte[] intestazioneVersione() {
            return unisci((versione >= 3.4f ? "3.4" : "3.3").getBytes(), ZERI12);
        }

        /**
         * Manda un comando e aspetta la risposta. Se atteso e' diverso da zero
         * si pretende quel comando indietro: e' il caso della stretta di mano,
         * dove una risposta di altro tipo vuol dire chiave sbagliata.
         */
        private byte[] scambia(int comando, byte[] payload, int atteso) throws Exception {
            manda(comando, payload);
            byte[][] r = ricevi();
            int cmd = ByteBuffer.wrap(r[0]).getInt(8);
            if (atteso != 0 && cmd != atteso) {
                throw new Exception("la lampada ha risposto " + cmd + " invece di " + atteso
                        + ": chiave locale sbagliata?");
            }
            return r[1];
        }

        private void manda(int comando, byte[] payload) throws Exception {
            boolean conHmac = versione >= 3.4f;
            int coda = conHmac ? 36 : 8;

            ByteBuffer b = ByteBuffer.allocate(16 + payload.length + coda);
            b.putInt(PREFISSO);
            b.putInt(sequenza++);
            b.putInt(comando);
            b.putInt(payload.length + coda);
            b.put(payload);

            byte[] finora = new byte[16 + payload.length];
            System.arraycopy(b.array(), 0, finora, 0, finora.length);
            if (conHmac) {
                b.put(hmac(chiaveCorrente, finora));
            } else {
                CRC32 crc = new CRC32();
                crc.update(finora);
                b.putInt((int) crc.getValue());
            }
            b.putInt(SUFFISSO);

            out.write(b.array());
            out.flush();
        }

        /** Restituisce { intestazione, payload in chiaro }. */
        private byte[][] ricevi() throws Exception {
            byte[] testa = esatti(16);
            ByteBuffer b = ByteBuffer.wrap(testa);
            if (b.getInt() != PREFISSO) throw new Exception("risposta non riconosciuta");
            b.getInt();                       // numero di sequenza, non serve
            b.getInt();                       // comando, lo legge chi chiama
            int lunghezza = b.getInt();
            if (lunghezza < 12 || lunghezza > 65536) throw new Exception("risposta di lunghezza assurda");

            byte[] resto = esatti(lunghezza);
            int coda = (versione >= 3.4f) ? 36 : 8;
            // I primi quattro byte sono l'esito, che non serve; in fondo ci
            // sono firma e suffisso.
            int quanti = lunghezza - coda - 4;
            if (quanti <= 0) return new byte[][] { testa, new byte[0] };

            byte[] payload = new byte[quanti];
            System.arraycopy(resto, 4, payload, 0, quanti);
            return new byte[][] { testa, chiaro(payload) };
        }

        /**
         * Toglie l'intestazione di versione, se c'e', e decifra. L'ordine e'
         * rovesciato fra le due versioni, come in andata.
         */
        private byte[] chiaro(byte[] payload) throws Exception {
            if (payload.length == 0) return payload;
            if (versione >= 3.4f) {
                return senzaIntestazione(decifra(chiaveCorrente, payload));
            }
            return decifra(chiaveCorrente, senzaIntestazione(payload));
        }

        private byte[] senzaIntestazione(byte[] d) {
            if (d.length > 15 && d[0] == '3' && d[1] == '.'
                    && (d[2] == '1' || d[2] == '3' || d[2] == '4' || d[2] == '5')) {
                byte[] r = new byte[d.length - 15];
                System.arraycopy(d, 15, r, 0, r.length);
                return r;
            }
            return d;
        }

        private byte[] esatti(int quanti) throws Exception {
            byte[] buf = new byte[quanti];
            int letti = 0;
            while (letti < quanti) {
                int n = in.read(buf, letti, quanti - letti);
                if (n < 0) throw new Exception("la lampada ha chiuso il collegamento");
                letti += n;
            }
            return buf;
        }

        private JSONObject json(byte[] chiaro) throws Exception {
            String testo = new String(chiaro, "UTF-8").trim();
            int graffa = testo.indexOf('{');
            if (graffa < 0) throw new Exception("la lampada non ha mandato uno stato");
            return new JSONObject(testo.substring(graffa));
        }
    }

    // ---- annunci in broadcast ----

    /**
     * La chiave con cui sono cifrati gli annunci: e' la stessa per tutte le
     * lampade del mondo, quindi non protegge niente. Serve solo a togliere di
     * mezzo chi non sa che formato sia.
     */
    private static final String CHIAVE_ANNUNCI = "yGAdlopoPVldABfn";

    /**
     * Il JSON dentro un annuncio ricevuto in broadcast, o null se il pacchetto
     * non e' di una lampada Tuya. Sulle stesse porte parlano anche altre cose:
     * qui non si solleva un errore, si restituisce null e si tira avanti.
     */
    public static String annuncioInChiaro(byte[] dati, int quanti) {
        try {
            byte[] chiave = java.security.MessageDigest.getInstance("MD5")
                    .digest(CHIAVE_ANNUNCI.getBytes("UTF-8"));

            byte[] corpo = dati;
            int da = 0, lunghezza = quanti;

            // Gli annunci viaggiano nella stessa busta dei comandi. Se c'e', si
            // salta l'intestazione piu' l'esito, e si taglia la firma in coda.
            if (quanti > 24 && ByteBuffer.wrap(dati, 0, 4).getInt() == PREFISSO) {
                int dichiarata = ByteBuffer.wrap(dati, 12, 4).getInt();
                int fine = Math.min(quanti, 16 + dichiarata) - 8;
                da = 20;
                lunghezza = fine - da;
                if (lunghezza <= 0) return null;
            }

            // Qualche lampada manda l'annuncio in chiaro: se e' gia' JSON non
            // c'e' niente da decifrare.
            if (corpo[da] == '{') return new String(corpo, da, lunghezza, "UTF-8");

            byte[] cifrato = new byte[lunghezza];
            System.arraycopy(corpo, da, cifrato, 0, lunghezza);
            return new String(decifra(chiave, cifrato), "UTF-8");
        } catch (Exception e) {
            return null;
        }
    }

    // ---- crittografia ----

    private static byte[] cifra(byte[] chiave, byte[] dati, boolean conRiempimento) throws Exception {
        Cipher c = Cipher.getInstance(conRiempimento ? "AES/ECB/PKCS5Padding" : "AES/ECB/NoPadding");
        c.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(chiave, "AES"));
        return c.doFinal(dati);
    }

    private static byte[] decifra(byte[] chiave, byte[] dati) throws Exception {
        Cipher c = Cipher.getInstance("AES/ECB/PKCS5Padding");
        c.init(Cipher.DECRYPT_MODE, new SecretKeySpec(chiave, "AES"));
        return c.doFinal(dati);
    }

    private static byte[] hmac(byte[] chiave, byte[] dati) throws Exception {
        Mac m = Mac.getInstance("HmacSHA256");
        m.init(new SecretKeySpec(chiave, "HmacSHA256"));
        return m.doFinal(dati);
    }

    private static byte[] unisci(byte[] a, byte[] b) {
        byte[] r = new byte[a.length + b.length];
        System.arraycopy(a, 0, r, 0, a.length);
        System.arraycopy(b, 0, r, a.length, b.length);
        return r;
    }
}
