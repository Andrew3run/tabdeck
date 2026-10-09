using System.Windows;

namespace TabDeck;

/// <summary>
/// Internet al tablet attraverso il PC, col cavo: la scelta in Gestione › Tablet.
///
/// « Solo TabDeck » non ha niente da accendere qui: il tablet lo sa dal frame CONFIG, e le
/// pagine che chiede le scarica <see cref="Engine"/>. « Tutto il tablet » vuole il secondo
/// canale (<see cref="Rete"/>), che sta acceso solo mentre si e' collegati col cavo.
/// </summary>
public partial class MainWindow
{
    private Rete? rete;

    private void PreparaInternet()
    {
        rete = new Rete(adbPath);
        rete.Log += messaggio => Registra(messaggio);
        rete.StatoCambiato += () => Dispatcher.BeginInvoke(MostraInternet);
        AggiornaInternet();
    }

    /// <summary>Accende o spegne il secondo canale secondo la scelta e il collegamento.</summary>
    private void AggiornaInternet()
    {
        if (rete is null) return;
        bool serve = settings.Tablet.Internet == "tutto"
                     && engine.Connected
                     && engine.Transport.Equals("usb", StringComparison.OrdinalIgnoreCase);
        if (serve) rete.Avvia();
        else rete.Ferma();
        MostraInternet();
    }

    private void MostraInternet()
    {
        if (rete is null) return;
        string testo = settings.Tablet.Internet switch
        {
            "tutto" when !engine.Connected || !engine.Transport.Equals("usb", StringComparison.OrdinalIgnoreCase)
                => "Parte quando il tablet e' collegato col cavo.",
            "tutto" => rete.Stato,
            _ => "",
        };
        TestoInternet.Text = testo;
        TestoInternet.Visibility = testo.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
