package dev.tabdeck;

import android.animation.Animator;
import android.animation.AnimatorListenerAdapter;
import android.animation.ValueAnimator;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.view.HapticFeedbackConstants;
import android.view.View;

import java.util.Calendar;

/**
 * La ghiera che scorre: due anelli di numeri che girano attorno a un
 * indicatore fermo in cima.
 *
 * <h3>Perche' una ghiera e non due frecce</h3>
 *
 * Le frecce <b>+</b> e <b>−</b> costano un tocco per passo: portare una sveglia
 * dalle 7 alle 22 sono quindici tocchi, e quindici occasioni di premere
 * accanto. Una ghiera fa lo stesso viaggio con un gesto solo, e mentre lo fa
 * <b>mostra dove si sta andando</b>: si vedono passare le ore, e ci si ferma
 * quando si e' arrivati invece di contare. E un cerchio di ore e' gia' la forma
 * di un orologio: il punto luminoso sul bordo e' il minuto vero, adesso.
 *
 * <h3>Come si comporta</h3>
 *
 * <b>L'anello si trascina, non si spinge</b>: il numero sotto il dito ci resta.
 * <b>Si aggancia allo scatto piu' vicino</b> con un'animazione corta, se no si
 * resterebbe fra due ore senza sapere quale e' stata scelta. <b>Fa tic</b> a
 * ogni scatto: qui con {@code VIRTUAL_KEY}, perche' {@code CLOCK_TICK} arriva
 * con Android 5 - e il Tab 3 Wi-Fi il motorino non ce l'ha, quindi resta un
 * gesto gentile verso un apparecchio che non c'e'. <b>I numeri restano
 * dritti</b>: girano le posizioni, non le cifre.
 *
 * Non e' una View: e' un pezzo di disegno che {@link OrologioView} ospita
 * dentro la sua, una View disegnata a mano per sezione.
 *
 * Arriva cosi' com'e' dall'altro tablet: le misure sono tutte frazioni del
 * raggio, e il raggio lo decide chi la ospita.
 */
public final class Quadrante {

    /** Quanto dura l'aggancio allo scatto: una conferma, non uno spettacolo. */
    private static final int AGGANCIO_MS = 160;

    /** Lontano dall'indicatore il numero e' quasi trasparente, ma non del tutto:
     *  un cerchio con meta' numeri mancanti sembra un errore di disegno. */
    private static final int ALFA_VICINO = 0xFF, ALFA_LONTANO = 0x2E;

    private final View casa;

    private final Paint pNumero = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pTacca = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pArco = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pCentro = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pAnello = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF appoggio = new RectF();
    private final Path cuneo = new Path();
    private float cuneoRaggio = Float.NaN;

    private float cx, cy, raggio;
    private final Paint pDisco = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pBordoDisco = new Paint(Paint.ANTI_ALIAS_FLAG);
    private float discoPer = -1f;
    {
        pBordoDisco.setStyle(Paint.Style.STROKE);
        pBordoDisco.setStrokeWidth(1.5f);
    }
    private float rEsterno, rInterno;
    private float corpoEsterno, corpoInterno, corpoCentro, corpoUnita;

    private int slotEsterni = 24, slotInterni = 12;
    private String[] etichetteEsterne = new String[0];
    private String[] etichetteInterne = new String[0];

    /** La posizione delle due ghiere, in scatti frazionari: durante il
     *  trascinamento la ghiera sta fra due valori, come una manopola. */
    private float posEsterna, posInterna;

    /** 0 esterno, 1 interno, -1 nessuno. */
    private int anelloTrascinato = -1;
    private float angoloPrecedente;
    private int ultimoScatto;
    private ValueAnimator aggancio;

    private int tinta = Tinte.OROLOGIO;

    /** Chi vuole sapere che il valore e' cambiato. */
    public interface Cambio {
        /** Durante il trascinamento, a ogni scatto: solo per lo schermo. */
        void suScatto();
        /** Dito alzato e ghiera agganciata: adesso si puo' salvare. */
        void suFermata();
    }

    private Cambio cambio;

