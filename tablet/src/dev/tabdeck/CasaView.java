package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * La sezione Casa: le scene a sinistra, le luci stanza per stanza a destra.
 *
 * <h3>La divisione</h3>
 *
 * In cima una fascia sola: quante luci ci sono e quante sono accese — la cosa che
 * si legge passando davanti al tablet — con « Tutte spente » e la freccia che
 * rilegge. Sotto, due pannelli affiancati. <b>A sinistra le scene</b>, pastiglie
 * con l'icona di quel che fanno e un nome corto: la colonna e' stretta apposta,
 * allargarla non le rende ne' piu' leggibili ne' piu' premibili. <b>A destra le
 * luci</b>, raggruppate per stanza quando il PC le ha date, ognuna una tessera.
 *
 * Il vuoto non si toglie ingrandendo le tessere — tessere da mezzo schermo per un
 * nome e una parola — ma dandogli un bordo: dentro un pannello con un titolo, lo
 * spazio che avanza si legge come posto per le prossime luci.
 *
 * <h3>Una tessera per luce</h3>
 *
 * Toccata, la luce si accende o si spegne: e' quello che si fa nove volte su dieci.
 * La barra in fondo e' la luminosita' e si trascina; il valore arriva alla lampada a
 * dito alzato, perche' una lampada Tuya accetta un comando alla volta. L'angolo in
 * alto a destra apre il dettaglio, a pagina intera: colori, bianchi, luminosita'.
 *
 * <h3>I testi stanno nei loro bordi</h3>
 *
 * Ogni scritta dentro un pulsante passa da {@link Testo.Riga}: prima si fa piu'
 * piccola fino a un minimo, e solo se non basta si taglia. I pulsanti con una
 * parola dentro — l'interruttore di una stanza, « Tutte spente » — sono larghi
 * quanto la parola misurata, non una frazione indovinata.
 */
public final class CasaView extends View implements Luci.Ascolto {

    /** Il colore della sezione: il verde di TabDeck, piu' morbido. */
    static final int TINTA = 0xFF6FD6A6;

    private static final int[] TINTE = {
            0xFFFF2D2D, 0xFFFF6A00, 0xFFFFB300, 0xFFFFE94D, 0xFF8CD832, 0xFF2ED573,
            0xFF1ABC9C, 0xFF00C2FF, 0xFF2E7CF6, 0xFF6C5CE7, 0xFFB14BFF, 0xFFFF4FA3,
    };
    private static final int[] BIANCHI = { 0, 50, 100 };
    private static final String[] NOMI_BIANCHI = { "Caldo", "Neutro", "Freddo" };

    private final Misure m;
    private final Luci luci;
    private Vetro vetro;

    private final Paint pTitolo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pNome = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pNota = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pGrande = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pSegno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pPieno = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final int soglia;
    private final Tasti tasti;
    private final RectF pieno = new RectF();
    /** La luce verde della sezione, in alto: fatta sulle misure. */
    private final Paint pAlone = new Paint();
    /** Dove cominciano le scene e le luci, nella pagina che scorre. */
    private float yScene, yLuci;

    // ---- la fascia in cima ----
    private final RectF fascia = new RectF();
    private final RectF tutteSpente = new RectF();
    private final RectF aggiorna = new RectF();

    // ---- le scene ----
    private final RectF pannelloScene = new RectF();
    private final List<Routine> tessere = new ArrayList<Routine>();
    private final List<RectF> areeTessere = new ArrayList<RectF>();
    private final List<Testo.Riga> righeTessere = new ArrayList<Testo.Riga>();

    // ---- le luci ----
    private final RectF pannelloLuci = new RectF();
    private final RectF areaLuci = new RectF();

    private static final class Stanza {
        final String nome;
        final List<Lampada> lampade = new ArrayList<Lampada>();
        final RectF testa = new RectF();
        final RectF interruttore = new RectF();

        Stanza(String nome) {
            this.nome = nome;
        }
    }

    private static final class Tessera {
        final Lampada lampada;
        final RectF area = new RectF();
        final RectF barra = new RectF();
        final RectF regola = new RectF();
        final Testo.Riga nome = new Testo.Riga();
        final Testo.Riga stato = new Testo.Riga();

        Tessera(Lampada l) {
            lampada = l;
        }
    }

    private final List<Stanza> stanze = new ArrayList<Stanza>();
    private final List<Tessera> tessereLuci = new ArrayList<Tessera>();
    private boolean conStanze;
    private float altezzaLuci;
    private float scorri;

    // ---- il dettaglio, a pagina intera ----
    private Lampada aperta;
    private final RectF dettaglio = new RectF();
    private final RectF chiudi = new RectF();
    private final RectF interruttoreDettaglio = new RectF();
    private final RectF barraDettaglio = new RectF();
    private final RectF[] areeTinte = new RectF[TINTE.length];
    private final RectF[] areeBianchi = new RectF[BIANCHI.length + 1];
    private final Testo.Riga rTitoloDettaglio = new Testo.Riga();

