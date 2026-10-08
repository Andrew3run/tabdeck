package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.Handler;
import android.view.MotionEvent;
import android.view.View;

import java.util.List;
import java.util.Locale;

/**
 * Timer e sveglie, attorno a una ghiera che scorre.
 *
 * Prima erano due pagine fatte di riquadri: il timer con un tastierino a cifre
 * e i tagli, la sveglia con i tasti « +1 h » e « +5 m ». Questa schermata
 * arriva invece dall'altro tablet, cosi' com'era, e sostituisce tutte e due.
 * Nella barra le voci restano due - Timer e Sveglia sono due gesti diversi -
 * ma aprono la stessa ghiera con la scheda giusta accesa.
 *
 * <h3>Una ghiera al posto delle frecce</h3>
 *
 * Portare una sveglia dalle 7 alle 22 erano quindici tocchi; {@link Quadrante}
 * fa lo stesso viaggio con un gesto solo, e mentre lo fa mostra dove si sta
 * andando. E un cerchio di ore e' gia' la forma di un orologio.
 *
 * <h3>Una ghiera sola per due mestieri</h3>
 *
 * <b>Un timer e una sveglia sono la stessa domanda posta in due modi</b> - « fra
 * quanto » e « a che ora » - e meritano lo stesso gesto. Due schede in cima
 * dicono che cosa sta scegliendo, a destra c'e' l'elenco di quello che e' gia'
 * in piedi. Nel modo timer l'anello esterno sono i minuti e quello interno le
 * ore; nel modo sveglie l'esterno sono le ventiquattro ore e l'interno i minuti
 * a passi di cinque.
 *
 * <h3>Toccare una sveglia la porta nella ghiera</h3>
 *
 * Non c'e' un « modifica »: si tocca la riga, e la ghiera ci si porta sopra. Il
 * pallino a destra resta l'interruttore, perche' accendere e spegnere e' la
 * cosa che si fa piu' spesso.
 *
 * <h3>Le luci quando suona</h3>
 *
 * L'unica cosa che l'altro tablet non aveva. Nel modo sveglie il pannello in
 * alto a destra - dove nel modo timer stanno le durate pronte - elenca le
 * routine delle luci: quella scelta parte quando la sveglia suona. Sta nello
 * stesso posto e ha la stessa forma delle durate pronte apposta, perche' e' la
 * stessa cosa: una scorciatoia accanto alla ghiera. Se non ci sono routine il
 * pannello non c'e', e l'elenco si prende il suo posto.
 */
public final class OrologioView extends View implements Ora.Ascolto {

    /** Chi deve sapere che si e' cambiata scheda: l'activity, per la barra. */
    public interface Schede {
        void suScheda(boolean sveglie);
    }

    private static final int TIMER = 0, SVEGLIE = 1;
    private static final String[] NOMI_SCHEDE = { "Timer", "Sveglie" };

    /** Le durate pronte, in minuti. La ghiera resta per tutte le altre. */
    private static final int[] PRONTI = { 1, 3, 5, 10, 15, 30 };
    private static final String[] NOMI_PRONTI = { "1", "3", "5", "10", "15", "30" };

    /** Quante righe si mostrano: oltre, diventerebbero troppo basse per un dito. */
    private static final int MAX_TIMER = 3;
    private static final int MAX_SVEGLIE = 5;

    /** Lunedi' per primo, come su un calendario italiano: nella stessa
     *  posizione di {@link Ora#ORDINE_GIORNI}. */
    private static final String[] INIZIALI = { "L", "M", "M", "G", "V", "S", "D" };

    private static final String[] ETICHETTE_ORE = new String[24];
    private static final String[] ETICHETTE_MIN5 = new String[12];
    private static final String[] ETICHETTE_MIN60 = new String[60];
    private static final String[] ETICHETTE_ORE6 = new String[6];
    static {
        for (int i = 0; i < 24; i++) ETICHETTE_ORE[i] = String.format(Locale.ITALIAN, "%02d", i);
        for (int i = 0; i < 12; i++) ETICHETTE_MIN5[i] = String.format(Locale.ITALIAN, "%02d", i * 5);
        // I null non sono buchi: sessanta scatti di cui si scrivono solo i
        // multipli di cinque. Le tacche del bordo danno il minuto esatto.
        for (int i = 0; i < 60; i++) ETICHETTE_MIN60[i] = (i % 5 == 0) ? String.valueOf(i) : null;
        for (int i = 0; i < 6; i++) ETICHETTE_ORE6[i] = i + "h";
    }

    private final Misure m;
    private final Ora ora;
    private final Luci luci;
    private Vetro vetro;
    private Schede ascoltoSchede;

    private final Paint pTitolo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pGrande = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pNota = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pSegno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pPiano = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pBordo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Tasti tasti;
    private final Paint pAloneSezione = new Paint();
    /** Dove sta l'etichetta dell'elenco, sopra il suo piano. */
    private float yEtichettaElenco;

    private final Quadrante ghiera = new Quadrante(this);

    // ---- aree ----
    private final RectF[] areeSchede = { new RectF(), new RectF() };
    private final RectF schede = new RectF();
    private final RectF azione = new RectF();
    private final RectF cestino = new RectF();
    private final RectF interoAzione = new RectF();
    private final RectF lineetta = new RectF();
    private final RectF[] areeGiorni = new RectF[7];
    /** Le sei pastiglie in alto a destra: le durate pronte, o le routine. */
    private final RectF[] areePronti = new RectF[PRONTI.length];
    private final RectF[] righe = new RectF[Math.max(MAX_TIMER, MAX_SVEGLIE)];
    private final RectF[] pallini = new RectF[Math.max(MAX_TIMER, MAX_SVEGLIE)];
    private final Testo.Riga[] rRighe = new Testo.Riga[Math.max(MAX_TIMER, MAX_SVEGLIE)];
    private final Testo.Riga[] rSotto = new Testo.Riga[Math.max(MAX_TIMER, MAX_SVEGLIE)];
    private final Testo.Riga[] rPastiglie = new Testo.Riga[PRONTI.length];

