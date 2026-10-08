package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.View;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.Locale;

/**
 * Il salvaschermo, disegnato. Tre stili, scelti sul PC:
 *
 *   foto      le immagini a tutto schermo, che si avvicinano e scorrono piano
 *             mentre restano davanti, e sfumano l'una nell'altra;
 *   aurora    quattro chiazze di luce che si muovono lente su un fondo scuro;
 *   orologio  solo l'ora, su nero, che si sposta di qualche pixel ogni minuto
 *             perche' un pannello acceso per ore non ci resti segnato.
 *
 * L'ora sta in basso a sinistra, grande e sottile, con la data sotto. Un tocco
 * lo chiude, e quel tocco non arriva a niente di quello che c'e' dietro.
 *
 * Batte solo mentre si vede: una trentina di ridisegni al secondo per foto e
 * aurora, uno al secondo per l'orologio.
 */
public final class SalvaschermoView extends View {

    public interface Chiusura {
        void chiuso();
    }

    private static final long SFUMATURA_MS = 1800;
    private static final long PASSO_ANIMAZIONE = 33;
    private static final int[] AURORA = { 0xFF7C5CFF, 0xFF22D3EE, 0xFFF472B6, 0xFF34D399 };

    private final Salvaschermo modello;
    private final Chiusura chiusura;
    private final float density;

    private final Paint pennelloFoto = new Paint(Paint.FILTER_BITMAP_FLAG | Paint.ANTI_ALIAS_FLAG);
    private final Paint velo = new Paint();
    private final Paint scrim = new Paint();
    private final Paint testoOra = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint testoData = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint testoNota = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint[] chiazze = new Paint[AURORA.length];
    private final Matrix matrice = new Matrix();

    private final SimpleDateFormat formatoOra = new SimpleDateFormat("HH:mm", Locale.ITALIAN);
    private final SimpleDateFormat formatoData = new SimpleDateFormat("EEEE d MMMM", Locale.ITALIAN);

    private boolean attivo;
    private long partito;

    private ArrayList<Salvaschermo.Foto> foto = new ArrayList<Salvaschermo.Foto>();
    private int indice = -1;
    private Bitmap attuale;
    private long attualeDa;
    private int attualeVerso;
    private String didascalia = "";
    private Bitmap precedente;
    private long precedenteDa;
    private int precedenteVerso;
    private boolean inCarico;

    public SalvaschermoView(Context context, Salvaschermo modello, Chiusura chiusura) {
        super(context);
        this.modello = modello;
        this.chiusura = chiusura;
        density = context.getResources().getDisplayMetrics().density;

        Typeface sottile = Typeface.create("sans-serif-light", Typeface.NORMAL);
        testoOra.setTypeface(sottile);
        testoOra.setColor(0xFFF4F6FA);
        testoData.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        testoData.setColor(0xEEF4F6FA);
        testoNota.setTypeface(Typeface.create("sans-serif", Typeface.NORMAL));
        testoNota.setColor(0x99F4F6FA);
        for (Paint p : new Paint[] { testoOra, testoData, testoNota }) {
            // L'ombra tiene leggibile il testo sopra una foto chiara, senza un riquadro dietro.
            p.setShadowLayer(6 * density, 0, 1.5f * density, 0x80000000);
        }
        setVisibility(INVISIBLE);
    }

    public boolean attivo() {
        return attivo;
    }

    public void avvia() {
        if (attivo) return;
        attivo = true;
        partito = SystemClock.uptimeMillis();
        foto = modello.disponibili();
        indice = -1;
        setVisibility(VISIBLE);
        if (FOTO.equals(stile())) prossima();
        invalidate();
    }

    public void ferma() {
        if (!attivo) return;
        attivo = false;
        setVisibility(INVISIBLE);
        // Le foto si liberano subito: sono un paio di megabyte l'una, e sul Tab 3
        // la memoria per l'interfaccia e' poca.
        if (attuale != null) attuale.recycle();
        if (precedente != null) precedente.recycle();
        attuale = null;
        precedente = null;
    }

