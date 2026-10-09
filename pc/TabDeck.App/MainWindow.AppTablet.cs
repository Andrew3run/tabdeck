using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace TabDeck;

/// <summary>
/// L'app del tablet tenuta al passo con quella compilata qui.
///
/// Prima si aggiornava solo da « Prepara un tablet », quattro passi pensati per un
/// tablet nuovo, oppure da reinstalla-tablet.bat: cambiata l'interfaccia, il tablet
/// restava con quella vecchia e niente lo diceva. Adesso la pagina Tablet e la
/// dashboard dicono se l'app di la' e' la stessa di TabDeck.apk, e « Aggiorna » la
/// rimette tenendo i dati.
///
/// Il confronto e' sul contenuto: l'APK installato si legge col cavo (pm path, poi
/// pull, ~160 KB) e se ne fa l'impronta. Il numero di versione nel manifesto e'
/// sempre 1, e la data direbbe « diverso » anche per lo stesso file ricompilato.
/// Serve il cavo, perche' l'installazione passa da adb: in rete si dice e basta.
/// Il controllo legge e non cambia niente; aggiornare resta un pulsante.
/// </summary>
public partial class MainWindow
{
    private enum StatoApp { Ignoto, SenzaApk, SenzaCavo, Troppi, NonInstallata, Uguale, Diversa }

    private sealed record EsitoApp(StatoApp Stato, string Seriale = "", DateTime? SulTablet = null);

    private EsitoApp esitoApp = new(StatoApp.Ignoto);
    private bool controllandoApp;
    private bool aggiornandoApp;

    private string? ApkDelProgetto() => TrovaNelProgetto(@"tablet\TabDeck.apk", @"tablet\build\TabDeck.apk");

    private async void ControllaAppTablet()
    {
        if (controllandoApp || aggiornandoApp) return;
        controllandoApp = true;
        BtnControllaApp.IsEnabled = false;
        TestoAppStato.Text = "Controllo…";
        try
        {
            string? apk = ApkDelProgetto();
            esitoApp = await Task.Run(() => LeggiAppTablet(apk));
        }
        finally
        {
            controllandoApp = false;
            BtnControllaApp.IsEnabled = true;
        }
        MostraAppTablet();
    }

    /// <summary>Thread di lavoro: niente finestra qui dentro.</summary>
    private EsitoApp LeggiAppTablet(string? apk)
    {
        if (apk is null) return new(StatoApp.SenzaApk);

        // Solo i tablet che hanno gia' TabDeck: con l'E960 dell'altro progetto attaccato
        // allo stesso PC, « il primo che adb elenca » sarebbe quello sbagliato.
        var pronti = Adb.Seriali(adbPath).Where(s => s.Stato == "device").Select(s => s.Seriale).ToList();
        if (pronti.Count == 0) return new(StatoApp.SenzaCavo);
        var conApp = pronti
            .Select(s => (Seriale: s, Percorso: PercorsoApk(s)))
            .Where(x => x.Percorso.Length > 0)
            .ToList();
        if (conApp.Count == 0) return new(StatoApp.NonInstallata, pronti.Count == 1 ? pronti[0] : "");
        if (conApp.Count > 1)
        {
            // « Prepara un tablet » lascia scritto quale si e' scelto.
            string? scelto = Environment.GetEnvironmentVariable("ANDROID_SERIAL");
            conApp = conApp.Where(x => x.Seriale == scelto).ToList();
            if (conApp.Count != 1) return new(StatoApp.Troppi);
        }

        var (seriale, percorso) = conApp[0];
        DateTime? quando = DataInstallazione(seriale);
        string copia = Path.Combine(Path.GetTempPath(), "tabdeck-sul-tablet.apk");
        try
        {
            var (uscita, _) = Adb.Esegui(adbPath, $"-s {seriale} pull \"{percorso}\" \"{copia}\"", 60_000);
            if (uscita != 0 || !File.Exists(copia)) return new(StatoApp.Ignoto, seriale, quando);
            bool uguale = Impronta(copia).SequenceEqual(Impronta(apk));
            return new(uguale ? StatoApp.Uguale : StatoApp.Diversa, seriale, quando);
        }
        finally
        {
            try { File.Delete(copia); } catch (IOException) { }
        }
    }

    private string PercorsoApk(string seriale)
    {
        string riga = Adb.Shell(adbPath, seriale, $"pm path {PreparaTablet.Pacchetto}")
            .Split('\n').Select(r => r.Trim()).FirstOrDefault(r => r.StartsWith("package:", StringComparison.Ordinal)) ?? "";
        return riga.Length > 0 ? riga["package:".Length..] : "";
    }

