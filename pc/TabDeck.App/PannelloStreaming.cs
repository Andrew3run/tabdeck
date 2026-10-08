using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TabDeck;

/// <summary>
/// Il collegamento a OBS o a Twitch, dentro l'editor del pulsante: compare
/// solo quando l'azione e' una delle due, perche' e' li' che serve. Ce n'e' uno
/// sotto l'azione del pulsante e uno sotto il passo della sequenza; leggono e
/// scrivono tutti e due la stessa config/streaming.json.
/// </summary>
public sealed class PannelloStreaming : Grid
{
    private string tipo = "";

    private readonly TextBox host = new() { Width = 170 };
    private readonly TextBox porta = new() { Width = 64, Margin = new Thickness(6, 0, 0, 0) };
    private readonly PasswordBox password = new() { Width = 240 };
    private readonly TextBox clientId = new() { Width = 300 };
    private readonly Button azione = new() { MinWidth = 0, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button annulla = new() { Content = "Annulla", MinWidth = 0, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock stato = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
    private readonly TextBlock aiutoTwitch = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 480, Margin = new Thickness(0, 0, 0, 10) };

    private readonly FrameworkElement[] righeObs;
    private readonly FrameworkElement[] righeTwitch;

    private readonly StackPanel rigaPassword = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel comandi = new() { Orientation = Orientation.Horizontal };

    private CancellationTokenSource? attesa;
    private bool riempendo;

    public PannelloStreaming()
    {
        Margin = new Thickness(0, 12, 0, 0);
        Visibility = Visibility.Collapsed;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Loaded += (_, _) => Stili();

        // ---- OBS: indirizzo e porta, password, prova ----
        var indirizzo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        indirizzo.Children.Add(host);
        indirizzo.Children.Add(porta);
        rigaPassword.Children.Add(password);

        righeObs = new FrameworkElement[]
        {
            Etichetta("OBS", 0), Metti(indirizzo, 0),
            Etichetta("Password", 1), Metti(rigaPassword, 1),
        };

        // ---- Twitch: client id, account ----
        var link = new Hyperlink(new Run("dev.twitch.tv/console")) { NavigateUri = new Uri("https://dev.twitch.tv/console/apps/create") };
        link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        // L'unica riga di spiegazione: senza, il campo vuoto non dice dove
        // prendere quel che chiede.
        aiutoTwitch.Inlines.Add(new Run("Un'app di tipo « Public » su "));
        aiutoTwitch.Inlines.Add(link);
        aiutoTwitch.Inlines.Add(new Run(", indirizzo http://localhost."));

        var conto = new StackPanel { Orientation = Orientation.Horizontal };
        conto.Children.Add(clientId);

        righeTwitch = new FrameworkElement[]
        {
            Etichetta("Client ID", 0), Metti(conto, 0),
            Metti(aiutoTwitch, 1),
            Etichetta("Account", 2),
        };

        comandi.Children.Add(azione);
        comandi.Children.Add(annulla);
        comandi.Children.Add(stato);

        host.LostFocus += (_, _) => SalvaObs();
        porta.LostFocus += (_, _) => SalvaObs();
        password.LostFocus += (_, _) => SalvaObs();
        clientId.TextChanged += (_, _) =>
        {
            if (riempendo) return;
            Streaming.Config.TwitchClientId = clientId.Text.Trim();
            Aggiorna();
        };
        clientId.LostFocus += (_, _) => Streaming.Salva();
        azione.Click += Azione_Click;
        annulla.Click += (_, _) => attesa?.Cancel();
    }

    /// <summary>"obs", "twitch", o qualunque altra cosa per nasconderlo.</summary>
    public void Mostra(string codice)
    {
        tipo = codice is "obs" or "twitch" ? codice : "";
        Visibility = tipo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tipo.Length == 0) return;

        foreach (var e in righeObs) e.Visibility = tipo == "obs" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var e in righeTwitch) e.Visibility = tipo == "twitch" ? Visibility.Visible : Visibility.Collapsed;

        // Prova sta accanto alla password; Collega su una riga sua.
        if (comandi.Parent is Panel prima) prima.Children.Remove(comandi);
        if (tipo == "obs")
        {
            rigaPassword.Children.Add(comandi);
        }
        else
        {
            SetRow(comandi, 2);
            SetColumn(comandi, 1);
            Children.Add(comandi);
        }

