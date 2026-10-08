using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace TabDeck;

/// <summary>
/// La pagina Tablet: com'e' messo, cosa mostra, e come ci si mette o si toglie TabDeck.
/// Le scelte si fanno a tablet collegato o no: si salvano qui, e al tablet arrivano
/// subito o al collegamento dopo.
/// </summary>
public partial class MainWindow
{
    private void PreparaTablet_Click(object sender, RoutedEventArgs e) => ApriTablet(togli: false);

    private void DisinstallaTablet_Click(object sender, RoutedEventArgs e) => ApriTablet(togli: true);

    private void ApriTablet(bool togli)
    {
        var finestra = new PreparaTablet(this, adbPath,
            TrovaNelProgetto(@"tablet\TabDeck.apk", @"tablet\build\TabDeck.apk"),
            AppDaRiposo(), settings.UsbPort, togli,
            collega: seriale =>
            {
                // Con piu' tablet attaccati « adb forward » vuole sapere quale: la variabile
                // la leggono tutti gli adb che TabDeck lancia da qui in poi.
                Environment.SetEnvironmentVariable("ANDROID_SERIAL", seriale);
                return engine.Connected ? "" : engine.Connect("usb", adbPath);
            },
            scollega: () =>
            {
                if (engine.Connected) engine.Disconnect();
            });
        finestra.ShowDialog();
    }

    /// <summary>Come adb: accanto alla cartella config, nel pacchetto o nel sorgente.</summary>
    private string? TrovaNelProgetto(params string[] relativi)
    {
        var dir = new DirectoryInfo(configDir).Parent;
        for (int i = 0; i < 4 && dir is not null; i++, dir = dir.Parent)
            foreach (var relativo in relativi)
            {
                string candidato = Path.Combine(dir.FullName, relativo);
                if (File.Exists(candidato)) return candidato;
            }
        return null;
    }

    /// <summary>Le app di sistema che TabDeck non usa, da tablet\sistema\bloccati.txt.</summary>
    private IReadOnlyList<string> AppDaRiposo()
    {
        string? file = TrovaNelProgetto(@"tablet\sistema\bloccati.txt");
        if (file is null) return Array.Empty<string>();
        return File.ReadAllLines(file)
            .Select(r => r.Trim())
            .Where(r => r.Length > 0 && !r.StartsWith('#'))
            .ToList();
    }

    private void MostraStatoTablet()
    {
        bool collegato = engine.Connected;
        TestoTabletStato.Text = !collegato ? "Non collegato"
            : engine.Transport.Equals("usb", StringComparison.OrdinalIgnoreCase) ? "Collegato col cavo"
            : "Collegato in rete";
        TestoTabletDettaglio.Text = collegato
            ? $"Risponde a {engine.Peer}. Le scelte di questa pagina arrivano subito."
            : "Le scelte di questa pagina si salvano qui, e arrivano al tablet appena si collega.";
        foreach (var pulsante in new[] { BtnTabletImpostazioni, BtnTabletWifi, BtnTabletRiavvia })
            pulsante.IsEnabled = collegato;
    }

    /// <summary>{"sezioni":{...}} scelte sul tablet, anche mentre era scollegato. Thread di lettura.</summary>
    private void ConfigDalTablet(string json)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ConfigDalTablet(json));
            return;
        }

        JsonObject? sezioni;
        try
        {
            sezioni = JsonNode.Parse(json)?["sezioni"] as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (sezioni is null) return;

        bool prima = caricamento;
        caricamento = true;
        // Due scelte; un tablet di prima ne manda una sola, e vale per tutte e due.
        var con = sezioni["collegato"] as JsonObject ?? sezioni;
        var senza = sezioni["scollegato"] as JsonObject ?? sezioni;
        SpuntaSezDashboard.IsChecked = (bool?)con["dashboard"] ?? true;
        SpuntaSezDeck.IsChecked = (bool?)con["deck"] ?? true;
        SpuntaSezSchermo.IsChecked = (bool?)con["schermo"] ?? true;
        SpuntaSezCasa.IsChecked = (bool?)con["casa"] ?? true;
        SpuntaSezOrologio.IsChecked = (bool?)con["orologio"] ?? true;
        SpuntaSenzaDashboard.IsChecked = (bool?)senza["dashboard"] ?? true;
        SpuntaSenzaDeck.IsChecked = (bool?)senza["deck"] ?? true;
        SpuntaSenzaCasa.IsChecked = (bool?)senza["casa"] ?? true;
        SpuntaSenzaOrologio.IsChecked = (bool?)senza["orologio"] ?? true;
        caricamento = prima;
        // Il momento della scelta fatta sul tablet diventa quello di qui: al prossimo
        // collegamento i due lati sono d'accordo, e nessuno dei due la rimanda indietro.
        if (sezioni["cambiate"] is JsonValue quando && quando.TryGetValue(out long ms))
            settings.Tablet.Sezioni.Cambiate = ms;
        SalvaImpostazioni();
        Registra("Sezioni della barra scelte sul tablet.");
    }
}