    /// <summary>« lastUpdateTime=2026-10-08 15:02:11 » da dumpsys, nell'ora del tablet.</summary>
    private DateTime? DataInstallazione(string seriale)
    {
        // Filtrato qui e non con grep: sul toolbox di KitKat non e' detto che ci sia.
        string testo = Adb.Shell(adbPath, seriale, $"dumpsys package {PreparaTablet.Pacchetto}")
            .Split('\n').Select(r => r.Trim()).FirstOrDefault(r => r.StartsWith("lastUpdateTime=", StringComparison.Ordinal)) ?? "";
        int i = testo.IndexOf('=');
        return i > 0 && DateTime.TryParseExact(testo[(i + 1)..].Trim(), "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static byte[] Impronta(string file)
    {
        using var flusso = File.OpenRead(file);
        return SHA256.HashData(flusso);
    }

    private void MostraAppTablet()
    {
        string? apk = ApkDelProgetto();
        string data(DateTime d) => d.ToString("d MMM HH:mm", new CultureInfo("it-IT"));

        TestoAppStato.Text = esitoApp.Stato switch
        {
            StatoApp.SenzaApk => "Manca TabDeck.apk",
            StatoApp.SenzaCavo => "Serve il cavo",
            StatoApp.Troppi => "Piu' tablet con TabDeck",
            StatoApp.NonInstallata => "Non installata",
            StatoApp.Uguale => "Aggiornata",
            StatoApp.Diversa => "Da aggiornare",
            _ => "Non controllata",
        };
        TestoAppStato.Foreground = (System.Windows.Media.Brush)FindResource(
            esitoApp.Stato == StatoApp.Diversa ? "Attenzione" : esitoApp.Stato == StatoApp.Uguale ? "Acceso" : "TestoForte");

        var righe = new List<string>();
        if (esitoApp.SulTablet is { } sul) righe.Add("Sul tablet: " + data(sul));
        if (apk is not null) righe.Add("Sul PC: " + data(File.GetLastWriteTime(apk)));
        if (esitoApp.Stato == StatoApp.SenzaApk) righe.Add("Compilala con reinstalla-tablet.bat");
        if (esitoApp.Stato == StatoApp.Troppi) righe.Add("Scegli il tablet da « Prepara un tablet »");
        TestoAppDettaglio.Text = string.Join("  ·  ", righe);

        bool puo = esitoApp.Stato == StatoApp.Diversa && !aggiornandoApp;
        BtnAggiornaApp.IsEnabled = puo;
        RigaAppDashboard.Visibility = esitoApp.Stato == StatoApp.Diversa ? Visibility.Visible : Visibility.Collapsed;
        BtnAggiornaAppDashboard.IsEnabled = puo;
    }

    private void ControllaApp_Click(object sender, RoutedEventArgs e) => ControllaAppTablet();

    /// <summary>
    /// « install -r » tiene i dati: deck, luci, sveglie ed estensioni restano dov'erano.
    /// L'installazione chiude l'app, e con lei il collegamento: si stacca prima, si riapre
    /// l'app e, se si era collegati, ci si ricollega per la stessa strada. Fa parte del
    /// pulsante premuto, non e' un tentativo automatico.
    /// </summary>
    private async void AggiornaApp_Click(object sender, RoutedEventArgs e)
    {
        string? apk = ApkDelProgetto();
        if (aggiornandoApp || apk is null || esitoApp.Stato != StatoApp.Diversa) return;
        string seriale = esitoApp.Seriale;

        aggiornandoApp = true;
        MostraAppTablet();
        TestoAppStato.Text = "Aggiorno…";
        Registra("Aggiorno l'app del tablet: si chiude e si riapre.");

        string transport = engine.Connected ? engine.Transport : "";
        if (engine.Connected) engine.Disconnect();

        var (_, testo) = await Task.Run(() =>
        {
            var esito = Adb.Esegui(adbPath, $"-s {seriale} install -r \"{apk}\"", 180_000);
            if (esito.Testo.Contains("Success", StringComparison.Ordinal))
                Adb.Shell(adbPath, seriale, $"am start -n {PreparaTablet.Pacchetto}/.MainActivity");
            return esito;
        });
        bool riuscito = testo.Contains("Success", StringComparison.Ordinal);
        aggiornandoApp = false;

        if (!riuscito)
        {
            var righe = testo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Registra("App del tablet non aggiornata: " + (righe.Length > 0 ? righe[^1] : "adb non ha detto niente"), true);
            MostraAppTablet();
            return;
        }

        Registra("App del tablet aggiornata.");
        if (transport.Length > 0)
        {
            // Il tempo che l'app riapra la sua socket.
            await Task.Delay(2500);
            string modo = transport.Equals("usb", StringComparison.OrdinalIgnoreCase) ? "usb" : "wifi";
            string errore = await Task.Run(() => engine.Connected ? "" : engine.Connect(modo, adbPath));
            if (errore.Length > 0) Registra("Non ricollegato: " + errore, true);
        }
        ControllaAppTablet();
    }

    private void SchedaTablet_Aperta(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Schede && Schede.SelectedItem == SchedaTablet) ControllaAppTablet();
    }
}