    /** Senza foto, lo stile foto diventa l'aurora: un salvaschermo nero sembrerebbe un guasto. */
    private String stile() {
        // Sull'elenco letto all'avvio, non sul disco: lo si chiede trenta volte al secondo.
        if (Salvaschermo.FOTO.equals(modello.stile) && foto.isEmpty()) return Salvaschermo.AURORA;
        return modello.stile;
    }

    private static final String FOTO = Salvaschermo.FOTO;

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        if (e.getActionMasked() == MotionEvent.ACTION_DOWN && attivo) chiusura.chiuso();
        return true;
    }

    @Override
    protected void onSizeChanged(int w, int h, int vw, int vh) {
        float r = Math.max(w, h) * 0.55f;
        for (int i = 0; i < chiazze.length; i++) {
            Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
            int colore = AURORA[i];
            p.setShader(new RadialGradient(0, 0, r,
                    new int[] { (colore & 0x00FFFFFF) | 0x90000000, (colore & 0x00FFFFFF) | 0x30000000, colore & 0x00FFFFFF },
                    new float[] { 0f, 0.45f, 1f }, Shader.TileMode.CLAMP));
            chiazze[i] = p;
        }
        scrim.setShader(new LinearGradient(0, h * 0.45f, 0, h,
                0x00000000, 0xA0000000, Shader.TileMode.CLAMP));
        testoOra.setTextSize(h * 0.22f);
        testoData.setTextSize(h * 0.046f);
        testoNota.setTextSize(h * 0.027f);
    }

    @Override
    protected void onDraw(Canvas c) {
        if (!attivo) return;
        long t = SystemClock.uptimeMillis();
        int w = getWidth(), h = getHeight();
        String stile = stile();

        c.drawColor(0xFF05070A);
        if (FOTO.equals(stile)) {
            disegnaFoto(c, t, w, h);
            c.drawRect(0, 0, w, h, scrim);
        } else if (Salvaschermo.AURORA.equals(stile)) {
            disegnaAurora(c, t, w, h);
        }

        if (modello.attenuazione > 0) {
            velo.setColor((int) (modello.attenuazione * 2.55f) << 24);
            c.drawRect(0, 0, w, h, velo);
        }

        disegnaOra(c, stile, w, h);

        postInvalidateDelayed(Salvaschermo.OROLOGIO.equals(stile) ? 1000 : PASSO_ANIMAZIONE);
    }

    private void disegnaFoto(Canvas c, long t, int w, int h) {
        if (attuale != null && t - attualeDa > modello.ogniMs && !inCarico) prossima();

        float entrata = attuale == null ? 1f : Math.min(1f, (t - attualeDa) / (float) SFUMATURA_MS);
        if (precedente != null) {
            disegnaMossa(c, precedente, t - precedenteDa, precedenteVerso, 255, w, h);
            if (entrata >= 1f) {
                precedente.recycle();
                precedente = null;
            }
        }
        if (attuale != null) disegnaMossa(c, attuale, t - attualeDa, attualeVerso, (int) (entrata * 255), w, h);
    }

    /**
     * Si avvicina e scorre, un verso per foto: il movimento dura quanto la foto
     * resta davanti piu' la sfumatura, cosi' non si ferma mai mentre la si guarda.
     */
    private void disegnaMossa(Canvas c, Bitmap b, long eta, int verso, int alfa, int w, int h) {
        float durata = modello.ogniMs + SFUMATURA_MS;
        float p = Math.min(1f, eta / durata);
        // Un'accelerazione morbida: all'inizio e alla fine il movimento rallenta.
        float morbido = p * p * (3 - 2 * p);
        float copertura = Math.max(w / (float) b.getWidth(), h / (float) b.getHeight());
        float scala = copertura * (1.04f + 0.10f * morbido);
        float larga = b.getWidth() * scala, alta = b.getHeight() * scala;
        float margineX = (larga - w) / 2f, margineY = (alta - h) / 2f;
        float x = -margineX + verso * margineX * (morbido * 2 - 1) * 0.9f;
        float y = -margineY + margineY * (verso > 0 ? 0.4f : -0.4f) * (morbido * 2 - 1);
        matrice.setScale(scala, scala);
        matrice.postTranslate(x, y);
        pennelloFoto.setAlpha(alfa);
        c.drawBitmap(b, matrice, pennelloFoto);
    }

    private void disegnaAurora(Canvas c, long t, int w, int h) {
        double s = (t - partito) / 1000.0;
        for (int i = 0; i < chiazze.length; i++) {
            Paint p = chiazze[i];
            if (p == null) continue;
            float cx = (float) (w * (0.5 + 0.42 * Math.sin(s * (0.045 + i * 0.013) + i * 1.7)));
            float cy = (float) (h * (0.5 + 0.38 * Math.cos(s * (0.037 + i * 0.011) + i * 2.3)));
            float r = Math.max(w, h) * 0.55f;
            c.save();
            c.translate(cx, cy);
            c.drawCircle(0, 0, r, p);
            c.restore();
        }
    }

    private void disegnaOra(Canvas c, String stile, int w, int h) {
        if (!modello.ora && !modello.data && didascalia.length() == 0) return;
        Date adesso = new Date();
        float margine = Math.min(w, h) * 0.08f;
        float x = margine;
        float base = h - margine;

        if (Salvaschermo.OROLOGIO.equals(stile)) {
            // Qualche pixel diverso ogni minuto, sempre dentro il suo angolo.
            long minuti = System.currentTimeMillis() / 60000L;
            x += (minuti * 37 % 90) * density;
            base -= (minuti * 23 % 60) * density;
        }

        if (FOTO.equals(stile) && didascalia.length() > 0) {
            c.drawText(taglia(didascalia, testoNota, w - x - margine), x, base, testoNota);
            base -= testoNota.getTextSize() * 1.8f;
        }
        if (modello.data) {
            String giorno = formatoData.format(adesso);
            giorno = Character.toUpperCase(giorno.charAt(0)) + giorno.substring(1);
            c.drawText(giorno, x, base, testoData);
            base -= testoData.getTextSize() * 1.35f;
        }
        if (modello.ora) {
            // drawText parte dal bordo sinistro del primo glifo piu' il suo margine: si toglie,
            // cosi' l'ora e la data stanno sulla stessa linea a sinistra.
            float rientro = testoOra.getTextSize() * 0.04f;
            c.drawText(formatoOra.format(adesso), x - rientro, base, testoOra);
        }
    }

    private static String taglia(String s, Paint p, float largo) {
        if (p.measureText(s) <= largo) return s;
        int n = p.breakText(s, true, largo - p.measureText("…"), null);
        return s.substring(0, Math.max(0, n)).trim() + "…";
    }

    /** La foto dopo, letta fuori dal thread dell'interfaccia: un JPEG da 1024x600 costa decine di millisecondi. */
    private void prossima() {
        if (foto.isEmpty()) return;
        indice = (indice + 1) % foto.size();
        final Salvaschermo.Foto scelta = foto.get(indice);
        inCarico = true;
        new Thread(new Runnable() {
            @Override public void run() {
                BitmapFactory.Options o = new BitmapFactory.Options();
                o.inPreferredConfig = Bitmap.Config.RGB_565;
                Bitmap letta = null;
                try {
                    letta = BitmapFactory.decodeFile(modello.file(scelta.nome).getPath(), o);
                } catch (OutOfMemoryError e) {
                    // Resta la foto di prima: meglio ferma che un'app che si chiude.
                }
                final Bitmap pronta = letta;
                post(new Runnable() {
                    @Override public void run() {
                        inCarico = false;
                        if (pronta == null) return;
                        if (!attivo) {
                            pronta.recycle();
                            return;
                        }
                        if (precedente != null) precedente.recycle();
                        precedente = attuale;
                        precedenteDa = attualeDa;
                        precedenteVerso = attualeVerso;
                        attuale = pronta;
                        attualeDa = SystemClock.uptimeMillis();
                        attualeVerso = indice % 2 == 0 ? 1 : -1;
                        didascalia = scelta.didascalia;
                        invalidate();
                    }
                });
            }
        }, "tabdeck-salvaschermo").start();
    }
}
