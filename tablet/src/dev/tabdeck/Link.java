package dev.tabdeck;

import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.io.Closeable;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.UnknownHostException;
import java.net.ServerSocket;
import java.net.Socket;

/**
 * Server del collegamento, uno solo per processo, su due trasporti in ascolto
 * insieme:
 *
 *   USB   socket astratta UNIX "tabdeck", raggiunta da "adb forward";
 *   WiFi  ServerSocket sulla porta {@link Proto#TCP_PORT}, piu' un risponditore
 *         UDP che fa trovare il tablet senza scrivere l'IP a mano.
 *
 * Sessione una alla volta: chi arriva secondo viene chiuso subito. Non e' una
 * limitazione sentita — il tablet ha un PC solo davanti — ed evita che due
 * programmi si contendano il pannello mandando frame alternati.
 *
 * Il singleton non e' un vezzo: le socket sono risorse di processo, e quando
 * TabDeck fa da Home l'activity viene ricreata piu' volte. Legandolo
 * all'activity, ogni ricreazione lasciava vivo il thread precedente e ne
 * avviava un altro che trovava il nome gia' occupato: decine di thread a
 * girare a vuoto su "Address already in use", logcat intasato e tablet lento.
 * L'activity ora si limita ad agganciarsi e sganciarsi come ascoltatore.
 *
 * Le callback dei frame video ({@link Listener#onTile} e
 * {@link Listener#onPresent}) arrivano sul thread di rete, non sul thread UI:
 * decodifica e disegno restano fuori dal main looper. Le altre arrivano gia'
 * marshallate sul thread UI.
 */
public final class Link {

    private static final String TAG = "TabDeck.Link";

    // Qui c'era System.setProperty("java.net.preferIPv4Stack", "true"), messo
    // per costringere le socket a nascere IPv4. Non serviva a niente: quella
    // proprieta' e' di OpenJDK, e libcore di Android apre l'fd sempre con
    // AF_INET6 dentro IoBridge.socket(), qualunque cosa dica la proprieta'.
    //
    // Verificato sul tablet: le socket stanno in /proc/net/tcp6 e
    // /proc/net/udp6, legate a "::", con la proprieta' impostata.
    //
    //     tcp6  :::223D (8765)  LISTEN
    //     udp6  :::223E (8766)
    //
    // Non e' grave come sembrava. Misurato con tre sonde dal PC: l'unicast
    // arriva, il broadcast di sottorete (192.168.1.255) pure — il doppio stack
    // li consegna — e solo il broadcast limitato (255.255.255.255) no. E'
    // quest'ultimo il motivo per cui il PC, in ProbeTargets(), deve mandare il
    // richiamo anche ai singoli indirizzi della sottorete: non le schede di
    // rete multiple, come si era scritto di la', ma il 255.255.255.255 che a
    // una socket IPv6 non viene consegnato.

    /** Attesa minima e massima fra due tentativi di apertura di una socket. */
    private static final long RETRY_MIN_MS = 250;
    private static final long RETRY_MAX_MS = 10000;

    public interface Listener {
        /** Thread UI. {@code transport} vale "USB", "WiFi" o "" da scollegati. */
        void onLinkState(boolean connected, String transport);
        /** Thread di rete. Il buffer e' riusato: va consumato prima di ritornare. */
        void onTile(int x, int y, int w, int h, byte[] buf, int off, int len);
        /** Thread di rete. */
        void onPresent();
        /**
         * Thread di rete. Il buffer e' riusato: va consumato prima di ritornare.
         *
         * Sta fuori da onJson perche' e' un PNG, e passare per una stringa
         * vorrebbe dire codificarlo in base64 dall'altra parte — un terzo di
         * byte in piu' per niente.
         */
        void onIcon(String nome, byte[] buf, int off, int len);
        /** Thread di rete, buffer riusato: il pacchetto di un'estensione, da salvare prima di ritornare. */
        void onCodiceEstensione(String id, byte[] buf, int off, int len);
        /** Thread di rete, buffer riusato: una foto del salvaschermo, da salvare prima di ritornare. */
        void onFotoSalvaschermo(String nome, byte[] buf, int off, int len);
        /** Thread UI. */
        void onJson(int type, String json);
    }

    private static Link instance;

    /** Unica istanza del processo. */
    public static synchronized Link get() {
        if (instance == null) instance = new Link();
        return instance;
    }

    private final Handler ui = new Handler(Looper.getMainLooper());
    private final Object writeLock = new Object();

    /** Sostituito a ogni ricreazione dell'activity, azzerato quando sparisce. */
    private volatile Listener listener;

