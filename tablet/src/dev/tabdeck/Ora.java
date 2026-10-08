package dev.tabdeck;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Calendar;
import java.util.List;
import java.util.Locale;

/**
 * Quello che suona: i timer e le sveglie.
 *
 * Nascono da una cosa semplice: staccato dal PC, il tablet e' un pannello da
 * 1024x600 attaccato alla corrente su un comodino, e le due sezioni che ha —
 * lo schermo e il deck — non servono a niente finche' il PC non c'e'. Le luci
 * gia' funzionano da sole; questi sono gli altri due mestieri che un pannello
 * acceso accanto al letto sa fare senza chiedere niente a nessuno.
 *
 * Lo stato vive qui, e si vede in {@link OrologioView}; il suono sta in
 * {@link Suoneria}.
 *
 * <h3>Piu' d'uno</h3>
 *
 * All'inizio c'era un timer e una sveglia. Ma la pasta e il forno scadono in
 * momenti diversi, e la sveglia dei giorni feriali non e' quella della
 * domenica: adesso sono due elenchi, e ogni voce ha un numero suo che non si
 * ricicla mai. Quel numero e' anche il codice della sua busta in AlarmManager:
 * due buste con lo stesso codice per Android sono la stessa, e la seconda
 * cancellerebbe la prima.
 *
 * <h3>Chi tiene il tempo</h3>
 *
 * Non un thread e non un ciclo: <b>AlarmManager</b>, con {@code setExact}. Su
 * KitKat e' l'unica strada esatta — {@code setAlarmClock} e
 * {@code setExactAndAllowWhileIdle} arrivano dopo — e qui basta, perche'
 * Android 4.4 non ha il Doze che raggruppa e rimanda. Il conto alla rovescia
 * sullo schermo e' solo una sottrazione fra l'ora di adesso e una scadenza
 * scritta; quello che fa suonare e' Android, anche a processo morto e a
 * pannello spento.
 *
 * <h3>Cosa resta scritto</h3>
 *
 * {@code ora.json}, scritto con {@link Archivio} in modo atomico. Le sveglie di
 * Android si dimenticano a ogni riavvio, reinstallazione o cambio d'ora, e
 * vanno rimesse da qui: lo fa {@link #riprogramma()}, che il costruttore chiama
 * sempre e che {@link BootReceiver} chiama quando Android le ha buttate.
 *
 * Fino alla versione con una sveglia sola lo stato stava nelle preferenze, una
 * chiave per campo: al primo avvio le si legge, se ne fa una sveglia e un
 * timer, e le chiavi vecchie si tolgono. Vedi {@link #migra()}.
 */
public final class Ora {

    private static final String TAG = "TabDeck.Ora";

    private static final String FILE = "ora.json";

    // Le chiavi della versione con una sveglia sola: servono solo a migrare.
    private static final String PREFS = "tabdeck";
    private static final String KEY_TIMER_FINE = "ora.timerFine";
    private static final String KEY_TIMER_SEC  = "ora.timerDurata";
    private static final String KEY_MIEI       = "ora.mieiTagli";
    private static final String KEY_SVEGLIA_H  = "ora.sveglaOre";
    private static final String KEY_SVEGLIA_M  = "ora.sveglaMinuti";
    private static final String KEY_SVEGLIA_ON = "ora.sveglaAttiva";
    private static final String KEY_GIORNI     = "ora.giorni";

    /** Dice all'activity che cosa e' scattato: {@link #TIMER} o {@link #SVEGLIA}. */
    public static final String EXTRA_COSA = "dev.tabdeck.SUONA";
    /** E quale, fra i tanti: il numero della sveglia o del timer. */
    public static final String EXTRA_ID = "dev.tabdeck.SUONA_ID";
    public static final String TIMER = "timer";
    public static final String SVEGLIA = "sveglia";

    private static final String AZIONE = "dev.tabdeck.SUONA";

    /** Di quanto si rimanda premendo « ancora ». Cinque minuti: dieci sono un
     *  secondo sonno, due non bastano ad alzarsi. */
    public static final int RINVIO_MIN = 5;