    // ---- il dito ----
    private static final int P_SCENA = 0, P_TUTTE = 1, P_AGGIORNA = 2, P_STANZA = 3, P_LUCE = 4,
            P_BARRA = 5, P_REGOLA = 6, P_CHIUDI = 7, P_ACCENDI = 8, P_BARRA_DETTAGLIO = 9,
            P_TINTA = 10, P_BIANCO = 11, P_SCORRI = 12;
    private int premuto = -1, indice = -1;
    private float giuX, giuY, scorriGiu;
    /** La luminosita' che il dito sta scegliendo, o -1: si disegna prima che la lampada risponda. */
    private int trascinata = -1;

    public CasaView(Context c, Misure misure, Luci luci) {
        super(c);
        m = misure;
        this.luci = luci;
        setClickable(true);
        soglia = ViewConfiguration.get(c).getScaledTouchSlop();
        tasti = new Tasti(misure);

        pTitolo.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pNome.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pNota.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pGrande.setTypeface(Typeface.create("sans-serif-light", Typeface.NORMAL));
        for (int i = 0; i < areeTinte.length; i++) areeTinte[i] = new RectF();
        for (int i = 0; i < areeBianchi.length; i++) areeBianchi[i] = new RectF();

        Vetro.chiedi(m, new Vetro.Pronto() {
            @Override public void vetroPronto(Vetro v) {
                vetro = v;
                v.adattaA(getWidth(), getHeight());
                invalidate();
            }
        });
    }

    /** Quando la sezione torna in vista: si rilegge, ma solo allora. */
    public void risveglia() {
        luci.aggiorna();
        luci.scopri();
    }

    /** Il tasto indietro: chiude il dettaglio, se e' aperto. */
    public boolean indietro() {
        if (aperta == null) return false;
        aperta = null;
        invalidate();
        return true;
    }

    @Override
    public void luceCambiata(Lampada l) {
        invalidate();
    }

    @Override
    public void elencoCambiato() {
        if (aperta != null && !luci.elenco().contains(aperta)) aperta = null;
        disponi();
        invalidate();
    }

    @Override
    public boolean hasOverlappingRendering() {
        return false;
    }

    // ---- misure ----

    @Override
    protected void onSizeChanged(int w, int h, int vw, int vh) {
        super.onSizeChanged(w, h, vw, vh);
        if (vetro != null) vetro.adattaA(w, h);
        pAlone.setShader(Vetro.aloneSezione(w, h, TINTA));
        disponi();
    }

    /** L'altezza della fascia in cima: la stessa del deck e della Home. */
    private float altaFascia() {
        return m.dp(52);
    }

