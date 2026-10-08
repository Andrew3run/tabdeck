using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TabDeck;

/// <summary>Rettangolo di schermo in pixel fisici.</summary>
public readonly record struct Rect(int X, int Y, int W, int H);

/// <summary>Una porzione cambiata, gia' compressa.</summary>
public readonly record struct DirtyTile(int X, int Y, int W, int H, byte[] Jpeg, int Length);

/// <summary>
/// Cattura una porzione di desktop con GDI e ne comprime solo le tessere
/// cambiate.
///
/// GDI invece di Desktop Duplication perche' qui l'area e' piccola (meno di un
/// megapixel) e BitBlt la copia in pochi millisecondi senza tirarsi dietro
/// Direct3D. Il confronto per tessere e' il vero risparmio: su una scrivania
/// ferma cambia una tessera o nessuna, e la banda sul cavo USB crolla.
/// </summary>
public sealed unsafe class ScreenCapture : IDisposable
{
    private readonly int quality;
    private readonly int tileCols;
    private readonly int tileRows;

    private IntPtr screenDc;
    private IntPtr memDc;
    private IntPtr bitmap;
    private IntPtr oldBitmap;
    private byte* pixels;

    private int width, height, stride;
    private ulong[] hashes = Array.Empty<ulong>();
    private bool hashesValid;

    /// <summary>Area di schermo catturata, in pixel fisici.</summary>
    public Rect Source { get; private set; }

    /// <summary>Dimensioni di cio' che viene effettivamente inviato.</summary>
    public int OutputWidth => width;
    public int OutputHeight => height;

    private bool scaling;

    public ScreenCapture(int quality, int tileCols, int tileRows)
    {
        this.quality = Math.Clamp(quality, 20, 95);
        this.tileCols = Math.Max(1, tileCols);
        this.tileRows = Math.Max(1, tileRows);
    }