    public Quadrante(View casa) {
        this.casa = casa;
        pNumero.setTextAlign(Paint.Align.CENTER);
        pNumero.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pTacca.setStyle(Paint.Style.STROKE);
        pTacca.setStrokeCap(Paint.Cap.ROUND);
        pArco.setStyle(Paint.Style.STROKE);
        pArco.setStrokeCap(Paint.Cap.ROUND);
        pCentro.setTextAlign(Paint.Align.CENTER);
        pCentro.setTypeface(Typeface.create("sans-serif-thin", Typeface.NORMAL));
        pAnello.setStyle(Paint.Style.STROKE);
    }

    public void setCambio(Cambio c) {
        cambio = c;
    }

    public void setTinta(int t) {
        tinta = t;
    }

    /** Dove sta e quanto e' grande. */
    public void posiziona(float cx, float cy, float raggio) {
        this.cx = cx;
        this.cy = cy;
        this.raggio = raggio;
        // Fra la corona dei numeri esterni e quella interna resta un anello di
        // vuoto largo quanto una cifra: e' la sola cosa che le fa leggere come
        // due cose diverse, e non sessanta cifre mescolate a sei.
        rEsterno = raggio * 0.835f;
        rInterno = raggio * 0.545f;
        corpoEsterno = raggio * 0.092f;
        corpoInterno = raggio * 0.080f;
        corpoCentro = raggio * 0.330f;
        corpoUnita = raggio * 0.082f;
        pTacca.setStrokeWidth(Math.max(1.5f, raggio * 0.010f));
        pArco.setStrokeWidth(Math.max(3f, raggio * 0.026f));
        pAnello.setStrokeWidth(Math.max(1f, raggio * 0.005f));
    }

    /** Rifa' i due anelli, con etichette gia' composte da chi chiama. */
    public void anelli(String[] esterne, String[] interne) {
        etichetteEsterne = esterne;
        etichetteInterne = interne;
        slotEsterni = esterne.length;
        slotInterni = interne.length;
        posEsterna = normalizza(posEsterna, slotEsterni);
        posInterna = normalizza(posInterna, slotInterni);
    }

    /** Porta la ghiera su due valori, senza animazione. */
    public void vaiA(int esterno, int interno) {
        fermaAggancio();
        posEsterna = normalizza(esterno, slotEsterni);
        posInterna = normalizza(interno, slotInterni);
    }

    public int esterno() {
        return (int) normalizza(Math.round(posEsterna), slotEsterni);
    }

    public int interno() {
        return (int) normalizza(Math.round(posInterna), slotInterni);
    }

    public boolean staTrascinando() {
        return anelloTrascinato >= 0;
    }

    // ---- disegno ----

