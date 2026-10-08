using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TabDeck;

/// <summary>Uno schermo come lo vede Windows, con il nome che si legge in Impostazioni.</summary>
public sealed record MonitorInfo(
    string DeviceName,     // \\.\DISPLAY2
    string Adapter,        // "Parsec Virtual Display Adapter"
    string Monitor,        // "Schermo generico PnP"
    Rect Bounds,
    bool IsPrimary,
    bool IsVirtual)
{
    /// <summary>Etichetta per l'elenco nella finestra.</summary>
    public string Caption =>
        $"{Bounds.W}x{Bounds.H} — {(IsVirtual ? "schermo virtuale" : Adapter)}"
        + (IsPrimary ? " (principale)" : "");
}

/// <summary>Un adattatore di schermo virtuale trovato fra i dispositivi di sistema.</summary>
public sealed record VirtualAdapter(string InstanceId, string Name, bool Enabled);

/// <summary>
/// Schermi e schermi virtuali.
///
/// Il tablet non e' un ritaglio del desktop: e' un monitor in piu' che Windows
/// deve vedere per conto suo, altrimenti non ci si possono trascinare dentro le
/// finestre. Quel monitor lo crea un driver di display virtuale; qui non se ne
/// installa nessuno, si trova quello presente e lo si accende o spegne come
/// farebbe Gestione dispositivi.
/// </summary>
public static class Displays
{
    // ---- enumerazione ----

    /// <summary>
    /// Schermi attivi, nell'ordine in cui Windows li numera. Serve
    /// EnumDisplayDevices e non EnumDisplayMonitors perche' solo la prima
    /// riporta il nome dell'adattatore, cioe' l'unico modo per riconoscere uno
    /// schermo virtuale da uno vero.
    /// </summary>
    public static List<MonitorInfo> List()
    {
        var result = new List<MonitorInfo>();

        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(adapter.DeviceName, ENUM_CURRENT_SETTINGS, ref mode)) continue;

