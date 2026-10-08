package dev.tabdeck;

import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.util.LongSparseArray;

/**
 * Le icone dell'orologio: <b>Material Symbols di Google</b> (stile rounded, peso
 * 400, Apache 2.0), le stesse di Android, che si riconoscono senza impararle.
 *
 * Si chiama cosi' e non Icone perche' {@link Icone} in TabDeck c'e' gia', ed e'
 * un'altra cosa: le immagini dei pulsanti del deck mandate dal PC. Queste sono
 * nove forme vettoriali, e arrivano dall'altro tablet insieme alla ghiera.
 *
 * Non sono glifi di un font - quelli di KitKat sono stati provati uno per uno,
 * e ne mancano - ma percorsi: la stringa si analizza la prima volta che l'icona
 * si vede, diventa un {@link Path} nel quadrato da zero a uno, e da li' in poi
 * disegnarla costa una matrice e un {@code drawPath}.
 */
public final class Simboli {

    private Simboli() {}

    public static final int SVEGLIA = 0;
    public static final int SVEGLIA_PIENA = 1;
    public static final int AVVIA = 2;
    public static final int CHIUDI = 3;
    public static final int CESTINO = 4;
    public static final int SPUNTA = 5;
    public static final int TIMER = 6;
    public static final int SVEGLIA_PIU = 7;
    public static final int CLESSIDRA = 8;

