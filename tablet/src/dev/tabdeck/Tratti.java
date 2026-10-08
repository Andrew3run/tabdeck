package dev.tabdeck;

import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.util.LongSparseArray;

/**
 * Le icone a tratto della Dashboard e della Casa: Lucide, come la barra.
 *
 * Sono gia' portate alla loro misura e tenute da parte, una per coppia (icona,
 * misura), per la stessa ragione di {@link Simboli}: ingrandire il Canvas attorno a
 * un percorso da ventiquattro unita' darebbe al disegno accelerato una texture
 * minuscola da stirare, e il tratto verrebbe sfocato.
 */
public final class Tratti {

    private Tratti() {}

    public static final int LAMPADINA = 0;
    public static final int CASA = 1;
    public static final int MONITOR = 2;
    public static final int ACCENSIONE = 3;
    public static final int SCINTILLE = 4;
    public static final int AVVIA = 5;
    public static final int AVANTI = 6;
    public static final int INDIETRO = 7;
    public static final int REGOLA = 8;
    public static final int GRIGLIA = 9;
    public static final int OROLOGIO = 10;
    public static final int COLLEGA = 11;
    public static final int ESTENSIONE = 12;
    public static final int SOLE = 13;
    public static final int LUNA = 14;
    public static final int FILM = 15;
    public static final int AGGIORNA = 16;

    private static final String[] DATI = {
        /* LAMPADINA  */ "M15 14c.2-1 .7-1.7 1.5-2.5 1-.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5 M9 18h6 M10 22h4",
        /* CASA       */ "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8"
                + " M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        /* MONITOR    */ "M4,3 h16 a2,2 0 0 1 2,2 v10 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-10 a2,2 0 0 1 2,-2 Z M8,21 L16,21 M12,17 L12,21",
        /* ACCENSIONE */ "M12 2v10 M18.4 6.6a9 9 0 1 1-12.77.04",
        /* SCINTILLE  */ "M9.937 15.5A2 2 0 0 0 8.5 14.063l-6.135-1.582a.5.5 0 0 1 0-.962L8.5 9.936A2 2 0 0 0 9.937 8.5"
                + "l1.582-6.135a.5.5 0 0 1 .963 0L14.063 8.5A2 2 0 0 0 15.5 9.937l6.135 1.581a.5.5 0 0 1 0 .964"
                + "L15.5 14.063a2 2 0 0 0-1.437 1.437l-1.582 6.135a.5.5 0 0 1-.963 0z M20 3v4 M22 5h-4",
        /* AVVIA      */ "M6 3 L20 12 L6 21 Z",
        /* AVANTI     */ "M9 18 l6-6-6-6",
        /* INDIETRO   */ "M15 18 l-6-6 6-6",
        /* REGOLA     */ "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4",
        /* GRIGLIA    */ "M4,4 h16 a2,2 0 0 1 2,2 v12 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-12 a2,2 0 0 1 2,-2 Z"
                + " M6 8h.01 M10 8h.01 M14 8h.01 M18 8h.01 M8 12h.01 M12 12h.01 M16 12h.01 M7 16h10",
        /* OROLOGIO   */ "M2,12 a10,10 0 1,0 20,0 a10,10 0 1,0 -20,0 M12,6 L12,12 L16,14",
        /* COLLEGA    */ "M4,3 h16 a2,2 0 0 1 2,2 v10 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-10 a2,2 0 0 1 2,-2 Z"
                + " M8,21 L16,21 M12,17 L12,21 M7 10h8 M12 7l3 3-3 3",
        /* ESTENSIONE */ "M15.39 4.39a1 1 0 0 0 1.68-.474 2.5 2.5 0 1 1 3.014 3.015 1 1 0 0 0-.474 1.68l1.683 1.682"
                + "a2.414 2.414 0 0 1 0 3.414L19.61 15.39a1 1 0 0 1-1.68-.474 2.5 2.5 0 1 0-3.014 3.015 1 1 0 0 1 .474 1.68"
                + "l-1.683 1.682a2.414 2.414 0 0 1-3.414 0L8.61 19.61a1 1 0 0 0-1.68.474 2.5 2.5 0 1 1-3.014-3.015"
                + " 1 1 0 0 0 .474-1.68l-1.683-1.682a2.414 2.414 0 0 1 0-3.414L4.39 8.61a1 1 0 0 1 1.68.474"
                + " 2.5 2.5 0 1 0 3.014-3.015 1 1 0 0 1-.474-1.68l1.683-1.682a2.414 2.414 0 0 1 3.414 0z",
        /* SOLE       */ "M8,12 a4,4 0 1,0 8,0 a4,4 0 1,0 -8,0 M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41"
                + " M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41",
        /* LUNA       */ "M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z",
        /* FILM       */ "M4,2 h16 a2,2 0 0 1 2,2 v16 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-16 a2,2 0 0 1 2,-2 Z"
                + " M7 3v18 M3 7.5h4 M3 12h18 M3 16.5h4 M17 3v18 M17 7.5h4 M17 16.5h4",
        /* AGGIORNA   */ "M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8 M21 3v5h-5",
    };

    private static final Path[] LETTI = new Path[DATI.length];
    private static final LongSparseArray<Path> PRONTE = new LongSparseArray<Path>();
    private static final Matrix MATRICE = new Matrix();

    /**
     * Un'icona centrata in (cx, cy), grande {@code lato}, col colore del Paint. Il
     * tratto e' quello di Lucide, due su ventiquattro, ma mai sotto il pixel e mezzo:
     * sul pannello del Tab 3 un tratto piu' fine si perde.
     */
    public static void disegna(Canvas c, int icona, float cx, float cy, float lato, Paint p) {
        Path forma = pronta(icona, lato);
        if (forma == null) return;
        Paint.Style stile = p.getStyle();
        float spessore = p.getStrokeWidth();
        Paint.Cap capo = p.getStrokeCap();
        Paint.Join giunto = p.getStrokeJoin();
        p.setStyle(icona == AVVIA ? Paint.Style.FILL_AND_STROKE : Paint.Style.STROKE);
        p.setStrokeWidth(Math.max(1.5f, lato * 2f / 24f));
        p.setStrokeCap(Paint.Cap.ROUND);
        p.setStrokeJoin(Paint.Join.ROUND);
        int s = c.save();
        c.translate(Math.round(cx - lato / 2f), Math.round(cy - lato / 2f));
        c.drawPath(forma, p);
        c.restoreToCount(s);
        p.setStyle(stile);
        p.setStrokeWidth(spessore);
        p.setStrokeCap(capo);
        p.setStrokeJoin(giunto);
    }

    private static Path pronta(int icona, float lato) {
        if (icona < 0 || icona >= DATI.length || lato <= 0f) return null;
        int quarti = Math.round(lato * 4f);
        long chiave = ((long) icona << 32) | quarti;
        Path gia = PRONTE.get(chiave);
        if (gia != null) return gia;
        if (LETTI[icona] == null) LETTI[icona] = Lucide.percorso(DATI[icona]);
        Path fatta = new Path();
        MATRICE.setScale(quarti / 4f / 24f, quarti / 4f / 24f);
        LETTI[icona].transform(MATRICE, fatta);
        PRONTE.put(chiave, fatta);
        return fatta;
    }
}