            // Il monitor appeso all'adattatore: qui vive il nome leggibile.
            string monitorName = "";
            var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0))
                monitorName = monitor.DeviceString;

            result.Add(new MonitorInfo(
                adapter.DeviceName,
                adapter.DeviceString,
                monitorName,
                new Rect(mode.dmPositionX, mode.dmPositionY, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight),
                (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                LooksVirtual(adapter.DeviceString + " " + monitorName)));
        }

        return result;
    }

    /// <summary>
    /// Riconoscimento per nome. Nessun driver virtuale espone una bandiera che
    /// dica "sono finto", ma tutti si presentano con un nome che lo dice.
    /// </summary>
    private static bool LooksVirtual(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("parsec")
            || n.Contains("usbmmidd")
            || n.Contains("virtual")
            || n.Contains("idd")
            || n.Contains("amyuni")
            || n.Contains("spacedesk");
    }

    // ---- risoluzione ----

    /// <summary>
    /// Porta uno schermo alla risoluzione voluta. Sullo schermo virtuale serve
    /// per farlo combaciare al pannello del tablet: 1024x600 di la', 1024x600
    /// di qua, e nessuna scalatura fra i due — il testo resta nitido.
    /// </summary>
    public static string SetResolution(string deviceName, int w, int h)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode))
            return "impossibile leggere la modalita' attuale dello schermo";

        if (mode.dmPelsWidth == w && mode.dmPelsHeight == h) return "";

        mode.dmPelsWidth = (uint)w;
        mode.dmPelsHeight = (uint)h;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

        // Prova a vuoto: dice se la modalita' e' accettata senza toccare nulla.
        int test = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL)
            return $"{w}x{h} non e' fra le modalita' di questo schermo";

        int applied = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return applied == DISP_CHANGE_SUCCESSFUL ? "" : $"cambio risoluzione rifiutato (codice {applied})";
    }

    /// <summary>Risoluzioni disponibili su uno schermo, senza duplicati.</summary>
    public static List<(int W, int H)> AvailableModes(string deviceName)
    {
        var seen = new HashSet<(int, int)>();
        var modes = new List<(int, int)>();
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

        for (int i = 0; EnumDisplaySettings(deviceName, i, ref mode); i++)
        {
            var pair = ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
            if (seen.Add(pair)) modes.Add(pair);
        }

        modes.Sort((a, b) => (b.Item1 * b.Item2).CompareTo(a.Item1 * a.Item2));
        return modes;
    }

    // ---- adattatori virtuali ----

    /// <summary>
    /// Cerca fra i dispositivi di classe Display quelli che sembrano virtuali.
    /// Passa da PowerShell e non da pnputil perche' pnputil stampa le
    /// intestazioni tradotte: su Windows italiano "Instance ID" diventa "ID
    /// istanza" e ogni parser fatto sui nomi si rompe. ConvertTo-Json no.
    /// </summary>
    public static List<VirtualAdapter> FindVirtualAdapters()
    {
        // Niente -AsArray: quel parametro esiste solo in PowerShell 7, e su
        // Windows PowerShell 5.1 — quello che c'e' su ogni macchina — il
        // comando falliva in blocco, cosi' l'elenco tornava sempre vuoto e la
        // finestra dichiarava "nessun driver installato" anche con il driver
        // acceso davanti. Senza quel parametro un solo dispositivo arriva come
        // oggetto singolo invece che come lista: si accettano tutti e due.
        var (ok, output) = RunPowerShell(
            "Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | " +
            "Select-Object InstanceId,FriendlyName,Status | ConvertTo-Json -Compress");

        if (!ok || output.Length == 0) return new List<VirtualAdapter>();

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            var found = new List<VirtualAdapter>();

            if (root.ValueKind == JsonValueKind.Array)
                foreach (var el in root.EnumerateArray()) Collect(el, found);
            else
                Collect(root, found);

            return found;
        }
        catch (JsonException)
        {
            return new List<VirtualAdapter>();
        }
    }

    private static void Collect(JsonElement el, List<VirtualAdapter> into)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        string name = el.TryGetProperty("FriendlyName", out var f) ? f.GetString() ?? "" : "";
        string id = el.TryGetProperty("InstanceId", out var i) ? i.GetString() ?? "" : "";
        string status = el.TryGetProperty("Status", out var s) ? s.GetString() ?? "" : "";
        if (id.Length == 0 || !LooksVirtual(name)) return;
        into.Add(new VirtualAdapter(id, name, status.Equals("OK", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Vero se il programma gira con i permessi di amministratore.</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Accende o spegne l'adattatore virtuale: e' la stessa cosa che fa
    /// Gestione dispositivi con "Disattiva dispositivo", e vuole i permessi di
    /// amministratore. Se il programma li ha gia' l'operazione e' muta; se non
    /// li ha, Windows chiede il consenso ogni volta — motivo per cui TabDeck si
    /// avvia elevato.
    /// </summary>
    public static string SetAdapterEnabled(string instanceId, bool enabled)
    {
        string verb = enabled ? "Enable-PnpDevice" : "Disable-PnpDevice";
        string command = $"{verb} -InstanceId '{instanceId.Replace("'", "''")}' -Confirm:$false";

        var (ok, output) = IsElevated() ? RunPowerShell(command) : RunPowerShellElevated(command);
        return ok ? "" : (output.Length > 0 ? output : "operazione annullata o non riuscita");
    }

    /// <summary>
    /// Aspetta che il monitor virtuale compaia (o sparisca) fra gli schermi.
    /// Fra l'accensione del dispositivo e l'arrivo del monitor passano un paio
    /// di secondi: leggere subito l'elenco darebbe la fotografia di prima.
    /// </summary>
    public static MonitorInfo? WaitForVirtualScreen(bool present, int timeoutMs = 9000)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var found = List().FirstOrDefault(m => m.IsVirtual);
            if (present && found is not null) return found;
            if (!present && found is null) return null;
            if (clock.ElapsedMilliseconds >= timeoutMs) return found;
            Thread.Sleep(400);
        }
    }

    private static (bool ok, string output) RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, "impossibile avviare powershell");
            string outText = p.StandardOutput.ReadToEnd();
            string errText = p.StandardError.ReadToEnd();
            p.WaitForExit(20_000);
            return (p.ExitCode == 0, p.ExitCode == 0 ? outText.Trim() : errText.Trim());
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    /// <summary>
    /// Come sopra, ma con la richiesta di elevazione. UseShellExecute e' quello
    /// che fa comparire il consenso di Windows, e impedisce di leggere l'output:
    /// resta solo il codice di uscita, che per accendere un dispositivo basta.
    /// </summary>
    private static (bool ok, string output) RunPowerShellElevated(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, "impossibile avviare powershell");
            p.WaitForExit(60_000);
            return (p.ExitCode == 0, "");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "permessi di amministratore negati");
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    // ---- interoperabilita' ----

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x01;
    private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x04;
    private const uint DM_PELSWIDTH = 0x00080000;
    private const uint DM_PELSHEIGHT = 0x00100000;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int CDS_TEST = 0x02;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        // Sugli schermi questa parte della union e' la posizione nel desktop.
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE mode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE mode, IntPtr hwnd, int flags, IntPtr param);
}