    /** I due pannelli della colonna di destra: il secondo comincia dove finisce
     *  il primo, cosi' sovrapporli e' impossibile e non solo evitato. */
    private final RectF pannelloPronti = new RectF();
    private final RectF pannelloElenco = new RectF();
    /** La fascia del titolo dell'elenco: col volume a zero diventa un comando. */
    private final RectF fasciaElenco = new RectF();

    // ---- scritte gia' composte: dentro onDraw non si alloca ----
    private final String[] testoRiga = new String[Math.max(MAX_TIMER, MAX_SVEGLIE)];
    private final String[] testoSotto = new String[Math.max(MAX_TIMER, MAX_SVEGLIE)];
    private String testoCentro = "", testoUnita, testoAzione = "", testoCima = "";

    private float xDestra, larghezzaDestra, passoRiga, altezzaRiga;

    private int scheda = TIMER;

    /** La sveglia che la ghiera sta regolando. null = se ne compone una nuova. */
    private Ora.Sveglia scelta;

    /** I giorni e la routine della sveglia ancora da creare. */
    private int giorniNuova = 0x3E;          // da lunedi' a venerdi'
    private String routineNuova = "";

    /** « Nessuna » e i nomi delle routine delle luci, al massimo cinque. */
    private String[] nomiRoutine = { "Nessuna" };

    /** Il canale della sveglia e' a zero: suonerebbe in silenzio. */
    private boolean muta;

    private boolean davanti;

    private int premutoCosa = -1, premutoIndice = -1;
    private static final int P_SCHEDA = 0, P_AZIONE = 1, P_CESTINO = 2, P_GIORNO = 3,
            P_PRONTO = 4, P_RIGA = 5, P_PALLINO = 6, P_ROUTINE = 7, P_VOLUME = 8;

    /** Dove sta la pastiglia accesa delle due schede, in indici frazionari. */
    private final Anima.Inseguito scorrimentoScheda = new Anima.Inseguito(TIMER, 70f);

    /**
     * Il puntino del minuto e il conto alla rovescia si muovono da soli:
     * finche' la sezione e' davanti si ridisegna al secondo, e dietro si ferma.
     */
    private final Handler battito = new Handler();
    private final Runnable tic = new Runnable() {
        @Override public void run() {
            if (!davanti) return;
            componiTesti();
            invalidate();
            battito.postDelayed(this, 1000 - (System.currentTimeMillis() % 1000));
        }
    };

    public OrologioView(Context c, Misure misure, Ora ora, Luci luci) {
        super(c);
        this.m = misure;
        this.ora = ora;
        this.luci = luci;
        // Senza clickable Android non consegna ACTION_DOWN, e senza il DOWN non
        // arriva mai l'UP: la View sembra morta al tocco.
        setClickable(true);
        tasti = new Tasti(misure);
        pBordo.setStyle(Paint.Style.STROKE);
        pBordo.setStrokeWidth(1f);

        for (int i = 0; i < 7; i++) areeGiorni[i] = new RectF();
        for (int i = 0; i < areePronti.length; i++) {
            areePronti[i] = new RectF();
            rPastiglie[i] = new Testo.Riga();
        }
        for (int i = 0; i < righe.length; i++) {
            righe[i] = new RectF();
            pallini[i] = new RectF();
            rRighe[i] = new Testo.Riga();
            rSotto[i] = new Testo.Riga();
        }

        pTitolo.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pGrande.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
        pNota.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pSegno.setStyle(Paint.Style.STROKE);
        pSegno.setStrokeCap(Paint.Cap.ROUND);
        pSegno.setStrokeJoin(Paint.Join.ROUND);

        ghiera.setTinta(Tinte.OROLOGIO);
        ghiera.setCambio(new Quadrante.Cambio() {
            @Override public void suScatto() {
                // Mentre il dito gira la sveglia cambia solo a schermo, cosi' la
                // si vede muoversi anche nell'elenco. Su disco non si scrive.
                if (scheda == SVEGLIE && scelta != null) {
                    scelta.ore = ghiera.esterno();
                    scelta.minuti = ghiera.interno() * 5;
                }
                componiTesti();
                invalidate();
            }

            @Override public void suFermata() {
                // Dito alzato e ghiera agganciata: adesso si scrive e si
                // riprogramma la sveglia di Android.
                if (scheda == SVEGLIE && scelta != null) {
                    ora.regola(scelta.id, ghiera.esterno(), ghiera.interno() * 5);
                }
                componiTesti();
                invalidate();
            }
        });
        anelliDellaScheda();
        ghiera.vaiA(5, 0);

        ora.aggiungiAscolto(this);
        Vetro.chiedi(m, new Vetro.Pronto() {
            @Override public void vetroPronto(Vetro v) {
                vetro = v;
                v.adattaA(getWidth(), getHeight());
                invalidate();
            }
        });
    }

    public void setSchede(Schede s) {
        ascoltoSchede = s;
    }

    /**
     * La scheda che deve stare accesa, decisa da fuori: e' la voce della barra
     * che e' stata premuta. Fuori scena la pastiglia si mette al suo posto
     * senza scorrere, perche' nessuno la stava guardando partire.
     */
    public void mostra(boolean sveglie) {
        int voluta = sveglie ? SVEGLIE : TIMER;
        if (scheda == voluta) return;
        cambiaScheda(voluta);
        if (!davanti) scorrimentoScheda.subito(voluta);
    }

