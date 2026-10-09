package dev.tabdeck;

import android.content.Context;
import android.content.Intent;
import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.net.VpnService;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelFileDescriptor;
import android.util.Log;

import java.io.DataInputStream;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.OutputStream;

/**
 * « Internet col cavo: tutto il tablet ». Una VPN locale: Android le consegna i pacchetti
 * IP di tutte le app, e lei li manda al PC sul secondo canale del cavo
 * ({@link Proto#SOCKET_RETE}); il PC apre le connessioni vere e rimanda le risposte
 * (Rete.cs). E' lo schema di gnirehtet, ma rovesciato: su Android 4.4 non c'e'
 * « adb reverse », quindi e' il PC a collegarsi qui, come per il canale principale.
 *
 * La VPN vive quanto il canale: il PC si collega solo col cavo e solo con quella scelta,
 * e chiudendolo la spegne. Il consenso di Android si chiede la prima volta, e una volta
 * sola per accensione dell'app: rifiutato, il PC riprova in silenzio.
 */
public final class Tunnel extends VpnService {

    private static final String TAG = "TabDeck.Tunnel";

    /** L'indirizzo del tablet dentro la VPN, e quello finto che il PC tratta da DNS. */
    static final String INDIRIZZO = "10.111.0.2";
    static final String DNS = "10.111.0.1";

    /** Chi mostra la richiesta di consenso: l'activity, quando c'e'. Thread UI. */
    public interface Consenso {
        void chiedi(Intent richiesta);
    }

    public static volatile Consenso consenso;

    private static volatile boolean inAscolto;
    private static volatile boolean chiesto;
    private static LocalSocket canale;
    private static Tunnel attivo;

    private ParcelFileDescriptor tun;

    /** Idempotente: il canale si ascolta una volta per processo, come quello principale. */
    public static synchronized void avvia(final Context context) {
        if (inAscolto) return;
        inAscolto = true;
        final Context app = context.getApplicationContext();
        Thread t = new Thread(new Runnable() {
            @Override public void run() { ascolta(app); }
        }, "tabdeck-rete");
        t.setDaemon(true);
        t.start();
    }

    private static void ascolta(Context app) {
        LocalServerSocket server = null;
        while (true) {
            try {
                if (server == null) server = new LocalServerSocket(Proto.SOCKET_RETE);
                LocalSocket s = server.accept();
                final Intent richiesta = VpnService.prepare(app);
                if (richiesta != null) {
                    // Senza consenso la VPN non parte: si chiede una volta, e il canale si
                    // chiude. Il PC riprova, e dopo il « si' » trova la strada libera.
                    chiudi(s);
                    if (!chiesto) {
                        chiesto = true;
                        new Handler(Looper.getMainLooper()).post(new Runnable() {
                            @Override public void run() {
                                Consenso c = consenso;
                                if (c != null) c.chiedi(richiesta);
                                else chiesto = false;
                            }
                        });
                    }
                    continue;
                }
                LocalSocket prima;
                synchronized (Tunnel.class) {
                    prima = canale;
                    canale = s;
                }
                if (prima != null) chiudi(prima);
                app.startService(new Intent(app, Tunnel.class));
            } catch (IOException e) {
                Log.w(TAG, "canale di rete non in ascolto, riprovo: " + e.getMessage());
                if (server != null) {
                    try { server.close(); } catch (IOException ignorata) { }
                }
                server = null;
                try { Thread.sleep(5000); } catch (InterruptedException ignorata) { return; }
            }
        }
    }

    /** Il consenso e' stato dato: alla prossima richiesta del PC la VPN parte. */
    public static void consensoDato() {
        chiesto = false;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        final LocalSocket s;
        synchronized (Tunnel.class) {
            s = canale;
            canale = null;
        }
        if (s == null) return START_NOT_STICKY;

        spegni();
        try {
            tun = new Builder()
                    .setSession("TabDeck")
                    .setMtu(1500)
                    .addAddress(INDIRIZZO, 32)
                    .addRoute("0.0.0.0", 0)
                    .addDnsServer(DNS)
                    .establish();
        } catch (Exception e) {
            Log.w(TAG, "VPN non avviata: " + e.getMessage());
            tun = null;
        }
        if (tun == null) {
            chiudi(s);
            stopSelf();
            return START_NOT_STICKY;
        }
        synchronized (Tunnel.class) {
            attivo = this;
        }
        Log.i(TAG, "internet dal PC col cavo: acceso");

        final ParcelFileDescriptor mio = tun;
        Thread versoPc = new Thread(new Runnable() {
            @Override public void run() { versoPc(mio, s); }
        }, "tabdeck-rete-su");
        Thread dalPc = new Thread(new Runnable() {
            @Override public void run() { dalPc(mio, s); }
        }, "tabdeck-rete-giu");
        versoPc.start();
        dalPc.start();
        return START_NOT_STICKY;
    }

    /** Dalla VPN al PC: un pacchetto per lettura, preceduto dalla sua lunghezza. */
    private void versoPc(ParcelFileDescriptor mio, LocalSocket s) {
        byte[] pacchetto = new byte[32767];
        byte[] testa = new byte[2];
        try {
            FileInputStream in = new FileInputStream(mio.getFileDescriptor());
            OutputStream out = s.getOutputStream();
            while (true) {
                int n = in.read(pacchetto);
                if (n < 0) break;
                if (n == 0) {
                    Thread.sleep(10);
                    continue;
                }
                testa[0] = (byte) (n >> 8);
                testa[1] = (byte) n;
                out.write(testa);
                out.write(pacchetto, 0, n);
            }
        } catch (Exception e) {
            Log.i(TAG, "verso il PC: " + e.getMessage());
        }
        fine(mio, s);
    }

    /** Dal PC alla VPN: le risposte, gia' pacchetti IP completi. */
    private void dalPc(ParcelFileDescriptor mio, LocalSocket s) {
        byte[] pacchetto = new byte[65535];
        try {
            DataInputStream in = new DataInputStream(s.getInputStream());
            FileOutputStream out = new FileOutputStream(mio.getFileDescriptor());
            while (true) {
                int n = in.readUnsignedShort();
                in.readFully(pacchetto, 0, n);
                out.write(pacchetto, 0, n);
            }
        } catch (Exception e) {
            Log.i(TAG, "dal PC: " + e.getMessage());
        }
        fine(mio, s);
    }

    /** Il canale e' finito, da una parte o dall'altra: si spegne tutto, una volta sola. */
    private void fine(ParcelFileDescriptor mio, LocalSocket s) {
        chiudi(s);
        synchronized (Tunnel.class) {
            if (tun != mio) return;
            spegni();
            if (attivo == this) attivo = null;
        }
        Log.i(TAG, "internet dal PC col cavo: spento");
        stopSelf();
    }

    private void spegni() {
        if (tun == null) return;
        try { tun.close(); } catch (IOException ignorata) { }
        tun = null;
    }

    @Override
    public void onRevoke() {
        // L'utente l'ha spenta dalle impostazioni di Android: si chiude anche il canale.
        synchronized (Tunnel.class) {
            spegni();
            if (attivo == this) attivo = null;
        }
        stopSelf();
    }

    @Override
    public void onDestroy() {
        synchronized (Tunnel.class) {
            spegni();
            if (attivo == this) attivo = null;
        }
        super.onDestroy();
    }

    private static void chiudi(LocalSocket s) {
        try { s.close(); } catch (IOException ignorata) { }
    }
}