    /**
     * La pagina di Casa: le scene in una fila di tessere larghe, a tutta riga,
     * poi le luci in tessere grandi - per stanza quando il PC le ha date. Le
     * tessere crescono per riempire la pagina: due luci e tre scene non sono un
     * angolo pieno e mezzo schermo vuoto.
     */
    private void disponi() {
        int w = getWidth(), h = getHeight();
        if (w <= 0 || h <= 0) return;
        float mg = m.margine + m.s1;

        // ---- la fascia: il nome e il riepilogo, a destra « rileggi » e « Tutte spente » ----
        float testa = altaFascia();
        fascia.set(mg, 0, w - mg, testa);
        float altaChip = Math.max(m.dp(34), m.bersaglio * 0.8f);
        float yc = (testa - altaChip) / 2f;
        float largaTutte = tasti.larghezzaChip("Tutte spente", altaChip);
        tutteSpente.set(fascia.right - largaTutte, yc, fascia.right, yc + altaChip);
        aggiorna.set(tutteSpente.left - m.s2 - altaChip, yc, tutteSpente.left - m.s2, yc + altaChip);

        areaLuci.set(0, testa, w, h);
        pannelloScene.setEmpty();
        pannelloLuci.set(mg, testa, w - mg, h - mg);
        float larga = w - mg * 2f;
        float gap = m.s3;
        float altaEtichetta = m.micro + m.s3;

        // ---- le scene: una fila larga, fino a quattro per riga ----
        tessere.clear();
        for (Routine r : luci.routine()) if (r.scena) tessere.add(r);
        for (Routine r : luci.routine()) if (!r.scena) tessere.add(r);
        int colScene = Math.max(1, Math.min(4, tessere.size()));
        float largaScena = (larga - gap * (colScene - 1)) / colScene;
        float altaScena = m.dp(92);
        int righeScene = (tessere.size() + colScene - 1) / colScene;

        // ---- le luci, per stanza quando il PC le ha date ----
        stanze.clear();
        tessereLuci.clear();
        Map<String, Stanza> perNome = new LinkedHashMap<String, Stanza>();
        Stanza senza = null;
        for (Lampada l : luci.elenco()) {
            String nome = l.stanza.trim();
            if (nome.length() == 0) {
                if (senza == null) senza = new Stanza("");
                senza.lampade.add(l);
                continue;
            }
            String chiave = nome.toLowerCase(Locale.ITALIAN);
            Stanza s = perNome.get(chiave);
            if (s == null) {
                s = new Stanza(nome);
                perNome.put(chiave, s);
            }
            s.lampade.add(l);
        }
        stanze.addAll(perNome.values());
        if (senza != null) stanze.add(senza);
        conStanze = !perNome.isEmpty();

        // Tre luci per riga, due se sono poche: tessere larghe, non francobolli.
        int quanteLuci = luci.elenco().size();
        int colLuci = quanteLuci <= 2 ? 2 : 3;
        float largaLuce = (larga - gap * (colLuci - 1)) / colLuci;
        int righeLuci = 0;
        for (Stanza s : stanze) righeLuci += (s.lampade.size() + colLuci - 1) / colLuci;
        float testeStanze = conStanze ? stanze.size() * (m.dp(30) + m.s3) : altaEtichetta;

        // L'altezza delle luci: quella che riempie la pagina, fra un minimo e un massimo.
        float occupato = testa + m.s2 + (tessere.isEmpty() ? 0 : altaEtichetta + righeScene * altaScena
                + (righeScene - 1) * gap + m.s5) + testeStanze + mg;
        float resta = h - occupato - Math.max(0, righeLuci - 1) * gap - Math.max(0, stanze.size() - 1) * m.s5;
        float altaLuce = righeLuci == 0 ? m.dp(130)
                : Math.max(m.dp(118), Math.min(m.dp(290), resta / righeLuci));

        float y = testa + m.s2;
        areeTessere.clear();
        if (!tessere.isEmpty()) {
            yScene = y;
            y += altaEtichetta;
            for (int i = 0; i < tessere.size(); i++) {
                float x = mg + (largaScena + gap) * (i % colScene);
                float yy = y + (altaScena + gap) * (i / colScene);
                areeTessere.add(new RectF(x, yy, x + largaScena, yy + altaScena));
            }
            y += righeScene * altaScena + (righeScene - 1) * gap + m.s5;
        }

        yLuci = y;
        if (!conStanze) y += altaEtichetta;
        for (Stanza s : stanze) {
            if (conStanze) {
                float altaInt = m.dp(30);
                s.testa.set(mg, y, w - mg, y + altaInt);
                float largaInt = Math.max(tasti.larghezzaChip("Spegni", altaInt), tasti.larghezzaChip("Accendi", altaInt));
                s.interruttore.set(w - mg - largaInt, y, w - mg, y + altaInt);
                y = s.testa.bottom + m.s3;
            } else {
                s.testa.setEmpty();
                s.interruttore.setEmpty();
            }
            for (int i = 0; i < s.lampade.size(); i++) {
                Tessera t = new Tessera(s.lampade.get(i));
                float tx = mg + (largaLuce + gap) * (i % colLuci);
                float ty = y + (altaLuce + gap) * (i / colLuci);
                t.area.set(tx, ty, tx + largaLuce, ty + altaLuce);
                t.barra.set(t.area);
                float l = Math.max(m.bersaglio, m.dp(44));
                t.regola.set(t.area.right - l, t.area.top, t.area.right, t.area.top + l);
                tessereLuci.add(t);
            }
            int righe = (s.lampade.size() + colLuci - 1) / colLuci;
            y += righe * altaLuce + Math.max(0, righe - 1) * gap + m.s5;
        }
        altezzaLuci = Math.max(0f, y - m.s5 + mg - areaLuci.top);
        scorri = Math.max(0f, Math.min(scorri, altezzaLuci - areaLuci.height()));

        // ---- il dettaglio ----
        dettaglio.set(mg, m.margine, w - mg, h - m.margine);
        float dx0 = dettaglio.left + m.s4, dx1 = dettaglio.right - m.s4;
        float latoC = Math.max(m.bersaglio, h * 0.080f);
        chiudi.set(dx0, dettaglio.top + m.s4, dx0 + latoC, dettaglio.top + m.s4 + latoC);
        float largaInt = tasti.larghezzaChip("Accendi", latoC);
        interruttoreDettaglio.set(dx1 - largaInt, chiudi.top, dx1, chiudi.bottom);

        float yd = chiudi.bottom + m.s5 + m.micro;
        barraDettaglio.set(dx0, yd + m.s2, dx1, yd + m.s2 + Math.max(m.bersaglio, h * 0.085f));
        yd = barraDettaglio.bottom + m.s5 + m.micro;
        float altaComando = Math.max(m.bersaglio, h * 0.075f);
        float passo = (dx1 - dx0) / TINTE.length;
        for (int i = 0; i < TINTE.length; i++) {
            areeTinte[i].set(dx0 + passo * i + m.dp(4), yd + m.s2, dx0 + passo * (i + 1) - m.dp(4), yd + m.s2 + altaComando);
        }
        yd = areeTinte[0].bottom + m.s5 + m.micro;
        passo = (dx1 - dx0) / areeBianchi.length;
        for (int i = 0; i < areeBianchi.length; i++) {
            areeBianchi[i].set(dx0 + passo * i + m.dp(5), yd + m.s2, dx0 + passo * (i + 1) - m.dp(5), yd + m.s2 + altaComando);
        }
    }

