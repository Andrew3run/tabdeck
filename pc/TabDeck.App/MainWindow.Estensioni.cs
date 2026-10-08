using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TabDeck.Estensioni;

namespace TabDeck;

/// <summary>
/// La pagina Estensioni: quello che TabDeck non fa di serie, installato da un file.
///
/// TabDeck funziona tutto senza: la pagina elenca quello che c'e' in
/// config\estensioni, e ogni estensione si accende, si regola e si toglie da qui.
/// Le voci si rifanno a ogni cambio — sono poche, e cosi' lo stato mostrato non
/// puo' restare indietro rispetto a quello vero.
/// </summary>
public partial class MainWindow
{
    private GestoreEstensioni? estensioni;

    private void PreparaEstensioni()
    {
        estensioni = new GestoreEstensioni(engine, settings, SalvaImpostazioni, Path.Combine(configDir, "estensioni"));
        estensioni.Log += messaggio => Registra(messaggio);
        estensioni.Cambiato += MostraEstensioni;
        estensioni.Carica();
    }

    private void MostraEstensioni()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => MostraEstensioni());
            return;
        }
        if (estensioni is null) return;

        ElencoEstensioni.Children.Clear();
        var elenco = estensioni.Elenco
            .OrderBy(i => i.Manifesto.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (elenco.Count == 0)
        {
            ElencoEstensioni.Children.Add(new Border
            {
                Style = (Style)FindResource("Riquadro"),
                Child = new TextBlock
                {
                    Text = "Nessuna estensione installata.",
                    Style = (Style)FindResource("Aiuto"),
                },
            });
            return;
        }

        foreach (var installata in elenco) ElencoEstensioni.Children.Add(Voce(installata));
    }

    private GroupBox Voce(Installata installata)
    {
        var m = installata.Manifesto;
        var corpo = new StackPanel();

        if (m.Descrizione.Length > 0)
            corpo.Children.Add(new TextBlock
            {
                Text = m.Descrizione,
                Style = (Style)FindResource("Aiuto"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

        var accesa = new CheckBox { Content = "Accesa", IsChecked = installata.Accesa };
        accesa.Click += (_, _) =>
        {
            if (estensioni!.Accendi(m.Id, accesa.IsChecked == true) is { } errore)
                Registra($"{m.Nome} non si accende: {errore}", true);
        };
        corpo.Children.Add(accesa);

        foreach (var opzione in m.Opzioni)
        {
            var spunta = new CheckBox
            {
                Content = opzione.Etichetta,
                IsChecked = estensioni!.Opzione(installata, opzione.Chiave),
                IsEnabled = installata.Accesa,
                Margin = new Thickness(0, 10, 0, 0),
            };
            string chiave = opzione.Chiave;
            spunta.Click += (_, _) => estensioni!.ImpostaOpzione(m.Id, chiave, spunta.IsChecked == true);
            corpo.Children.Add(spunta);
        }

        corpo.Children.Add(Riga(installata.Stato, 14));
        if (installata.Accesa)
            corpo.Children.Add(Riga(
                !engine.Connected ? "Tablet non collegato: la sua parte arriva al prossimo collegamento."
                : installata.Tablet.Length > 0 ? installata.Tablet
                : "In attesa del tablet.", 4));

        var rimuovi = new Button
        {
            Content = "Rimuovi",
            Style = (Style)FindResource("Tenue"),
            MinWidth = 0,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        rimuovi.Click += (_, _) =>
        {
            var scelta = MessageBox.Show(this,
                $"Rimuovere {m.Nome}?\nSi toglie da questo PC e dal tablet, con le sue scelte.",
                "TabDeck", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (scelta == MessageBoxResult.Yes) estensioni!.Rimuovi(m.Id);
        };
        corpo.Children.Add(rimuovi);

        return new GroupBox
        {
            Header = m.Versione.Length > 0 ? $"{m.Nome.ToUpperInvariant()} · {m.Versione}" : m.Nome.ToUpperInvariant(),
            Content = corpo,
        };
    }

    private TextBlock Riga(string testo, double sopra) => new()
    {
        Text = testo,
        Style = (Style)FindResource("Aiuto"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, sopra, 0, 0),
    };

    private void InstallaEstensione_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Installa un'estensione",
            Filter = $"Estensione di TabDeck (*{GestoreEstensioni.EstensioneFile})|*{GestoreEstensioni.EstensioneFile}",
        };
        if (dialogo.ShowDialog(this) != true) return;

        try
        {
            estensioni!.Installa(dialogo.FileName);
        }
        catch (Exception errore)
        {
            // Il pacchetto viene da fuori: qualunque cosa abbia dentro, la finestra resta
            // in piedi e l'installazione di prima, se c'era, non e' stata toccata.
            Registra($"Estensione non installata: {errore.Message}", true);
            MessageBox.Show(this, $"L'estensione non si installa:\n{errore.Message}",
                "TabDeck", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