    /** La sezione e' andata davanti, o e' passata dietro. */
    public void attivo(boolean adesso) {
        if (davanti == adesso) return;
        davanti = adesso;
        battito.removeCallbacks(tic);
        if (davanti) {
            disponi();
            invalidate();
            battito.postDelayed(tic, 1000 - (System.currentTimeMillis() % 1000));
        }
    }

    @Override
    public void oraCambiata() {
        // La sveglia in mano puo' essere stata tolta: la ghiera la lascia.
        if (scelta != null && ora.trova(scelta.id) == null) scelta = null;
        muta = scheda == SVEGLIE && ora.muta();
        disponi();
        invalidate();
    }

    @Override
    public boolean hasOverlappingRendering() {
        return false;
    }

    @Override
    protected void onDetachedFromWindow() {
        super.onDetachedFromWindow();
        battito.removeCallbacks(tic);
    }

    // ---- misure ----

    @Override
    protected void onSizeChanged(int w, int h, int vw, int vh) {
        super.onSizeChanged(w, h, vw, vh);
        pAloneSezione.setShader(Vetro.aloneSezione(w, h, Tinte.OROLOGIO));
        if (vetro != null) vetro.adattaA(w, h);
        pTitolo.setTextSize(m.micro);
        pGrande.setTextSize(m.titolo);
        pNota.setTextSize(m.corpo);
        pSegno.setStrokeWidth(Math.max(2f, h * 0.004f));
        disponi();
    }

    /** L'altezza della fascia in cima: la stessa del deck, della Home e di Casa. */
    private float altaFascia() {
        return m.dp(52);
    }

    /** Il pannello in alto a destra c'e' nel modo timer, e nel modo sveglie se ci sono routine. */
    private boolean conPannelloAlto() {
        return scheda == TIMER || nomiRoutine.length > 1;
    }

    /**
     * Tutte le aree, rifatte a ogni cambio: la ghiera e' piu' alta nel modo
     * timer, che non ha i giorni sotto. Solo {@code RectF.set} su rettangoli gia'
     * fatti, quindi rifarla non costa.
     *
     * A sinistra la ghiera e il suo tasto, a destra quello che si preme al volo e
     * quello che e' gia' in piedi: due colonne uguali, come nel modello.
     */
    private void disponi() {
        leggiRoutine();
        int w = getWidth(), h = getHeight();
        if (w <= 0 || h <= 0) return;
        float mg = m.margine + m.s1;
        float testa = altaFascia();
        float gapCol = m.dp(22);
        float taglio = mg + (w - mg * 2f - gapCol) / 2f;
        xDestra = taglio + gapCol;
        larghezzaDestra = w - mg - xDestra;

        // Le due schede, nella fascia: un interruttore solo diviso in due.
        float altaScheda = m.dp(38);
        float largaScheda = m.dp(220);
        schede.set(mg, (testa - altaScheda) / 2f, mg + largaScheda, (testa + altaScheda) / 2f);
        for (int i = 0; i < 2; i++) {
            areeSchede[i].set(schede.left + schede.width() / 2f * i + m.dp(3), schede.top + m.dp(3),
                    schede.left + schede.width() / 2f * (i + 1) - m.dp(3), schede.bottom - m.dp(3));
        }

        // Il tasto in fondo, e il cestino accanto quando si regola una sveglia
        // che esiste gia': piccolo e in fondo, perche' e' l'unico gesto che non
        // si annulla.
        float altaAzione = m.dp(54);
        azione.set(mg, h - mg - altaAzione, taglio - altaAzione - m.s3, h - mg);
        cestino.set(taglio - altaAzione, azione.top, taglio, azione.bottom);

        // I sette giorni, sopra il tasto: iniziali e non nomi.
        float altaGiorni = m.dp(40);
        float gapG = m.dp(8);
        float passo = (taglio - mg + gapG) / 7f;
        float fondoGiorni = azione.top - m.s3;
        for (int i = 0; i < 7; i++) {
            areeGiorni[i].set(mg + passo * i, fondoGiorni - altaGiorni, mg + passo * (i + 1) - gapG, fondoGiorni);
        }

        // La ghiera prende quel che resta.
        float cimaGhiera = testa + m.s2;
        float fondoGhiera = (scheda == SVEGLIE ? areeGiorni[0].top : azione.top) - m.s3;
        float raggio = Math.min((taglio - mg) / 2f, (fondoGhiera - cimaGhiera) / 2f);
        ghiera.posiziona((mg + taglio) / 2f, (cimaGhiera + fondoGhiera) / 2f, raggio);

        // ---- la colonna di destra ----
        float yEtichetta = testa + m.s2;
        float cimaTasti = yEtichetta + m.micro + m.s3;
        float gap = m.s3;
        if (scheda == TIMER) {
            float alto = m.dp(78);
            float largo = (larghezzaDestra - gap * 2f) / 3f;
            for (int i = 0; i < PRONTI.length; i++) {
                float x = xDestra + (largo + gap) * (i % 3);
                float y = cimaTasti + (alto + gap) * (i / 3);
                areePronti[i].set(x, y, x + largo, y + alto);
            }
        } else {
            float alto = m.dp(86);
            float largo = (larghezzaDestra - gap * 3f) / 4f;
            for (int i = 0; i < areePronti.length; i++) {
                float x = xDestra + (largo + gap) * (i % 4);
                float y = cimaTasti + (alto + gap) * (i / 4);
                if (i < nomiRoutine.length) areePronti[i].set(x, y, x + largo, y + alto);
                else areePronti[i].setEmpty();
            }
        }
        float fondoTasti = cimaTasti;
        for (RectF r : areePronti) if (!r.isEmpty()) fondoTasti = Math.max(fondoTasti, r.bottom);
        pannelloPronti.set(xDestra, yEtichetta, w - mg, fondoTasti);

        // L'elenco: l'etichetta fuori, il piano sotto fino in fondo.
        yEtichettaElenco = conPannelloAlto() ? pannelloPronti.bottom + m.s5 : yEtichetta;
        pannelloElenco.set(xDestra, yEtichettaElenco + m.micro + m.s3, w - mg, h - mg);
        // Col volume a zero la cima del piano diventa un comando: « alza il volume ».
        float altaFascia = muta ? m.dp(44) : 0f;
        fasciaElenco.set(pannelloElenco.left + m.s2, pannelloElenco.top + m.s2,
                pannelloElenco.right - m.s2, pannelloElenco.top + m.s2 + altaFascia);

        altezzaRiga = Math.max(m.bersaglio * 1.1f, h * 0.098f);
        passoRiga = altezzaRiga + m.s2;
        float dentro = pannelloElenco.top + m.s3 + (muta ? altaFascia + m.s2 : 0f);
        int quante = scheda == TIMER ? MAX_TIMER : MAX_SVEGLIE;
        for (int i = 0; i < righe.length; i++) {
            float y = dentro + passoRiga * i;
            // Una riga che non ci sta non si disegna: meglio quattro intere che
            // cinque di cui l'ultima tagliata.
            if (i >= quante || y + altezzaRiga > pannelloElenco.bottom - m.s3) {
                righe[i].setEmpty();
                pallini[i].setEmpty();
                continue;
            }
            righe[i].set(pannelloElenco.left + m.s3, y, pannelloElenco.right - m.s3, y + altezzaRiga);
            float lato = Math.max(m.bersaglio, altezzaRiga * 0.66f);
            pallini[i].set(righe[i].right - m.s2 - lato, righe[i].centerY() - lato / 2f,
                    righe[i].right - m.s2, righe[i].centerY() + lato / 2f);
        }
        componiTesti();
    }