    /** Quanto e' larga la scritta della testa di una stanza: il nome e quante accese. */
    private float largaTitoloStanza(Stanza s) {
        pTitolo.setTextSize(m.micro);
        pNota.setTextSize(m.micro);
        String nome = (s.nome.length() > 0 ? s.nome : "Altre luci").toUpperCase(Locale.ITALIAN);
        return pTitolo.measureText(nome) + m.s2 + pNota.measureText("3 di 3");
    }

    // ---- disegno ----

    @Override
    protected void onDraw(Canvas c) {
        if (vetro == null || !vetro.vivo()) {
            c.drawColor(Tinte.FONDO);
            return;
        }
        vetro.disegnaSfondo(c, getWidth(), getHeight());
        if (aperta != null) {
            disegnaDettaglio(c, aperta);
            return;
        }
        c.drawRect(0, 0, getWidth(), getHeight(), pAlone);
        disegnaFascia(c);

        int s = c.save();
        c.clipRect(areaLuci);
        c.translate(0, -scorri);
        if (!tessere.isEmpty()) disegnaScene(c);
        disegnaLuci(c);
        c.restoreToCount(s);

        // Il segno che sotto c'e' altro: una sfumatura sul bordo basso vale piu' di una barra.
        if (altezzaLuci > areaLuci.height() + 1f && scorri < altezzaLuci - areaLuci.height() - 1f) {
            float alto = m.s5 * 2f;
            pPieno.setShader(new LinearGradient(0, areaLuci.bottom - alto, 0, areaLuci.bottom,
                    Tinte.con(Tinte.FONDO_BASSO, 0x00), Tinte.con(Tinte.FONDO_BASSO, 0xE0), Shader.TileMode.CLAMP));
            c.drawRect(0, areaLuci.bottom - alto, getWidth(), areaLuci.bottom, pPieno);
            pPieno.setShader(null);
        }
    }

    private void disegnaFascia(Canvas c) {
        List<Lampada> elenco = luci.elenco();
        int accese = accese(elenco);
        float base = fascia.centerY() - (pTitolo.descent() + pTitolo.ascent()) / 2f;
        pTitolo.setTextSize(m.corpo);
        pTitolo.setColor(Tinte.TESTO);
        base = fascia.centerY() - (pTitolo.descent() + pTitolo.ascent()) / 2f;
        c.drawText("Casa", fascia.left, base, pTitolo);
        float x = fascia.left + pTitolo.measureText("Casa") + m.s3;

        String riepilogo = elenco.isEmpty() ? "nessuna luce"
                : (elenco.size() == 1 ? "una luce" : elenco.size() + " luci") + " · "
                + (accese == 0 ? "nessuna accesa" : accese == 1 ? "una accesa" : accese + " accese");
        pNota.setTextSize(m.nota);
        pNota.setColor(accese > 0 ? Tinte.TESTO_MEDIO : Tinte.TESTO_TENUE);
        c.drawText(Testo.taglia(pNota, riepilogo, aggiorna.left - m.s3 - x), x, base, pNota);

        if (!luci.vuoto()) {
            tasti.chip(c, vetro, tutteSpente, Tratti.ACCENSIONE, null, "Tutte spente", TINTA, 0f, premuto == P_TUTTE);
        }
        tasti.chip(c, vetro, aggiorna, Tratti.AGGIORNA, null, "", TINTA, 0f, premuto == P_AGGIORNA);
    }

    private void disegnaScene(Canvas c) {
        boolean conScene = false, conRoutine = false;
        for (Routine r : tessere) {
            if (r.scena) conScene = true;
            else conRoutine = true;
        }
        etichetta(c, conScene && conRoutine ? "SCENE E ROUTINE" : conRoutine ? "ROUTINE" : "SCENE", yScene);

        for (int i = 0; i < areeTessere.size(); i++) {
            Routine r = tessere.get(i);
            RectF t = areeTessere.get(i);
            boolean giu = premuto == P_SCENA && indice == i;
            float raggio = m.dp(16);
            vetro.tasto(c, t, raggio, r.colore, giu ? 1.45f : r.inCorso ? 0.55f : 1f);
            if (r.inCorso) tasti.alone(c, t, r.colore);

            // L'icona in un cerchio chiaro a sinistra, il nome grande accanto.
            float lato = t.height() * 0.52f;
            float cx = t.left + m.dp(16) + lato / 2f, cy = t.centerY();
            pPieno.setColor(0x26FFFFFF);
            c.drawCircle(cx, cy, lato / 2f, pPieno);
            pSegno.setColor(0xFFFFFFFF);
            Tratti.disegna(c, icona(r), cx, cy, lato * 0.5f, pSegno);

            float x = cx + lato / 2f + m.dp(14);
            float largo = t.right - m.dp(14) - x;
            pNome.setTextSize(m.dp(17));
            pNome.setColor(0xFFFFFFFF);
            c.drawText(Testo.taglia(pNome, r.nome, largo), x, cy - m.dp(2), pNome);
            pNota.setTextSize(m.dp(12));
            pNota.setColor(0xB3FFFFFF);
            c.drawText(r.inCorso ? "in corso…" : r.scena ? "Scena" : "Routine", x, cy + m.dp(16), pNota);
        }
    }