    /**
     * @param centro   il valore grande in mezzo, gia' composto
     * @param unita    la parola sotto, o null
     * @param frazione quanto dell'arco esterno colorare, da 0 a 1; sotto zero niente
     */
    public void disegna(Canvas c, String centro, String unita, float frazione) {
        // Il disco: un piano piu' chiaro del fondo, con un filo di bordo. Senza,
        // la ghiera era fatta di sole tacche sul buio, ed era la schermata piu'
        // scura del tablet.
        if (raggio > 0) {
            if (pDisco.getShader() == null || discoPer != raggio) {
                pDisco.setShader(new android.graphics.RadialGradient(cx, cy - raggio * 0.35f, raggio * 1.25f,
                        0xFF2A2D34, 0xFF1A1B20, android.graphics.Shader.TileMode.CLAMP));
                discoPer = raggio;
            }
            c.drawCircle(cx, cy, raggio * 1.04f, pDisco);
            pBordoDisco.setColor(0x24FFFFFF);
            c.drawCircle(cx, cy, raggio * 1.04f, pBordoDisco);
        }

        // Le tacche stanno fuori dai numeri, corte, e solo le dodici delle ore
        // si vedono davvero: le altre sono un velo che da' la texture.
        for (int i = 0; i < 60; i++) {
            boolean ora = i % 5 == 0;
            float ang = (float) Math.toRadians(i * 6);
            float sin = (float) Math.sin(ang), cos = (float) Math.cos(ang);
            float fuori = raggio;
            float dentro = raggio - raggio * (ora ? 0.040f : 0.020f);
            pTacca.setColor(Tinte.con(ora ? Tinte.TESTO_MEDIO : Tinte.TESTO_TENUE,
                    ora ? 0x7A : 0x1C));
            c.drawLine(cx + sin * dentro, cy - cos * dentro,
                    cx + sin * fuori, cy - cos * fuori, pTacca);
        }

        // Il filo fra i due anelli: « di qua i minuti, di la' le ore ».
        pAnello.setColor(Tinte.con(Tinte.TESTO_TENUE, 0x22));
        c.drawCircle(cx, cy, (rEsterno + rInterno) / 2f, pAnello);

        // Il tempo che manca, come arco che si consuma dall'alto.
        if (frazione >= 0f) {
            appoggio.set(cx - raggio, cy - raggio, cx + raggio, cy + raggio);
            pArco.setColor(Tinte.con(tinta, 0xDD));
            c.drawArc(appoggio, -90f, Math.max(0.5f, 360f * frazione), false, pArco);
        }

        // Il minuto vero, adesso: un puntino sul bordo.
        Calendar adesso = Calendar.getInstance();
        float angOra = (float) Math.toRadians(adesso.get(Calendar.MINUTE) * 6);
        pTacca.setStyle(Paint.Style.FILL);
        pTacca.setColor(Tinte.con(Tinte.TESTO, 0x99));
        c.drawCircle(cx + (float) Math.sin(angOra) * raggio,
                cy - (float) Math.cos(angOra) * raggio, raggio * 0.020f, pTacca);
        pTacca.setStyle(Paint.Style.STROKE);

        anello(c, etichetteEsterne, posEsterna, rEsterno, corpoEsterno);
        anello(c, etichetteInterne, posInterna, rInterno, corpoInterno);

        // L'indicatore: un cuneo in cima, che punta. Il percorso si rifa' solo
        // se cambia il raggio.
        pTacca.setStyle(Paint.Style.FILL);
        pTacca.setColor(tinta);
        if (cuneoRaggio != raggio) {
            cuneoRaggio = raggio;
            cuneo.reset();
            cuneo.moveTo(0f, raggio * 0.068f);
            cuneo.lineTo(-raggio * 0.040f, 0f);
            cuneo.lineTo(raggio * 0.040f, 0f);
            cuneo.close();
        }
        int sCuneo = c.save();
        c.translate(cx, cy - raggio);
        c.drawPath(cuneo, pTacca);
        c.restoreToCount(sCuneo);
        pTacca.setStyle(Paint.Style.STROKE);

        // Il valore scelto, grande, al centro.
        pCentro.setColor(Tinte.TESTO);
        pCentro.setTextSize(corpoCentro);
        float spostamento = unita != null ? corpoUnita * 0.7f : 0f;
        c.drawText(centro, cx,
                cy - (pCentro.descent() + pCentro.ascent()) / 2f - spostamento, pCentro);
        if (unita != null) {
            pCentro.setColor(Tinte.TESTO_TENUE);
            pCentro.setTextSize(corpoUnita);
            c.drawText(unita, cx, cy + corpoCentro * 0.50f, pCentro);
        }
    }

    /** Un anello di numeri: quelli lontani dall'indicatore sbiadiscono e rimpiccioliscono. */
    private void anello(Canvas c, String[] etichette, float pos, float r, float corpo) {
        if (etichette.length == 0) return;
        float passo = 360f / etichette.length;
        for (int i = 0; i < etichette.length; i++) {
            if (etichette[i] == null) continue;
            float gradi = (i - pos) * passo;
            while (gradi > 180f) gradi -= 360f;
            while (gradi < -180f) gradi += 360f;

            float vicinanza = 1f - Math.min(1f, Math.abs(gradi) / 90f);
            int alfa = (int) (ALFA_LONTANO + (ALFA_VICINO - ALFA_LONTANO) * vicinanza * vicinanza);
            pNumero.setColor(Tinte.con(Math.abs(gradi) < passo * 0.5f ? tinta : Tinte.TESTO, alfa));
            pNumero.setTextSize(corpo * (0.78f + 0.22f * vicinanza));

            float ang = (float) Math.toRadians(gradi);
            float x = cx + (float) Math.sin(ang) * r;
            float y = cy - (float) Math.cos(ang) * r;
            c.drawText(etichette[i], x, y - (pNumero.descent() + pNumero.ascent()) / 2f, pNumero);
        }
    }