    /** I nomi delle routine delle luci, riletti a ogni cambio: il PC li puo' aver mandati. */
    private void leggiRoutine() {
        List<Routine> tutte = luci.routine();
        int quante = Math.min(tutte.size(), PRONTI.length - 1);
        if (nomiRoutine.length == quante + 1) {
            boolean uguali = true;
            for (int i = 0; i < quante && uguali; i++) {
                uguali = nomiRoutine[i + 1].equals(tutte.get(i).nome);
            }
            if (uguali) return;
        }
        String[] nomi = new String[quante + 1];
        nomi[0] = "Nessuna";
        for (int i = 0; i < quante; i++) nomi[i + 1] = tutte.get(i).nome;
        nomiRoutine = nomi;
    }

    /** Tutto quello che onDraw scrive, composto qui una volta per cambiamento. */
    private void componiTesti() {
        if (scheda == TIMER) {
            muta = false;
            int minuti = ghiera.esterno(), ore = ghiera.interno();
            if (ore > 0) {
                testoCentro = String.format(Locale.ITALIAN, "%d:%02d", ore, minuti);
                testoUnita = "ore e minuti";
            } else {
                testoCentro = String.valueOf(minuti);
                testoUnita = minuti == 1 ? "minuto" : "minuti";
            }
            testoAzione = "Avvia";
            int quanti = ora.conti().size();
            testoCima = quanti == 0 ? "nessun timer in corso"
                    : quanti == 1 ? "un timer in corso" : quanti + " timer in corso";
        } else {
            testoCentro = String.format(Locale.ITALIAN, "%02d:%02d",
                    ghiera.esterno(), ghiera.interno() * 5);
            testoUnita = quando(scelta != null ? scelta.giorni : giorniNuova);
            testoAzione = scelta != null ? "Fatto" : "Aggiungi sveglia";
            // Il volume a zero vince sulla prossima sveglia: e' la cosa da
            // sapere adesso invece che domattina, e il rimedio sta qui.
            muta = ora.muta();
            String prossima = ora.prossimaSveglia();
            testoCima = muta ? "volume della sveglia a zero: tocca qui per alzarlo"
                    : prossima != null ? "prossima " + prossima : "nessuna sveglia accesa";
        }

        List<Ora.Conto> conti = ora.conti();
        List<Ora.Sveglia> sveglie = ora.sveglie();
        long adesso = System.currentTimeMillis();
        for (int i = 0; i < testoRiga.length; i++) {
            if (scheda == TIMER && i < MAX_TIMER && i < conti.size()) {
                Ora.Conto t = conti.get(i);
                testoRiga[i] = Ora.scorrere(t.restano());
                testoSotto[i] = "di " + t.nome();
            } else if (scheda == SVEGLIE && i < MAX_SVEGLIE && i < sveglie.size()) {
                Ora.Sveglia s = sveglie.get(i);
                testoRiga[i] = s.orario();
                String sotto = s.rinvio > adesso ? "rimandata di " + Ora.RINVIO_MIN + " minuti"
                        : s.attiva ? s.quando() : "spenta";
                testoSotto[i] = s.routine.length() > 0 ? sotto + " · luci: " + s.routine : sotto;
            } else {
                testoRiga[i] = null;
                testoSotto[i] = null;
            }
        }
    }

    /** Gli stessi giorni detti in una riga, per la scritta sotto la ghiera. */
    private static String quando(int giorni) {
        if (giorni == 0) return "una volta sola";
        if (giorni == 0x7F) return "tutti i giorni";
        if (giorni == 0x3E) return "da lunedi' a venerdi'";
        if (giorni == 0x41) return "sabato e domenica";
        return "nei giorni scelti";
    }