    /**
     * L'icona di una scena, indovinata dal nome come nella Home dell'altro tablet: le
     * parole che si scrivono davvero — notte, cinema, tutte — bastano, e per il resto
     * c'e' il segno di scena o di routine.
     */
    static int icona(Routine r) {
        String n = r.nome.toLowerCase(Locale.ITALIAN);
        if (n.contains("notte") || n.contains("dormi")) return Tratti.LUNA;
        if (n.contains("cinema") || n.contains("film") || n.contains("tv")) return Tratti.FILM;
        if (n.contains("accese") || n.contains("giorno") || n.contains("sveglia")) return Tratti.SOLE;
        if (n.contains("spent")) return Tratti.ACCENSIONE;
        return r.scena ? Tratti.SCINTILLE : Tratti.AVVIA;
    }

    private void disegnaLuci(Canvas c) {
        if (luci.vuoto()) {
            float cx = getWidth() / 2f;
            float cy = (areaLuci.top + areaLuci.bottom) / 2f;
            pSegno.setColor(Tinte.SPENTO);
            Tratti.disegna(c, Tratti.LAMPADINA, cx, cy - m.icona * 1.5f, m.icona * 2f, pSegno);
            pNome.setTextSize(m.corpo);
            pNome.setColor(Tinte.TESTO_TENUE);
            pNome.setTextAlign(Paint.Align.CENTER);
            c.drawText("Nessuna luce", cx, cy + m.s4, pNome);
            pNome.setTextAlign(Paint.Align.LEFT);
            return;
        }
        if (!conStanze) etichetta(c, "LUCI", yLuci);

        int t = 0;
        for (int i = 0; i < stanze.size(); i++) {
            Stanza st = stanze.get(i);
            if (conStanze) disegnaTestaStanza(c, st, i);
            for (int k = 0; k < st.lampade.size(); k++, t++) disegnaLuce(c, tessereLuci.get(t), t);
        }
    }

    private void disegnaTestaStanza(Canvas c, Stanza st, int i) {
        int accese = accese(st.lampade);
        String nome = (st.nome.length() > 0 ? st.nome : "Altre luci").toUpperCase(Locale.ITALIAN);
        String quante = accese == 0 ? "spente" : accese == st.lampade.size() ? "accese" : accese + " di " + st.lampade.size();
        pTitolo.setTextSize(m.micro);
        pTitolo.setColor(Tinte.TESTO_TENUE);
        float base = st.testa.centerY() - (pTitolo.descent() + pTitolo.ascent()) / 2f;
        float x = st.testa.left + m.s1;
        c.drawText(nome, x, base, pTitolo);
        pNota.setTextSize(m.micro);
        pNota.setColor(accese > 0 ? Tinte.TESTO_MEDIO : Tinte.SPENTO);
        c.drawText(quante, x + pTitolo.measureText(nome) + m.s2, base, pNota);
        tasti.chip(c, vetro, st.interruttore, Tratti.ACCENSIONE, null, accese > 0 ? "Spegni" : "Accendi",
                TINTA, 0f, premuto == P_STANZA && indice == i);
    }