    // ---- tocco ----

    /** @return true se ha preso il tocco. */
    public boolean giu(float x, float y) {
        float dx = x - cx, dy = y - cy;
        float d = (float) Math.sqrt(dx * dx + dy * dy);
        // Il confine fra i due anelli sta in mezzo ai due raggi: ogni anello ha
        // la sua meta' di corona, e non c'e' una fascia che non risponde.
        float confine = (rEsterno + rInterno) / 2f;
        if (d > raggio * 1.05f) return false;
        if (d < rInterno * 0.55f) return false;
        fermaAggancio();
        anelloTrascinato = d >= confine ? 0 : 1;
        angoloPrecedente = angolo(x, y);
        ultimoScatto = anelloTrascinato == 0 ? esterno() : interno();
        return true;
    }

    public void muovi(float x, float y) {
        if (anelloTrascinato < 0) return;
        float adesso = angolo(x, y);
        float delta = adesso - angoloPrecedente;
        while (delta > 180f) delta -= 360f;
        while (delta < -180f) delta += 360f;
        angoloPrecedente = adesso;

        if (anelloTrascinato == 0) {
            posEsterna = normalizza(posEsterna - delta / (360f / slotEsterni), slotEsterni);
        } else {
            posInterna = normalizza(posInterna - delta / (360f / slotInterni), slotInterni);
        }

        int adessoScatto = anelloTrascinato == 0 ? esterno() : interno();
        if (adessoScatto != ultimoScatto) {
            ultimoScatto = adessoScatto;
            casa.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY,
                    HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING);
            if (cambio != null) cambio.suScatto();
        }
        casa.invalidate();
    }

    public void su() {
        if (anelloTrascinato < 0) return;
        boolean esterna = anelloTrascinato == 0;
        anelloTrascinato = -1;
        float da = esterna ? posEsterna : posInterna;
        agganciaA(esterna, da, Math.round(da));
    }

    public void annulla() {
        anelloTrascinato = -1;
    }

    private void agganciaA(final boolean esterna, float da, float a) {
        fermaAggancio();
        if (Math.abs(a - da) < 0.001f) {
            if (cambio != null) cambio.suFermata();
            casa.invalidate();
            return;
        }
        aggancio = ValueAnimator.ofFloat(da, a);
        aggancio.setDuration(AGGANCIO_MS);
        aggancio.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() {
            @Override public void onAnimationUpdate(ValueAnimator an) {
                float v = (Float) an.getAnimatedValue();
                if (esterna) posEsterna = normalizza(v, slotEsterni);
                else posInterna = normalizza(v, slotInterni);
                casa.invalidate();
            }
        });
        aggancio.addListener(new AnimatorListenerAdapter() {
            private boolean annullato;

            @Override public void onAnimationCancel(Animator an) {
                annullato = true;
            }

            @Override public void onAnimationEnd(Animator an) {
                // Solo qui si salva: ogni scatto riscriverebbe il file e
                // riprogrammerebbe la sveglia, e mezzo giro sono venti scatti.
                // Un aggancio interrotto da un nuovo tocco non salva: salvera'
                // quello che arriva dopo.
                if (!annullato && cambio != null) cambio.suFermata();
            }
        });
        aggancio.start();
    }

    private void fermaAggancio() {
        if (aggancio != null) {
            aggancio.cancel();
            aggancio = null;
        }
    }

    /** Zero in cima, positivo in senso orario: la stessa convenzione dei numeri. */
    private float angolo(float x, float y) {
        return (float) Math.toDegrees(Math.atan2(x - cx, cy - y));
    }

    private static float normalizza(float v, int quanti) {
        if (quanti <= 0) return 0f;
        float r = v % quanti;
        return r < 0 ? r + quanti : r;
    }
}
