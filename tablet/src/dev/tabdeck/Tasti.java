package dev.tabdeck;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;

/**
 * I tasti del deck fuori dal deck: Home, Casa, Orologio e Impostazioni li
 * disegnano tutti da qui.
 *
 * La regola e' quella di {@link DeckView}: quel che si preme e' un tasto nel
 * suo incavo, con l'icona al centro e il nome sotto; il colore e' della cosa
 * (una scena, una luce accesa), non della sezione. Tenerla in un posto solo e'
 * quel che impedisce alle quattro schermate di tornare ad essere quattro app.
 *
 * Ogni View ha il suo: i Paint cambiano corpo e colore mentre si disegna, e
 * condividerli fra View diverse vorrebbe dire ricordarsi di rimetterli a posto.
 */
public final class Tasti {

    private final Misure m;
    private final Paint pSegno = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pNome = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pNota = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pAlone = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pBarra = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pImmagine = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final RectF r = new RectF();

    public Tasti(Misure m) {
        this.m = m;
        pNome.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pNota.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        pNome.setTextAlign(Paint.Align.CENTER);
        pNota.setTextAlign(Paint.Align.CENTER);
    }

    /** Il raggio di un tasto alto {@code h}: lo stesso rapporto del deck. */
    public float raggio(RectF b) {
        return Math.min(Math.min(b.width(), b.height()) * 0.17f, m.dp(16));
    }

    /**
     * Il fondo del tasto. {@code quanto} e' la tinta: 0 grafite, 1 piena; premuto
     * si schiarisce come nel deck.
     */
    public void fondo(Canvas c, Vetro v, RectF b, int colore, float quanto, boolean giu) {
        v.tasto(c, b, raggio(b), colore, giu ? Math.max(1f, quanto) + 0.45f : quanto);
    }

    /** L'alone caldo di una luce accesa, dentro il tasto. */
    public void alone(Canvas c, RectF b, int colore) {
        float cx = b.centerX(), cy = b.top + b.height() * 0.38f;
        float raggio = Math.max(b.width(), b.height()) * 0.62f;
        pAlone.setShader(new RadialGradient(cx, cy, raggio,
                Tinte.con(colore, 0x5A), Tinte.con(colore, 0x00), Shader.TileMode.CLAMP));
        float rr = raggio(b);
        c.drawRoundRect(b, rr, rr, pAlone);
        pAlone.setShader(null);
    }

    /**
     * Icona al centro e nome sotto, con la nota piu' piccola in fondo. L'icona e'
     * un tratto di {@link Tratti}, o un nome di {@link Pittogrammi}, o
     * un'immagine; {@code -1} e null per nessuna.
     */
    public void contenuto(Canvas c, RectF b, int tratto, String pittogramma, Bitmap immagine,
                          String nome, String nota, boolean acceso) {
        float lato = Math.min(b.width(), b.height());
        // Le misure del modello: nome a 13, nota a 11, icona a 28 su un tasto da 130.
        float corpoNome = Math.max(m.dp(11.5f), Math.min(m.dp(13.5f), lato * 0.15f));
        float corpoNota = corpoNome * 0.84f;
        float icona = Math.max(m.dp(18), Math.min(lato * 0.3f, m.dp(28)));
        boolean conNota = nota != null && nota.length() > 0;

        // Il blocco icona-nome-nota sta al centro, tutto insieme.
        float spazio = lato * 0.075f;
        float alto = icona + spazio + corpoNome + (conNota ? corpoNota * 1.35f : 0);
        float y = b.centerY() - alto / 2f;

        pSegno.setColor(0xF2FFFFFF);
        float cy = y + icona / 2f;
        if (immagine != null) {
            r.set(b.centerX() - icona * 0.6f, cy - icona * 0.6f, b.centerX() + icona * 0.6f, cy + icona * 0.6f);
            c.drawBitmap(immagine, null, r, pImmagine);
        } else if (tratto >= 0) {
            Tratti.disegna(c, tratto, b.centerX(), cy, icona, pSegno);
        } else if (pittogramma != null) {
            Pittogrammi.disegna(c, pittogramma, b.centerX(), cy, icona, pSegno);
        }

        pNome.setTextSize(corpoNome);
        pNome.setColor(0xE6FFFFFF);
        float base = y + icona + spazio - pNome.ascent() * 0.8f;
        c.drawText(Testo.taglia(pNome, nome, b.width() - m.s2 * 2f), b.centerX(), base, pNome);
        if (conNota) {
            pNota.setTextSize(corpoNota);
            pNota.setColor(0x8CFFFFFF);
            c.drawText(Testo.taglia(pNota, nota, b.width() - m.s3 * 2f), b.centerX(), base + corpoNota * 1.35f, pNota);
        }
    }