    /// <summary>
    /// (Ri)prepara i buffer. Se l'uscita e' piu' piccola della sorgente, la
    /// riduzione avviene qui: mandare 1920x1080 al tablet e fargli scalare ogni
    /// frame in software vorrebbe dire cinque fotogrammi al secondo.
    /// </summary>
    public void SetSource(Rect r, int outW, int outH)
    {
        if (r.W <= 0 || r.H <= 0) throw new ArgumentException("area di cattura vuota");
        if (outW <= 0 || outH <= 0) throw new ArgumentException("dimensione di uscita non valida");
        if (Source == r && width == outW && height == outH && bitmap != IntPtr.Zero) return;

        Release();
        Source = r;
        width = outW;
        height = outH;
        stride = width * 4;
        scaling = r.W != outW || r.H != outH;

        screenDc = Native.GetDC(IntPtr.Zero);
        memDc = Native.CreateCompatibleDC(screenDc);

        var info = new Native.BITMAPINFO
        {
            bmiHeader = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
                biWidth = width,
                // Altezza negativa: DIB dall'alto verso il basso, cosi' le righe
                // in memoria sono nell'ordine che si aspetta il codificatore.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            }
        };

        bitmap = Native.CreateDIBSection(memDc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection fallita");
        pixels = (byte*)bits;
        oldBitmap = Native.SelectObject(memDc, bitmap);

        hashes = new ulong[tileCols * tileRows];
        hashesValid = false;
    }

    /// <summary>
    /// Cattura un frame e restituisce le sole tessere cambiate.
    /// Con <paramref name="forceFull"/> le rimanda tutte: serve alla prima
    /// connessione e come rete di sicurezza periodica contro le tessere perse.
    /// </summary>
    public List<DirtyTile> Grab(bool forceFull, bool drawCursor)
    {
        var dirty = new List<DirtyTile>();
        if (bitmap == IntPtr.Zero) return dirty;

        if (scaling)
        {
            Native.SetStretchBltMode(memDc, Native.HALFTONE);
            Native.SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
            Native.StretchBlt(memDc, 0, 0, width, height,
                screenDc, Source.X, Source.Y, Source.W, Source.H,
                Native.SRCCOPY | Native.CAPTUREBLT);
        }
        else
        {
            Native.BitBlt(memDc, 0, 0, width, height, screenDc, Source.X, Source.Y,
                Native.SRCCOPY | Native.CAPTUREBLT);
        }

        if (drawCursor) DrawCursor();

        bool full = forceFull || !hashesValid;
        for (int row = 0; row < tileRows; row++)
        {
            for (int col = 0; col < tileCols; col++)
            {
                var (tx, ty, tw, th) = TileBounds(col, row);
                if (tw <= 0 || th <= 0) continue;

                ulong h = HashTile(tx, ty, tw, th);
                int index = row * tileCols + col;
                if (!full && hashes[index] == h) continue;
                hashes[index] = h;

                byte[] jpeg = EncodeTile(tx, ty, tw, th);
                dirty.Add(new DirtyTile(tx, ty, tw, th, jpeg, jpeg.Length));
            }
        }
        hashesValid = true;
        return dirty;
    }

    private (int x, int y, int w, int h) TileBounds(int col, int row)
    {
        int x = col * width / tileCols;
        int y = row * height / tileRows;
        int x2 = (col + 1) * width / tileCols;
        int y2 = (row + 1) * height / tileRows;
        return (x, y, x2 - x, y2 - y);
    }

    /// <summary>
    /// FNV-1a a 64 bit su parole da 8 byte. Non serve robustezza crittografica:
    /// una collisione costa una tessera non aggiornata per un frame, e il
    /// rinfresco periodico la corregge comunque.
    /// </summary>
    private ulong HashTile(int tx, int ty, int tw, int th)
    {
        const ulong Prime = 1099511628211UL;
        ulong h = 14695981039346656037UL;
        int wordsPerRow = tw / 2;   // 2 pixel = 8 byte
        for (int y = 0; y < th; y++)
        {
            ulong* p = (ulong*)(pixels + (long)(ty + y) * stride + (long)tx * 4);
            for (int i = 0; i < wordsPerRow; i++)
            {
                h = (h ^ p[i]) * Prime;
            }
        }
        return h;
    }

    private byte[] EncodeTile(int tx, int ty, int tw, int th)
    {
        IntPtr start = (IntPtr)(pixels + (long)ty * stride + (long)tx * 4);
        int bufferSize = (th - 1) * stride + tw * 4;

        var source = BitmapSource.Create(
            tw, th, 96, 96, PixelFormats.Bgr32, null, start, bufferSize, stride);

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream(bufferSize / 8);
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Il puntatore non fa parte di BitBlt e va disegnato a mano. Senza, usare
    /// il tablet come schermo tattile diventa un esercizio alla cieca.
    /// </summary>
    private void DrawCursor()
    {
        var ci = new Native.CURSORINFO { cbSize = (uint)Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || ci.flags != Native.CURSOR_SHOWING) return;

        IntPtr icon = Native.CopyIcon(ci.hCursor);
        if (icon == IntPtr.Zero) return;
        try
        {
            if (!Native.GetIconInfo(icon, out Native.ICONINFO ii)) return;
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);

            // Il puntatore si disegna a dimensione naturale anche quando il
            // frame e' ridotto: rimpicciolito diventerebbe illeggibile.
            int x = (int)((ci.ptScreenPos.x - Source.X) * (double)width / Source.W) - ii.xHotspot;
            int y = (int)((ci.ptScreenPos.y - Source.Y) * (double)height / Source.H) - ii.yHotspot;
            if (x < -64 || y < -64 || x > width || y > height) return;

            Native.DrawIconEx(memDc, x, y, icon, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }
        finally
        {
            Native.DestroyIcon(icon);
        }
    }

    /// <summary>Schermi disponibili, in ordine di enumerazione di Windows.</summary>
    public static List<Rect> EnumerateMonitors()
    {
        var list = new List<Rect>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr _, IntPtr _, ref Native.RECT r, IntPtr _) =>
            {
                list.Add(new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top));
                return true;
            }, IntPtr.Zero);
        return list;
    }

    public static Rect VirtualScreen() => new(
        Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));

    private void Release()
    {
        if (memDc != IntPtr.Zero)
        {
            if (oldBitmap != IntPtr.Zero) Native.SelectObject(memDc, oldBitmap);
            Native.DeleteDC(memDc);
            memDc = IntPtr.Zero;
        }
        if (bitmap != IntPtr.Zero)
        {
            Native.DeleteObject(bitmap);
            bitmap = IntPtr.Zero;
        }
        if (screenDc != IntPtr.Zero)
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            screenDc = IntPtr.Zero;
        }
        pixels = null;
        oldBitmap = IntPtr.Zero;
    }

    public void Dispose() => Release();
}
