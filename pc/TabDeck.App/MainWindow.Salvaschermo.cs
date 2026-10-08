using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TabDeck;

/// <summary>
/// La pagina Salvaschermo: le scelte, l'anteprima, Bing e la galleria.
///
/// Ogni scelta si salva e parte subito verso il tablet: non c'e' un « Applica »,
/// perche' l'anteprima mostra gia' com'e' e il tablet la segue da solo.
/// </summary>
public partial class MainWindow
{
    private static readonly int[] MinutiDopo = { 1, 2, 5, 10, 15, 30 };
    private static readonly int[] SecondiOgni = { 20, 60, 120, 300, 900 };
    private static readonly string[] Stili = { "foto", "aurora", "orologio" };
    private static readonly string[] Fonti = { "bing", "galleria" };
    private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");

    private Salvaschermo? salvaschermo;

    /// <summary>L'ora dell'anteprima: batte solo mentre la pagina si vede.</summary>
    private readonly DispatcherTimer oraAnteprima = new() { Interval = TimeSpan.FromSeconds(15) };

    private void PreparaSalvaschermo()
    {
        salvaschermo = new Salvaschermo(engine, settings, SalvaImpostazioni, Path.Combine(configDir, "salvaschermo"));
        salvaschermo.Log += messaggio => Registra(messaggio);
        salvaschermo.Cambiato += () => Dispatcher.BeginInvoke(() => MostraFotoSalvaschermo());

        var s = settings.Salvaschermo;
        bool prima = caricamento;
        caricamento = true;
        SpuntaSalvaschermo.IsChecked = s.Acceso;
        ElencoDopo.SelectedIndex = Posizione(MinutiDopo, s.DopoMinuti, 2);
        ElencoStile.SelectedIndex = Math.Max(0, Array.IndexOf(Stili, s.Stile));
        ElencoFonte.SelectedIndex = Math.Max(0, Array.IndexOf(Fonti, s.Fonte));
        ElencoOgni.SelectedIndex = Posizione(SecondiOgni, s.OgniSecondi, 1);
        SpuntaSsOra.IsChecked = s.Ora;
        SpuntaSsData.IsChecked = s.Data;
        CursoreVelo.Value = s.Attenuazione;
        SpuntaSsPannello.IsChecked = s.PannelloAcceso;
        caricamento = prima;

        foreach (var spunta in new[] { SpuntaSalvaschermo, SpuntaSsOra, SpuntaSsData, SpuntaSsPannello })
            spunta.Click += (_, _) => ScegliSalvaschermo();
        foreach (var elenco in new[] { ElencoDopo, ElencoStile, ElencoFonte, ElencoOgni })
            elenco.SelectionChanged += (_, _) => ScegliSalvaschermo();
        CursoreVelo.ValueChanged += (_, _) => ScegliSalvaschermo();

        oraAnteprima.Tick += (_, _) => OraAnteprima();
        AnteprimaOra.IsVisibleChanged += (_, _) =>
        {
            if (AnteprimaOra.IsVisible)
            {
                OraAnteprima();
                oraAnteprima.Start();
            }
            else
            {
                oraAnteprima.Stop();
            }
        };

        MostraFotoSalvaschermo();
    }

    private static int Posizione(int[] valori, int valore, int altrimenti)
    {
        int i = Array.IndexOf(valori, valore);
        return i >= 0 ? i : altrimenti;
    }

    private void ScegliSalvaschermo()
    {
        if (caricamento || salvaschermo is null) return;
        var s = settings.Salvaschermo;
        s.Acceso = SpuntaSalvaschermo.IsChecked == true;
        s.DopoMinuti = MinutiDopo[Math.Max(0, ElencoDopo.SelectedIndex)];
        s.Stile = Stili[Math.Max(0, ElencoStile.SelectedIndex)];
        s.Fonte = Fonti[Math.Max(0, ElencoFonte.SelectedIndex)];
        s.OgniSecondi = SecondiOgni[Math.Max(0, ElencoOgni.SelectedIndex)];
        s.Ora = SpuntaSsOra.IsChecked == true;
        s.Data = SpuntaSsData.IsChecked == true;
        s.Attenuazione = (int)CursoreVelo.Value;
        s.PannelloAcceso = SpuntaSsPannello.IsChecked == true;
        SalvaImpostazioni();
        salvaschermo.Manda();
        AnteprimaSalvaschermo();
    }

    private void MostraFotoSalvaschermo()
    {
        if (salvaschermo is null) return;

        var bing = salvaschermo.Elenco("bing");
        var aggiornato = settings.Salvaschermo.BingAggiornato;
        TestoBing.Text = bing.Count == 0
            ? "Nessuna immagine ancora scaricata."
            : $"{bing.Count} immagini" + (aggiornato == default ? "." : $", aggiornate {aggiornato.ToString("d MMMM 'alle' HH:mm", Italiano)}.");

        ElencoGalleria.Children.Clear();
        var galleria = salvaschermo.Elenco("galleria");
        TestoGalleria.Text = galleria.Count switch
        {
            0 => "Nessuna immagine.",
            1 => "Un'immagine.",
            _ => $"{galleria.Count} immagini.",
        };
        foreach (var foto in galleria) ElencoGalleria.Children.Add(Miniatura(foto));

        AnteprimaSalvaschermo();
    }

