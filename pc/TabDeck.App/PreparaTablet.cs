using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace TabDeck;

/// <summary>
/// Un tablet Android qualunque diventa TabDeck, o torna com'era.
///
/// <para><b>Prepara.</b> Quattro passi col cavo, ognuno si apre quando quello prima e'
/// fatto: collegalo (con le istruzioni per il debug USB, e la scelta se ce n'e' piu'
/// d'uno), installa o aggiorna l'APK, preparalo (Home, risparmio, app di sistema a
/// riposo), collegalo a TabDeck.</para>
///
/// <para><b>Disinstalla.</b> Lo stesso primo passo, poi uno solo: via TabDeck coi suoi
/// dati, e le app messe a riposo di nuovo in servizio.</para>
///
/// <para>Il tablet si cerca ogni due secondi, ma solo mentre questa finestra e' aperta:
/// e' l'unico momento in cui qualcuno sta attaccando un cavo apposta.</para>
/// </summary>
public sealed class PreparaTablet : Window
{
    public const string Pacchetto = "dev.tabdeck";
    private const int SdkMinimo = 19;

    private readonly string adb;
    private readonly string? apk;
    private readonly IReadOnlyList<string> elencoRiposo;
    private readonly int portaUsb;
    private readonly bool togli;
    private readonly Func<string, string> collega;
    private readonly Action scollega;

    private readonly DispatcherTimer battito = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, Adb.Dispositivo> noti = new();
    private bool cercando;
    private string ultimaFirma = "?";
    private Adb.Dispositivo? scelto;
    private bool installato;
    private List<string> riposoQui = new();

    private readonly StackPanel colonna = new();
    private readonly List<Border> passi = new();

    private readonly TextBlock statoCavo = new();
    private readonly TextBlock aiutoCavo = new();
    private readonly StackPanel dispositivi = new();

    private readonly TextBlock testoInstalla = new();
    private readonly Button pulsanteInstalla = new();
    private readonly TextBlock esitoInstalla = new();

    private readonly CheckBox spuntaHome = new();
    private readonly CheckBox spuntaRisparmio = new();
    private readonly CheckBox spuntaRiposo = new();
    private readonly Button pulsantePrepara = new();
    private readonly TextBlock esitoPrepara = new();

    private readonly Button pulsanteCollega = new();
    private readonly TextBlock esitoCollega = new();

    private readonly TextBlock testoTogli = new();
    private readonly CheckBox spuntaRimetti = new();
    private readonly Button pulsanteTogli = new();
    private readonly TextBlock esitoTogli = new();

    /// <param name="collega">Col seriale scelto; torna vuoto se si e' collegato, altrimenti il motivo. Da un task.</param>
    /// <param name="scollega">Sul thread della finestra, prima di togliere l'app.</param>
    public PreparaTablet(Window proprietario, string adb, string? apk, IReadOnlyList<string> elencoRiposo,
        int portaUsb, bool togli, Func<string, string> collega, Action scollega)
    {
        this.adb = adb;
        this.apk = apk;
        this.elencoRiposo = elencoRiposo;
        this.portaUsb = portaUsb;
        this.togli = togli;
        this.collega = collega;
        this.scollega = scollega;

        Owner = proprietario;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = togli ? "Togli TabDeck da un tablet" : "Prepara un tablet";
        Width = 720;
        Height = 780;
        MinWidth = 520;
        MinHeight = 460;
        ShowInTaskbar = false;
        Background = Pennello("Sfondo");
        Foreground = Pennello("Testo");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        colonna.Margin = new Thickness(26, 22, 26, 26);
        colonna.Children.Add(new TextBlock { Text = Title, Style = Stile("TitoloPagina") });
        colonna.Children.Add(new TextBlock
        {
            Style = Stile("SottotitoloPagina"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 18),
            Text = togli
                ? "Il tablet torna com'era prima di TabDeck. Sul PC non si tocca niente."
                : "Un tablet Android dalla 4.4 in su diventa TabDeck, col cavo.",
        });

        CostruisciCavo();
        if (togli) CostruisciTogli();
        else CostruisciPrepara();

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = colonna };