    private volatile boolean running;
    private volatile String transport = "";

    private Closeable client;
    private DataOutputStream out;
    /**
     * Numero della sessione aperta. Serve a far chiudere solo la propria roba a
     * chi termina: senza, la sessione vecchia — svegliata dalla socket chiusa
     * sotto di lei — azzerava lo stato di quella nuova appena subentrata.
     */
    private long sessionId;

    private static final byte[] EMPTY = new byte[0];

    private Link() {}

    public void setListener(Listener l) {
        listener = l;
        // Chi si aggancia a collegamento gia' aperto deve sapere subito com'e'
        // messo, altrimenti resterebbe con l'indicatore spento fino al primo
        // frame.
        if (l != null) postState(isConnected());
    }

    /** Idempotente: chiamarla piu' volte non avvia un secondo giro di thread. */
    public synchronized void start() {
        if (running) return;
        running = true;
        spawn("tabdeck-usb", new Runnable() {
            @Override public void run() { acceptLocal(); }
        });
        spawn("tabdeck-wifi", new Runnable() {
            @Override public void run() { acceptTcp(); }
        });
        spawn("tabdeck-beacon", new Runnable() {
            @Override public void run() { answerDiscovery(); }
        });
    }

    private void spawn(String name, Runnable body) {
        Thread t = new Thread(body, name);
        t.setPriority(Thread.NORM_PRIORITY + 1);
        t.setDaemon(true);
        t.start();
    }

    public boolean isConnected() {
        synchronized (writeLock) {
            return out != null;
        }
    }

    /** "USB", "WiFi", oppure stringa vuota se non c'e' nessuno collegato. */
    public String transport() {
        return transport;
    }

    // ---- accettazione ----

    /** Trasporto USB: il PC arriva qui attraverso "adb forward". */
    private void acceptLocal() {
        LocalServerSocket server = null;
        long retry = RETRY_MIN_MS;
        String lastFailure = null;

        while (running) {
            try {
                if (server == null) {
                    server = new LocalServerSocket(Proto.SOCKET_NAME);
                    Log.i(TAG, "in ascolto su localabstract:" + Proto.SOCKET_NAME);
                }
                retry = RETRY_MIN_MS;
                lastFailure = null;
                LocalSocket s = server.accept();
                s.setSoTimeout(0);
                session(s.getInputStream(), s.getOutputStream(), s, "USB");
            } catch (IOException e) {
                if (!running) break;
                lastFailure = complain(lastFailure, "USB", e);
                server = closeLocalServer(server);
                sleep(retry);
                retry = Math.min(retry * 2, RETRY_MAX_MS);
            }
        }
        closeLocalServer(server);
    }

    /** Trasporto WiFi: il PC si collega direttamente all'IP del tablet. */
    private void acceptTcp() {
        ServerSocket server = null;
        long retry = RETRY_MIN_MS;
        String lastFailure = null;

        while (running) {
            try {
                if (server == null) {
                    server = new ServerSocket();
                    server.setReuseAddress(true);
                    server.bind(new InetSocketAddress(anyIPv4(), Proto.TCP_PORT));
                    Log.i(TAG, "in ascolto su tcp:" + Proto.TCP_PORT);
                }
                retry = RETRY_MIN_MS;
                lastFailure = null;
                Socket s = server.accept();
                // Le tile sono piccole e vanno consegnate subito: accodarle per
                // riempire un segmento aggiungerebbe soltanto ritardo.
                s.setTcpNoDelay(true);
                s.setSoTimeout(0);
                session(s.getInputStream(), s.getOutputStream(), s, "WiFi");
            } catch (IOException e) {
                if (!running) break;
                lastFailure = complain(lastFailure, "WiFi", e);
                server = closeTcpServer(server);
                sleep(retry);
                retry = Math.min(retry * 2, RETRY_MAX_MS);
            }
        }
        closeTcpServer(server);
    }

