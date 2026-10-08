# Genera le icone dei pulsanti del deck per tutti e due i lati:
#   tablet/src/dev/tabdeck/Pittogrammi.java
#   pc/TabDeck.App/Pittogrammi.cs
#
# Le icone sono Lucide (ISC), e i loro SVG stanno in svg/ accanto a questo file:
# rigenerare non scarica niente. Per aggiungerne una si mette il suo .svg in
# svg/ (da https://unpkg.com/lucide-static@0.469.0/icons/<nome>.svg), si
# aggiunge il nome a ORDINE e si rilancia:
#
#   python tools\pittogrammi\genera.py
#
# Il parser del tablet (Lucide.java) legge solo "d" di <path>: cerchi,
# rettangoli, linee e polilinee qui diventano tracciati.

import os
import re
import xml.etree.ElementTree as ET

QUI = os.path.dirname(os.path.abspath(__file__))
RADICE = os.path.normpath(os.path.join(QUI, '..', '..'))

# L'ordine e' quello della griglia di scelta nell'app.
ORDINE = """
terminal square-terminal code app-window monitor layout-grid layers maximize-2 minimize-2 square-plus
copy clipboard scissors undo-2 redo-2 save trash-2 search keyboard mouse-pointer-2
play pause skip-back skip-forward volume-1 volume-2 volume-x music headphones mic mic-off
camera video image brush palette file-text folder folder-open download upload
globe mail message-square bell calendar clock calculator printer wifi activity
settings power lock refresh-cw zap target rocket star heart bookmark
house lightbulb lamp sun moon coffee gamepad-2 plus minus x chevron-left chevron-right
gauge square-pen tablet puzzle scroll-text sliders-horizontal send plug unplug circle-dot
alarm-clock alarm-clock-plus alarm-clock-off timer hourglass clapperboard layout-dashboard
panel-left-open panel-left-close battery battery-charging ban sun-dim ellipsis check
cloud cloud-sun cloud-moon cloud-rain cloud-drizzle cloud-snow cloud-lightning cloud-fog map-pin sunrise sunset
radio circle cast tv film megaphone flag bookmark-plus eye eye-off users twitch
""".split()


def n(v):
    return '%g' % float(v)


def rettangolo(a):
    x = float(a.get('x', 0)); y = float(a.get('y', 0))
    w = float(a['width']); h = float(a['height'])
    rx = float(a.get('rx', a.get('ry', 0))); ry = float(a.get('ry', rx))
    if rx == 0:
        return f"M{n(x)} {n(y)}h{n(w)}v{n(h)}h{n(-w)}Z"
    return (f"M{n(x + rx)} {n(y)}h{n(w - 2 * rx)}a{n(rx)} {n(ry)} 0 0 1 {n(rx)} {n(ry)}v{n(h - 2 * ry)}"
            f"a{n(rx)} {n(ry)} 0 0 1 {n(-rx)} {n(ry)}h{n(-(w - 2 * rx))}a{n(rx)} {n(ry)} 0 0 1 {n(-rx)} {n(-ry)}"
            f"v{n(-(h - 2 * ry))}a{n(rx)} {n(ry)} 0 0 1 {n(rx)} {n(-ry)}Z")


def ellisse(cx, cy, rx, ry):
    return f"M{n(cx - rx)} {n(cy)}a{n(rx)} {n(ry)} 0 1 0 {n(2 * rx)} 0a{n(rx)} {n(ry)} 0 1 0 {n(-2 * rx)} 0Z"


NUMERO = r'[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?'


def assoluto(d):
    """
    Un « m » minuscolo in testa a un tracciato vale come assoluto, ma solo
    finche' il tracciato e' da solo: messo in coda a un altro diventerebbe
    relativo al punto dove quello e' finito. Diventa « M x y », e le coppie che
    lo seguivano, che erano linee relative, diventano un « l » esplicito.
    """
    d = d.strip()
    if not d.startswith('m'):
        return d
    m = re.match(r'm\s*(' + NUMERO + r')[\s,]*(' + NUMERO + r')\s*,?\s*(.*)$', d, re.S)
    resto = m.group(3)
    if resto and not resto[0].isalpha():
        resto = 'l' + resto
    return f"M{m.group(1)} {m.group(2)}" + resto