    private void anelliDellaScheda() {
        if (scheda == TIMER) ghiera.anelli(ETICHETTE_MIN60, ETICHETTE_ORE6);
        else ghiera.anelli(ETICHETTE_ORE, ETICHETTE_MIN5);
    }

    private void cambiaScheda(int voluta) {
        scheda = voluta;
        muta = scheda == SVEGLIE && ora.muta();
        scelta = null;
        anelliDellaScheda();
        // Si parte da un valore che si usa davvero - cinque minuti, le sette -
        // invece che dallo zero, che andrebbe comunque cambiato.
        if (scheda == TIMER) ghiera.vaiA(5, 0);
        else ghiera.vaiA(7, 0);
        disponi();
        invalidate();
    }

    // ---- disegno ----

    @Override
    protected void onDraw(Canvas c) {
        if (vetro == null || !vetro.vivo()) {
            // Il vetro si sta componendo: un paio di decimi di secondo, al
            // primo avvio. Meglio il fondo giusto che un rettangolo nero.
            c.drawColor(Tinte.FONDO);
            return;
        }
        vetro.disegnaSfondo(c, getWidth(), getHeight());
        // La luce ambra della sezione: l'Orologio era la schermata piu' scura.
        c.drawRect(0, 0, getWidth(), getHeight(), pAloneSezione);

        // L'arco che si consuma e' il primo timer che scade: fa vedere quanto
        // e' passato senza che nessuno legga un numero.
        float frazione = -1f;
        Ora.Conto primo = ora.primoConto();
        if (scheda == TIMER && primo != null && primo.durata > 0) {
            frazione = Math.max(0f, Math.min(1f, primo.restano() / (float) primo.durata));
        }
        ghiera.disegna(c, testoCentro, testoUnita, frazione);

        disegnaSchede(c);

        if (scheda == SVEGLIE) {
            for (int i = 0; i < 7; i++) disegnaGiorno(c, i);
            disegnaCestino(c);
            if (conPannelloAlto()) disegnaPannelloRoutine(c);
        } else {
            disegnaPannelloPronti(c);
        }
        disegnaAzione(c);
        disegnaPannelloElenco(c);
    }

    /**
     * Le due schede, nella fascia in cima: dentro un incavo scorre il tasto
     * acceso. Lo scorrimento e' quello che dice che sono due facce della stessa
     * cosa, e non due comandi che si accendono uno alla volta.
     */
    private void disegnaSchede(Canvas c) {
        float r = Math.min(schede.height() * 0.32f, m.dp(12));
        pPiano.setColor(Tinte.FONDO_BASSO);
        c.drawRoundRect(schede, r, r, pPiano);
        pBordo.setColor(Tinte.PIANO_BORDO);
        c.drawRoundRect(schede, r, r, pBordo);

        scorrimentoScheda.vaiA(scheda);
        if (scorrimentoScheda.passo()) postInvalidateOnAnimation();
        float dove = scorrimentoScheda.valore();
        float meta = schede.width() / 2f;
        float x = schede.left + m.dp(3) + meta * dove;
        RectF acceso = vetro.area(x, schede.top + m.dp(3), x + meta - m.dp(6), schede.bottom - m.dp(3));
        vetro.tasto(c, acceso, r - m.dp(2), 0, 0f);

        for (int i = 0; i < 2; i++) {
            RectF b = areeSchede[i];
            float vicinanza = Math.max(0f, 1f - Math.abs(dove - i));
            int colore = Tinte.fondi(Tinte.TESTO_TENUE, Tinte.TESTO, vicinanza);
            if (premutoCosa == P_SCHEDA && premutoIndice == i) colore = Tinte.TESTO;
            float lato = m.dp(15);
            pNota.setTextSize(m.dp(14));
            float largoTesto = pNota.measureText(NOMI_SCHEDE[i]);
            float partenza = b.centerX() - (lato + m.s2 + largoTesto) / 2f;
            pSegno.setColor(colore);
            Pittogrammi.disegna(c, i == TIMER ? "timer" : "alarm-clock", partenza + lato / 2f, b.centerY(), lato, pSegno);
            pNota.setColor(colore);
            c.drawText(NOMI_SCHEDE[i], partenza + lato + m.s2,
                    b.centerY() - (pNota.descent() + pNota.ascent()) / 2f, pNota);
        }
    }

    /** Il tasto quando non c'e' il cestino accanto: si prende anche il suo posto. */
    private RectF pieno() {
        interoAzione.set(azione.left, azione.top, cestino.right, azione.bottom);
        return interoAzione;
    }

    private RectF areaAzione() {
        return scheda == SVEGLIE && scelta != null ? azione : pieno();
    }

    /** Il tasto ambra, con l'icona di quel che fa: si preme senza leggere. */
    private void disegnaAzione(Canvas c) {
        boolean giu = premutoCosa == P_AZIONE;
        // Nel modo timer, con lo zero sotto l'indicatore il tasto e' spento: un
        // « avvia » che non avvia niente e' un tasto rotto.
        boolean vivo = scheda == SVEGLIE || ghiera.esterno() > 0 || ghiera.interno() > 0;
        RectF b = areaAzione();
        tasti.fondo(c, vetro, b, Tinte.OROLOGIO, vivo ? 1f : 0f, giu);
        String icona = scheda == TIMER ? "play" : scelta != null ? "check" : "alarm-clock-plus";
        tasti.inRiga(c, b, -1, icona, null, testoAzione, vivo);
    }

    private void disegnaCestino(Canvas c) {
        if (scelta == null) return;
        tasti.fondo(c, vetro, cestino, Tinte.ALLARME, 0.5f, premutoCosa == P_CESTINO);
        tasti.inRiga(c, cestino, -1, "trash-2", null, "", true);
    }

