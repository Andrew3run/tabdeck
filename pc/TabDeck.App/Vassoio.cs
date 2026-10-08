using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace TabDeck;

/// <summary>
/// L'icona nell'area di notifica, e la finestra che si ritira li' invece di
/// chiudersi.
///
/// La croce in alto a destra vuol dire « togliti di mezzo », non « stacca il
/// tablet »: chiudendo davvero cadrebbe il collegamento, sparirebbe lo schermo
/// virtuale e il deck resterebbe una griglia che non comanda niente. Da qui in
/// avanti la finestra si nasconde e resta tutto acceso; per spegnere davvero
/// c'e' « Esci » nel menu dell'icona, che e' un gesto in piu' proprio perche'
/// e' quello che costa.
///
/// L'icona la disegna Windows, non WPF: <c>Shell_NotifyIcon</c> vuole una
/// finestra a cui mandare i clic, e quella finestra non puo' essere la nostra
/// — che di norma e' nascosta — ma una finestra vuota creata qui apposta. Il
/// menu invece e' di WPF, cosi' e' scuro come il resto della finestra e non un
/// rettangolo bianco di sistema.
/// </summary>
public sealed class Vassoio : IDisposable
{
    // ---- Win32 ----

    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;

    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;

    /// <summary>Il messaggio con cui la shell ci racconta i clic sull'icona.</summary>
    private const int WM_VASSOIO = 0x8000 + 17;   // WM_APP + 17

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    /// <summary>Premuto il fumetto, o la notifica che Windows 11 ne fa.</summary>
    private const int NIN_BALLOONUSERCLICK = 0x0405;

    /// <summary>
    /// Un dispositivo e' comparso o sparito. Windows lo manda a tutte le
    /// finestre di primo livello, e questa lo e' anche se non si vede: e' il
    /// modo di sapere che il cavo e' stato attaccato senza chiederlo ad adb
    /// ogni tot secondi.
    /// </summary>
    private const int WM_DEVICECHANGE = 0x0219;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x80;

    /// <summary>Il messaggio va a tutte le finestre di primo livello.</summary>
    private static readonly IntPtr HWND_BROADCAST = 0xFFFF;

    /// <summary>
    /// Il nome della finestra nascosta. E' anche l'indirizzo a cui una seconda
    /// copia bussa per dire « ci sei gia' tu, fatti vedere ».
    /// </summary>
    private const string NomeFinestra = "TabDeck.Vassoio";

    /// <summary>
    /// « Mostrati ». Registrato per nome, non un numero scelto a caso: cosi'
    /// Windows garantisce che nessun altro programma usi lo stesso valore, ed
    /// e' l'unico modo onesto di mandare un messaggio in broadcast.
    /// </summary>
    private const string NomeMessaggioMostrati = "TabDeck.Mostrati";