    /**
     * Una luce: una tessera grande che si riempie di luce. Spenta e' grafite;
     * accesa, la luminosita' la riempie da sinistra col colore della lampada, e
     * il numero grande in basso a destra dice quanto. Un tocco accende o spegne,
     * il dito trascinato di lato regola, l'angolo in alto apre i colori.
     */
    private void disegnaLuce(Canvas c, Tessera t, int i) {
        Lampada l = t.lampada;
        Tuya.Stato s = l.stato;
        boolean accesa = s != null && s.raggiunta && s.accesa;
        boolean muta = s != null && !s.raggiunta;
        boolean giu = premuto == P_LUCE && indice == i;
        boolean trascinando = trascinata >= 0 && premuto == P_BARRA && indice == i;
        boolean regolabile = s != null && s.raggiunta && s.haLuminosita;
        RectF b = t.area;
        float raggio = m.dp(18);

        vetro.tasto(c, b, raggio, 0, giu ? 1.3f : 0f);
        int luce = trascinando ? trascinata : (s != null ? s.luminosita : -1);
        if (accesa || trascinando) {
            // Il riempimento: fino alla luminosita', o tutto se la lampada non la regola.
            float quanto = regolabile ? Math.max(0.06f, Math.min(1f, Math.max(1, luce) / 100f)) : 1f;
            pieno.set(b.left, b.top, b.left + b.width() * quanto, b.bottom);
            int sc = c.save();
            c.clipRect(pieno);
            vetro.tasto(c, b, raggio, l.colore, 1f);
            c.restoreToCount(sc);
            tasti.alone(c, b, 0xFFFFE8B0);
        }

        // In alto a sinistra la lampadina in un cerchio, in alto a destra i colori.
        float lato = m.dp(40);
        float cx = b.left + m.dp(16) + lato / 2f, cy = b.top + m.dp(16) + lato / 2f;
        pPieno.setColor(accesa ? 0x40FFFFFF : 0x1AFFFFFF);
        c.drawCircle(cx, cy, lato / 2f, pPieno);
        pSegno.setColor(accesa ? 0xFFFFFFFF : muta ? Tinte.ALLARME : 0xFFB4B7BE);
        Tratti.disegna(c, Tratti.LAMPADINA, cx, cy, lato * 0.5f, pSegno);
        pSegno.setColor(premuto == P_REGOLA && indice == i ? 0xFFFFFFFF : 0x99FFFFFF);
        Tratti.disegna(c, Tratti.REGOLA, b.right - m.dp(26), b.top + m.dp(26), m.dp(18), pSegno);

        // In basso il nome e lo stato; a destra, accesa, il numero grande.
        float x = b.left + m.dp(18);
        String valore = accesa && regolabile && luce > 0 ? luce + "%" : null;
        pGrande.setTextSize(m.dp(30));
        float largoValore = valore != null ? pGrande.measureText(valore) + m.dp(12) : 0;
        float largo = b.right - m.dp(16) - largoValore - x;
        pNome.setTextSize(m.dp(16));
        pNome.setColor(0xFFFFFFFF);
        c.drawText(t.nome.adatta(pNome, l.nome.length() > 0 ? l.nome : l.ip, largo, m.dp(16), m.dp(13)),
                x, b.bottom - m.dp(36), pNome);
        String stato = l.inCorso ? "…" : s == null ? "" : muta ? "non risponde" : accesa ? "Accesa" : "Spenta";
        pNota.setTextSize(m.dp(12));
        pNota.setColor(muta ? Tinte.ALLARME : accesa ? 0xCCFFFFFF : Tinte.TESTO_TENUE);
        c.drawText(stato, x, b.bottom - m.dp(17), pNota);
        if (valore != null) {
            pGrande.setColor(0xFFFFFFFF);
            c.drawText(valore, b.right - m.dp(16) - pGrande.measureText(valore), b.bottom - m.dp(17), pGrande);
        }
    }