    /** Coordinate nel riquadro {@code 0 -960 960 960}: x da 0 a 960, y da -960 a 0. */
    private static final String[] DATI = {
        /* SVEGLIA        */ "M520-456v-144q0-17-11.5-28.5T480-640q-17 0-28.5 11.5T440-600v159q0 8 3 15.5t9 13.5l112 112q11 11 28 11t28-11q11-11 11-28t-11-28L520-456ZM339.5-108.5q-65.5-28.5-114-77t-77-114Q120-365 120-440t28.5-140.5q28.5-65.5 77-114t114-77Q405-800 480-800t140.5 28.5q65.5 28.5 114 77t77 114Q840-515 840-440t-28.5 140.5q-28.5 65.5-77 114t-114 77Q555-80 480-80t-140.5-28.5ZM480-440ZM82-668q-11-11-11-28t11-28l114-114q11-11 28-11t28 11q11 11 11 28t-11 28L138-668q-11 11-28 11t-28-11Zm796 0q-11 11-28 11t-28-11L708-782q-11-11-11-28t11-28q11-11 28-11t28 11l114 114q11 11 11 28t-11 28ZM480-160q117 0 198.5-81.5T760-440q0-117-81.5-198.5T480-720q-117 0-198.5 81.5T200-440q0 117 81.5 198.5T480-160Z",
        /* SVEGLIA_PIENA  */ "M520-456v-144q0-17-11.5-28.5T480-640q-17 0-28.5 11.5T440-600v159q0 8 3 15.5t9 13.5l112 112q11 11 28 11t28-11q11-11 11-28t-11-28L520-456ZM339.5-108.5q-65.5-28.5-114-77t-77-114Q120-365 120-440t28.5-140.5q28.5-65.5 77-114t114-77Q405-800 480-800t140.5 28.5q65.5 28.5 114 77t77 114Q840-515 840-440t-28.5 140.5q-28.5 65.5-77 114t-114 77Q555-80 480-80t-140.5-28.5ZM82-668q-11-11-11-28t11-28l114-114q11-11 28-11t28 11q11 11 11 28t-11 28L138-668q-11 11-28 11t-28-11Zm796 0q-11 11-28 11t-28-11L708-782q-11-11-11-28t11-28q11-11 28-11t28 11l114 114q11 11 11 28t-11 28Z",
        /* AVVIA          */ "M320-273v-414q0-17 12-28.5t28-11.5q5 0 10.5 1.5T381-721l326 207q9 6 13.5 15t4.5 19q0 10-4.5 19T707-446L381-239q-5 3-10.5 4.5T360-233q-16 0-28-11.5T320-273Z",
        /* CHIUDI         */ "M480-424 284-228q-11 11-28 11t-28-11q-11-11-11-28t11-28l196-196-196-196q-11-11-11-28t11-28q11-11 28-11t28 11l196 196 196-196q11-11 28-11t28 11q11 11 11 28t-11 28L536-480l196 196q11 11 11 28t-11 28q-11 11-28 11t-28-11L480-424Z",
        /* CESTINO        */ "M280-120q-33 0-56.5-23.5T200-200v-520q-17 0-28.5-11.5T160-760q0-17 11.5-28.5T200-800h160q0-17 11.5-28.5T400-840h160q17 0 28.5 11.5T600-800h160q17 0 28.5 11.5T800-760q0 17-11.5 28.5T760-720v520q0 33-23.5 56.5T680-120H280Zm148.5-171.5Q440-303 440-320v-280q0-17-11.5-28.5T400-640q-17 0-28.5 11.5T360-600v280q0 17 11.5 28.5T400-280q17 0 28.5-11.5Zm160 0Q600-303 600-320v-280q0-17-11.5-28.5T560-640q-17 0-28.5 11.5T520-600v280q0 17 11.5 28.5T560-280q17 0 28.5-11.5Z",
        /* SPUNTA         */ "m382-354 339-339q12-12 28-12t28 12q12 12 12 28.5T777-636L410-268q-12 12-28 12t-28-12L182-440q-12-12-11.5-28.5T183-497q12-12 28.5-12t28.5 12l142 143Z",
        /* TIMER          */ "M400-840q-17 0-28.5-11.5T360-880q0-17 11.5-28.5T400-920h160q17 0 28.5 11.5T600-880q0 17-11.5 28.5T560-840H400Zm108.5 428.5Q520-423 520-440v-160q0-17-11.5-28.5T480-640q-17 0-28.5 11.5T440-600v160q0 17 11.5 28.5T480-400q17 0 28.5-11.5Zm-168 303Q275-137 226-186t-77.5-114.5Q120-366 120-440t28.5-139.5Q177-645 226-694t114.5-77.5Q406-800 480-800q62 0 119 20t107 58l28-28q11-11 28-11t28 11q11 11 11 28t-11 28l-28 28q38 50 58 107t20 119q0 74-28.5 139.5T734-186q-49 49-114.5 77.5T480-80q-74 0-139.5-28.5Z",
        /* SVEGLIA_PIU    */ "M440-400v80q0 17 11.5 28.5T480-280q17 0 28.5-11.5T520-320v-80h80q17 0 28.5-11.5T640-440q0-17-11.5-28.5T600-480h-80v-80q0-17-11.5-28.5T480-600q-17 0-28.5 11.5T440-560v80h-80q-17 0-28.5 11.5T320-440q0 17 11.5 28.5T360-400h80ZM339.5-108.5q-65.5-28.5-114-77t-77-114Q120-365 120-440t28.5-140.5q28.5-65.5 77-114t114-77Q405-800 480-800t140.5 28.5q65.5 28.5 114 77t77 114Q840-515 840-440t-28.5 140.5q-28.5 65.5-77 114t-114 77Q555-80 480-80t-140.5-28.5ZM82-668q-11-11-11-28t11-28l114-114q11-11 28-11t28 11q11 11 11 28t-11 28L138-668q-11 11-28 11t-28-11Zm796 0q-11 11-28 11t-28-11L708-782q-11-11-11-28t11-28q11-11 28-11t28 11l114 114q11 11 11 28t-11 28Z",
        /* CLESSIDRA      */ "M320-160h320v-120q0-66-47-113t-113-47q-66 0-113 47t-47 113v120Zm273-407q47-47 47-113v-120H320v120q0 66 47 113t113 47q66 0 113-47ZM200-80q-17 0-28.5-11.5T160-120q0-17 11.5-28.5T200-160h40v-120q0-61 28.5-114.5T348-480q-51-32-79.5-85.5T240-680v-120h-40q-17 0-28.5-11.5T160-840q0-17 11.5-28.5T200-880h560q17 0 28.5 11.5T800-840q0 17-11.5 28.5T760-800h-40v120q0 61-28.5 114.5T612-480q51 32 79.5 85.5T720-280v120h40q17 0 28.5 11.5T800-120q0 17-11.5 28.5T760-80H200Z",
    };