    /// <summary>Il lato dell'icona piccola: 16 pixel a 100%, di piu' se scalato.</summary>
    private const int SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? classe, string? nome);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int messaggio, IntPtr wParam, IntPtr lParam);

    // ---- stato ----

    private readonly HwndSource finestra;
    private readonly IntPtr icona;

    /// <summary>
    /// Se Esplora risorse muore e rinasce, l'area di notifica riparte vuota e
    /// se ne accorge solo chi ascolta questo messaggio. Senza, l'icona sparisce
    /// e la finestra resta nascosta senza nessun modo di richiamarla.
    /// </summary>
    private readonly int messaggioBarraRifatta;

    /// <summary>Una seconda copia ha chiesto di far vedere questa. Vedi <see cref="App"/>.</summary>
    private readonly int messaggioMostrati;

    private bool aggiunta;
    private bool buttata;

    /// <summary>
    /// Vero se l'area di notifica ha accettato l'icona.
    ///
    /// Se non l'ha accettata, la finestra non deve nascondersi: sparirebbe
    /// senza lasciare niente da premere per riaprirla, e l'unico modo di
    /// riprenderla sarebbe il Gestione attivita'.
    /// </summary>
    public bool Acceso => aggiunta;

    /// <summary>La riga spenta in cima al menu: dice come sta il collegamento.</summary>
    public string Stato { get; set; } = "";

    /// <summary>Premuta l'icona, o « Apri » nel menu.</summary>
    public event Action? Apri;

    /// <summary>« Esci » nel menu: e' l'unico modo di spegnere davvero.</summary>
    public event Action? Esci;

    /// <summary>« Impostazioni » nel menu.</summary>
    public event Action? Impostazioni;

    /// <summary>Qualcosa e' stato attaccato o staccato. Non dice cosa: lo si chiede ad adb.</summary>
    public event Action? DispositiviCambiati;

    public Vassoio(string suggerimento)
    {
        var parametri = new HwndSourceParameters(NomeFinestra)
        {
            Width = 0,
            Height = 0,
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        };
        finestra = new HwndSource(parametri);
        finestra.AddHook(Procedura);

        messaggioBarraRifatta = RegisterWindowMessage("TaskbarCreated");
        messaggioMostrati = RegisterWindowMessage(NomeMessaggioMostrati);
        icona = CaricaIcona();

        var dati = Dati(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        dati.szTip = Taglia(suggerimento, 127);
        aggiunta = Shell_NotifyIcon(NIM_ADD, ref dati);
    }

    /// <summary>
    /// L'icona del programma, alla misura che l'area di notifica si aspetta.
    /// E' quella dentro TabDeck.exe: non c'e' nessun file da portarsi dietro, e
    /// resta la stessa che si vede sulla barra.
    /// </summary>
    private static IntPtr CaricaIcona()
    {
        int lato = Native.GetSystemMetrics(SM_CXSMICON);
        if (lato <= 0) lato = 16;

        string exe = Environment.ProcessPath ?? "";
        if (exe.Length > 0)
        {
            var trovate = new IntPtr[1];
            var numeri = new IntPtr[1];
            if (Native.PrivateExtractIcons(exe, 0, lato, lato, trovate, numeri, 1, 0) > 0
                && trovate[0] != IntPtr.Zero)
            {
                return trovate[0];
            }
        }

        // Ultima spiaggia: l'icona generica di Windows. Un'icona sbagliata e'
        // sempre meglio di nessuna icona, che vorrebbe dire finestra nascosta
        // e nessun modo di riaprirla.
        return LoadIcon(IntPtr.Zero, 32512);   // IDI_APPLICATION
    }

    private NOTIFYICONDATA Dati(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = finestra.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_VASSOIO,
        hIcon = icona,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private static string Taglia(string testo, int quanti) =>
        testo.Length <= quanti ? testo : testo[..quanti];

    /// <summary>Il testo che compare fermandosi sopra l'icona.</summary>
    public void Suggerimento(string testo)
    {
        if (!aggiunta) return;
        var dati = Dati(NIF_TIP);
        dati.szTip = Taglia(testo, 127);
        Shell_NotifyIcon(NIM_MODIFY, ref dati);
    }

    /// <summary>
    /// Un fumetto, che su Windows 10 e 11 diventa una notifica. Serve a dire
    /// dove e' finita la finestra, la prima volta, e poi quello che si e'
    /// scelto in Impostazioni: collegato, caduto, non riuscito. Premendolo si
    /// apre la finestra.
    /// </summary>
    /// <param name="guaio">L'icona gialla: qualcosa non e' andato.</param>
    public void Avviso(string titolo, string testo, bool guaio = false)
    {
        if (!aggiunta) return;
        var dati = Dati(NIF_INFO);
        dati.szInfoTitle = Taglia(titolo, 63);
        dati.szInfo = Taglia(testo, 255);
        dati.dwInfoFlags = guaio ? 0x02 : 0x01;   // NIIF_WARNING : NIIF_INFO
        Shell_NotifyIcon(NIM_MODIFY, ref dati);
    }

    private IntPtr Procedura(IntPtr hwnd, int messaggio, IntPtr wParam, IntPtr lParam, ref bool gestito)
    {
        if (messaggio == messaggioMostrati)
        {
            Apri?.Invoke();
            gestito = true;
            return IntPtr.Zero;
        }

        if (messaggio == messaggioBarraRifatta && aggiunta)
        {
            var dati = Dati(NIF_MESSAGE | NIF_ICON);
            Shell_NotifyIcon(NIM_ADD, ref dati);
            gestito = true;
            return IntPtr.Zero;
        }

        if (messaggio == WM_DEVICECHANGE)
        {
            // Non gestito: la risposta la da' Windows, e ad alcune domande di
            // questo messaggio si risponde « si' ».
            DispositiviCambiati?.Invoke();
            return IntPtr.Zero;
        }

        if (messaggio != WM_VASSOIO) return IntPtr.Zero;

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
            case WM_LBUTTONDBLCLK:
            case NIN_BALLOONUSERCLICK:
                Apri?.Invoke();
                gestito = true;
                break;

            case WM_RBUTTONUP:
                ApriMenu();
                gestito = true;
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Quattro voci: aprire, sapere come sta il collegamento, le impostazioni
    /// — che sono quello che si cerca proprio quando la finestra e' nascosta:
    /// l'avvio con Windows, le notifiche — e uscire. Tutto il resto si fa
    /// nella finestra, che e' a un clic di distanza.
    /// </summary>
    private void ApriMenu()
    {
        // Senza questo, il menu resterebbe aperto anche cliccando altrove: la
        // finestra che lo possiede non e' in primo piano, e Windows non gli
        // manda la disdetta.
        SetForegroundWindow(finestra.Handle);

        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };

        var apri = new MenuItem { Header = "Apri TabDeck", FontWeight = FontWeights.Normal };
        apri.Click += (_, _) => Apri?.Invoke();
        menu.Items.Add(apri);

        if (Stato.Length > 0)
        {
            menu.Items.Add(new MenuItem { Header = Stato, IsEnabled = false });
        }

        var impostazioni = new MenuItem { Header = "Impostazioni" };
        impostazioni.Click += (_, _) => Impostazioni?.Invoke();
        menu.Items.Add(impostazioni);

        menu.Items.Add(new Separator());

        var esci = new MenuItem { Header = "Esci" };
        esci.Click += (_, _) => Esci?.Invoke();
        menu.Items.Add(esci);

        menu.IsOpen = true;
    }

    /// <summary>
    /// Chiede alla copia gia' in funzione di farsi vedere. La chiama la copia
    /// di troppo, un attimo prima di togliersi di mezzo.
    ///
    /// Prima si cerca la finestra per nome, che e' un colpo solo e va dritto;
    /// se non si trova — la copia di la' potrebbe essere ancora in fase di
    /// apertura — si manda a tutte, che e' quel che i messaggi registrati
    /// sono fatti per fare. In tutti e due i casi si posta e non si spedisce:
    /// aspettare la risposta di un programma occupato terrebbe fermo questo.
    /// </summary>
    public static void ChiediDiMostrarsi()
    {
        int messaggio = RegisterWindowMessage(NomeMessaggioMostrati);
        if (messaggio == 0) return;

        IntPtr dove = FindWindow(null, NomeFinestra);
        PostMessage(dove != IntPtr.Zero ? dove : HWND_BROADCAST,
            messaggio, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (buttata) return;
        buttata = true;

        if (aggiunta)
        {
            var dati = Dati(0);
            Shell_NotifyIcon(NIM_DELETE, ref dati);
            aggiunta = false;
        }

        if (icona != IntPtr.Zero) Native.DestroyIcon(icona);
        finestra.RemoveHook(Procedura);
        finestra.Dispose();
    }
}
