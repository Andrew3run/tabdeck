package dev.tabdeck;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.Handler;
import android.view.MotionEvent;
import android.view.View;

import java.util.Calendar;
import java.util.Locale;

/**
 * La schermata di quando qualcosa suona.
 *
 * Arriva dall'altro tablet, dove si chiamava « vela »: copre tutto quello che
 * c'era sotto, qualunque sezione fosse aperta, perche' mentre suona una sveglia
 * non deve esserci nient'altro da toccare. Chi si e' appena svegliato preme
 * quello che vede.
 *
 * <h3>Due tasti, e uno e' grande il doppio</h3>
 *
 * « Basta » e « ancora cinque minuti » non hanno lo stesso peso: il primo e' la
 * cosa che si vuole quasi sempre, il secondo e' la scappatoia. Farli uguali
 * vuol dire premere quello sbagliato a occhi chiusi, quindi sessanta e
 * quaranta. Per il timer il secondo non c'e' proprio: la pasta rimandata di
 * cinque minuti non e' rimandata, e' scotta.
 *
 * <h3>Il tocco a vuoto non zittisce</h3>
 *
 * Toccare il vetro fuori dai tasti non fa niente, di proposito: una manica che
 * sfiora lo schermo non deve poter spegnere una sveglia. Ma il tocco si prende
 * lo stesso, perche' non arrivi alla sezione che sta sotto.
 *
 * Non decide niente: compare e sparisce dietro a {@link Ora#suonando()}, e
 * l'activity la mostra o la nasconde di conseguenza.
 */
public final class SuoneriaView extends View implements Ora.Ascolto {

    private final Misure m;
    private final Ora ora;
    private Vetro vetro;

    private final Paint pVelo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pTitolo = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pOra = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pTasto = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pSegno = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final RectF card = new RectF();
    private final RectF tBasta = new RectF();
    private final RectF tAncora = new RectF();
    private final RectF arco = new RectF();

    private final Testo.Riga rTitolo = new Testo.Riga();
    private final Calendar cal = Calendar.getInstance();

    private String titolo = "";
    private boolean sveglia;
    private String scrittaAncora = "ANCORA " + Ora.RINVIO_MIN + " MINUTI";

    private float corpoTitolo, corpoOra, corpoTasto;
    private int premuto = -1;

    /** L'ora grande cambia da sola: si ridisegna al secondo, ma solo finche' suona. */
    private final Handler battito = new Handler();
    private final Runnable tic = new Runnable() {
        @Override public void run() {
            if (ora.suonando() == null) return;
            invalidate();
            battito.postDelayed(this, 1000 - (System.currentTimeMillis() % 1000));
        }
    };

    public SuoneriaView(Context c, Misure misure, Ora ora) {
        super(c);
        this.m = misure;
        this.ora = ora;
        setClickable(true);

        // Quasi opaco: quello che c'era sotto resta intuibile, non leggibile.
        pVelo.setColor(0xE60B0F14);
        pTitolo.setColor(Tinte.TESTO_MEDIO);
        pTitolo.setTextAlign(Paint.Align.CENTER);
        pTitolo.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pOra.setColor(Tinte.TESTO);
        pOra.setTextAlign(Paint.Align.CENTER);
        pOra.setTypeface(Typeface.create("sans-serif-thin", Typeface.NORMAL));
        pTasto.setTextAlign(Paint.Align.CENTER);
        pTasto.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        pSegno.setStyle(Paint.Style.STROKE);
        pSegno.setStrokeCap(Paint.Cap.ROUND);

        ora.aggiungiAscolto(this);
        Vetro.chiedi(m, new Vetro.Pronto() {
            @Override public void vetroPronto(Vetro v) {
                vetro = v;
                v.adattaA(getWidth(), getHeight());
                invalidate();
            }
        });
    }