    /**
     * Risponde a chi manda {@link Proto#DISCOVERY_PROBE} in broadcast. Serve
     * solo a togliere di mezzo la ricerca dell'IP: senza, a ogni cambio di
     * indirizzo bisognerebbe leggerlo dalle impostazioni del tablet e
     * riscriverlo sul PC.
     */
    private void answerDiscovery() {
        DatagramSocket socket = null;
        byte[] in = new byte[64];
        byte[] reply = (Proto.DISCOVERY_REPLY + " " + Proto.TCP_PORT + " "
                + android.os.Build.MODEL).getBytes();

        while (running) {
            try {
                if (socket == null) {
                    socket = new DatagramSocket(null);
                    socket.setReuseAddress(true);
                    socket.setBroadcast(true);
                    socket.bind(new InetSocketAddress(anyIPv4(), Proto.DISCOVERY_PORT));
                    Log.i(TAG, "richiamo in ascolto su udp:" + Proto.DISCOVERY_PORT);
                }
                DatagramPacket p = new DatagramPacket(in, in.length);
                socket.receive(p);
                String probe = new String(in, 0, p.getLength()).trim();
                if (!Proto.DISCOVERY_PROBE.equals(probe)) continue;
                socket.send(new DatagramPacket(reply, reply.length, p.getAddress(), p.getPort()));
            } catch (IOException e) {
                if (!running) break;
                // Silenzioso, questo giro era: quando la socket del richiamo
                // non si apriva, l'unico sintomo era il PC che non trovava piu'
                // il tablet, senza una riga da nessuna parte.
                Log.w(TAG, "richiamo non in ascolto, riprovo: " + e.getMessage());
                if (socket != null) socket.close();
                socket = null;
                sleep(RETRY_MAX_MS);
            }
        }
        if (socket != null) socket.close();
    }

    // ---- annuncio al PC ----

    /** Esito dell'annuncio, consegnato sul thread UI. */
    public interface Annuncio {
        /** {@code destinazioni} quante ne hanno accettato il datagramma. */
        void mandato(int destinazioni, String errore);
    }

    /**
     * Dice al PC « sono qui », in broadcast, una volta sola.
     *
     * E' la ricerca al contrario, e serve perche' quella dritta ha un buco che
     * nessuna quantita' di tentativi dal lato PC puo' chiudere: un tablet fermo
     * non risponde all'ARP, e senza ARP il PC non arriva nemmeno a bussare alla
     * porta 8765. Chi preme questo pulsante invece ha in mano un tablet sveglio
     * per definizione, e un tablet sveglio trasmette: il datagramma esce, il PC
     * impara l'indirizzo e apre lui il collegamento come ha sempre fatto.
     *
     * Nessun ruolo cambia: il tablet resta il server, il protocollo e' lo
     * stesso. Cambia solo chi parla per primo.
     *
     * Una volta sola e non a ripetizione: un annuncio ogni tot secondi sarebbe
     * un servizio in ascolto acceso per sempre, e la corrente la si e' appena
     * finita di contare.
     */
    public void annuncia(final Annuncio esito) {
        spawn("tabdeck-annuncio", new Runnable() {
            @Override public void run() {
                int mandati = 0;
                String errore = null;
                DatagramSocket s = null;
                try {
                    s = new DatagramSocket();
                    s.setBroadcast(true);
                    byte[] msg = (Proto.ANNOUNCE + " " + Proto.TCP_PORT + " "
                            + android.os.Build.MODEL).getBytes();
                    for (InetAddress dest : destinazioniBroadcast()) {
                        try {
                            s.send(new DatagramPacket(msg, msg.length, dest, Proto.ANNOUNCE_PORT));
                            mandati++;
                        } catch (IOException e) {
                            // Una scheda giu', o un indirizzo che il kernel non
                            // accetta: si prova la prossima. Basta che ne passi
                            // una.
                            Log.w(TAG, "annuncio non spedito a " + dest + ": " + e.getMessage());
                        }
                    }
                } catch (IOException e) {
                    errore = e.getMessage();
                } finally {
                    if (s != null) s.close();
                }

                final int n = mandati;
                final String err = errore;
                ui.post(new Runnable() {
                    @Override public void run() { esito.mandato(n, err); }
                });
            }
        });
    }

    /**
     * Dove mandare l'annuncio: il broadcast di ogni rete a cui il tablet e'
     * attaccato, piu' il 255.255.255.255 in coda.
     *
     * L'ordine conta. Il broadcast di sottorete e' quello che funziona davvero
     * — misurato in tutte e due le direzioni — mentre il 255.255.255.255 e' un
     * ripiego per il caso in cui la maschera non si riesca a leggere. Sul PC
     * viene consegnato lo stesso, perche' li' la socket in ascolto e' IPv4
     * vera; e' sul tablet che non arriva, per via del doppio stack.
     */
    private static java.util.List<InetAddress> destinazioniBroadcast() {
        java.util.List<InetAddress> out = new java.util.ArrayList<InetAddress>();
        try {
            java.util.Enumeration<java.net.NetworkInterface> nics =
                    java.net.NetworkInterface.getNetworkInterfaces();
            while (nics != null && nics.hasMoreElements()) {
                java.net.NetworkInterface nic = nics.nextElement();
                if (!nic.isUp() || nic.isLoopback()) continue;
                for (java.net.InterfaceAddress ia : nic.getInterfaceAddresses()) {
                    InetAddress b = ia.getBroadcast();
                    if (b != null && !out.contains(b)) out.add(b);
                }
            }
        } catch (java.net.SocketException e) {
            Log.w(TAG, "interfacce non leggibili: " + e.getMessage());
        }
        try {
            out.add(InetAddress.getByName("255.255.255.255"));
        } catch (UnknownHostException ignored) {
            // Non succede con un indirizzo gia' in cifre.
        }
        return out;
    }