        Riempi();
        Aggiorna();
    }

    private void Riempi()
    {
        riempendo = true;
        var cfg = Streaming.Config;
        host.Text = cfg.ObsHost;
        porta.Text = cfg.ObsPorta.ToString();
        if (password.Password != cfg.ObsPassword) password.Password = cfg.ObsPassword;
        clientId.Text = cfg.TwitchClientId;
        riempendo = false;
    }

    private void Aggiorna()
    {
        var cfg = Streaming.Config;
        bool aspetta = attesa is not null;
        annulla.Visibility = aspetta ? Visibility.Visible : Visibility.Collapsed;

        if (tipo == "obs")
        {
            azione.Content = "Prova";
            azione.IsEnabled = true;
            return;
        }

        bool collegato = Streaming.Twitch.Collegato;
        clientId.IsEnabled = !collegato && !aspetta;
        aiutoTwitch.Visibility = !collegato && cfg.TwitchClientId.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        azione.Content = collegato ? "Scollega" : "Collega";
        azione.Visibility = aspetta ? Visibility.Collapsed : Visibility.Visible;
        azione.IsEnabled = collegato || cfg.TwitchClientId.Length > 0;
        if (!aspetta) Stato(collegato ? cfg.TwitchUtente : "non collegato", false);
    }

    private void SalvaObs()
    {
        if (riempendo) return;
        var cfg = Streaming.Config;
        int p = int.TryParse(porta.Text.Trim(), out int n) && n is > 0 and < 65536 ? n : 4455;
        string h = host.Text.Trim().Length > 0 ? host.Text.Trim() : "localhost";
        if (h == cfg.ObsHost && p == cfg.ObsPorta && password.Password == cfg.ObsPassword) return;

        cfg.ObsHost = h;
        cfg.ObsPorta = p;
        cfg.ObsPassword = password.Password;
        Streaming.Salva();
        Streaming.Obs.Dimentica();   // il collegamento aperto era verso l'indirizzo di prima
        Riempi();
        Stato("", false);
    }

    private async void Azione_Click(object sender, RoutedEventArgs e)
    {
        if (tipo == "obs")
        {
            SalvaObs();
            azione.IsEnabled = false;
            Stato("…", false);
            try
            {
                Stato("OBS " + await Task.Run(Streaming.Obs.Prova), false);
            }
            catch (StreamingErrore x)
            {
                Stato(x.Message, true);
            }
            azione.IsEnabled = true;
            return;
        }

        if (Streaming.Twitch.Collegato)
        {
            Streaming.Twitch.Scollega();
            Aggiorna();
            return;
        }

        attesa = new CancellationTokenSource();
        Aggiorna();
        try
        {
            var (codice, indirizzo, dispositivo, ogni, scade) = await Streaming.Twitch.Inizia();
            Stato("Codice " + codice, false);
            Process.Start(new ProcessStartInfo(indirizzo) { UseShellExecute = true });
            string chi = await Streaming.Twitch.Attendi(dispositivo, ogni, scade, attesa.Token);
            attesa = null;
            Aggiorna();
            Stato(chi, false);
        }
        catch (OperationCanceledException)
        {
            attesa = null;
            Aggiorna();
        }
        catch (StreamingErrore x)
        {
            attesa = null;
            Aggiorna();
            Stato(x.Message, true);
        }
    }

    private void Stato(string testo, bool male)
    {
        stato.Text = testo;
        stato.Foreground = (Brush)FindResource(male ? "Attenzione" : "TestoTenue");
    }

    private void Stili()
    {
        azione.Style = (Style)FindResource("Tenue");
        annulla.Style = (Style)FindResource("Tenue");
        aiutoTwitch.Style = (Style)FindResource("Aiuto");
        foreach (var t in Children.OfType<TextBlock>().Where(t => t.Tag as string == "etichetta"))
            t.Style = (Style)FindResource("Etichetta");
    }

    private TextBlock Etichetta(string testo, int riga)
    {
        var t = new TextBlock { Text = testo, Tag = "etichetta", Margin = new Thickness(0, 0, 0, 10) };
        SetRow(t, riga);
        Children.Add(t);
        return t;
    }

    private T Metti<T>(T e, int riga) where T : FrameworkElement
    {
        SetRow(e, riga);
        SetColumn(e, 1);
        Children.Add(e);
        return e;
    }
}