    private FrameworkElement Miniatura(Salvaschermo.Foto foto)
    {
        var togli = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("Tenue"),
            MinWidth = 0,
            Padding = new Thickness(7, 2, 7, 3),
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Togli dalla galleria",
        };
        togli.Click += (_, _) => salvaschermo?.Togli(foto.Nome);

        var strato = new Grid();
        strato.Children.Add(new Image { Source = Leggi(foto.Percorso, 320), Stretch = Stretch.UniformToFill });
        strato.Children.Add(togli);
        return new Border
        {
            Width = 160,
            Height = 94,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 10, 10),
            Background = (Brush)FindResource("Campo"),
            Child = strato,
        };
    }

    /// <summary>Letta in memoria: il file resta libero, e « Togli » lo puo' cancellare.</summary>
    private static BitmapImage? Leggi(string percorso, int larghezza)
    {
        try
        {
            var immagine = new BitmapImage();
            immagine.BeginInit();
            immagine.CacheOption = BitmapCacheOption.OnLoad;
            immagine.DecodePixelWidth = larghezza;
            immagine.UriSource = new Uri(percorso);
            immagine.EndInit();
            immagine.Freeze();
            return immagine;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Lo stesso disegno del tablet: stile, prima foto, velo, ora e data.</summary>
    private void AnteprimaSalvaschermo()
    {
        if (salvaschermo is null) return;
        var s = settings.Salvaschermo;
        var foto = salvaschermo.Elenco(s.Fonte);
        string stile = s.Stile == "foto" && foto.Count == 0 ? "aurora" : s.Stile;

        bool conFoto = stile == "foto";
        AnteprimaAurora.Visibility = stile == "aurora" ? Visibility.Visible : Visibility.Collapsed;
        AnteprimaFoto.Source = conFoto ? Leggi(foto[0].Percorso, 1024) : null;
        AnteprimaScrim.Visibility = conFoto ? Visibility.Visible : Visibility.Collapsed;
        AnteprimaDidascalia.Text = conFoto ? foto[0].Didascalia : "";
        AnteprimaDidascalia.Visibility = AnteprimaDidascalia.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AnteprimaVelo.Opacity = s.Attenuazione / 100.0;
        AnteprimaOra.Visibility = s.Ora ? Visibility.Visible : Visibility.Collapsed;
        AnteprimaData.Visibility = s.Data ? Visibility.Visible : Visibility.Collapsed;
        TestoVelo.Text = $"{s.Attenuazione}%";

        ElencoFonte.IsEnabled = s.Stile == "foto";
        ElencoOgni.IsEnabled = s.Stile == "foto";
        OraAnteprima();
    }

    private void OraAnteprima()
    {
        var adesso = DateTime.Now;
        AnteprimaOra.Text = adesso.ToString("HH:mm", Italiano);
        string giorno = adesso.ToString("dddd d MMMM", Italiano);
        AnteprimaData.Text = char.ToUpper(giorno[0], Italiano) + giorno[1..];
    }

    private void ProvaSalvaschermo_Click(object sender, RoutedEventArgs e)
    {
        if (!engine.Connected)
        {
            Registra("Il salvaschermo si prova col tablet collegato.", true);
            return;
        }
        salvaschermo?.Manda();
        engine.SendCommand("salvaschermo");
        Registra("Salvaschermo avviato sul tablet: un tocco lo chiude.");
    }

    private async void AggiornaBing_Click(object sender, RoutedEventArgs e)
    {
        if (salvaschermo is null) return;
        PulsanteBing.IsEnabled = false;
        TestoBing.Text = "Scarico da Bing.";
        try
        {
            string esito = await salvaschermo.AggiornaBing(true);
            MostraFotoSalvaschermo();
            if (esito.StartsWith("Bing non", StringComparison.Ordinal)) TestoBing.Text = esito;
        }
        finally
        {
            PulsanteBing.IsEnabled = true;
        }
    }

    private async void AggiungiFoto_Click(object sender, RoutedEventArgs e)
    {
        if (salvaschermo is null) return;
        var dialogo = new OpenFileDialog
        {
            Title = "Aggiungi immagini al salvaschermo",
            Filter = "Immagini|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic",
            Multiselect = true,
        };
        if (dialogo.ShowDialog(this) != true) return;

        TestoGalleria.Text = "Preparo le immagini.";
        var scartate = new List<string>();
        foreach (string file in dialogo.FileNames)
        {
            try
            {
                await Task.Run(() => salvaschermo.Aggiungi(file));
            }
            catch (Exception errore) when (errore is IOException or NotSupportedException or ArgumentException
                                               or InvalidOperationException or FileFormatException or UnauthorizedAccessException)
            {
                scartate.Add($"{Path.GetFileName(file)} ({errore.Message})");
            }
        }
        if (scartate.Count > 0) Registra("Immagini non lette: " + string.Join(", ", scartate), true);
        MostraFotoSalvaschermo();
        if (settings.Salvaschermo.Fonte == "galleria") salvaschermo.Manda();
    }
}