    private void disegnaGiorno(Canvas c, int i) {
        RectF b = areeGiorni[i];
        int giorni = scelta != null ? scelta.giorni : giorniNuova;
        boolean acceso = (giorni & (1 << (Ora.ORDINE_GIORNI[i] - 1))) != 0;
        boolean giu = premutoCosa == P_GIORNO && premutoIndice == i;
        // Il giorno acceso e' un ambra scuro: l'ambra pieno e' del tasto qui sotto.
        tasti.fondo(c, vetro, b, Tinte.OROLOGIO, acceso ? 0.36f : 0f, giu);
        pNota.setColor(acceso ? Tinte.TESTO : Tinte.TESTO_TENUE);
        pNota.setTextSize(m.dp(14));
        pNota.setTextAlign(Paint.Align.CENTER);
        c.drawText(INIZIALI[i], b.centerX(), b.centerY() - (pNota.descent() + pNota.ascent()) / 2f, pNota);
        pNota.setTextAlign(Paint.Align.LEFT);
    }

    /** Le durate pronte: l'etichetta, e sotto i sei tasti. */
    private void disegnaPannelloPronti(Canvas c) {
        etichetta(c, "AVVIO RAPIDO", pannelloPronti.left, pannelloPronti.top, Tinte.TESTO_TENUE);
        for (int i = 0; i < PRONTI.length; i++) disegnaPronto(c, i);
    }

    /** Lo stesso posto nel modo sveglie: le routine delle luci, a tasti. */
    private void disegnaPannelloRoutine(Canvas c) {
        etichetta(c, "LUCI QUANDO SUONA", pannelloPronti.left, pannelloPronti.top, Tinte.TESTO_TENUE);
        String voluta = scelta != null ? scelta.routine : routineNuova;
        List<Routine> tutte = luci.routine();
        for (int i = 0; i < nomiRoutine.length; i++) {
            RectF b = areePronti[i];
            if (b.isEmpty()) continue;
            boolean acceso = i == 0 ? voluta.length() == 0 : nomiRoutine[i].equals(voluta);
            boolean giu = premutoCosa == P_ROUTINE && premutoIndice == i;
            tasti.fondo(c, vetro, b, Tinte.OROLOGIO, acceso ? 0.36f : 0f, giu);
            int tratto = -1;
            if (i > 0) {
                for (Routine r : tutte) if (r.nome.equals(nomiRoutine[i])) tratto = CasaView.icona(r);
            }
            tasti.contenuto(c, b, tratto, i == 0 ? "ban" : null, null, nomiRoutine[i], null, acceso);
        }
    }

    /** L'elenco di quel che e' gia' in piedi: l'etichetta fuori, le righe dentro il piano. */
    private void disegnaPannelloElenco(Canvas c) {
        etichetta(c, scheda == TIMER ? "IN CORSO" : "SVEGLIE", pannelloElenco.left, yEtichettaElenco,
                Tinte.TESTO_TENUE);
        vetro.pannello(c, pannelloElenco, m.raggio, Tinte.OROLOGIO, 0x10);
        if (muta) {
            tasti.fondo(c, vetro, fasciaElenco, Tinte.ALLARME, 0.6f, premutoCosa == P_VOLUME);
            tasti.inRiga(c, fasciaElenco, -1, "volume-x", null, "Volume della sveglia a zero: alzalo", true);
        }

        boolean niente = true;
        for (int i = 0; i < righe.length; i++) {
            if (righe[i].isEmpty() || testoRiga[i] == null) continue;
            niente = false;
            if (scheda == TIMER) disegnaRigaTimer(c, i);
            else disegnaRigaSveglia(c, i);
        }

        // Il vuoto si dice una volta sola, con l'icona di quello che ci andrebbe.
        if (niente) {
            float cx = pannelloElenco.centerX();
            float cy = pannelloElenco.centerY();
            pSegno.setColor(Tinte.TESTO_TENUE);
            Pittogrammi.disegna(c, scheda == TIMER ? "hourglass" : "alarm-clock", cx, cy - m.dp(14), m.dp(22), pSegno);
            pNota.setColor(Tinte.TESTO_TENUE);
            pNota.setTextSize(m.dp(14));
            pNota.setTextAlign(Paint.Align.CENTER);
            c.drawText(scheda == TIMER ? "Nessun timer" : "Nessuna sveglia", cx, cy + m.dp(20), pNota);
            pNota.setTextAlign(Paint.Align.LEFT);
        }
    }

    /** Il titolino in maiuscolo sopra un gruppo, sempre allo stesso posto. */
    private void etichetta(Canvas c, String testo, float x, float y, int colore) {
        pTitolo.setColor(colore);
        pTitolo.setTextSize(m.micro);
        c.drawText(Testo.taglia(pTitolo, testo, larghezzaDestra), x + m.s1, y + m.micro, pTitolo);
    }

    /** Una durata pronta: il numero grande, e « min » sotto, piu' piccolo. */
    private void disegnaPronto(Canvas c, int i) {
        RectF b = areePronti[i];
        boolean giu = premutoCosa == P_PRONTO && premutoIndice == i;
        tasti.fondo(c, vetro, b, 0, 0f, giu);

        pGrande.setTextSize(Math.min(m.dp(30), b.height() * 0.4f));
        pGrande.setColor(Tinte.TESTO);
        pGrande.setTextAlign(Paint.Align.CENTER);
        pNota.setTextSize(m.dp(11));
        pNota.setColor(Tinte.TESTO_TENUE);
        pNota.setTextAlign(Paint.Align.CENTER);
        float alto = -pGrande.ascent() * 0.75f + m.s1 + m.dp(11);
        float base = b.centerY() - alto / 2f - pGrande.ascent() * 0.75f;
        c.drawText(NOMI_PRONTI[i], b.centerX(), base, pGrande);
        c.drawText("min", b.centerX(), base + m.s1 + m.dp(11), pNota);
        pGrande.setTextAlign(Paint.Align.LEFT);
        pNota.setTextAlign(Paint.Align.LEFT);
    }