    @Override
    public void oraCambiata() {
        titolo = ora.titolo();
        sveglia = ora.siPuoRimandare();
        premuto = -1;
        disponi(getWidth(), getHeight());
        battito.removeCallbacks(tic);
        if (ora.suonando() != null) battito.post(tic);
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

    @Override
    protected void onSizeChanged(int w, int h, int vw, int vh) {
        super.onSizeChanged(w, h, vw, vh);
        if (vetro != null) vetro.adattaA(w, h);
        disponi(w, h);
    }

    private void disponi(int w, int h) {
        if (w <= 0 || h <= 0) return;
        float mg = m.margine;
        card.set(w * 0.16f, h * 0.13f, w * 0.84f, h * 0.87f);

        corpoTitolo = h * 0.050f;
        corpoOra = h * 0.190f;
        corpoTasto = h * 0.045f;
        pTitolo.setTextSize(corpoTitolo);
        pOra.setTextSize(corpoOra);
        pTasto.setTextSize(corpoTasto);
        pSegno.setStrokeWidth(Math.max(3f, h * 0.007f));

        float altezzaTasto = Math.max(m.bersaglio * 1.6f, h * 0.145f);
        float fondo = card.bottom - mg * 1.4f;
        if (sveglia) {
            // Sessanta al « basta », quaranta al rinvio: la differenza di misura
            // e' quello che li distingue a occhi socchiusi.
            float taglio = card.left + card.width() * 0.60f;
            tBasta.set(card.left + mg, fondo - altezzaTasto, taglio - mg * 0.4f, fondo);
            tAncora.set(taglio + mg * 0.4f, fondo - altezzaTasto, card.right - mg, fondo);
        } else {
            tBasta.set(card.left + mg, fondo - altezzaTasto, card.right - mg, fondo);
            tAncora.setEmpty();
        }
    }

    @Override
    protected void onDraw(Canvas c) {
        c.drawRect(0, 0, getWidth(), getHeight(), pVelo);
        if (vetro == null || !vetro.vivo()) return;

        int colore = sveglia ? Tinte.OROLOGIO : Tinte.ALLARME;
        vetro.pannello(c, card, m.raggio, colore, 0x3A);

        float cx = card.centerX();

        // La campanella, con l'alone: il segnale che si legge prima delle
        // parole, e da lontano l'unico.
        float cyCampana = card.top + m.margine * 1.6f + card.height() * 0.13f;
        vetro.bagliore(c, cx, cyCampana, card.height() * 0.20f, colore);
        campanella(c, cx, cyCampana, card.height() * 0.10f, colore);

        pTitolo.setColor(Tinte.TESTO_MEDIO);
        float yTitolo = cyCampana + card.height() * 0.20f;
        c.drawText(rTitolo.adatta(pTitolo, titolo, card.width() - m.margine * 2f,
                        corpoTitolo, corpoTitolo * 0.7f),
                cx, yTitolo, pTitolo);

        // L'ora vera, grande: chi si sveglia di soprassalto la prima cosa che
        // vuole sapere e' che ore sono.
        cal.setTimeInMillis(System.currentTimeMillis());
        String adesso = String.format(Locale.ITALIAN, "%02d:%02d",
                cal.get(Calendar.HOUR_OF_DAY), cal.get(Calendar.MINUTE));
        pOra.setColor(Tinte.TESTO);
        pOra.setTextSize(corpoOra);
        float yOra = (yTitolo + tBasta.top) / 2f;
        c.drawText(adesso, cx, yOra - (pOra.descent() + pOra.ascent()) / 2f, pOra);

        tasto(c, tBasta, "BASTA", colore, premuto == 0, true);
        if (sveglia) tasto(c, tAncora, scrittaAncora, Tinte.TESTO_TENUE, premuto == 1, false);
    }

    private void tasto(Canvas c, RectF b, String testo, int colore, boolean giu, boolean pieno) {
        vetro.pannello(c, b, b.height() * 0.28f, colore, giu ? 0x6E : (pieno ? 0x50 : 0x24));
        pTasto.setColor(pieno ? Tinte.TESTO : Tinte.TESTO_MEDIO);
        pTasto.setTextSize(corpoTasto);
        // Il nome del tasto si rimpicciolisce se non ci sta, invece di uscire.
        float largo = b.width() - m.margine;
        while (pTasto.measureText(testo) > largo && pTasto.getTextSize() > corpoTasto * 0.6f) {
            pTasto.setTextSize(pTasto.getTextSize() * 0.92f);
        }
        c.drawText(testo, b.centerX(),
                b.centerY() - (pTasto.descent() + pTasto.ascent()) / 2f, pTasto);
    }

    /** Una campanella: la campana, il battaglio e il manico. */
    private void campanella(Canvas c, float cx, float cy, float r, int colore) {
        pSegno.setColor(colore);
        pSegno.setStyle(Paint.Style.STROKE);
        // Con il RectF: la variante a quattro float arriva con Android 5.
        arco.set(cx - r, cy - r, cx + r, cy + r);
        c.drawArc(arco, 200f, 140f, false, pSegno);
        c.drawLine(cx - r, cy + r * 0.34f, cx + r, cy + r * 0.34f, pSegno);
        c.drawLine(cx - r * 0.94f, cy + r * 0.34f, cx - r * 0.94f, cy - r * 0.10f, pSegno);
        c.drawLine(cx + r * 0.94f, cy + r * 0.34f, cx + r * 0.94f, cy - r * 0.10f, pSegno);
        pSegno.setStyle(Paint.Style.FILL);
        c.drawCircle(cx, cy + r * 0.62f, r * 0.17f, pSegno);
        c.drawCircle(cx, cy - r * 0.96f, r * 0.12f, pSegno);
        pSegno.setStyle(Paint.Style.STROKE);
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        float x = e.getX(), y = e.getY();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                premuto = quale(x, y);
                invalidate();
                return true;
            case MotionEvent.ACTION_UP:
                int su = quale(x, y);
                int era = premuto;
                premuto = -1;
                invalidate();
                if (su >= 0 && su == era) {
                    if (su == 0) ora.ferma();
                    else ora.rinvia();
                }
                return true;
            case MotionEvent.ACTION_CANCEL:
                premuto = -1;
                invalidate();
                return true;
        }
        // Anche il resto si prende: sotto c'e' una sezione che non deve sentirlo.
        return true;
    }

    private int quale(float x, float y) {
        if (tBasta.contains(x, y)) return 0;
        if (sveglia && tAncora.contains(x, y)) return 1;
        return -1;
    }
}