    // ---- sessione ----

    /**
     * Serve un client fino a che non si scollega.
     *
     * Chi arriva subentra a chi c'era: quando si passa dal cavo alla rete la
     * sessione di prima e' quasi sempre gia' morta, ma il tablet non se n'e'
     * accorto — la socket inoltrata da adb resta aperta dal suo lato anche
     * dopo che il PC l'ha chiusa. Rifiutare il nuovo arrivato voleva dire non
     * potersi piu' collegare in WiFi finche' non si riavviava l'app, e per di
     * piu' in silenzio: nel log non compariva niente.
     */
    private void session(InputStream in, OutputStream os, Closeable sock, String via) {
        Closeable previous;
        long mySession;
        synchronized (writeLock) {
            previous = client;
            client = sock;
            out = new DataOutputStream(os);
            transport = via;
            mySession = ++sessionId;
        }

        // Fuori dal lock: chiudendo la socket vecchia il suo thread si sveglia
        // e cerca a sua volta il lock per fare pulizia.
        if (previous != null) {
            Log.i(TAG, "subentra un collegamento " + via + ", chiudo quello di prima");
            closeQuietly(previous);
        }

        Log.i(TAG, "PC collegato via " + via);
        postState(true);

        byte[] buf = new byte[64 * 1024];
        try {
            while (running) {
                int type = in.read();
                if (type < 0) break;
                int len = readInt(in);
                if (len < 0 || len > 8 * 1024 * 1024) {
                    throw new IOException("lunghezza frame assurda: " + len);
                }
                if (buf.length < len) buf = new byte[Math.max(len, buf.length * 2)];
                readFully(in, buf, 0, len);
                dispatch(type, buf, len);
            }
        } catch (IOException e) {
            Log.i(TAG, "PC scollegato: " + e.getMessage());
        }

        boolean stillMine;
        synchronized (writeLock) {
            // Se nel frattempo e' subentrato qualcun altro, non e' roba nostra.
            stillMine = mySession == sessionId;
            if (stillMine) {
                closeLocked();
                transport = "";
            }
        }
        if (stillMine) postState(false);
    }

    private void dispatch(int type, byte[] buf, int len) {
        Listener l = listener;
        if (l == null) return;   // activity sganciata: si scarta senza disturbare

        switch (type) {
            case Proto.TILE: {
                if (len < 8) return;
                int x = readU16(buf, 0);
                int y = readU16(buf, 2);
                int w = readU16(buf, 4);
                int h = readU16(buf, 6);
                l.onTile(x, y, w, h, buf, 8, len - 8);
                break;
            }
            case Proto.PRESENT:
                l.onPresent();
                break;
            case Proto.ICON: {
                if (len < 2) return;
                int nameLen = readU16(buf, 0);
                if (nameLen < 0 || 2 + nameLen > len) return;
                String nome = new String(buf, 2, nameLen);
                l.onIcon(nome, buf, 2 + nameLen, len - 2 - nameLen);
                break;
            }
            case Proto.SALVASCHERMO_FOTO: {
                if (len < 2) return;
                int nomeLen = readU16(buf, 0);
                if (nomeLen < 0 || 2 + nomeLen > len) return;
                l.onFotoSalvaschermo(new String(buf, 2, nomeLen), buf, 2 + nomeLen, len - 2 - nomeLen);
                break;
            }
            case Proto.PLUGIN_CODICE: {
                // Binario come l'icona, e per la stessa ragione: centinaia di KB
                // di zip non devono passare da una stringa.
                if (len < 1) return;
                int idLen = buf[0] & 0xFF;
                if (1 + idLen > len) return;
                l.onCodiceEstensione(new String(buf, 1, idLen), buf, 1 + idLen, len - 1 - idLen);
                break;
            }
            default: {
                final int t = type;
                final String json = new String(buf, 0, len);
                ui.post(new Runnable() {
                    @Override public void run() {
                        Listener cur = listener;
                        if (cur != null) cur.onJson(t, json);
                    }
                });
                break;
            }
        }
    }