        battito.Tick += (_, _) => Cerca();
        Loaded += (_, _) =>
        {
            Cerca();
            battito.Start();
        };
        Closed += (_, _) => battito.Stop();
        for (int i = 1; i < passi.Count; i++) Abilita(i, false);
    }

    // ---- costruzione ----

    private void CostruisciCavo()
    {
        var corpo = Passo(1, "Collega il tablet col cavo");
        statoCavo.FontSize = 14;
        statoCavo.FontWeight = FontWeights.Normal;
        statoCavo.Foreground = Pennello("TestoForte");
        statoCavo.Text = "Cerco…";
        corpo.Children.Add(statoCavo);
        aiutoCavo.Style = Stile("Aiuto");
        aiutoCavo.TextWrapping = TextWrapping.Wrap;
        aiutoCavo.Margin = new Thickness(0, 6, 0, 0);
        aiutoCavo.LineHeight = 20;
        corpo.Children.Add(aiutoCavo);
        dispositivi.Margin = new Thickness(0, 4, 0, 0);
        corpo.Children.Add(dispositivi);
    }

    private void CostruisciPrepara()
    {
        var installa = Passo(2, "Installa TabDeck");
        testoInstalla.Style = Stile("Aiuto");
        testoInstalla.TextWrapping = TextWrapping.Wrap;
        installa.Children.Add(testoInstalla);
        Pulsante(pulsanteInstalla, "Installa TabDeck", "Primario", Installa);
        installa.Children.Add(pulsanteInstalla);
        installa.Children.Add(Esito(esitoInstalla));

        var prepara = Passo(3, "Preparalo");
        Spunta(spuntaHome, "TabDeck come schermata Home: parte da solo all'accensione, e non si esce per sbaglio", true);
        Spunta(spuntaRisparmio, "Il pannello si spegne anche col cavo attaccato, e il Wi-Fi resta acceso per farsi trovare dal PC", true);
        Spunta(spuntaRiposo, "", false);
        prepara.Children.Add(spuntaHome);
        prepara.Children.Add(spuntaRisparmio);
        prepara.Children.Add(spuntaRiposo);
        Pulsante(pulsantePrepara, "Applica", "Primario", Prepara);
        prepara.Children.Add(pulsantePrepara);
        prepara.Children.Add(Esito(esitoPrepara));

        var tabdeck = Passo(4, "Collegalo a TabDeck");
        tabdeck.Children.Add(new TextBlock
        {
            Style = Stile("Aiuto"),
            TextWrapping = TextWrapping.Wrap,
            Text = "Il tablet riceve deck, luci, impostazioni e le estensioni accese. Da li' in poi si collega anche in rete.",
        });
        Pulsante(pulsanteCollega, "Collega col cavo", "Primario", Collega);
        tabdeck.Children.Add(pulsanteCollega);
        tabdeck.Children.Add(Esito(esitoCollega));
    }

    private void CostruisciTogli()
    {
        var corpo = Passo(2, "Togli TabDeck");
        testoTogli.Style = Stile("Aiuto");
        testoTogli.TextWrapping = TextWrapping.Wrap;
        corpo.Children.Add(testoTogli);
        Spunta(spuntaRimetti, "", true);
        corpo.Children.Add(spuntaRimetti);
        Pulsante(pulsanteTogli, "Disinstalla TabDeck", "Rischio", Togli);
        corpo.Children.Add(pulsanteTogli);
        corpo.Children.Add(Esito(esitoTogli));
    }

    private StackPanel Passo(int numero, string titolo)
    {
        var testa = new TextBlock
        {
            FontSize = 14.5,
            FontWeight = FontWeights.Normal,
            Foreground = Pennello("TestoForte"),
            Margin = new Thickness(0, 0, 0, 8),
        };
        testa.Inlines.Add(new Run(numero + "   ") { Foreground = Pennello("Acceso") });
        testa.Inlines.Add(new Run(titolo));

        var corpo = new StackPanel();
        var dentro = new StackPanel();
        dentro.Children.Add(testa);
        dentro.Children.Add(corpo);
        var riquadro = new Border
        {
            Style = Stile("Riquadro"),
            Padding = new Thickness(18, 14, 18, 16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = dentro,
        };
        passi.Add(riquadro);
        colonna.Children.Add(riquadro);
        return corpo;
    }

    private void Abilita(int indice, bool acceso)
    {
        if (indice >= passi.Count) return;
        passi[indice].IsEnabled = acceso;
        passi[indice].Opacity = acceso ? 1 : 0.45;
    }

    private void Spunta(CheckBox spunta, string testo, bool acceso)
    {
        spunta.Content = new TextBlock { Text = testo, TextWrapping = TextWrapping.Wrap };
        spunta.IsChecked = acceso;
        spunta.Margin = new Thickness(0, 8, 0, 0);
    }

    private void Pulsante(Button pulsante, string testo, string stile, Action azione)
    {
        pulsante.Content = testo;
        pulsante.Style = Stile(stile);
        pulsante.HorizontalAlignment = HorizontalAlignment.Left;
        pulsante.Margin = new Thickness(0, 14, 0, 0);
        pulsante.Click += (_, _) => azione();
    }

    private TextBlock Esito(TextBlock esito)
    {
        esito.TextWrapping = TextWrapping.Wrap;
        esito.Margin = new Thickness(0, 10, 0, 0);
        esito.Visibility = Visibility.Collapsed;
        return esito;
    }

    private void Scrivi(TextBlock esito, string testo, bool? riuscito)
    {
        esito.Text = testo;
        esito.Visibility = testo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        esito.Foreground = Pennello(riuscito switch { true => "Acceso", false => "Attenzione", null => "TestoTenue" });
    }

    private Brush Pennello(string chiave) => (Brush)FindResource(chiave);
    private Style Stile(string chiave) => (Style)FindResource(chiave);

    // ---- passo 1: il tablet ----

    private async void Cerca()
    {
        if (cercando) return;
        cercando = true;
        try
        {
            var trovati = await Task.Run(() =>
            {
                var elenco = new List<(string Seriale, string Stato, Adb.Dispositivo? Chi)>();
                foreach (var (seriale, stato) in Adb.Seriali(adb))
                {
                    Adb.Dispositivo? chi = null;
                    if (stato == "device" && !noti.TryGetValue(seriale, out chi))
                        noti[seriale] = chi = Adb.Descrivi(adb, seriale);
                    elenco.Add((seriale, stato, chi));
                }
                return elenco;
            });
            MostraDispositivi(trovati);
        }
        finally
        {
            cercando = false;
        }
    }

    private void MostraDispositivi(List<(string Seriale, string Stato, Adb.Dispositivo? Chi)> trovati)
    {
        string firma = string.Join("|", trovati.Select(t => t.Seriale + ":" + t.Stato));
        if (firma == ultimaFirma) return;
        ultimaFirma = firma;

        dispositivi.Children.Clear();
        var pronti = trovati.Where(t => t.Chi is not null).Select(t => t.Chi!).ToList();
        if (pronti.Count == 0)
        {
            bool chiede = trovati.Any(t => t.Stato == "unauthorized");
            bool mezzo = trovati.Any(t => t.Stato == "offline");
            statoCavo.Text = chiede ? "C'e' un tablet, ma non ha ancora dato il permesso"
                : mezzo ? "Il tablet risponde a meta'"
                : "Nessun tablet collegato";
            aiutoCavo.Text = chiede
                ? "Sblocca lo schermo del tablet: c'e' la richiesta « Consenti debug USB ». Spunta « Consenti sempre da questo computer » e tocca OK."
                : mezzo
                    ? "Stacca e riattacca il cavo. Se resta cosi', sul tablet spegni e riaccendi « Debug USB »."
                    : "1.  Sul tablet apri Impostazioni › Info sul dispositivo, e tocca sette volte « Numero build ».\n"
                      + "2.  Torna indietro: in « Opzioni sviluppatore » accendi « Debug USB ».\n"
                      + "3.  Collega il cavo al PC. Se non compare niente prova un altro cavo — molti sono di sola ricarica — "
                      + "e, su alcuni tablet, installa il driver USB del produttore.";
            aiutoCavo.Visibility = Visibility.Visible;
            Scegli(null, false);
            return;
        }

        statoCavo.Text = pronti.Count == 1 ? "Tablet collegato" : $"{pronti.Count} tablet collegati: scegli quale";
        aiutoCavo.Visibility = Visibility.Collapsed;
        RadioButton? daScegliere = null;
        var adatti = pronti.Where(d => d.Sdk >= SdkMinimo).ToList();
        foreach (var d in pronti)
        {
            bool adatto = d.Sdk >= SdkMinimo;
            var voce = new RadioButton
            {
                GroupName = "tablet",
                IsEnabled = adatto,
                Foreground = Pennello("Testo"),
                Margin = new Thickness(0, 8, 0, 0),
                Content = adatto
                    ? $"{d.Nome}  ·  Android {d.Android}"
                    : $"{d.Nome}  ·  Android {d.Android} — troppo vecchio: TabDeck vuole Android 4.4",
            };
            var questo = d;
            voce.Checked += (_, _) => Scegli(questo, false);
            dispositivi.Children.Add(voce);
            if (adatto && (d.Seriale == scelto?.Seriale || adatti.Count == 1)) daScegliere = voce;
        }
        if (daScegliere is not null) daScegliere.IsChecked = true;
        else Scegli(null, false);
    }

    private async void Scegli(Adb.Dispositivo? d, bool rileggi)
    {
        if (!rileggi && d?.Seriale == scelto?.Seriale && (d is null || passi.Count < 2 || passi[1].IsEnabled)) return;
        scelto = d;
        for (int i = 1; i < passi.Count; i++) Abilita(i, false);
        if (d is null) return;

        var (c, riposo) = await Task.Run(() => (
            Adb.Pacchetti(adb, d.Seriale).Contains(Pacchetto),
            Adb.Riposabili(adb, d, elencoRiposo, inServizio: !togli)));
        if (scelto != d) return;
        installato = c;
        riposoQui = riposo;
        AggiornaPassi();
    }

    private void AggiornaPassi()
    {
        if (togli)
        {
            Abilita(1, true);
            testoTogli.Text = installato
                ? "Si toglie TabDeck con tutto quello che il tablet si era salvato: deck, luci, sveglie, estensioni e foto del salvaschermo."
                : "Su questo tablet TabDeck non c'e'.";
            ((TextBlock)spuntaRimetti.Content).Text = $"Rimetti in servizio le {riposoQui.Count} app di sistema messe a riposo";
            spuntaRimetti.Visibility = riposoQui.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            pulsanteTogli.Content = installato ? "Disinstalla TabDeck" : "Rimetti in servizio le app";
            pulsanteTogli.IsEnabled = installato || riposoQui.Count > 0;
            return;
        }

        Abilita(1, apk is not null);
        long kb = apk is null ? 0 : new FileInfo(apk).Length / 1024;
        testoInstalla.Text = apk is null
            ? "Manca TabDeck.apk: compilalo con tablet\\build.ps1, oppure usa il pacchetto di TabDeck."
            : installato
                ? $"TabDeck c'e' gia'. Aggiornarlo ({kb} KB) tiene deck, luci, sveglie ed estensioni."
                : $"TabDeck, {kb} KB.";
        pulsanteInstalla.Content = installato ? "Aggiorna TabDeck" : "Installa TabDeck";
        Abilita(2, installato);
        Abilita(3, installato);
        ((TextBlock)spuntaRiposo.Content).Text =
            $"Metti a riposo {riposoQui.Count} app di sistema che TabDeck non usa: si rimettono in servizio disinstallando";
        spuntaRiposo.Visibility = riposoQui.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- passi 2, 3, 4 ----

    private async void Installa()
    {
        if (scelto is not { } d || apk is null) return;
        pulsanteInstalla.IsEnabled = false;
        Scrivi(esitoInstalla, installato ? "Aggiorno…" : "Installo…", null);
        var (_, testo) = await Task.Run(() => Adb.Esegui(adb, $"-s {d.Seriale} install -r \"{apk}\"", 180_000));
        bool riuscito = testo.Contains("Success", StringComparison.Ordinal);
        Scrivi(esitoInstalla, riuscito ? (installato ? "Aggiornato." : "Installato.") : "Non installato: " + Motivo(testo), riuscito);
        pulsanteInstalla.IsEnabled = true;
        if (riuscito)
        {
            installato = true;
            AggiornaPassi();
        }
    }

    private async void Prepara()
    {
        if (scelto is not { } d) return;
        bool home = spuntaHome.IsChecked == true;
        bool risparmio = spuntaRisparmio.IsChecked == true;
        var riposo = spuntaRiposo.IsChecked == true ? riposoQui.ToList() : new List<string>();
        pulsantePrepara.IsEnabled = false;
        Scrivi(esitoPrepara, "Preparo…", null);

        await Task.Run(() =>
        {
            // wifi_sleep_policy 2 e' « mai »: a schermo spento il tablet deve ancora rispondere
            // al PC che lo cerca in rete. Il pannello invece si spegne anche col cavo attaccato.
            if (risparmio)
                Adb.Shell(adb, d.Seriale, "settings put global stay_on_while_plugged_in 0; settings put global wifi_sleep_policy 2");
            if (riposo.Count > 0) Adb.Riposo(adb, d, riposo, metti: true);
            Adb.Shell(adb, d.Seriale, home
                ? "am start -a android.intent.action.MAIN -c android.intent.category.HOME"
                : $"am start -n {Pacchetto}/.MainActivity");
        });

        if (riposo.Count > 0) riposoQui = new List<string>();
        AggiornaPassi();
        pulsantePrepara.IsEnabled = true;
        Scrivi(esitoPrepara,
            "Fatto." + (riposo.Count > 0 ? $" {riposo.Count} app a riposo." : "")
            + (home ? " Adesso sul tablet scegli TabDeck e tocca « Sempre »: da li' in poi e' la sua Home." : ""),
            true);
    }

    private async void Collega()
    {
        if (scelto is not { } d) return;
        pulsanteCollega.IsEnabled = false;
        Scrivi(esitoCollega, "Mi collego…", null);
        string errore = await Task.Run(() => collega(d.Seriale));
        pulsanteCollega.IsEnabled = true;
        Scrivi(esitoCollega, errore.Length == 0
                ? "Collegato. Da qui in poi si usa dalla finestra di TabDeck, e questa si puo' chiudere."
                : $"Non collegato: {errore}. Controlla che TabDeck sia aperto sul tablet.",
            errore.Length == 0);
    }

    private async void Togli()
    {
        if (scelto is not { } d) return;
        bool conApp = installato;
        var rimetti = spuntaRimetti.IsChecked == true ? riposoQui.ToList() : new List<string>();
        if (conApp && MessageBox.Show(this,
                $"Disinstallare TabDeck da {d.Nome}?\nSul tablet si perdono deck, luci, sveglie, estensioni e foto salvate.",
                "TabDeck", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        pulsanteTogli.IsEnabled = false;
        Scrivi(esitoTogli, "Tolgo…", null);
        if (conApp) scollega();

        string testo = await Task.Run(() =>
        {
            Adb.Esegui(adb, $"-s {d.Seriale} forward --remove tcp:{portaUsb}");
            if (rimetti.Count > 0) Adb.Riposo(adb, d, rimetti, metti: false);
            string esito = conApp ? Adb.Esegui(adb, $"-s {d.Seriale} uninstall {Pacchetto}", 60_000).Testo : "";
            Adb.Shell(adb, d.Seriale, "am start -a android.intent.action.MAIN -c android.intent.category.HOME");
            return esito;
        });

        bool riuscito = !conApp || testo.Contains("Success", StringComparison.Ordinal);
        Scrivi(esitoTogli, riuscito
                ? "Fatto: " + (conApp ? "TabDeck non c'e' piu'" : "niente da togliere")
                  + (rimetti.Count > 0 ? $", e {rimetti.Count} app di sistema sono di nuovo in servizio" : "")
                  + ". Se il tablet chiede quale schermata Home usare, scegli quella di sistema."
                : "Non disinstallato: " + Motivo(testo),
            riuscito);
        Scegli(d, rileggi: true);
    }

    /// <summary>L'ultima riga di adb, detta in chiaro quando la si conosce.</summary>
    private static string Motivo(string testo)
    {
        if (testo.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE") || testo.Contains("INCONSISTENT_CERTIFICATES"))
            return "sul tablet c'e' un TabDeck firmato con un'altra chiave. Toglilo prima con « Disinstalla »";
        if (testo.Contains("INSTALL_FAILED_OLDER_SDK")) return "Android troppo vecchio, serve la 4.4";
        if (testo.Contains("INSUFFICIENT_STORAGE")) return "la memoria del tablet e' piena";
        var righe = testo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return righe.Length > 0 ? righe[^1] : "adb non ha detto niente";
    }
}
