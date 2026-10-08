package dev.tabdeck;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Rect;
import android.os.Handler;
import android.os.SystemClock;
import android.util.Log;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.ViewConfiguration;

/**
 * Schermo remoto. Le tile JPEG arrivano dal thread di rete, vengono decodificate
 * e compositate su un bitmap RGB_565 a piena area sorgente; a ogni PRESENT il
 * bitmap viene riversato sulla surface. Nessun passaggio dal thread UI: su un
 * PXA986 il main looper non reggerebbe venti decodifiche al secondo.
 *
 * Il touch viene tradotto in mouse: tocco = clic sinistro, trascinamento = drag,
 * pressione lunga = clic destro, due dita = rotella.
 */
public final class ScreenView extends SurfaceView implements SurfaceHolder.Callback {

    private static final String TAG = "TabDeck.Screen";
    private final Object frameLock = new Object();
    /**
     * lockCanvas non ammette due chiamate contemporanee. A present() si arriva
     * sia dal thread di rete (nuovo frame) sia da quello UI (cambio di stato o
     * di dimensioni), quindi il disegno va serializzato a parte.
     */
    private final Object presentLock = new Object();
    private final Paint paint = new Paint();
    private final BitmapFactory.Options opts = new BitmapFactory.Options();
    private final Handler handler = new Handler();
    private final int touchSlop;

    private Link link;
    private Bitmap frame;          // composito a piena area sorgente
    private Canvas frameCanvas;
    private Bitmap scratch;        // bitmap riusato per decodificare le tile
    private int srcW, srcH;
    private volatile boolean surfaceReady;
    private String status = "In attesa del PC";

    // Misura della resa reale del pannello. Serve a rispondere a una domanda
    // sola: il tablet sta dietro al PC, o e' lui il collo di bottiglia?
    private static final long REPORT_MS = 5000;
    private long windowStart;
    private int framesInWindow, tilesInWindow;
    private long decodeMsInWindow, drawMsInWindow;

    // Stato del gesto in corso.
    private static final int MODE_IDLE = 0, MODE_TAP = 1, MODE_DRAG = 2,
            MODE_SCROLL = 3, MODE_SCROLL1 = 4, MODE_DRAG2 = 5;
    private int mode = MODE_IDLE;
    private float downX, downY;
    private long lastMoveSent;
    private float scrollAccum;
    private boolean gestureConsumed;