    private void disegnaDettaglio(Canvas c, Lampada l) {
        Tuya.Stato s = l.stato;
        boolean accesa = s != null && s.raggiunta && s.accesa;
        vetro.pannello(c, dettaglio, m.raggio, TINTA, 0x10);

        tasti.chip(c, vetro, chiudi, Tratti.INDIETRO, null, "", TINTA, 0f, premuto == P_CHIUDI);

        float x = chiudi.right + m.s3;
        float largo = interruttoreDettaglio.left - x - m.s3;
        pNome.setColor(Tinte.TESTO);
        c.drawText(rTitoloDettaglio.adatta(pNome, l.nome.length() > 0 ? l.nome : l.ip, largo, m.voce, m.corpo),
                x, chiudi.centerY() - m.s1, pNome);
        String stato = l.inCorso ? "…" : s == null ? "da leggere" : !s.raggiunta ? "non risponde: " + s.errore
                : (s.accesa ? "accesa" : "spenta") + (l.stanza.length() > 0 ? " · " + l.stanza : "");
        pNota.setTextSize(m.nota);
        pNota.setColor(s != null && !s.raggiunta ? Tinte.ALLARME : Tinte.TESTO_TENUE);
        c.drawText(Testo.taglia(pNota, stato, largo), x, chiudi.centerY() + m.nota * 1.1f, pNota);

        tasti.chip(c, vetro, interruttoreDettaglio, Tratti.ACCENSIONE, null, accesa ? "Spegni" : "Accendi",
                l.colore, accesa ? 1f : 0f, premuto == P_ACCENDI);

        if (s == null || !s.raggiunta) return;

        if (s.haLuminosita) {
            titoletto(c, "LUMINOSITA'", barraDettaglio);
            int luce = trascinata >= 0 && premuto == P_BARRA_DETTAGLIO ? trascinata : Math.max(0, s.luminosita);
            RectF tr = barraDettaglio;
            float r = Math.min(tr.height() * 0.3f, m.dp(12));
            vetro.incavo(c, tr, r, 2.5f);
            pPieno.setColor(0xFF1A1B1F);
            c.drawRoundRect(tr, r, r, pPieno);
            float frazione = Math.max(0f, Math.min(1f, luce / 100f));
            RectF pieno = vetro.area(tr.left, tr.top, tr.left + Math.max(tr.height(), tr.width() * frazione), tr.bottom);
            vetro.tasto(c, pieno, r, accesa ? l.colore : 0, accesa ? 1f : 0f);
            pSegno.setColor(Tinte.TESTO);
            Tratti.disegna(c, Tratti.SOLE, tr.left + tr.height() * 0.5f, tr.centerY(), m.icona * 0.8f, pSegno);
            pGrande.setTextSize(m.voce);
            pGrande.setColor(Tinte.TESTO);
            String valore = luce + "%";
            c.drawText(valore, tr.right - tr.height() * 0.5f - pGrande.measureText(valore),
                    tr.centerY() - (pGrande.descent() + pGrande.ascent()) / 2f, pGrande);
        }

        if (s.haColore) {
            titoletto(c, "COLORE", areeTinte[0]);
            for (int i = 0; i < TINTE.length; i++) {
                // Qui il colore e' il colore vero: si sceglie quello, e non va scurito.
                RectF t = areeTinte[i];
                float r = Math.min(t.height(), t.width()) * 0.24f;
                vetro.incavo(c, t, r, 2.5f);
                pPieno.setColor(premuto == P_TINTA && indice == i ? Tinte.fondi(TINTE[i], 0xFFFFFFFF, 0.25f) : TINTE[i]);
                c.drawRoundRect(t, r, r, pPieno);
            }
        }

        if (s.haColore || s.haTemperatura) {
            titoletto(c, "BIANCO", areeBianchi[0]);
            for (int i = 0; i < areeBianchi.length; i++) {
                // Il primo e' il bianco semplice; gli altri tre la temperatura, se la lampada la regola.
                if (i > 0 && !s.haTemperatura) break;
                String nome = i == 0 ? "Bianco" : NOMI_BIANCHI[i - 1];
                tasti.chip(c, vetro, areeBianchi[i], Tratti.SOLE, null, nome, TINTA, 0f,
                        premuto == P_BIANCO && indice == i);
            }
        }
    }

    private void titoletto(Canvas c, String testo, RectF sotto) {
        pTitolo.setTextSize(m.micro);
        pTitolo.setColor(Tinte.TESTO_TENUE);
        c.drawText(testo, dettaglio.left + m.s4, sotto.top - m.s2, pTitolo);
    }

    /** Il titolino in maiuscolo sopra un gruppo di tasti, sempre allo stesso posto. */
    private void etichetta(Canvas c, String testo, float y) {
        pTitolo.setColor(Tinte.TESTO_TENUE);
        pTitolo.setTextSize(m.micro);
        c.drawText(testo, fascia.left + m.s1, y + m.micro, pTitolo);
    }

    private static int accese(List<Lampada> lampade) {
        int n = 0;
        for (Lampada l : lampade) {
            if (l.stato != null && l.stato.raggiunta && l.stato.accesa) n++;
        }
        return n;
    }

    // ---- tocco ----

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        float x = e.getX(), y = e.getY();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                giuX = x;
                giuY = y;
                scorriGiu = scorri;
                trova(x, y);
                if (premuto == P_BARRA || premuto == P_BARRA_DETTAGLIO) {
                    trascinata = valoreBarra(x);
                    if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
                }
                invalidate();
                return true;

            case MotionEvent.ACTION_MOVE:
                if (premuto == P_LUCE && indice >= 0 && indice < tessereLuci.size()
                        && Math.abs(x - giuX) > soglia && Math.abs(x - giuX) > Math.abs(y - giuY)) {
                    // Di lato su una luce: si regola la luminosita', dove la si vede.
                    Tuya.Stato st = tessereLuci.get(indice).lampada.stato;
                    if (st != null && st.raggiunta && st.haLuminosita) {
                        premuto = P_BARRA;
                        if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
                    }
                }
                if (premuto == P_BARRA || premuto == P_BARRA_DETTAGLIO) {
                    trascinata = valoreBarra(x);
                    invalidate();
                } else if (premuto != P_SCORRI && aperta == null && areaLuci.contains(giuX, giuY)
                        && Math.abs(y - giuY) > soglia && altezzaLuci > areaLuci.height()) {
                    // Un dito che si muove in verticale sulle luci scorre, e non accende niente.
                    premuto = P_SCORRI;
                }
                if (premuto == P_SCORRI) {
                    scorri = Math.max(0f, Math.min(altezzaLuci - areaLuci.height(), scorriGiu - (y - giuY)));
                    invalidate();
                }
                return true;

