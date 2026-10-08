using System.Windows;

namespace TabDeck;

/// <summary>
/// L'avvio, e la regola che ne esce una sola.
///
/// Da quando la croce ritira la finestra nell'area di notifica invece di
/// chiudere, il collegamento sopravvive alla finestra — e il collegamento e'
/// una cosa sola: una porta, un tablet, uno schermo virtuale. Premendo il
/// collegamento sul desktop mentre TabDeck e' gia' li' nascosto, una seconda
/// copia non sarebbe una seconda finestra della stessa cosa: sarebbe un
/// secondo programma che prova a prendersi la porta 8767 gia' occupata, a
/// mandare il suo deck allo stesso tablet, e ad accendere un altro schermo
/// virtuale. Quindi non parte: dice a quella che c'e' gia' di farsi vedere, e
/// se ne va.
///
/// Il posto di questa regola e' qui e non nella finestra: quando si scopre di
/// essere di troppo, la finestra non dev'essere ancora nata — costruirla vuol
/// dire aprire le socket e leggere la configurazione, cioe' fare proprio il
/// danno che si sta cercando di evitare. Per questo <c>StartupUri</c> non c'e'
/// piu' in App.xaml: la finestra la apre la riga qui sotto, dopo il
/// controllo.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Il nome e' locale alla sessione: due utenti diversi collegati alla
    /// stessa macchina hanno due tablet, due configurazioni e due scrivanie,
    /// e non c'e' ragione che si diano fastidio.
    /// </summary>
    private const string UnaSola = @"Local\TabDeck.unaSola";

    /// <summary>
    /// Tenuto in un campo per tutta la vita del programma: un Mutex locale che
    /// il raccoglitore di rifiuti porta via e' un Mutex rilasciato, e da quel
    /// momento la seconda copia partirebbe.
    /// </summary>
    private Mutex? presidio;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        presidio = new Mutex(true, UnaSola, out bool nostro);
        if (!nostro)
        {
            Vassoio.ChiediDiMostrarsi();
            Shutdown();
            return;
        }

        // Partito dall'attivita' dell'accesso a Windows: lo dice l'argomento,
        // e la finestra decide se farsi vedere o restare accanto all'orologio.
        bool daAccesso = e.Args.Any(
            a => string.Equals(a, Accesso.Argomento, StringComparison.OrdinalIgnoreCase));
        new MainWindow().Avvia(daAccesso);
    }
}