    /**
     * Quanto un timer scaduto resta in memoria dopo la sua ora. Serve al caso
     * del processo chiuso: Android consegna la sveglia, l'activity rinasce e
     * rilegge il file, e il timer deve esserci ancora per sapere come si
     * chiama. Oltre questo margine ha perso il suo momento e si butta.
     */
    private static final long MARGINE_TIMER_MS = 2 * 60 * 1000L;

    /** I giorni, nell'ordine in cui si leggono su un calendario italiano. */
    public static final int[] ORDINE_GIORNI = {
            Calendar.MONDAY, Calendar.TUESDAY, Calendar.WEDNESDAY, Calendar.THURSDAY,
            Calendar.FRIDAY, Calendar.SATURDAY, Calendar.SUNDAY };
    private static final String[] SIGLE_GIORNI = { "lun", "mar", "mer", "gio", "ven", "sab", "dom" };
    private static final String[] NOMI_GIORNI = {
            "lunedi'", "martedi'", "mercoledi'", "giovedi'", "venerdi'", "sabato", "domenica" };

    private static final int TUTTI = 0x7F;
    private static final int FERIALI = 0x3E;
    private static final int FESTIVI = 0x41;

    public interface Ascolto {
        /** Thread UI: qualcosa e' cambiato, ridisegna. */
        void oraCambiata();
    }

    /** Un timer che corre. */
    public static final class Conto {
        public final int id;
        /** Quando scade, in millisecondi assoluti. */
        public final long fine;
        /** Con che durata era partito, in secondi. */
        public final int durata;

        Conto(int id, long fine, int durata) {
            this.id = id;
            this.fine = fine;
            this.durata = durata;
        }

        /** Quanti secondi mancano, per eccesso: da 1 non si vede mai « 0:00 ». */
        public long restano() {
            return Math.max(0, (fine - System.currentTimeMillis() + 999) / 1000);
        }

        /** Il nome del timer e' la sua durata: « 10 minuti ». Chiedere di
         *  battezzarlo sarebbe una tastiera in mezzo a un gesto da un tocco. */
        public String nome() {
            return durataInParole(durata);
        }
    }

    /** Una sveglia: ora, giorni, se e' accesa, e cosa fare alle luci. */
    public static final class Sveglia {
        public final int id;
        public int ore = 7;
        public int minuti = 0;
        /**
         * I giorni in cui si ripete, un bit per giorno, con il bit numero
         * {@code DAY_OF_WEEK - 1}: bit 0 domenica, bit 6 sabato. Zero vuol dire
         * « una volta sola »: suona alla prossima occorrenza e poi si spegne.
         */
        public int giorni;
        public boolean attiva = true;
        /** Se e' stata rimandata, il momento a cui risuona. Zero se no. */
        public long rinvio;
        /** Il nome della routine delle luci da far partire, o vuoto. */
        public String routine = "";

        Sveglia(int id) {
            this.id = id;
        }

        public boolean ripete() {
            return giorni != 0;
        }

        /** Vero se si ripete in quel giorno di {@link Calendar}. */
        public boolean giorno(int dayOfWeek) {
            return (giorni & (1 << (dayOfWeek - 1))) != 0;
        }

        /** « 07:30 ». */
        public String orario() {
            return String.format(Locale.ITALIAN, "%02d:%02d", ore, minuti);
        }

