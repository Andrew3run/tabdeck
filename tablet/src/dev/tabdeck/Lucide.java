package dev.tabdeck;

import android.graphics.Path;

/**
 * I tracciati delle icone Lucide (ISC), gli stessi che AiWork OS usa sul PC.
 *
 * Il tablet e l'app che comanda parlano cosi' la stessa lingua per immagini: la
 * griglia, il monitor, la casa sono le stesse figure da una parte e dall'altra.
 * Sono icone a tratto in un quadrato di 24, e a differenza di {@link Simboli},
 * che e' pieno, qui servono gli archi: quasi ogni angolo arrotondato di Lucide e'
 * un « a ». Si leggono una volta e diventano un {@link Path}; da li' in poi
 * disegnarle e' un drawPath.
 */
public final class Lucide {

    private Lucide() {}

    public static Path percorso(String d) {
        Path p = new Path();
        int[] i = { 0 };
        int n = d.length();
        char comando = 0;
        float x = 0, y = 0, ix = 0, iy = 0, cx = 0, cy = 0;
        boolean curva = false;

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

            switch (tipo) {
                case 'M': {
                    float a = numero(d, i), b = numero(d, i);
                    x = rel ? x + a : a;
                    y = rel ? y + b : b;
                    p.moveTo(x, y);
                    ix = x;
                    iy = y;
                    comando = rel ? 'l' : 'L';
                    curva = false;
                    break;
                }
                case 'L': {
                    float a = numero(d, i), b = numero(d, i);
                    x = rel ? x + a : a;
                    y = rel ? y + b : b;
                    p.lineTo(x, y);
                    curva = false;
                    break;
                }
                case 'H': {
                    float a = numero(d, i);
                    x = rel ? x + a : a;
                    p.lineTo(x, y);
                    curva = false;
                    break;
                }
                case 'V': {
                    float a = numero(d, i);
                    y = rel ? y + a : a;
                    p.lineTo(x, y);
                    curva = false;
                    break;
                }
                case 'C': {
                    float x1 = numero(d, i), y1 = numero(d, i), x2 = numero(d, i), y2 = numero(d, i);
                    float ex = numero(d, i), ey = numero(d, i);
                    if (rel) { x1 += x; y1 += y; x2 += x; y2 += y; ex += x; ey += y; }
                    p.cubicTo(x1, y1, x2, y2, ex, ey);
                    cx = x2; cy = y2; x = ex; y = ey;
                    curva = true;
                    break;
                }
                case 'S': {
                    float x1 = curva ? 2 * x - cx : x, y1 = curva ? 2 * y - cy : y;
                    float x2 = numero(d, i), y2 = numero(d, i), ex = numero(d, i), ey = numero(d, i);
                    if (rel) { x2 += x; y2 += y; ex += x; ey += y; }
                    p.cubicTo(x1, y1, x2, y2, ex, ey);
                    cx = x2; cy = y2; x = ex; y = ey;
                    curva = true;
                    break;
                }
                case 'Q': {
                    float x1 = numero(d, i), y1 = numero(d, i), ex = numero(d, i), ey = numero(d, i);
                    if (rel) { x1 += x; y1 += y; ex += x; ey += y; }
                    p.quadTo(x1, y1, ex, ey);
                    cx = x1; cy = y1; x = ex; y = ey;
                    curva = true;
                    break;
                }
                case 'T': {
                    float x1 = curva ? 2 * x - cx : x, y1 = curva ? 2 * y - cy : y;
                    float ex = numero(d, i), ey = numero(d, i);
                    if (rel) { ex += x; ey += y; }
                    p.quadTo(x1, y1, ex, ey);
                    cx = x1; cy = y1; x = ex; y = ey;
                    curva = true;
                    break;
                }
                case 'A': {
                    float rx = numero(d, i), ry = numero(d, i), rot = numero(d, i);
                    boolean grande = bandiera(d, i), orario = bandiera(d, i);
                    float ex = numero(d, i), ey = numero(d, i);
                    if (rel) { ex += x; ey += y; }
                    arco(p, x, y, rx, ry, rot, grande, orario, ex, ey);
                    x = ex; y = ey;
                    curva = false;
                    break;
                }
                case 'Z':
                    p.close();
                    x = ix;
                    y = iy;
                    curva = false;
                    break;
                default:
                    return p;
            }
        }
        return p;
    }

    /**
     * Un arco ellittico in curve di Bezier, un quarto di giro alla volta: e' la
     * conversione dell'appendice F.6 di SVG, perche' Path di Android gli archi
     * li conosce solo dentro un rettangolo, non fra due punti.
     */
    private static void arco(Path p, float x0, float y0, float rx, float ry, float rotazione,
                             boolean grande, boolean orario, float x, float y) {
        if (x0 == x && y0 == y) return;
        if (rx == 0 || ry == 0) {
            p.lineTo(x, y);
            return;
        }
        double arx = Math.abs(rx), ary = Math.abs(ry);
        double phi = Math.toRadians(rotazione), cos = Math.cos(phi), sin = Math.sin(phi);
        double dx = (x0 - x) / 2.0, dy = (y0 - y) / 2.0;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;

        double lambda = (x1 * x1) / (arx * arx) + (y1 * y1) / (ary * ary);
        if (lambda > 1) {
            double s = Math.sqrt(lambda);
            arx *= s;
            ary *= s;
        }
        double rx2 = arx * arx, ry2 = ary * ary;
        double num = rx2 * ry2 - rx2 * y1 * y1 - ry2 * x1 * x1;
        double den = rx2 * y1 * y1 + ry2 * x1 * x1;
        double k = den == 0 ? 0 : Math.sqrt(Math.max(0, num / den));
        if (grande == orario) k = -k;
        double cxp = k * arx * y1 / ary, cyp = -k * ary * x1 / arx;
        double centroX = cos * cxp - sin * cyp + (x0 + x) / 2.0;
        double centroY = sin * cxp + cos * cyp + (y0 + y) / 2.0;

        double ux = (x1 - cxp) / arx, uy = (y1 - cyp) / ary;
        double vx = (-x1 - cxp) / arx, vy = (-y1 - cyp) / ary;
        double inizio = angolo(1, 0, ux, uy);
        double giro = angolo(ux, uy, vx, vy);
        if (!orario && giro > 0) giro -= 2 * Math.PI;
        else if (orario && giro < 0) giro += 2 * Math.PI;

        int pezzi = Math.max(1, (int) Math.ceil(Math.abs(giro) / (Math.PI / 2)));
        double passo = giro / pezzi;
        double t = 4.0 / 3.0 * Math.tan(passo / 4);
        double a = inizio;
        for (int n = 0; n < pezzi; n++) {
            double ca = Math.cos(a), sa = Math.sin(a), cb = Math.cos(a + passo), sb = Math.sin(a + passo);
            double p1x = ca - t * sa, p1y = sa + t * ca;
            double p2x = cb + t * sb, p2y = sb - t * cb;
            p.cubicTo(
                    (float) (centroX + arx * p1x * cos - ary * p1y * sin), (float) (centroY + arx * p1x * sin + ary * p1y * cos),
                    (float) (centroX + arx * p2x * cos - ary * p2y * sin), (float) (centroY + arx * p2x * sin + ary * p2y * cos),
                    (float) (centroX + arx * cb * cos - ary * sb * sin), (float) (centroY + arx * cb * sin + ary * sb * cos));
            a += passo;
        }
    }

    private static double angolo(double ux, double uy, double vx, double vy) {
        double lunghezza = Math.sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        if (lunghezza == 0) return 0;
        double a = Math.acos(Math.max(-1, Math.min(1, (ux * vx + uy * vy) / lunghezza)));
        return ux * vy - uy * vx < 0 ? -a : a;
    }

    private static void salta(String d, int[] i) {
        while (i[0] < d.length()) {
            char c = d.charAt(i[0]);
            if (c == ' ' || c == ',' || c == '\n' || c == '\r' || c == '\t') i[0]++;
            else break;
        }
    }

    /** Le bandiere di un arco sono una cifra sola, e possono stare attaccate al numero dopo. */
    private static boolean bandiera(String d, int[] i) {
        salta(d, i);
        if (i[0] >= d.length()) return false;
        return d.charAt(i[0]++) == '1';
    }

    private static float numero(String d, int[] i) {
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