    /** Icona e nome in riga, centrati: i tasti bassi e larghi in fondo alla Home. */
    public void inRiga(Canvas c, RectF b, int tratto, String pittogramma, Bitmap immagine, String nome, boolean acceso) {
        float icona = icona(b.height());
        pNome.setTextSize(corpo(b.height()));
        pNome.setTextAlign(Paint.Align.LEFT);
        String reso = Testo.taglia(pNome, nome, b.width() - icona - m.s3 * 3f);
        float largo = icona + m.s2 + pNome.measureText(reso);
        float x = b.centerX() - largo / 2f;
        pSegno.setColor(acceso ? 0xFFFFFFFF : 0xFFC9CBD0);
        if (immagine != null) {
            r.set(x, b.centerY() - icona / 2f, x + icona, b.centerY() + icona / 2f);
            c.drawBitmap(immagine, null, r, pImmagine);
        } else if (tratto >= 0) {
            Tratti.disegna(c, tratto, x + icona / 2f, b.centerY(), icona, pSegno);
        } else if (pittogramma != null) {
            Pittogrammi.disegna(c, pittogramma, x + icona / 2f, b.centerY(), icona, pSegno);
        }
        pNome.setColor(acceso ? 0xFFFFFFFF : 0xE6FFFFFF);
        c.drawText(reso, x + icona + m.s2, b.centerY() - (pNome.descent() + pNome.ascent()) / 2f, pNome);
        pNome.setTextAlign(Paint.Align.CENTER);
    }

    /** La barretta della luminosita' in fondo al tasto di una luce accesa. */
    public void barra(Canvas c, RectF b, float quanto) {
        float alta = Math.max(3f, m.dp(4));
        float mg = b.width() * 0.11f;
        r.set(b.left + mg, b.bottom - mg * 0.9f - alta, b.right - mg, b.bottom - mg * 0.9f);
        pBarra.setColor(0x1FFFFFFF);
        c.drawRoundRect(r, alta / 2f, alta / 2f, pBarra);
        r.right = r.left + r.width() * Math.max(0f, Math.min(1f, quanto));
        pBarra.setColor(0xFFFFFFFF);
        c.drawRoundRect(r, alta / 2f, alta / 2f, pBarra);
    }

    /**
     * Il tasto piccolo delle fasce in cima: icona e scritta in riga, alto quanto
     * un dito. {@code testo} vuoto per la sola icona.
     */
    public void chip(Canvas c, Vetro v, RectF b, int tratto, String pittogramma, String testo,
                     int colore, float quanto, boolean giu) {
        v.tasto(c, b, Math.min(b.height() * 0.3f, m.dp(11)), colore, giu ? Math.max(1f, quanto) + 0.45f : quanto);
        inRiga(c, b, tratto, pittogramma, null, testo, quanto > 0.5f);
    }

    /** Quanto e' largo un chip con quella scritta, alto {@code alto}. */
    public float larghezzaChip(String testo, float alto) {
        pNome.setTextSize(corpo(alto));
        float icona = icona(alto);
        if (testo == null || testo.length() == 0) return alto;
        return icona + m.s2 + pNome.measureText(testo) + m.s3 * 3.4f;
    }

    /** Il corpo della scritta di un tasto basso: leggibile anche nei chip delle fasce. */
    private float corpo(float alto) {
        return Math.min(m.dp(14.5f), Math.max(m.nota * 0.85f, alto * 0.42f));
    }

    private float icona(float alto) {
        return Math.min(Math.max(m.dp(15), alto * 0.44f), m.dp(22));
    }
}