            case MotionEvent.ACTION_UP: {
                int cosa = premuto, quale = indice;
                if (cosa == P_BARRA || cosa == P_BARRA_DETTAGLIO) {
                    int valore = valoreBarra(x);
                    premuto = indice = -1;
                    trascinata = -1;
                    regolaLuce(cosa == P_BARRA ? tessereLuci.get(quale).lampada : aperta, valore);
                    invalidate();
                    return true;
                }
                if (cosa == P_SCORRI) {
                    premuto = indice = -1;
                    return true;
                }
                trova(x, y);
                boolean stessoPosto = cosa >= 0 && cosa == premuto && quale == indice;
                premuto = indice = -1;
                if (stessoPosto) agisci(cosa, quale);
                invalidate();
                return true;
            }

            case MotionEvent.ACTION_CANCEL:
                premuto = indice = -1;
                trascinata = -1;
                invalidate();
                return true;
        }
        return super.onTouchEvent(e);
    }

    private int valoreBarra(float x) {
        float sinistra, destra;
        if (premuto == P_BARRA && indice >= 0 && indice < tessereLuci.size()) {
            RectF a = tessereLuci.get(indice).area;
            sinistra = a.left;
            destra = a.right;
        } else {
            sinistra = barraDettaglio.left;
            destra = barraDettaglio.right;
        }
        float frazione = (x - sinistra) / Math.max(1f, destra - sinistra);
        return Math.max(1, Math.min(100, Math.round(frazione * 100f)));
    }

    /** A dito alzato: una lampada spenta prima si accende, poi prende la luminosita'. */
    private void regolaLuce(Lampada l, int valore) {
        if (l == null) return;
        Tuya.Stato s = l.stato;
        if (s == null || !s.raggiunta || !s.haLuminosita) return;
        if (!s.accesa) luci.accendi(l, true);
        luci.luminosita(l, valore);
    }

    private void trova(float x, float y) {
        premuto = indice = -1;

        if (aperta != null) {
            if (chiudi.contains(x, y)) premuto = P_CHIUDI;
            else if (interruttoreDettaglio.contains(x, y)) premuto = P_ACCENDI;
            else if (barraDettaglio.contains(x, y) && aperta.stato != null && aperta.stato.haLuminosita) {
                premuto = P_BARRA_DETTAGLIO;
            } else {
                for (int i = 0; i < areeTinte.length; i++) {
                    if (areeTinte[i].contains(x, y)) {
                        premuto = P_TINTA;
                        indice = i;
                        return;
                    }
                }
                for (int i = 0; i < areeBianchi.length; i++) {
                    if (areeBianchi[i].contains(x, y)) {
                        premuto = P_BIANCO;
                        indice = i;
                        return;
                    }
                }
            }
            return;
        }

        if (aggiorna.contains(x, y)) {
            premuto = P_AGGIORNA;
            return;
        }
        if (!luci.vuoto() && tutteSpente.contains(x, y)) {
            premuto = P_TUTTE;
            return;
        }
        if (!areaLuci.contains(x, y)) return;
        float yy = y + scorri;
        for (int i = 0; i < areeTessere.size(); i++) {
            if (areeTessere.get(i).contains(x, yy)) {
                premuto = P_SCENA;
                indice = i;
                return;
            }
        }
        for (int i = 0; i < stanze.size(); i++) {
            if (stanze.get(i).interruttore.contains(x, yy)) {
                premuto = P_STANZA;
                indice = i;
                return;
            }
        }
        for (int i = 0; i < tessereLuci.size(); i++) {
            Tessera t = tessereLuci.get(i);
            if (!t.area.contains(x, yy)) continue;
            indice = i;
            Tuya.Stato s = t.lampada.stato;
            if (t.regola.contains(x, yy)) premuto = P_REGOLA;
            else premuto = P_LUCE;
            return;
        }
    }

    private void agisci(int cosa, int i) {
        switch (cosa) {
            case P_SCENA:
                if (i < tessere.size()) luci.esegui(tessere.get(i));
                break;
            case P_TUTTE:
                luci.tutteSpente();
                break;
            case P_AGGIORNA:
                risveglia();
                break;
            case P_STANZA: {
                Stanza s = stanze.get(i);
                boolean qualcunaAccesa = accese(s.lampade) > 0;
                for (Lampada l : s.lampade) luci.accendi(l, !qualcunaAccesa);
                break;
            }
            case P_LUCE:
                luci.inverti(tessereLuci.get(i).lampada);
                break;
            case P_REGOLA:
                aperta = tessereLuci.get(i).lampada;
                luci.aggiorna();
                break;
            case P_CHIUDI:
                aperta = null;
                break;
            case P_ACCENDI:
                if (aperta != null) luci.inverti(aperta);
                break;
            case P_TINTA:
                if (aperta != null) luci.colore(aperta, TINTE[i] & 0xFFFFFF);
                break;
            case P_BIANCO:
                if (aperta == null) break;
                if (i == 0) luci.bianco(aperta);
                else luci.temperatura(aperta, BIANCHI[i - 1]);
                break;
        }
    }
}