def tracciato(nome):
    testo = open(os.path.join(QUI, 'svg', nome + '.svg'), encoding='utf-8').read()
    testo = re.sub(r'<!--.*?-->', '', testo, flags=re.S)
    parti = []
    for e in ET.fromstring(testo):
        tag = e.tag.split('}')[-1]
        a = e.attrib
        if tag == 'path':
            parti.append(assoluto(a['d']))
        elif tag == 'line':
            parti.append(f"M{a['x1']} {a['y1']}L{a['x2']} {a['y2']}")
        elif tag in ('polyline', 'polygon'):
            p = a['points'].replace(',', ' ').split()
            s = "M" + p[0] + " " + p[1] + "".join(f"L{p[i]} {p[i + 1]}" for i in range(2, len(p), 2))
            parti.append(s + ("Z" if tag == 'polygon' else ""))
        elif tag == 'rect':
            parti.append(rettangolo(a))
        elif tag == 'circle':
            parti.append(ellisse(float(a['cx']), float(a['cy']), float(a['r']), float(a['r'])))
        elif tag == 'ellipse':
            parti.append(ellisse(float(a['cx']), float(a['cy']), float(a['rx']), float(a['ry'])))
        else:
            raise SystemExit(f'{nome}: <{tag}> non previsto')
    return " ".join(parti)


icone = [(nome, tracciato(nome)) for nome in ORDINE]

JAVA = '''package dev.tabdeck;

import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.util.LongSparseArray;

import java.util.HashMap;

/**
 * Le icone dei pulsanti del deck: Lucide (ISC), a tratto, nel quadrato di 24.
 *
 * Prendono il posto dei ventinove glifi del font di KitKat. Viaggiano per nome
 * nel campo « glyph » del frame DECK: se il nome e' uno di questi si disegna il
 * tracciato, altrimenti il campo resta un glifo e si scrive com'e'. Un deck
 * vecchio, tutto glifi, continua quindi a vedersi uguale.
 *
 * GENERATO da tools/pittogrammi/genera.py, che scrive anche
 * pc/TabDeck.App/Pittogrammi.cs: l'anteprima sul PC disegna quel che disegna il
 * tablet. Non modificare a mano.
 */
public final class Pittogrammi {

    private Pittogrammi() {}

    private static final String[][] DATI = {
%RIGHE%
    };

    private static final HashMap<String, Integer> INDICE = new HashMap<String, Integer>();
    static {
        for (int i = 0; i < DATI.length; i++) INDICE.put(DATI[i][0], i);
    }

    private static final Path[] LETTI = new Path[DATI.length];
    private static final LongSparseArray<Path> PRONTE = new LongSparseArray<Path>();
    private static final Matrix MATRICE = new Matrix();

    /** Vero se quel nome e' un'icona e non un glifo. */
    public static boolean conosce(String nome) {
        return nome != null && INDICE.containsKey(nome);
    }

    /**
     * L'icona centrata in (cx, cy), grande {@code lato}, col colore del Paint. Il
     * tratto e' 1,75 su ventiquattro, mai sotto il pixel e mezzo: sul pannello del
     * Tab 3 un tratto piu' fine si perde. Torna false se il nome non e' un'icona.
     */
    public static boolean disegna(Canvas c, String nome, float cx, float cy, float lato, Paint p) {
        Integer i = nome == null ? null : INDICE.get(nome);
        if (i == null || lato <= 0f) return false;
        Path forma = pronta(i, lato);
        Paint.Style stile = p.getStyle();
        float spessore = p.getStrokeWidth();
        Paint.Cap capo = p.getStrokeCap();
        Paint.Join giunto = p.getStrokeJoin();
        p.setStyle(Paint.Style.STROKE);
        p.setStrokeWidth(Math.max(1.5f, lato * 1.75f / 24f));
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
        return true;
    }

    /** Il percorso gia' portato alla sua misura: ingrandire il Canvas lo sfocherebbe. */
    private static Path pronta(int icona, float lato) {
        int quarti = Math.round(lato * 4f);
        long chiave = ((long) icona << 32) | quarti;
        Path gia = PRONTE.get(chiave);
        if (gia != null) return gia;
        if (LETTI[icona] == null) LETTI[icona] = Lucide.percorso(DATI[icona][1]);
        Path fatta = new Path();
        MATRICE.setScale(quarti / 4f / 24f, quarti / 4f / 24f);
        LETTI[icona].transform(MATRICE, fatta);
        PRONTE.put(chiave, fatta);
        return fatta;
    }
}
'''