    private void disegnaRigaTimer(Canvas c, int i) {
        RectF b = righe[i];
        List<Ora.Conto> conti = ora.conti();
        if (i >= conti.size()) return;
        long restano = conti.get(i).restano();
        // Sotto il minuto la riga si accende: e' il momento di guardarla.
        boolean quasi = restano <= 60;
        vetro.pannello(c, b, m.raggioPiccolo, Tinte.OROLOGIO, quasi ? 0x4E : 0x1A);

        float lato = m.icona;
        float x = b.left + m.s3;
        pSegno.setColor(quasi ? Tinte.OROLOGIO : Tinte.TESTO_TENUE);
        Simboli.disegna(c, Simboli.TIMER, x + lato / 2f, b.centerY(), lato, pSegno);

        x += lato + m.s3;
        float largo = pallini[i].left - x - m.s2;
        pGrande.setColor(quasi ? Tinte.OROLOGIO : Tinte.TESTO);
        c.drawText(rRighe[i].adatta(pGrande, testoRiga[i], largo, m.titolo, m.titolo * 0.66f),
                x, b.centerY() - m.s1 * 0.2f, pGrande);

        pNota.setColor(Tinte.TESTO_TENUE);
        c.drawText(rSotto[i].adatta(pNota, testoSotto[i], largo, m.nota, m.nota),
                x, b.bottom - m.s2, pNota);

        // La croce, che qui prende il posto dell'interruttore.
        RectF p = pallini[i];
        if (premutoCosa == P_PALLINO && premutoIndice == i) {
            vetro.pannello(c, p, p.width() * 0.5f, Tinte.OROLOGIO, 0x4E);
        }
        pSegno.setColor(Tinte.TESTO_MEDIO);
        Simboli.disegna(c, Simboli.CHIUDI, p.centerX(), p.centerY(), m.icona * 0.86f, pSegno);
    }

    /**
     * Una sveglia. Quella che la ghiera sta regolando porta un filo di luce a
     * sinistra: senza, girando si vedrebbe cambiare una riga e non si saprebbe
     * perche' proprio quella.
     */
    private void disegnaRigaSveglia(Canvas c, int i) {
        RectF b = righe[i];
        List<Ora.Sveglia> sveglie = ora.sveglie();
        if (i >= sveglie.size()) return;
        Ora.Sveglia s = sveglie.get(i);
        boolean inMano = s == scelta;
        boolean giu = premutoCosa == P_RIGA && premutoIndice == i;
        vetro.pannello(c, b, m.raggioPiccolo, Tinte.OROLOGIO,
                inMano ? 0x4E : (s.attiva ? 0x26 : (giu ? 0x22 : 0x10)));

        if (inMano) {
            pSegno.setStyle(Paint.Style.FILL);
            pSegno.setColor(Tinte.OROLOGIO);
            lineetta.set(b.left, b.top + b.height() * 0.2f,
                    b.left + m.dp(3.5f), b.bottom - b.height() * 0.2f);
            c.drawRoundRect(lineetta, m.dp(2), m.dp(2), pSegno);
            pSegno.setStyle(Paint.Style.STROKE);
        }

        float x = b.left + m.s3;
        float largo = pallini[i].left - x - m.s2;
        pGrande.setColor(s.attiva ? Tinte.TESTO : Tinte.SPENTO);
        c.drawText(rRighe[i].adatta(pGrande, testoRiga[i], largo, m.titolo, m.titolo * 0.66f),
                x, b.centerY() - m.s1 * 0.2f, pGrande);

        pNota.setColor(s.attiva ? Tinte.TESTO_TENUE : Tinte.SPENTO);
        c.drawText(rSotto[i].adatta(pNota, testoSotto[i], largo, m.nota, m.nota),
                x, b.bottom - m.s2, pNota);

        // L'interruttore: una levetta che scorre, senza un momento speso a
        // capire se il pallino vuoto vuol dire spenta o « non lo so ».
        RectF p = pallini[i];
        float alta = Math.min(p.height() * 0.56f, m.icona * 1.05f);
        float larga = alta * 1.75f;
        RectF lev = vetro.area(p.centerX() - larga / 2f, p.centerY() - alta / 2f,
                p.centerX() + larga / 2f, p.centerY() + alta / 2f);
        vetro.pannello(c, lev, alta * 0.5f, Tinte.OROLOGIO,
                s.attiva ? 0x7A : (premutoCosa == P_PALLINO && premutoIndice == i ? 0x3A : 0x18));
        pSegno.setStyle(Paint.Style.FILL);
        pSegno.setColor(s.attiva ? Tinte.TESTO : Tinte.SPENTO);
        float r = alta * 0.34f;
        c.drawCircle(s.attiva ? lev.right - alta * 0.5f : lev.left + alta * 0.5f,
                lev.centerY(), r, pSegno);
        pSegno.setStyle(Paint.Style.STROKE);
    }

    // ---- tocco ----

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        float x = e.getX(), y = e.getY();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                // La ghiera ha la precedenza: chi la tocca sta girando.
                if (ghiera.giu(x, y)) return true;
                trova(x, y);
                invalidate();
                return true;

            case MotionEvent.ACTION_MOVE:
                if (ghiera.staTrascinando()) ghiera.muovi(x, y);
                return true;

