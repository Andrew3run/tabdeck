package dev.tabdeck;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.net.wifi.WifiManager;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.RandomAccessFile;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.nio.ByteBuffer;
import java.nio.channels.DatagramChannel;
import java.nio.channels.SelectionKey;
import java.nio.channels.Selector;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Le luci di casa conosciute dal tablet: chi sono, dove stanno, come stanno.
 *
 * L'elenco arriva dal PC una volta sola (frame LUCI) e viene salvato qui
 * dentro. Da quel momento il tablet e' autonomo: la sezione Casa funziona a
 * cavo staccato, a PC spento e senza internet, purche' il tablet sia sulla
 * stessa rete delle lampade.
 *
 * Nessun timer interroga le lampade tutto il giorno: le Tuya spengono la radio
 * per risparmiare, e svegliarle a ciclo toglie loro proprio quello. Lo stato si
 * rilegge in quattro momenti — quando si apre la sezione e per tutto il tempo
 * che resta aperta, dopo ogni comando, quando una lampada si annuncia dopo
 * essere stata muta o spenta, e quando il Wi-Fi torna.
 *
 * Il terzo e' quello che mancava. Tre giorni spente al muro, e le lampade
 * tornavano con un altro indirizzo: il tablet le cercava a quello vecchio per
 * mezzo minuto, l'indirizzo nuovo arrivava a lettura gia' partita e veniva
 * perso, e ogni tocco nel frattempo cadeva. Servivano tre « Aggiorna » di fila.
 * Adesso gli annunci si ascoltano sempre, mentre l'app e' davanti — vedi
 * {@link #avvia()} — e un indirizzo nuovo arriva anche alla lettura in corso.
 */
public final class Luci {

    private static final String TAG = "TabDeck.Luci";

    /** Il file dove resta l'elenco fra un avvio e l'altro. */
    private static final String FILE = "luci.json";

    /**
     * Le lampade si annunciano in chiaro su queste due porte: la 6666 e' delle
     * vecchie, la 6667 delle 3.3 in poi. Serve solo a ritrovare l'indirizzo di
     * una lampada che il router ha spostato: l'identificativo non cambia mai.
     */
    private static final int[] PORTE_ANNUNCI = { 6666, 6667 };

    /** Quanto si sta in ascolto degli annunci. Le lampade parlano ogni ~5 s. */
    private static final int ASCOLTO_MS = 6000;

    public interface Ascolto {
        /** Thread UI: una lampada ha cambiato stato, ridisegna la sua riga. */
        void luceCambiata(Lampada l);
        /** Thread UI: l'elenco e' diverso, rifai la lista. */
        void elencoCambiato();
    }

    private final Context context;
    private final Handler ui = new Handler(Looper.getMainLooper());

    /**
     * Quattro thread: le lampade si interrogano in parallelo, cosi' una spenta
     * al muro — che ora, insistendo, costa fino a mezzo minuto di attesa — non
     * trattiene le altre. Quattro e non tre perche' le lampade di casa sono
     * tre: con la corsia in piu' nessuna aspetta il turno di un'altra.
     */
    private final ExecutorService lavoro = Executors.newFixedThreadPool(4);

    private final List<Lampada> elenco = new ArrayList<Lampada>();
    private final List<Routine> routine = new ArrayList<Routine>();
    private Ascolto ascolto;
    private volatile boolean cercando;

    public Luci(Context context) {
        this.context = context;
        carica();
    }

    public void setAscolto(Ascolto a) {
        ascolto = a;
    }

    public List<Lampada> elenco() {
        return elenco;
    }

    public List<Routine> routine() {
        return routine;
    }

    public boolean vuoto() {
        return elenco.isEmpty();
    }

    // ---- elenco ----

    /**
     * L'elenco mandato dal PC. Si sovrascrive e si salva: il PC e' l'unico
     * posto dove si scrivono chiavi e nomi, il tablet li riceve e basta.
     */
    public void configura(String json) {
        List<Lampada> nuove = leggi(json);
        List<Routine> nuoveRoutine = leggiRoutine(json);
        elenco.clear();
        elenco.addAll(nuove);
        routine.clear();
        routine.addAll(nuoveRoutine);
        salva(json);
        if (ascolto != null) ascolto.elencoCambiato();
        aggiorna();
        // Gli indirizzi appena arrivati sono quelli di luci.json sul PC, che
        // puo' averli scritti prima che il router spostasse le lampade.
        scopri();
    }

    private static List<Lampada> leggi(String json) {
        List<Lampada> fuori = new ArrayList<Lampada>();
        try {
            JSONArray a = new JSONObject(json).optJSONArray("luci");
            if (a == null) return fuori;
            for (int i = 0; i < a.length(); i++) {
                JSONObject o = a.optJSONObject(i);
                if (o == null) continue;
                Lampada l = Lampada.da(o);
                // Una lampada senza chiave non risponderebbe a nessuno:
                // mostrarla vorrebbe dire mettere in lista un pulsante che
                // non puo' funzionare.
                if (l.completa()) fuori.add(l);
            }
        } catch (Exception e) {
            Log.w(TAG, "elenco luci illeggibile: " + e.getMessage());
        }
        return fuori;
    }

    private static List<Routine> leggiRoutine(String json) {
        List<Routine> fuori = new ArrayList<Routine>();
        try {
            JSONArray a = new JSONObject(json).optJSONArray("routine");
            if (a == null) return fuori;
            for (int i = 0; i < a.length(); i++) {
                JSONObject o = a.optJSONObject(i);
                if (o == null) continue;
                Routine r = Routine.da(o);
                // Una routine senza passi e' un pulsante che non fa niente.
                if (!r.passi.isEmpty()) fuori.add(r);
            }
        } catch (Exception e) {
            Log.w(TAG, "routine illeggibili: " + e.getMessage());
        }
        return fuori;
    }

    private void carica() {
        File f = new File(context.getFilesDir(), FILE);
        if (!f.exists()) return;
        RandomAccessFile r = null;
        try {
            r = new RandomAccessFile(f, "r");
            byte[] buf = new byte[(int) r.length()];
            r.readFully(buf);
            String json = new String(buf, "UTF-8");
            elenco.addAll(leggi(json));
            routine.addAll(leggiRoutine(json));
        } catch (Exception e) {
            Log.w(TAG, "non riesco a rileggere " + FILE + ": " + e.getMessage());
        } finally {
            if (r != null) try { r.close(); } catch (Exception ignored) { }
        }
    }

    private void salva(String json) {
        FileOutputStream out = null;
        try {
            out = context.openFileOutput(FILE, Context.MODE_PRIVATE);
            out.write(json.getBytes("UTF-8"));
        } catch (Exception e) {
            Log.w(TAG, "non riesco a salvare " + FILE + ": " + e.getMessage());
        } finally {
            if (out != null) try { out.close(); } catch (Exception ignored) { }
        }
    }

    /** Rimette per iscritto quello che c'e' adesso, indirizzi aggiornati compresi. */
    private void risalva() {
        try {
            JSONArray a = new JSONArray();
            for (Lampada l : elenco) a.put(l.json());
            JSONArray r = new JSONArray();
            for (Routine x : routine) r.put(x.json());
            JSONObject o = new JSONObject();
            o.put("luci", a);
            o.put("routine", r);
            salva(o.toString());
        } catch (Exception e) {
            Log.w(TAG, "non riesco a risalvare: " + e.getMessage());
        }
    }

    // ---- comandi ----

    public void aggiorna() {
        for (Lampada l : elenco) accoda(l, Azione.LEGGI, 0);
    }

    public void inverti(Lampada l) {
        accoda(l, Azione.INVERTI, 0);
    }

    public void accendi(Lampada l, boolean on) {
        accoda(l, on ? Azione.ACCENDI : Azione.SPEGNI, 0);
    }

    public void luminosita(Lampada l, int percento) {
        accoda(l, Azione.LUMINOSITA, percento);
    }

    public void colore(Lampada l, int rgb) {
        accoda(l, Azione.COLORE, rgb);
    }

    public void bianco(Lampada l) {
        accoda(l, Azione.BIANCO, 0);
    }

    public void temperatura(Lampada l, int percento) {
        accoda(l, Azione.TEMPERATURA, percento);
    }

    public void tutteSpente() {
        for (Lampada l : elenco) accoda(l, Azione.SPEGNI, 0);
    }

    private enum Azione { LEGGI, ACCENDI, SPEGNI, INVERTI, LUMINOSITA, COLORE, BIANCO, TEMPERATURA }

    /**
     * Esegue una routine: i passi in ordine, uno alla volta.
     *
     * Gira su un thread solo perche' due comandi verso la stessa lampada si
     * pesterebbero i piedi - una lampada Tuya accetta una connessione per
     * volta - e perche' « spegni tutto poi accendi il comodino » deve succedere
     * in quest'ordine, non a caso.
     */
    public void esegui(final Routine r) {
        if (r.inCorso) return;
        r.inCorso = true;
        if (ascolto != null) ascolto.elencoCambiato();

        lavoro.execute(new Runnable() {
            @Override public void run() {
                for (Routine.Passo passo : r.passi) {
                    if (Routine.ATTESA.equals(passo.azione)) {
                        // « Spegni la plafoniera, aspetta un minuto, spegni il
                        // comodino »: il tempo di arrivare a letto. Occupa una
                        // corsia del pool per tutta l'attesa, e le corsie sono
                        // quattro per tre lampade: le altre restano libere.
                        try {
                            Thread.sleep(Routine.secondiAttesa(passo.valore) * 1000L);
                        } catch (InterruptedException chiusa) {
                            break;                     // chiudi(): l'app se ne va
                        }
                        continue;
                    }
                    for (Lampada l : bersagli(passo.luce)) {
                        final Tuya.Stato s = applica(l, passo);
                        final Lampada quale = l;
                        ui.post(new Runnable() {
                            @Override public void run() {
                                quale.stato = s;
                                avvisa(quale);
                            }
                        });
                    }
                }
                ui.post(new Runnable() {
                    @Override public void run() {
                        r.inCorso = false;
                        if (ascolto != null) ascolto.elencoCambiato();
                    }
                });
            }
        });
    }

    /** Le lampade toccate da un passo: una sola, oppure tutte se non e' detto. */
    private List<Lampada> bersagli(String id) {
        if (id == null || id.length() == 0) return new ArrayList<Lampada>(elenco);
        List<Lampada> una = new ArrayList<Lampada>();
        for (Lampada l : elenco) if (l.id.equals(id)) una.add(l);
        return una;
    }

    private static Tuya.Stato applica(Lampada l, Routine.Passo passo) {
        Tuya t = l.canale();
        String a = passo.azione;
        if ("on".equals(a)) return t.accendi(true);
        if ("off".equals(a)) return t.accendi(false);
        if ("inverti".equals(a)) return t.inverti();
        if ("bianco".equals(a)) return t.bianco();
        if ("luce".equals(a)) return t.luminosita(numero(passo.valore, 100));
        if ("bianchezza".equals(a)) return t.temperatura(numero(passo.valore, 50));
        if ("colore".equals(a)) return t.colore(Tinta.leggi(passo.valore, 0xFFFFFFFF) & 0xFFFFFF);
        return t.leggi();
    }

    private static int numero(String testo, int riserva) {
        try {
            return Integer.parseInt(testo.trim());
        } catch (Exception e) {
            return riserva;
        }
    }

    /**
     * Un comando su una lampada, fuori dal thread dell'interfaccia. Su Android
     * la rete sul thread principale e' vietata, e comunque quattro secondi di
     * attesa su una lampada spenta congelerebbero tutto il resto.
     */
    private void accoda(final Lampada l, final Azione azione, final int valore) {
        if (l.inCorso) {
            // Quello che si chiede mentre la lampada e' occupata non si butta
            // piu': si fa appena ha finito. Prima cadeva, e una lettura partita
            // verso l'indirizzo vecchio teneva la lampada occupata mezzo minuto
            // — ogni tocco in quel mezzo minuto era un tocco perso.
            //
            // Lo stesso comando due volte resta il doppio tocco di sempre, e il
            // primo basta. Una lettura dietro una lettura invece si tiene, ma si
            // rifa' solo se la prima e' andata male: e' il caso dell'indirizzo
            // cambiato a lettura partita.
            if (azione.ordinal() != l.inVolo() || azione == Azione.LEGGI) {
                l.dopo = azione.ordinal();
                l.dopoValore = valore;
            }
            return;
        }
        l.inCorso = true;
        l.inVolo = azione.ordinal();
        avvisa(l);
        lavoro.execute(new Runnable() {
            @Override public void run() {
                final Tuya.Stato s;
                Tuya t = l.canale();
                switch (azione) {
                    case ACCENDI:     s = t.accendi(true); break;
                    case SPEGNI:      s = t.accendi(false); break;
                    case INVERTI:     s = t.inverti(); break;
                    case LUMINOSITA:  s = t.luminosita(valore); break;
                    case COLORE:      s = t.colore(valore); break;
                    case BIANCO:      s = t.bianco(); break;
                    case TEMPERATURA: s = t.temperatura(valore); break;
                    default:          s = t.leggi(); break;
                }
                ui.post(new Runnable() {
                    @Override public void run() {
                        l.inCorso = false;
                        l.inVolo = -1;
                        l.stato = s;
                        if (s.raggiunta) l.rincorseAVuoto = 0;
                        avvisa(l);

                        int dopo = l.dopo;
                        l.dopo = -1;
                        if (dopo >= 0) {
                            Azione prossima = Azione.values()[dopo];
                            if (prossima != Azione.LEGGI || !s.raggiunta) {
                                accoda(l, prossima, l.dopoValore);
                            }
                        }

                        // Una lampada che non risponde puo' essersi solo
                        // spostata. Con l'orecchio aperto sugli annunci
                        // l'indirizzo nuovo arriva da se', e scopri() non fa
                        // niente; senza, si ascolta per qualche secondo.
                        if (!s.raggiunta) scopri();
                    }
                });
            }
        });
    }

    private void avvisa(Lampada l) {
        if (ascolto != null) ascolto.luceCambiata(l);
    }

    // ---- da vicino ----

    /** Mentre la sezione Casa e' aperta, lo stato si rilegge con questo passo. */
    private static final long OGNI_MS = 5000;

    private boolean daVicino;

    private final Runnable giro = new Runnable() {
        @Override public void run() {
            if (!daVicino) return;
            // Chi e' ancora occupato col giro prima non si accoda: una lampada
            // spenta al muro si fa aspettare mezzo minuto, e dietro di lei si
            // accumulerebbe una fila di letture.
            for (Lampada l : elenco) {
                if (!l.inCorso) accoda(l, Azione.LEGGI, 0);
            }
            ui.postDelayed(this, OGNI_MS);
        }
    };

    /**
     * Rilegge le lampade ogni cinque secondi, ma solo mentre qualcuno le sta
     * guardando.
     *
     * Svegliarle tutto il giorno le consuma; per i venti secondi in cui la
     * sezione e' aperta invece e' esattamente il momento in cui lo stato deve
     * essere vero — la lampada accesa da un interruttore o dal telefono non
     * deve restare « spenta » sul tablet. Preso dal tablet di casa, dove
     * funziona cosi'.
     */
    public void seguiDaVicino(boolean si) {
        if (daVicino == si) return;
        daVicino = si;
        ui.removeCallbacks(giro);
        if (si) ui.postDelayed(giro, OGNI_MS);
    }

    // ---- mentre l'app e' davanti ----

    /**
     * Otto annunci persi di fila non sono un pacchetto caduto: sono una lampada
     * che non c'era. E una lampada che torna dopo essere stata spenta al muro
     * si riaccende da sola, quindi lo stato ricordato e' diventato falso.
     */
    private static final long SILENZIO_MS = 40000;

    /** La prima rincorsa dopo un annuncio, e il tetto a cui arriva raddoppiando. */
    private static final long RINCORSA_MS = 15000, RINCORSA_MAX_MS = 5 * 60000L;

    private volatile boolean inAscolto;
    private volatile Selector selettore;
    private WifiManager.MulticastLock lucchetto;
    private BroadcastReceiver rete;
    private boolean reteSu = true;

    /**
     * Da onResume: apre l'orecchio sugli annunci e si fa dire quando il Wi-Fi
     * torna. TabDeck e' la Home, quindi e' davanti quasi sempre; a pannello
     * spento arriva onPause e si chiude tutto.
     *
     * L'orecchio e' un thread fermo in {@code select()}: nessun risveglio
     * quando nessuno parla. Il lucchetto multicast serve perche' il Wi-Fi dei
     * Samsung puo' scartare i broadcast per risparmiare, e allora l'orecchio
     * resterebbe aperto senza sentire niente. Si tiene solo a pannello acceso.
     */
    public void avvia() {
        if (elenco.isEmpty()) return;
        prendiLucchetto();
        ascoltaGliAnnunci();
        ascoltaLaRete();
    }

    /** Da onPause. */
    public void ferma() {
        seguiDaVicino(false);
        smettiDiAscoltareGliAnnunci();
        smettiDiAscoltareLaRete();
        lasciaLucchetto();
    }

    private void prendiLucchetto() {
        if (lucchetto != null) return;
        try {
            WifiManager wm = (WifiManager) context.getApplicationContext()
                    .getSystemService(Context.WIFI_SERVICE);
            if (wm == null) return;
            WifiManager.MulticastLock l = wm.createMulticastLock("TabDeck-luci");
            l.setReferenceCounted(false);
            l.acquire();
            lucchetto = l;
        } catch (Exception e) {
            // Senza, i broadcast arrivano lo stesso sulla maggior parte dei
            // driver: peggio, non rotto.
            Log.w(TAG, "niente lucchetto multicast: " + e.getMessage());
        }
    }

    private void lasciaLucchetto() {
        if (lucchetto == null) return;
        try {
            lucchetto.release();
        } catch (Exception ignored) {
        }
        lucchetto = null;
    }

    /**
     * Ascolta gli annunci delle lampade finche' l'app e' davanti, non solo per
     * i dodici secondi di {@link #scopri()}. Un {@link Selector} e due canali:
     * un thread solo, e chiuderlo e' una {@code wakeup}.
     */
    private void ascoltaGliAnnunci() {
        if (inAscolto) return;
        final Selector sel;
        final List<DatagramChannel> canali = new ArrayList<DatagramChannel>(PORTE_ANNUNCI.length);
        try {
            sel = Selector.open();
        } catch (Exception e) {
            Log.w(TAG, "niente orecchio sugli annunci: " + e.getMessage());
            return;
        }
        for (int porta : PORTE_ANNUNCI) {
            DatagramChannel ch = null;
            try {
                ch = DatagramChannel.open();
                ch.socket().setReuseAddress(true);
                ch.socket().setBroadcast(true);
                ch.socket().bind(new InetSocketAddress(porta));
                ch.configureBlocking(false);
                ch.register(sel, SelectionKey.OP_READ);
                canali.add(ch);
            } catch (Exception e) {
                Log.w(TAG, "non ascolto la " + porta + ": " + e.getMessage());
                if (ch != null) try { ch.close(); } catch (Exception ignored) { }
            }
        }
        if (canali.isEmpty()) {
            try { sel.close(); } catch (Exception ignored) { }
            return;
        }
        selettore = sel;
        inAscolto = true;
        Thread orecchio = new Thread(new Runnable() {
            @Override public void run() {
                ByteBuffer buf = ByteBuffer.allocate(2048);
                try {
                    while (inAscolto) {
                        if (sel.select() == 0) continue;
                        Iterator<SelectionKey> chi = sel.selectedKeys().iterator();
                        while (chi.hasNext()) {
                            SelectionKey k = chi.next();
                            chi.remove();
                            buf.clear();
                            if (((DatagramChannel) k.channel()).receive(buf) == null) continue;
                            senti(buf.array(), buf.position());
                        }
                    }
                } catch (Exception e) {
                    if (inAscolto) Log.w(TAG, "l'orecchio sugli annunci si e' chiuso: " + e.getMessage());
                } finally {
                    inAscolto = false;
                    for (DatagramChannel ch : canali) try { ch.close(); } catch (Exception ignored) { }
                    try { sel.close(); } catch (Exception ignored) { }
                }
            }
        }, "TabDeck-luci-annunci");
        orecchio.setDaemon(true);
        orecchio.start();
    }

    private void smettiDiAscoltareGliAnnunci() {
        inAscolto = false;
        Selector sel = selettore;
        selettore = null;
        if (sel != null) sel.wakeup();
    }

    /** Thread dell'orecchio: si capisce chi e', il resto si fa sull'UI. */
    private void senti(byte[] dati, int quanti) {
        try {
            String testo = Tuya.annuncioInChiaro(dati, quanti);
            if (testo == null) return;
            JSONObject o = new JSONObject(testo);
            final String id = o.optString("gwId", o.optString("devId", ""));
            final String ip = o.optString("ip", "");
            if (id.length() == 0) return;
            ui.post(new Runnable() {
                @Override public void run() { sentita(id, ip); }
            });
        } catch (Exception rumore) {
            // Sulle stesse porte parlano anche altre marche: non e' un errore.
        }
    }

    /**
     * Thread UI: una lampada si e' appena fatta sentire. Se ha cambiato
     * indirizzo, se era muta, o se e' stata zitta abbastanza da essere stata
     * spenta e riaccesa, la si rilegge. Altrimenti niente: l'annuncio di una
     * lampada che sta gia' bene non diventa una lettura a ciclo.
     */
    private void sentita(String id, String ip) {
        for (Lampada l : elenco) {
            if (!l.id.equals(id)) continue;
            long adesso = SystemClock.uptimeMillis();
            long prima = l.sentita;
            l.sentita = adesso;

            boolean spostata = ip.length() > 0 && !ip.equals(l.ip);
            if (spostata) {
                Log.i(TAG, l.nome + " si e' spostata: " + l.ip + " -> " + ip);
                // Anche al canale: una lettura partita verso l'indirizzo
                // vecchio lo rilegge al tentativo dopo, e non muore li'.
                l.spostaA(ip);
                risalva();
                if (ascolto != null) ascolto.elencoCambiato();
            }
            boolean muta = l.stato == null || !l.stato.raggiunta;
            boolean tornata = prima > 0 && adesso - prima > SILENZIO_MS;
            if (!spostata && !muta && !tornata) return;
            if (l.inCorso) {
                // Occupata: la lettura si fa appena ha finito, ma solo se
                // quella in corso non ci arriva.
                if (l.dopo < 0) l.dopo = Azione.LEGGI.ordinal();
                return;
            }

            // Una lampada che si annuncia ma non si lascia leggere — una chiave
            // cambiata — non va inseguita ogni cinque secondi per sempre: si
            // raddoppia l'attesa a ogni giro a vuoto, fino a cinque minuti.
            long attesa = Math.min(RINCORSA_MAX_MS, RINCORSA_MS << Math.min(l.rincorseAVuoto, 5));
            if (!spostata && !tornata && l.rincorsa > 0 && adesso - l.rincorsa < attesa) return;
            l.rincorsa = adesso;
            l.rincorseAVuoto++;
            accoda(l, Azione.LEGGI, 0);
            return;
        }
    }

    /**
     * Il Wi-Fi e' tornato: il sistema ha svuotato la tabella degli indirizzi
     * hardware, ogni lampada e' di nuovo fredda e il router puo' averle
     * spostate. E' il momento di rifare il giro, invece di scoprirlo al primo
     * tocco andato a vuoto. Su KitKat non c'e' la callback di rete, e si
     * ascolta il broadcast di connettivita'.
     */
    private void ascoltaLaRete() {
        if (rete != null) return;
        rete = new BroadcastReceiver() {
            @Override public void onReceive(Context c, Intent intent) {
                boolean su = wifiConnesso();
                // Il primo arriva alla registrazione e racconta com'e' adesso:
                // non e' un ritorno.
                if (!isInitialStickyBroadcast() && su && !reteSu) {
                    Log.i(TAG, "la rete e' tornata: si rilegge e si riascolta");
                    aggiorna();
                    scopri();
                }
                reteSu = su;
            }
        };
        try {
            context.registerReceiver(rete, new IntentFilter(ConnectivityManager.CONNECTIVITY_ACTION));
        } catch (Exception e) {
            Log.w(TAG, "non mi faccio avvisare dalla rete: " + e.getMessage());
            rete = null;
        }
    }

    private void smettiDiAscoltareLaRete() {
        if (rete == null) return;
        try {
            context.unregisterReceiver(rete);
        } catch (Exception ignored) {
        }
        rete = null;
    }

    private boolean wifiConnesso() {
        try {
            ConnectivityManager cm = (ConnectivityManager)
                    context.getSystemService(Context.CONNECTIVITY_SERVICE);
            NetworkInfo n = cm == null ? null : cm.getActiveNetworkInfo();
            return n != null && n.isConnected();
        } catch (Exception e) {
            return true;
        }
    }

    // ---- riscoperta degli indirizzi ----

    /**
     * Ascolta gli annunci che le lampade mandano in broadcast e riallinea gli
     * indirizzi salvati.
     *
     * Serve perche' l'indirizzo e' l'unica cosa che invecchia: basta un riavvio
     * del router e la lampada risponde altrove. L'identificativo invece resta
     * quello per sempre, quindi si riconosce da quello e si aggiorna l'IP.
     */
    public void scopri() {
        // Con l'orecchio aperto gli indirizzi si riallineano un annuncio alla
        // volta: altre due socket sulle stesse porte non sentirebbero di piu'.
        if (inAscolto) return;
        if (cercando || elenco.isEmpty()) return;
        cercando = true;
        // Un thread suo e non una corsia del pool: sta dodici secondi fermo ad
        // ascoltare, e se prendesse un posto in coda terrebbe fuori una lampada
        // proprio nel momento in cui la si sta aspettando.
        Thread cerca = new Thread(new Runnable() {
            @Override public void run() {
                // Le due porte insieme e non una dopo l'altra: prima si stava
                // sei secondi sulla 6666, che le lampade 3.3 e 3.4 non usano, e
                // solo dopo si ascoltava la 6667.
                final boolean[] cambiato = new boolean[1];
                Thread vecchie = new Thread(new Runnable() {
                    @Override public void run() {
                        if (ascolta(PORTE_ANNUNCI[0])) cambiato[0] = true;
                    }
                }, "TabDeck-luci-6666");
                vecchie.setDaemon(true);
                vecchie.start();
                boolean nuove = ascolta(PORTE_ANNUNCI[1]);
                try {
                    vecchie.join(ASCOLTO_MS + 2000);
                } catch (InterruptedException ignored) {
                }
                final boolean rinfrescare = nuove || cambiato[0];
                ui.post(new Runnable() {
                    @Override public void run() {
                        cercando = false;
                        if (rinfrescare) {
                            risalva();
                            if (ascolto != null) ascolto.elencoCambiato();
                            aggiorna();
                        }
                    }
                });
            }
        }, "TabDeck-luci-scopri");
        cerca.setDaemon(true);
        cerca.start();
    }

    private boolean ascolta(int porta) {
        DatagramSocket socket = null;
        boolean cambiato = false;
        try {
            socket = new DatagramSocket(null);
            socket.setReuseAddress(true);
            socket.setBroadcast(true);
            socket.setSoTimeout(1000);
            socket.bind(new java.net.InetSocketAddress(porta));

            long fine = System.currentTimeMillis() + ASCOLTO_MS;
            byte[] buf = new byte[2048];
            while (System.currentTimeMillis() < fine) {
                DatagramPacket p = new DatagramPacket(buf, buf.length);
                try {
                    socket.receive(p);
                } catch (Exception scaduto) {
                    continue;                  // nessuno ha parlato: si riprova
                }
                cambiato |= annuncio(buf, p.getLength());
            }
        } catch (Exception e) {
            Log.w(TAG, "ascolto sulla " + porta + " fallito: " + e.getMessage());
        } finally {
            if (socket != null) socket.close();
        }
        return cambiato;
    }

    /**
     * Un annuncio: se e' di una lampada che conosciamo, ne prende l'indirizzo.
     * Synchronized perche' le due porte si ascoltano adesso insieme.
     */
    private synchronized boolean annuncio(byte[] buf, int quanti) {
        try {
            String testo = Tuya.annuncioInChiaro(buf, quanti);
            if (testo == null) return false;
            JSONObject o = new JSONObject(testo);
            String id = o.optString("gwId", o.optString("devId", ""));
            String ip = o.optString("ip", "");
            if (id.length() == 0 || ip.length() == 0) return false;
            for (Lampada l : elenco) {
                if (l.id.equals(id) && !ip.equals(l.ip)) {
                    Log.i(TAG, l.nome + " si e' spostata: " + l.ip + " -> " + ip);
                    l.spostaA(ip);
                    return true;
                }
            }
        } catch (Exception ignored) {
            // Sulla stessa porta parlano anche telefoni e altre marche: un
            // pacchetto che non si capisce non e' un errore, e' rumore.
        }
        return false;
    }

    public void chiudi() {
        ferma();
        lavoro.shutdownNow();
    }
}