    private final Runnable longPress = new Runnable() {
        @Override public void run() {
            if (mode != MODE_TAP) return;
            gestureConsumed = true;
            mode = MODE_IDLE;
            sendAt(Proto.TOUCH_RIGHT, downX, downY);
            performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);
        }
    };

    public ScreenView(Context ctx) {
        super(ctx);
        touchSlop = ViewConfiguration.get(ctx).getScaledTouchSlop();
        opts.inPreferredConfig = Bitmap.Config.RGB_565;
        opts.inMutable = true;
        paint.setFilterBitmap(true);
        paint.setDither(false);
        getHolder().addCallback(this);
        setFocusable(true);
    }

    public void setLink(Link link) {
        this.link = link;
    }

    public void setStatus(String s) {
        status = s;
        present();
    }

    /** Chiamato quando il PC annuncia la geometria dello schermo trasmesso. */
    public void configure(int w, int h) {
        if (w <= 0 || h <= 0) return;
        synchronized (frameLock) {
            if (frame != null && frame.getWidth() == w && frame.getHeight() == h) return;
            recycle();
            try {
                frame = Bitmap.createBitmap(w, h, Bitmap.Config.RGB_565);
            } catch (OutOfMemoryError e) {
                Log.e(TAG, "memoria insufficiente per un frame " + w + "x" + h);
                return;
            }
            frame.eraseColor(Color.BLACK);
            frameCanvas = new Canvas(frame);
            srcW = w;
            srcH = h;
        }
        Log.i(TAG, "area sorgente " + w + "x" + h);
    }

    /** Thread di rete. */
    public void onTile(int x, int y, int w, int h, byte[] buf, int off, int len) {
        long t0 = SystemClock.uptimeMillis();
        synchronized (frameLock) {
            if (frameCanvas == null) return;
            Bitmap tile = decode(buf, off, len, w, h);
            if (tile == null) return;
            frameCanvas.drawBitmap(tile, x, y, null);
            if (scratch != null && scratch != tile) scratch.recycle();
            scratch = tile;
        }
        decodeMsInWindow += SystemClock.uptimeMillis() - t0;
        tilesInWindow++;
    }

    /**
     * Decodifica riusando l'allocazione della tile precedente: senza riuso il
     * garbage collector di Dalvik viene chiamato a ogni tile e lo scorrimento
     * diventa a scatti.
     */
    private Bitmap decode(byte[] buf, int off, int len, int w, int h) {
        opts.inBitmap = isReusable(scratch, w, h) ? scratch : null;
        try {
            return BitmapFactory.decodeByteArray(buf, off, len, opts);
        } catch (IllegalArgumentException e) {
            opts.inBitmap = null;   // allocazione non compatibile: si riprova pulito
            try {
                return BitmapFactory.decodeByteArray(buf, off, len, opts);
            } catch (RuntimeException e2) {
                Log.w(TAG, "tile illeggibile: " + e2.getMessage());
                return null;
            }
        } catch (OutOfMemoryError e) {
            Log.w(TAG, "memoria esaurita nella decodifica di una tile");
            return null;
        }
    }

    /** Thread di rete, ma anche UI: vedi presentLock. */
    public void present() {
        if (!surfaceReady) return;
        long t0 = SystemClock.uptimeMillis();
        synchronized (presentLock) {
            if (!surfaceReady) return;
            SurfaceHolder holder = getHolder();
            Canvas c = holder.lockCanvas();
            if (c == null) return;
            try {
                synchronized (frameLock) {
                    if (frame == null) {
                        drawPlaceholder(c);
                    } else {
                        Rect dst = fit(c.getWidth(), c.getHeight());
                        if (dst.width() != c.getWidth() || dst.height() != c.getHeight()) {
                            c.drawColor(Color.BLACK);
                        }
                        paint.setFilterBitmap(dst.width() != srcW || dst.height() != srcH);
                        c.drawBitmap(frame, null, dst, paint);
                    }
                }
            } finally {
                holder.unlockCanvasAndPost(c);
            }
        }
        drawMsInWindow += SystemClock.uptimeMillis() - t0;
        report(t0);
    }

    /** Una riga ogni cinque secondi, non una per frame: il log non deve pesare. */
    private void report(long now) {
        framesInWindow++;
        if (windowStart == 0) {
            windowStart = now;
            return;
        }
        long span = now - windowStart;
        if (span < REPORT_MS) return;
        Log.i(TAG, String.format("resa: %.1f fps, %.1f tessere/frame, decodifica %d ms/s, disegno %d ms/s",
                framesInWindow * 1000f / span,
                framesInWindow > 0 ? (float) tilesInWindow / framesInWindow : 0f,
                decodeMsInWindow * 1000 / span,
                drawMsInWindow * 1000 / span));
        windowStart = now;
        framesInWindow = 0;
        tilesInWindow = 0;
        decodeMsInWindow = 0;
        drawMsInWindow = 0;
    }

    /** Rettangolo di destinazione che conserva le proporzioni della sorgente. */
    private Rect fit(int vw, int vh) {
        if (srcW <= 0 || srcH <= 0) return new Rect(0, 0, vw, vh);
        float scale = Math.min((float) vw / srcW, (float) vh / srcH);
        int w = Math.round(srcW * scale), h = Math.round(srcH * scale);
        int left = (vw - w) / 2, top = (vh - h) / 2;
        return new Rect(left, top, left + w, top + h);
    }

    private void drawPlaceholder(Canvas c) {
        c.drawColor(Color.BLACK);
        Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        p.setColor(0xFF8A8D95);
        p.setTextSize(18 * getResources().getDisplayMetrics().density);
        p.setTextAlign(Paint.Align.CENTER);
        c.drawText(status, c.getWidth() / 2f, c.getHeight() / 2f, p);
    }

    private static boolean isReusable(Bitmap b, int w, int h) {
        if (b == null || b.isRecycled() || !b.isMutable()) return false;
        // Su API 19 basta che l'allocazione sia capiente quanto la nuova tile.
        return b.getAllocationByteCount() >= w * h * 2;
    }

    private void recycle() {
        if (frame != null) frame.recycle();
        if (scratch != null) scratch.recycle();
        frame = null;
        scratch = null;
        frameCanvas = null;
    }

    // ---- touch verso mouse ----

    /**
     * Come vanno letti i gesti. Sono due filosofie diverse, non due varianti
     * dello stesso comportamento:
     *
     * PUNTATORE - il dito e' il mouse. Muoverlo trascina col tasto sinistro,
     * che sul desktop disegna il rettangolo di selezione. Va bene per lavorare
     * dentro una finestra, non per leggere.
     *
     * TOCCO - il dito e' il contenuto, come su un telefono. Uno swipe scorre,
     * un tocco clicca, due dita trascinano davvero. E' quello che ci si aspetta
     * da uno schermo touch, ed e' il default.
     */
    public static final int TOUCH_POINTER = 0, TOUCH_NATURAL = 1;

    private volatile int touchMode = TOUCH_NATURAL;
    private volatile long longPressMs = 550;
    private volatile int wheelNotchPx = 48;
    private volatile boolean invertScroll;

    public void setTouchConfig(int mode, long longPress, int notchPx, boolean invert) {
        touchMode = mode;
        longPressMs = Math.max(150, longPress);
        wheelNotchPx = Math.max(4, notchPx);
        invertScroll = invert;
        Log.i(TAG, "gesti: " + (mode == TOUCH_POINTER ? "puntatore" : "tocco")
                + ", pressione lunga " + longPressMs + " ms, tacca " + wheelNotchPx + " px");
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        if (link == null || srcW <= 0) return true;

        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                mode = MODE_TAP;
                gestureConsumed = false;
                downX = e.getX();
                downY = e.getY();
                scrollAccum = 0;
                // Col puntatore il cursore segue subito il dito: si vuole vedere
                // dove si sta per cliccare. Nella modalita' a tocco no, altrimenti
                // ogni scorrimento porterebbe a spasso il cursore sul PC.
                if (touchMode == TOUCH_POINTER) sendAt(Proto.TOUCH_MOVE, downX, downY);
                handler.postDelayed(longPress, longPressMs);
                return true;

            case MotionEvent.ACTION_POINTER_DOWN:
                if (e.getPointerCount() == 2) {
                    handler.removeCallbacks(longPress);
                    if (mode == MODE_DRAG) sendAt(Proto.TOUCH_UP, e.getX(0), e.getY(0));
                    scrollAccum = 0;
                    if (touchMode == TOUCH_POINTER) {
                        mode = MODE_SCROLL;
                        downY = midY(e);
                    } else {
                        // Due dita nella modalita' a tocco = trascinamento vero:
                        // e' cosi' che si seleziona del testo o si sposta una
                        // finestra, senza che ogni semplice swipe lo faccia.
                        mode = MODE_DRAG2;
                        downX = midX(e);
                        downY = midY(e);
                        sendAt(Proto.TOUCH_DOWN, downX, downY);
                    }
                }
                return true;

            case MotionEvent.ACTION_MOVE:
                onMove(e);
                return true;

            case MotionEvent.ACTION_POINTER_UP:
                // Un dito si alza da un gesto a due: il gesto finisce qui, e
                // quello rimasto a terra non deve diventare un clic.
                if (mode == MODE_DRAG2) sendAt(Proto.TOUCH_UP, downX, downY);
                if (mode == MODE_DRAG2 || mode == MODE_SCROLL) {
                    mode = MODE_IDLE;
                    gestureConsumed = true;
                }
                return true;

            case MotionEvent.ACTION_UP:
                handler.removeCallbacks(longPress);
                if (mode == MODE_DRAG) {
                    sendAt(Proto.TOUCH_MOVE, e.getX(), e.getY());
                    sendAt(Proto.TOUCH_UP, e.getX(), e.getY());
                } else if (mode == MODE_DRAG2) {
                    sendAt(Proto.TOUCH_UP, e.getX(), e.getY());
                } else if (mode == MODE_TAP && !gestureConsumed) {
                    // Due tocchi ravvicinati diventano un doppio clic da soli:
                    // e' Windows a interpretarli come tali, quindi non serve
                    // mandare niente di diverso ne' aspettare per capire se ne
                    // arriva un secondo.
                    sendAt(Proto.TOUCH_DOWN, e.getX(), e.getY());
                    sendAt(Proto.TOUCH_UP, e.getX(), e.getY());
                }
                mode = MODE_IDLE;
                return true;

            case MotionEvent.ACTION_CANCEL:
                handler.removeCallbacks(longPress);
                if (mode == MODE_DRAG || mode == MODE_DRAG2) sendAt(Proto.TOUCH_UP, e.getX(), e.getY());
                mode = MODE_IDLE;
                return true;
        }
        return true;
    }

    private void onMove(MotionEvent e) {
        if (mode == MODE_SCROLL && e.getPointerCount() >= 2) {
            scroll(midY(e));
        } else if (mode == MODE_SCROLL1) {
            scroll(e.getY());
        } else if (mode == MODE_DRAG2 && e.getPointerCount() >= 2) {
            long now = SystemClock.uptimeMillis();
            if (now - lastMoveSent >= 16) {
                lastMoveSent = now;
                sendAt(Proto.TOUCH_MOVE, midX(e), midY(e));
            }
        } else if (mode == MODE_TAP) {
            if (Math.abs(e.getX() - downX) > touchSlop || Math.abs(e.getY() - downY) > touchSlop) {
                handler.removeCallbacks(longPress);
                if (touchMode == TOUCH_POINTER) {
                    mode = MODE_DRAG;
                    sendAt(Proto.TOUCH_DOWN, downX, downY);
                    sendAt(Proto.TOUCH_MOVE, e.getX(), e.getY());
                } else {
                    mode = MODE_SCROLL1;
                    downY = e.getY();
                    scrollAccum = 0;
                }
            }
        } else if (mode == MODE_DRAG) {
            long now = SystemClock.uptimeMillis();
            if (now - lastMoveSent >= 16) {          // circa 60 aggiornamenti al secondo
                lastMoveSent = now;
                sendAt(Proto.TOUCH_MOVE, e.getX(), e.getY());
            }
        }
    }

    /** Converte lo spostamento verticale del dito in tacche di rotella. */
    private void scroll(float y) {
        scrollAccum += (y - downY);
        downY = y;
        while (Math.abs(scrollAccum) >= wheelNotchPx) {
            int dir = scrollAccum > 0 ? 1 : -1;
            link.sendWheel((invertScroll ? -dir : dir) * 120);
            scrollAccum -= dir * wheelNotchPx;
        }
    }

    private static float midX(MotionEvent e) {
        return (e.getX(0) + e.getX(1)) / 2f;
    }

    private static float midY(MotionEvent e) {
        return (e.getY(0) + e.getY(1)) / 2f;
    }

    /** Converte da coordinate della vista a coordinate dell'area catturata sul PC. */
    private void sendAt(int action, float vx, float vy) {
        Rect dst = fit(getWidth(), getHeight());
        if (dst.width() == 0 || dst.height() == 0) return;
        int x = Math.round((vx - dst.left) * srcW / dst.width());
        int y = Math.round((vy - dst.top) * srcH / dst.height());
        x = Math.max(0, Math.min(srcW - 1, x));
        y = Math.max(0, Math.min(srcH - 1, y));
        link.sendTouch(action, x, y);
    }

    // ---- ciclo di vita della surface ----

    @Override public void surfaceCreated(SurfaceHolder h) {
        surfaceReady = true;
        present();
    }

    @Override public void surfaceChanged(SurfaceHolder h, int fmt, int w, int height) {
        present();
    }

    @Override public void surfaceDestroyed(SurfaceHolder h) {
        surfaceReady = false;
    }

    public void release() {
        synchronized (frameLock) {
            recycle();
        }
    }
}