    private static final Path[] FATTI = new Path[DATI.length];

    /** Dal riquadro dei Material Symbols al quadrato da zero a uno. */
    private static final Matrix NORMALIZZA = new Matrix();
    static {
        NORMALIZZA.setScale(1f / 960f, 1f / 960f);
        NORMALIZZA.preTranslate(0f, 960f);
    }

    /** I percorsi gia' grandi, per (icona, misura in quarti di pixel). Solo UI thread. */
    private static final LongSparseArray<Path> PRONTE = new LongSparseArray<Path>();
    private static final Matrix MATRICE = new Matrix();

    /**
     * Un'icona centrata in (cx, cy), grande {@code lato}. Il colore lo porta il
     * Paint di chi chiama; lo stile qui diventa pieno e poi torna com'era.
     */
    public static void disegna(Canvas c, int icona, float cx, float cy, float lato, Paint p) {
        Path forma = pronta(icona, lato);
        if (forma == null) return;
        Paint.Style prima = p.getStyle();
        p.setStyle(Paint.Style.FILL);
        int s = c.save();
        c.translate(Math.round(cx - lato / 2f), Math.round(cy - lato / 2f));
        c.drawPath(forma, p);
        c.restoreToCount(s);
        p.setStyle(prima);
    }

    /**
     * Il percorso gia' portato alla sua grandezza, uno per coppia (icona,
     * misura). Ingrandire il Canvas attorno a un percorso alto un pixel darebbe
     * al disegno accelerato una texture da un pixel da stirare; e un percorso
     * modificato a ogni fotogramma sarebbe per lui sempre un percorso nuovo.
     */
    private static Path pronta(int icona, float lato) {
        Path forma = forma(icona);
        if (forma == null || lato <= 0f) return null;
        int quarti = Math.round(lato * 4f);
        if (quarti <= 0) return null;
        long chiave = ((long) icona << 32) | quarti;
        Path gia = PRONTE.get(chiave);
        if (gia != null) return gia;
        Path fatta = new Path();
        MATRICE.setScale(quarti / 4f, quarti / 4f);
        forma.transform(MATRICE, fatta);
        PRONTE.put(chiave, fatta);
        return fatta;
    }

    private static Path forma(int icona) {
        if (icona < 0 || icona >= DATI.length) return null;
        Path gia = FATTI[icona];
        if (gia != null) return gia;
        Path fatto = analizza(DATI[icona]);
        fatto.transform(NORMALIZZA);
        FATTI[icona] = fatto;
        return fatto;
    }