    // ---- invio ----

    public void sendJson(int type, String json) {
        byte[] payload = json.getBytes();
        send(type, payload, 0, payload.length);
    }

    /** Un frame binario di plugin: un tratto, un blocco di voce. */
    public void sendBytes(int type, byte[] payload) {
        send(type, payload, 0, payload.length);
    }

    /** Il tablet manda solo l'identificativo: l'azione vive tutta sul PC. */
    public void sendPress(String id) {
        sendJson(Proto.PRESS, "{\"id\":\"" + id.replace("\"", "") + "\"}");
    }

    public void sendTouch(int action, int x, int y) {
        byte[] p = new byte[5];
        p[0] = (byte) action;
        p[1] = (byte) (x >> 8); p[2] = (byte) x;
        p[3] = (byte) (y >> 8); p[4] = (byte) y;
        send(Proto.TOUCH, p, 0, p.length);
    }

    /**
     * Conferma al PC che il frame e' stato disegnato. E' il freno del flusso:
     * finche' non arriva, il PC non manda il frame successivo.
     */
    public void sendAck() {
        send(Proto.ACK, EMPTY, 0, 0);
    }

    public void sendWheel(int delta) {
        byte[] p = new byte[2];
        p[0] = (byte) (delta >> 8); p[1] = (byte) delta;
        send(Proto.WHEEL, p, 0, p.length);
    }

    private void send(int type, byte[] payload, int off, int len) {
        synchronized (writeLock) {
            if (out == null) return;
            try {
                out.write(type);
                out.writeInt(len);
                out.write(payload, off, len);
                out.flush();
            } catch (IOException e) {
                Log.w(TAG, "invio fallito: " + e.getMessage());
                closeLocked();
            }
        }
    }

    // ---- utilita' ----

    private void postState(final boolean connected) {
        final String via = transport;
        ui.post(new Runnable() {
            @Override public void run() {
                Listener cur = listener;
                if (cur != null) cur.onLinkState(connected, via);
            }
        });
    }

    /** Un solo log per causa: nel ciclo di ritentativi da solo intasa logcat. */
    private static String complain(String lastFailure, String via, IOException e) {
        String failure = via + ": " + e.getMessage();
        if (!failure.equals(lastFailure)) {
            Log.w(TAG, "socket non aperta, riprovo — " + failure);
        }
        return failure;
    }

    private void closeLocked() {
        closeQuietly(client);
        client = null;
        out = null;
    }

    private static void closeQuietly(Closeable c) {
        try {
            if (c != null) c.close();
        } catch (IOException ignored) {
        }
    }

    private static LocalServerSocket closeLocalServer(LocalServerSocket s) {
        try {
            if (s != null) s.close();
        } catch (IOException ignored) {
        }
        return null;
    }

    private static ServerSocket closeTcpServer(ServerSocket s) {
        try {
            if (s != null) s.close();
        } catch (IOException ignored) {
        }
        return null;
    }

    private static void readFully(InputStream in, byte[] b, int off, int len) throws IOException {
        int read = 0;
        while (read < len) {
            int n = in.read(b, off + read, len - read);
            if (n < 0) throw new IOException("stream chiuso a meta' frame");
            read += n;
        }
    }

    private static int readInt(InputStream in) throws IOException {
        int a = in.read(), b = in.read(), c = in.read(), d = in.read();
        if ((a | b | c | d) < 0) throw new IOException("stream chiuso nell'intestazione");
        return (a << 24) | (b << 16) | (c << 8) | d;
    }

    private static int readU16(byte[] b, int off) {
        return ((b[off] & 0xFF) << 8) | (b[off + 1] & 0xFF);
    }

    /**
     * Indirizzo jolly IPv4.
     *
     * Lasciando scegliere a Java, la socket si lega a "::" e finisce in
     * /proc/net/tcp6: le connessioni IPv4 normali passano lo stesso, ma i
     * datagrammi di broadcast IPv4 — che sono proprio quelli con cui il PC
     * cerca il tablet — non vengono consegnati. Legandosi a 0.0.0.0 il
     * problema non esiste, e non si perde niente: il PC parla IPv4.
     */
    private static InetAddress anyIPv4() {
        try {
            return InetAddress.getByName("0.0.0.0");
        } catch (UnknownHostException e) {
            return null;   // il bind cadra' sul jolly di sistema
        }
    }

    private static void sleep(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException ignored) {
        }
    }
}