        /** « da lunedi' a venerdi' », « una volta sola », « lun mer ven ». */
        public String quando() {
            if (giorni == 0) return "una volta sola";
            if (giorni == TUTTI) return "tutti i giorni";
            if (giorni == FERIALI) return "da lunedi' a venerdi'";
            if (giorni == FESTIVI) return "sabato e domenica";
            int quanti = Integer.bitCount(giorni);
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < ORDINE_GIORNI.length; i++) {
                if (!giorno(ORDINE_GIORNI[i])) continue;
                if (b.length() > 0) b.append(' ');
                // Un giorno solo si scrive per intero: « lun » da solo sembra
                // un'abbreviazione rimasta a meta'.
                b.append(quanti == 1 ? "solo " + NOMI_GIORNI[i] : SIGLE_GIORNI[i]);
            }
            return b.toString();
        }
    }

    private final Context context;
    private final AlarmManager allarmi;
    private final List<Ascolto> ascolti = new ArrayList<Ascolto>();
    private final Suoneria suoneria;

    private final List<Conto> conti = new ArrayList<Conto>();
    private final List<Sveglia> sveglie = new ArrayList<Sveglia>();
    private int prossimoId = 1;

    /** Che cosa sta suonando adesso, o null se non suona niente. */
    private String suonando;
    /** Il titolo della schermata che suona: « Sveglia delle 07:00 ». */
    private String titolo = "";
    /** Il numero di quello che suona, per il rinvio. */
    private int suonaId = -1;

    public Ora(Context context) {
        this.context = context.getApplicationContext();
        this.allarmi = (AlarmManager) this.context.getSystemService(Context.ALARM_SERVICE);
        this.suoneria = new Suoneria(this.context, new Runnable() {
            @Override public void run() {
                // Passati i tre minuti la suoneria si e' spenta da sola: la
                // schermata che suona deve andarsene con lei.
                suonando = null;
                suonaId = -1;
                avvisa();
            }
        });
        if (!Archivio.esiste(this.context, FILE)) migra();
        else carica();
        riprogramma();
    }

    /**
     * Rimette in Android tutte le sveglie scritte, senza aprire niente. E'
     * quello che serve dopo un riavvio, una reinstallazione o un cambio d'ora,
     * quando le buste registrate sono state buttate o puntano all'ora sbagliata.
     */
    public static void rimetti(Context context) {
        new Ora(context);
    }

    /**
     * Chi vuole essere avvisato. Piu' d'uno — l'orologio, la schermata che
     * suona, l'activity — e ognuno si ridisegna per conto suo.
     */
    public void aggiungiAscolto(Ascolto a) {
        if (a != null && !ascolti.contains(a)) ascolti.add(a);
    }

    // ---- lettura dello stato ----

    public List<Sveglia> sveglie() {
        return sveglie;
    }

    /** I timer che corrono ancora, dal primo che scade. */
    public List<Conto> conti() {
        List<Conto> vivi = new ArrayList<Conto>();
        long adesso = System.currentTimeMillis();
        for (Conto c : conti) {
            // Quelli arrivati a zero non si mostrano: sta per arrivare la
            // sveglia di Android che li fa suonare e li toglie.
            if (c.fine <= adesso) continue;
            int i = 0;
            while (i < vivi.size() && vivi.get(i).fine <= c.fine) i++;
            vivi.add(i, c);
        }
        return vivi;
    }

    /** Il primo che scade, o null: e' quello che consuma l'arco della ghiera. */
    public Conto primoConto() {
        List<Conto> vivi = conti();
        return vivi.isEmpty() ? null : vivi.get(0);
    }

    public Sveglia trova(int id) {
        for (Sveglia s : sveglie) if (s.id == id) return s;
        return null;
    }

    public String suonando() {
        return suonando;
    }

    /** « Sveglia delle 07:00 », « Timer di 10 minuti ». */
    public String titolo() {
        return titolo;
    }

    /** Solo le sveglie: un timer rimandato di cinque minuti e' un timer sbagliato. */
    public boolean siPuoRimandare() {
        return SVEGLIA.equals(suonando);
    }

    // ---- timer ----

    /** Ne avvia uno. Piu' di uno alla volta si puo'. Zero o meno: non si parte. */
    public void avviaTimer(int secondi) {
        if (secondi <= 0) return;
        conti.add(new Conto(prossimoId++, System.currentTimeMillis() + secondi * 1000L, secondi));
        dopoIlCambio();
    }

    public void fermaTimer(int id) {
        for (int i = 0; i < conti.size(); i++) {
            if (conti.get(i).id != id) continue;
            conti.remove(i);
            annulla(id, TIMER);
            dopoIlCambio();
            return;
        }
    }

    // ---- sveglie ----

    public Sveglia aggiungiSveglia(int ore, int minuti, int giorni) {
        Sveglia s = new Sveglia(prossimoId++);
        s.ore = Math.max(0, Math.min(23, ore));
        s.minuti = Math.max(0, Math.min(59, minuti));
        s.giorni = giorni & TUTTI;
        sveglie.add(s);
        dopoIlCambio();
        return s;
    }

    public void togliSveglia(int id) {
        Sveglia s = trova(id);
        if (s == null) return;
        sveglie.remove(s);
        annulla(id, SVEGLIA);
        dopoIlCambio();
    }

    public void accendi(int id, boolean attiva) {
        Sveglia s = trova(id);
        if (s == null) return;
        s.attiva = attiva;
        s.rinvio = 0;
        dopoIlCambio();
    }

    /**
     * Mette l'ora esatta. La chiama la ghiera <b>alla fine del
     * trascinamento</b>, non a ogni scatto: ogni chiamata riscrive il file e
     * riprogramma, e mezzo giro di ghiera sono venti scatti.
     */
    public void regola(int id, int ore, int minuti) {
        Sveglia s = trova(id);
        if (s == null) return;
        s.ore = Math.max(0, Math.min(23, ore));
        s.minuti = Math.max(0, Math.min(59, minuti));
        s.rinvio = 0;
        dopoIlCambio();
    }

    /** Accende o spegne un giorno della ripetizione. */
    public void inverti(int id, int dayOfWeek) {
        Sveglia s = trova(id);
        if (s == null) return;
        s.giorni ^= 1 << (dayOfWeek - 1);
        s.rinvio = 0;
        dopoIlCambio();
    }

    /** La routine delle luci che parte quando suona; null o vuoto per nessuna. */
    public void setRoutine(int id, String nome) {
        Sveglia s = trova(id);
        if (s == null) return;
        s.routine = nome == null ? "" : nome;
        salva();
        avvisa();
    }

    private void dopoIlCambio() {
        salva();
        riprogramma();
        avvisa();
    }

    /**
     * Il prossimo momento in cui la sveglia deve suonare.
     *
     * Un rinvio in corso vince su tutto. Senza giorni segnati e' la prossima
     * volta che l'orologio passa da quell'ora; con dei giorni segnati si va
     * avanti di giorno in giorno finche' non se ne trova uno buono. Otto
     * tentativi: sette per il giro della settimana, e uno perche' la sveglia
     * della sola domenica chiesta di domenica dopo la sua ora cade fra sette.
     *
     * Il confronto e' sul minuto e non sull'istante: impostare le 7:00 alle
     * 7:00:30 deve voler dire domani mattina, non fra trenta secondi.
     */
    public long quandoSuona(Sveglia s) {
        long adesso = System.currentTimeMillis();
        if (s.rinvio > adesso) return s.rinvio;
        Calendar c = Calendar.getInstance();
        c.set(Calendar.HOUR_OF_DAY, s.ore);
        c.set(Calendar.MINUTE, s.minuti);
        c.set(Calendar.SECOND, 0);
        c.set(Calendar.MILLISECOND, 0);
        for (int i = 0; i <= 8; i++) {
            boolean buono = s.giorni == 0 || s.giorno(c.get(Calendar.DAY_OF_WEEK));
            if (buono && c.getTimeInMillis() > adesso) return c.getTimeInMillis();
            c.add(Calendar.DAY_OF_MONTH, 1);
        }
        return c.getTimeInMillis();
    }

    /** « domani alle 07:00 », « oggi alle 22:30 », « lunedi' alle 07:00 »; null se nessuna. */
    public String prossimaSveglia() {
        Sveglia migliore = null;
        long quando = 0;
        for (Sveglia s : sveglie) {
            if (!s.attiva) continue;
            long q = quandoSuona(s);
            if (migliore == null || q < quando) {
                migliore = s;
                quando = q;
            }
        }
        if (migliore == null) return null;

        Calendar oggi = Calendar.getInstance();
        azzeraOre(oggi);
        Calendar poi = Calendar.getInstance();
        poi.setTimeInMillis(quando);
        int ore = poi.get(Calendar.HOUR_OF_DAY);
        int minuti = poi.get(Calendar.MINUTE);
        azzeraOre(poi);
        long giorni = Math.round((poi.getTimeInMillis() - oggi.getTimeInMillis()) / 86400000.0);
        String prefisso;
        if (giorni == 0) prefisso = "oggi";
        else if (giorni == 1) prefisso = "domani";
        else prefisso = NOMI_GIORNI[indice(poi.get(Calendar.DAY_OF_WEEK))];
        return prefisso + " alle " + String.format(Locale.ITALIAN, "%02d:%02d", ore, minuti);
    }

    private static void azzeraOre(Calendar c) {
        c.set(Calendar.HOUR_OF_DAY, 0);
        c.set(Calendar.MINUTE, 0);
        c.set(Calendar.SECOND, 0);
        c.set(Calendar.MILLISECOND, 0);
    }

    // ---- quando scatta ----

    /** Vero se questo numero e' ancora un timer o una sveglia che si conosce. */
    public boolean conosce(int id) {
        if (trova(id) != null) return true;
        for (Conto c : conti) if (c.id == id) return true;
        return false;
    }

    /**
     * Chiamata dall'activity quando Android ha consegnato una sveglia.
     *
     * Qui si rimette in ordine lo stato e poi si comincia a suonare. Il timer
     * finito sparisce. La sveglia che si ripete si riprogramma <b>subito</b>
     * per il prossimo giorno buono, prima ancora di suonare: se qualcuno spegne
     * il tablet mentre suona, quella di domani dev'esserci lo stesso. Quella
     * « una volta » si spegne, ma resta nell'elenco: domani si riaccende con un
     * tocco invece di rifarla.
     *
     * @return il nome della routine delle luci da far partire, o null. Solo al
     *         primo squillo: al ritorno da un rinvio le luci sono gia' come la
     *         sveglia le voleva, e rifarle vorrebbe dire riaccendere quella che
     *         intanto qualcuno ha spento.
     */
    public String scattato(int id) {
        for (int i = 0; i < conti.size(); i++) {
            Conto c = conti.get(i);
            if (c.id != id) continue;
            conti.remove(i);
            salva();
            riprogramma();
            inizia(TIMER, id, "Timer di " + c.nome());
            return null;
        }
        Sveglia s = trova(id);
        if (s == null) {
            Log.w(TAG, "e' scattato " + id + " ma non lo conosco piu'");
            return null;
        }
        boolean dalRinvio = s.rinvio > 0;
        s.rinvio = 0;
        // « Una volta » vuol dire una volta: si spegne da sola, se no
        // domattina suona di nuovo e nessuno se l'aspetta.
        if (!s.ripete()) s.attiva = false;
        salva();
        riprogramma();
        inizia(SVEGLIA, id, "Sveglia delle " + s.orario());
        return dalRinvio || s.routine.length() == 0 ? null : s.routine;
    }

    private void inizia(String cosa, int id, String titolo) {
        suoneria.taci();
        suonando = cosa;
        suonaId = id;
        this.titolo = titolo;
        suoneria.suona(SVEGLIA.equals(cosa));
        avvisa();
    }

    /** Basta: e' il tasto grande della schermata che suona. */
    public void ferma() {
        suoneria.taci();
        suonando = null;
        suonaId = -1;
        avvisa();
    }

    /**
     * Ancora cinque minuti. Il rinvio si scrive nella sveglia stessa: cosi'
     * sopravvive a un'activity rifatta — che essendo la Home capita — e usa la
     * sua stessa busta, senza lasciarne una in giro che nessuno sa di chi sia.
     */
    public void rinvia() {
        Sveglia s = SVEGLIA.equals(suonando) ? trova(suonaId) : null;
        suoneria.taci();
        suonando = null;
        suonaId = -1;
        if (s != null) {
            s.rinvio = System.currentTimeMillis() + RINVIO_MIN * 60 * 1000L;
            s.attiva = true;
            salva();
            riprogramma();
        }
        avvisa();
    }

    // ---- volume ----

    /** Vero se il canale della sveglia e' a zero: suonerebbe in silenzio. */
    public boolean muta() {
        return suoneria.muta();
    }

    /** Riporta il canale della sveglia a meta' corsa. */
    public void alzaVolume() {
        suoneria.alzaVolume();
        avvisa();
    }

    // ---- le sveglie di Android ----

    /**
     * Riscrive in Android tutte le buste, com'e' lo stato adesso: una per timer
     * che corre, una per sveglia accesa, e annullate quelle spente. Si chiama
     * dopo ogni cambiamento e all'avvio: e' l'unico posto che mette sveglie in
     * AlarmManager, cosi' non restano in giro buste di uno stato precedente.
     */
    public void riprogramma() {
        if (allarmi == null) return;
        long adesso = System.currentTimeMillis();
        for (Conto c : conti) {
            // Un timer gia' scaduto non si rimette: una busta nel passato
            // scatterebbe subito, e suonerebbe una seconda volta un timer che
            // sta gia' suonando o che ha perso il suo momento.
            if (c.fine > adesso) {
                allarmi.setExact(AlarmManager.RTC_WAKEUP, c.fine, busta(c.id, TIMER));
            }
        }
        for (Sveglia s : sveglie) {
            if (s.attiva) {
                allarmi.setExact(AlarmManager.RTC_WAKEUP, quandoSuona(s), busta(s.id, SVEGLIA));
            } else {
                annulla(s.id, SVEGLIA);
            }
        }
    }

    private PendingIntent busta(int id, String cosa) {
        Intent i = new Intent(context, Allarme.class);
        i.setAction(AZIONE);
        i.putExtra(EXTRA_COSA, cosa);
        i.putExtra(EXTRA_ID, id);
        // Il codice di richiesta e' il numero: due voci diverse devono essere
        // due buste diverse, se no la seconda sostituisce la prima.
        return PendingIntent.getBroadcast(context, id, i, PendingIntent.FLAG_UPDATE_CURRENT);
    }

    private void annulla(int id, String cosa) {
        if (allarmi != null) allarmi.cancel(busta(id, cosa));
    }

    // ---- memoria ----

    private void carica() {
        JSONObject o = Archivio.leggi(context, FILE);
        if (o == null) return;
        long adesso = System.currentTimeMillis();

        JSONArray a = o.optJSONArray("timer");
        if (a != null) {
            for (int i = 0; i < a.length(); i++) {
                JSONObject t = a.optJSONObject(i);
                if (t == null) continue;
                long fine = t.optLong("fine", 0);
                // Un timer la cui ora e' passata da un pezzo non si recupera:
                // ha gia' suonato, o ha perso il suo momento.
                if (fine <= adesso - MARGINE_TIMER_MS) continue;
                conti.add(new Conto(t.optInt("id", 0), fine, t.optInt("durata", 0)));
            }
        }

        JSONArray b = o.optJSONArray("sveglie");
        if (b != null) {
            for (int i = 0; i < b.length(); i++) {
                JSONObject j = b.optJSONObject(i);
                if (j == null) continue;
                Sveglia s = new Sveglia(j.optInt("id", 0));
                s.ore = Math.max(0, Math.min(23, j.optInt("ore", 7)));
                s.minuti = Math.max(0, Math.min(59, j.optInt("minuti", 0)));
                s.giorni = j.optInt("giorni", 0) & TUTTI;
                s.attiva = j.optBoolean("attiva", true);
                s.rinvio = j.optLong("rinvio", 0);
                s.routine = j.optString("routine", "");
                sveglie.add(s);
            }
        }

        // Il contatore non deve mai ridare un numero gia' usato, nemmeno se il
        // file e' stato scritto a mano o il campo manca.
        prossimoId = Math.max(1, o.optInt("prossimoId", 1));
        for (Sveglia s : sveglie) prossimoId = Math.max(prossimoId, s.id + 1);
        for (Conto c : conti) prossimoId = Math.max(prossimoId, c.id + 1);
    }

    private void salva() {
        try {
            JSONArray a = new JSONArray();
            for (Conto c : conti) {
                JSONObject t = new JSONObject();
                t.put("id", c.id);
                t.put("fine", c.fine);
                t.put("durata", c.durata);
                a.put(t);
            }
            JSONArray b = new JSONArray();
            for (Sveglia s : sveglie) {
                JSONObject j = new JSONObject();
                j.put("id", s.id);
                j.put("ore", s.ore);
                j.put("minuti", s.minuti);
                j.put("giorni", s.giorni);
                j.put("attiva", s.attiva);
                j.put("rinvio", s.rinvio);
                j.put("routine", s.routine);
                b.put(j);
            }
            JSONObject tutto = new JSONObject();
            tutto.put("versione", 1);
            tutto.put("prossimoId", prossimoId);
            tutto.put("sveglie", b);
            tutto.put("timer", a);
            Archivio.scrivi(context, FILE, tutto);
        } catch (Exception e) {
            Log.w(TAG, "non riesco a salvare " + FILE + ": " + e.getMessage());
        }
    }

    /**
     * Dalla versione con una sveglia sola, che teneva tutto nelle preferenze.
     *
     * Le chiavi diventano una sveglia — se c'erano: le scriveva tutte insieme,
     * quindi basta guardarne una — e un timer se stava ancora correndo. I tagli
     * propri del tastierino non passano: la ghiera ha le sue durate pronte, e
     * il tastierino non c'e' piu'. Le chiavi vecchie si tolgono solo dopo che
     * il file e' scritto: se la scrittura fallisce, al prossimo avvio si
     * riprova invece di aver perso tutto.
     *
     * Si annullano anche le buste della versione di prima, che avevano
     * un'altra azione e altri codici: lasciate li', una sveglia gia' spostata
     * suonerebbe domattina all'ora vecchia.
     */
    private void migra() {
        SharedPreferences prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        long adesso = System.currentTimeMillis();

        if (prefs.contains(KEY_SVEGLIA_H)) {
            Sveglia s = new Sveglia(prossimoId++);
            s.ore = Math.max(0, Math.min(23, prefs.getInt(KEY_SVEGLIA_H, 7)));
            s.minuti = Math.max(0, Math.min(59, prefs.getInt(KEY_SVEGLIA_M, 0)));
            s.attiva = prefs.getBoolean(KEY_SVEGLIA_ON, false);
            // Stessa convenzione di prima, bit DAY_OF_WEEK - 1: nessuna conversione.
            s.giorni = prefs.getInt(KEY_GIORNI, 0) & TUTTI;
            sveglie.add(s);
        }

        long fine = prefs.getLong(KEY_TIMER_FINE, 0);
        if (fine > adesso) {
            conti.add(new Conto(prossimoId++, fine, prefs.getInt(KEY_TIMER_SEC, 0)));
        }

        if (allarmi != null) {
            annullaVecchia(41, TIMER);
            annullaVecchia(42, SVEGLIA);
            annullaVecchia(43, TIMER);
            annullaVecchia(43, SVEGLIA);
        }

        salva();
        if (Archivio.esiste(context, FILE)) {
            prefs.edit()
                    .remove(KEY_TIMER_FINE)
                    .remove(KEY_TIMER_SEC)
                    .remove(KEY_MIEI)
                    .remove(KEY_SVEGLIA_H)
                    .remove(KEY_SVEGLIA_M)
                    .remove(KEY_SVEGLIA_ON)
                    .remove(KEY_GIORNI)
                    .apply();
        }
    }

    /** Una busta come la faceva la versione con una sveglia sola, per annullarla. */
    private void annullaVecchia(int codice, String cosa) {
        Intent i = new Intent(context, Allarme.class);
        i.setAction("dev.tabdeck.SUONA." + cosa + "." + codice);
        allarmi.cancel(PendingIntent.getBroadcast(context, codice, i,
                PendingIntent.FLAG_UPDATE_CURRENT));
    }

    private void avvisa() {
        for (int i = 0; i < ascolti.size(); i++) ascolti.get(i).oraCambiata();
    }

    /** Solo la suoneria: le sveglie di Android devono sopravvivere all'app. */
    public void chiudi() {
        suoneria.taci();
    }

    // ---- parole ----

    /** Da {@link Calendar#DAY_OF_WEEK} alla posizione nell'ordine italiano. */
    private static int indice(int dayOfWeek) {
        for (int i = 0; i < ORDINE_GIORNI.length; i++) {
            if (ORDINE_GIORNI[i] == dayOfWeek) return i;
        }
        return 0;
    }

    /** Il conto alla rovescia come si legge: 1:29:57 oppure 4:05. */
    public static String scorrere(long secondi) {
        long mi = secondi / 60, s = secondi % 60;
        if (mi >= 60) return String.format(Locale.ITALIAN, "%d:%02d:%02d", mi / 60, mi % 60, s);
        return String.format(Locale.ITALIAN, "%d:%02d", mi, s);
    }

    /** « 10 minuti », « un'ora e mezza », « 45 secondi ». */
    public static String durataInParole(int secondi) {
        if (secondi < 60) return secondi + (secondi == 1 ? " secondo" : " secondi");
        int minuti = secondi / 60;
        if (minuti < 60) return minuti + (minuti == 1 ? " minuto" : " minuti");
        int ore = minuti / 60, resto = minuti % 60;
        String testoOre = ore == 1 ? "un'ora" : ore + " ore";
        if (resto == 0) return testoOre;
        if (resto == 30) return testoOre + " e mezza";
        return testoOre + " e " + resto;
    }
}