CS = '''using System.Windows;
using System.Windows.Media;

namespace TabDeck;

/// <summary>
/// Le icone dei pulsanti del deck: Lucide (ISC), a tratto, nel quadrato di 24.
///
/// GENERATO da tools/pittogrammi/genera.py insieme a Pittogrammi.java: il nome
/// viaggia nel campo Glyph, e se e' uno di questi il tablet disegna il
/// tracciato invece del glifo. L'ordine e' quello della griglia di scelta.
/// Non modificare a mano.
/// </summary>
public static class Pittogrammi
{
    private static readonly Dictionary<string, string> Dati = new()
    {
%RIGHE%
    };

    /// <summary>I nomi, nell'ordine della griglia di scelta.</summary>
    public static IReadOnlyList<string> Nomi { get; } = Dati.Keys.ToList();

    private static readonly Dictionary<string, Geometry> Pronte = new();

    public static bool Conosce(string? nome) => nome is not null && Dati.ContainsKey(nome);

    /// <summary>Il tracciato nel quadrato di 24, o null se il nome non e' un'icona.</summary>
    public static Geometry? Forma(string? nome)
    {
        if (nome is null || !Dati.TryGetValue(nome, out var d)) return null;
        if (Pronte.TryGetValue(nome, out var g)) return g;
        g = Geometry.Parse(d);
        g.Freeze();
        Pronte[nome] = g;
        return g;
    }

    /// <summary>
    /// L'icona centrata in <paramref name="centro"/>, grande <paramref name="lato"/>,
    /// col tratto del tablet: 1,75 su ventiquattro.
    /// </summary>
    public static bool Disegna(DrawingContext ctx, string? nome, Point centro, double lato, Brush colore)
    {
        var forma = Forma(nome);
        if (forma is null || lato <= 0) return false;
        double k = lato / 24;
        var penna = new Pen(colore, Math.Max(1.75, 1.2 / k))
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        ctx.PushTransform(new MatrixTransform(k, 0, 0, k, centro.X - lato / 2, centro.Y - lato / 2));
        ctx.DrawGeometry(null, penna, forma);
        ctx.Pop();
        return true;
    }

    /// <summary>L'icona come immagine, per i pulsanti e le griglie di scelta della finestra.</summary>
    public static DrawingImage? Immagine(string? nome, Brush colore)
    {
        var forma = Forma(nome);
        if (forma is null) return null;
        var penna = new Pen(colore, 1.75)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        var gruppo = new DrawingGroup();
        // Il quadrato intero, trasparente: senza, l'immagine si stringerebbe
        // attorno al tracciato e le icone uscirebbero di misure diverse.
        gruppo.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 24, 24))));
        gruppo.Children.Add(new GeometryDrawing(null, penna, forma));
        var immagine = new DrawingImage(gruppo);
        immagine.Freeze();
        return immagine;
    }
}
'''

righe_java = ",\n".join(f'        {{ "{k}", "{v}" }}' for k, v in icone)
righe_cs = "\n".join(f'        ["{k}"] = "{v}",' for k, v in icone)

with open(os.path.join(RADICE, 'tablet', 'src', 'dev', 'tabdeck', 'Pittogrammi.java'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(JAVA.replace('%RIGHE%', righe_java))
with open(os.path.join(RADICE, 'pc', 'TabDeck.App', 'Pittogrammi.cs'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(CS.replace('%RIGHE%', righe_cs))
print(len(icone), 'icone')