            case MotionEvent.ACTION_UP: {
                if (ghiera.staTrascinando()) {
                    ghiera.su();
                    return true;
                }
                int cosa = premutoCosa, indice = premutoIndice;
                trova(x, y);
                boolean stessoPosto = cosa == premutoCosa && indice == premutoIndice && cosa >= 0;
                premutoCosa = premutoIndice = -1;
                if (stessoPosto) agisci(cosa, indice);
                invalidate();
                return true;
            }

            case MotionEvent.ACTION_CANCEL:
                // Un trascinamento interrotto si aggancia comunque, se no la
                // ghiera resterebbe fra due valori.
                if (ghiera.staTrascinando()) ghiera.su();
                premutoCosa = premutoIndice = -1;
                invalidate();
                return true;
        }
        return super.onTouchEvent(e);
    }

    private void trova(float x, float y) {
        premutoCosa = premutoIndice = -1;

        for (int i = 0; i < 2; i++) {
            if (areeSchede[i].contains(x, y)) {
                premutoCosa = P_SCHEDA;
                premutoIndice = i;
                return;
            }
        }
        if (scheda == SVEGLIE) {
            if (scelta != null && cestino.contains(x, y)) {
                premutoCosa = P_CESTINO;
                return;
            }
            for (int i = 0; i < 7; i++) {
                if (areeGiorni[i].contains(x, y)) {
                    premutoCosa = P_GIORNO;
                    premutoIndice = i;
                    return;
                }
            }
            if (conPannelloAlto()) {
                for (int i = 0; i < nomiRoutine.length; i++) {
                    if (areePronti[i].contains(x, y)) {
                        premutoCosa = P_ROUTINE;
                        premutoIndice = i;
                        return;
                    }
                }
            }
            if (muta && fasciaElenco.contains(x, y)) {
                premutoCosa = P_VOLUME;
                return;
            }
        } else {
            for (int i = 0; i < areePronti.length; i++) {
                if (areePronti[i].contains(x, y)) {
                    premutoCosa = P_PRONTO;
                    premutoIndice = i;
                    return;
                }
            }
        }
        if (areaAzione().contains(x, y)) {
            premutoCosa = P_AZIONE;
            return;
        }

        // L'interruttore prima della riga che lo contiene, se no vince sempre la riga.
        for (int i = 0; i < pallini.length; i++) {
            if (!pallini[i].isEmpty() && pallini[i].contains(x, y)) {
                premutoCosa = P_PALLINO;
                premutoIndice = i;
                return;
            }
        }
        for (int i = 0; i < righe.length; i++) {
            if (!righe[i].isEmpty() && righe[i].contains(x, y)) {
                premutoCosa = P_RIGA;
                premutoIndice = i;
                return;
            }
        }
    }

    private void agisci(int cosa, int i) {
        List<Ora.Conto> conti = ora.conti();
        List<Ora.Sveglia> sveglie = ora.sveglie();
        switch (cosa) {
            case P_SCHEDA:
                if (scheda != i) {
                    cambiaScheda(i);
                    // L'activity accende la voce giusta nella barra.
                    if (ascoltoSchede != null) ascoltoSchede.suScheda(i == SVEGLIE);
                }
                break;

            case P_AZIONE:
                if (scheda == TIMER) {
                    int durata = ghiera.interno() * 3600 + ghiera.esterno() * 60;
                    if (durata > 0) ora.avviaTimer(durata);
                } else if (scelta != null) {
                    // « Fatto »: la sveglia era gia' salvata a ogni aggancio,
                    // qui si lascia soltanto la presa.
                    scelta = null;
                } else {
                    Ora.Sveglia nuova = ora.aggiungiSveglia(ghiera.esterno(),
                            ghiera.interno() * 5, giorniNuova);
                    if (routineNuova.length() > 0) ora.setRoutine(nuova.id, routineNuova);
                    scelta = nuova;
                }
                disponi();
                break;

            case P_CESTINO:
                if (scelta != null) {
                    int via = scelta.id;
                    scelta = null;
                    ora.togliSveglia(via);
                    disponi();
                }
                break;

            case P_GIORNO: {
                int giornoDellaSettimana = Ora.ORDINE_GIORNI[i];
                if (scelta != null) ora.inverti(scelta.id, giornoDellaSettimana);
                else giorniNuova ^= 1 << (giornoDellaSettimana - 1);
                componiTesti();
                break;
            }

            case P_PRONTO:
                ora.avviaTimer(PRONTI[i] * 60);
                disponi();
                break;

            case P_ROUTINE: {
                if (i >= nomiRoutine.length) break;
                String nome = i == 0 ? "" : nomiRoutine[i];
                if (scelta != null) ora.setRoutine(scelta.id, nome);
                else routineNuova = nome;
                componiTesti();
                break;
            }

            case P_VOLUME:
                ora.alzaVolume();
                break;

            case P_PALLINO:
                if (scheda == TIMER) {
                    if (i < conti.size()) ora.fermaTimer(conti.get(i).id);
                } else if (i < sveglie.size()) {
                    Ora.Sveglia s = sveglie.get(i);
                    ora.accendi(s.id, !s.attiva);
                }
                disponi();
                break;

            case P_RIGA:
                // Toccare una sveglia la porta nella ghiera; toccarla di nuovo la lascia.
                if (scheda == SVEGLIE && i < sveglie.size()) {
                    Ora.Sveglia s = sveglie.get(i);
                    scelta = (s == scelta) ? null : s;
                    if (scelta != null) ghiera.vaiA(scelta.ore, scelta.minuti / 5);
                    componiTesti();
                }
                break;
        }
    }
}