    /**
     * Da « M160-200v-360q0-19 8.5-36 » a un Path: M L H V Q T C S Z, assolute e
     * relative. I numeri si attaccano - « 423.5-103.5 » sono due - e una
     * lettera che manca vuol dire ancora quella di prima, che dopo una M e' L.
     */
    private static Path analizza(String d) {
        Path p = new Path();
        int[] i = { 0 };
        int n = d.length();

        char comando = 0;
        float x = 0f, y = 0f;
        float cx = 0f, cy = 0f;
        float ix = 0f, iy = 0f;
        boolean curvaPrima = false;

        while (true) {
            salta(d, i);
            if (i[0] >= n) break;

            char c = d.charAt(i[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) {
                comando = c;
                i[0]++;
            } else if (comando == 0) {
                break;
            }

            boolean rel = comando >= 'a';
            char tipo = rel ? (char) (comando - 32) : comando;

            if (tipo == 'M') {
                float a = leggi(d, i), b = leggi(d, i);
                x = rel ? x + a : a;
                y = rel ? y + b : b;
                p.moveTo(x, y);
                ix = x;
                iy = y;
                comando = rel ? 'l' : 'L';
                curvaPrima = false;
            } else if (tipo == 'L') {
                float a = leggi(d, i), b = leggi(d, i);
                x = rel ? x + a : a;
                y = rel ? y + b : b;
                p.lineTo(x, y);
                curvaPrima = false;
            } else if (tipo == 'H') {
                float a = leggi(d, i);
                x = rel ? x + a : a;
                p.lineTo(x, y);
                curvaPrima = false;
            } else if (tipo == 'V') {
                float a = leggi(d, i);
                y = rel ? y + a : a;
                p.lineTo(x, y);
                curvaPrima = false;
            } else if (tipo == 'Q') {
                float a = leggi(d, i), b = leggi(d, i);
                float e = leggi(d, i), f = leggi(d, i);
                float qx = rel ? x + a : a, qy = rel ? y + b : b;
                float fx = rel ? x + e : e, fy = rel ? y + f : f;
                p.quadTo(qx, qy, fx, fy);
                cx = qx;
                cy = qy;
                x = fx;
                y = fy;
                curvaPrima = true;
            } else if (tipo == 'T') {
                float qx = curvaPrima ? 2f * x - cx : x;
                float qy = curvaPrima ? 2f * y - cy : y;
                float e = leggi(d, i), f = leggi(d, i);
                float fx = rel ? x + e : e, fy = rel ? y + f : f;
                p.quadTo(qx, qy, fx, fy);
                cx = qx;
                cy = qy;
                x = fx;
                y = fy;
                curvaPrima = true;
            } else if (tipo == 'C') {
                float a = leggi(d, i), b = leggi(d, i);
                float e = leggi(d, i), f = leggi(d, i);
                float g = leggi(d, i), h = leggi(d, i);
                float c1x = rel ? x + a : a, c1y = rel ? y + b : b;
                float c2x = rel ? x + e : e, c2y = rel ? y + f : f;
                float fx = rel ? x + g : g, fy = rel ? y + h : h;
                p.cubicTo(c1x, c1y, c2x, c2y, fx, fy);
                cx = c2x;
                cy = c2y;
                x = fx;
                y = fy;
                curvaPrima = true;
            } else if (tipo == 'S') {
                float c1x = curvaPrima ? 2f * x - cx : x;
                float c1y = curvaPrima ? 2f * y - cy : y;
                float e = leggi(d, i), f = leggi(d, i);
                float g = leggi(d, i), h = leggi(d, i);
                float c2x = rel ? x + e : e, c2y = rel ? y + f : f;
                float fx = rel ? x + g : g, fy = rel ? y + h : h;
                p.cubicTo(c1x, c1y, c2x, c2y, fx, fy);
                cx = c2x;
                cy = c2y;
                x = fx;
                y = fy;
                curvaPrima = true;
            } else if (tipo == 'Z') {
                p.close();
                x = ix;
                y = iy;
                curvaPrima = false;
            } else {
                // Un comando sconosciuto - un arco - girerebbe a vuoto: meglio
                // un'icona incompleta di un ciclo infinito.
                return p;
            }
        }
        return p;
    }

    private static void salta(String d, int[] i) {
        int n = d.length();
        while (i[0] < n) {
            char c = d.charAt(i[0]);
            if (c == ' ' || c == ',' || c == '\n' || c == '\r' || c == '\t') i[0]++;
            else break;
        }
    }

    private static float leggi(String d, int[] i) {
        salta(d, i);
        int n = d.length(), inizio = i[0];
        if (i[0] < n && (d.charAt(i[0]) == '-' || d.charAt(i[0]) == '+')) i[0]++;
        boolean punto = false;
        while (i[0] < n) {
            char c = d.charAt(i[0]);
            if (c >= '0' && c <= '9') {
                i[0]++;
            } else if (c == '.' && !punto) {
                punto = true;
                i[0]++;
            } else {
                break;
            }
        }
        if (i[0] == inizio) {
            i[0]++;
            return 0f;
        }
        return Float.parseFloat(d.substring(inizio, i[0]));
    }
}
