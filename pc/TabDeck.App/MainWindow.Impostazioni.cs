using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TabDeck;

/// <summary>
/// La pagina Impostazioni, e le cose che TabDeck fa senza che si prema un
/// pulsante: aprirsi con Windows, collegarsi all'apertura o al cavo attaccato,
/// riprovare quando il collegamento cade, avvisare con le notifiche.
///
/// Sono tutte scelte di chi usa il programma, una spunta ciascuna, e nessuna
/// decide al posto suo: di partenza sono spente, e quello che fanno si legge
/// nel registro come quello che si fa a mano.
///
/// I tentativi automatici girano su un thread a parte. Quelli a mano restano
/// sul thread della finestra con la clessidra, come prima: chi ha premuto il
/// pulsante sta aspettando proprio quello. Un tentativo automatico che
/// bloccasse la finestra per un secondo e mezzo di ricerca in rete, ogni
/// quindici secondi, la renderebbe inservibile.
/// </summary>
public partial class MainWindow
{
    private const int PassoAttesaSecondi = 15;

    /// <summary>Quante volte si chiede ad adb, dopo che Windows ha visto un dispositivo nuovo.</summary>
    private const int ControlliCavo = 4;

    private enum Occasione { Apertura, Cavo, Riprova }

    private ComportamentoSettings Comportamento => settings.Comportamento;

    /// <summary>Riprova a collegarsi ogni <see cref="PassoAttesaSecondi"/>. Null finche' non serve.</summary>
    private DispatcherTimer? attesa;
    private string attesaTrasporto = "usb";

    /// <summary>
    /// Chiede ad adb se il tablet c'e', qualche volta di seguito: il cavo
    /// attaccato arriva a Windows subito, ad adb un paio di secondi dopo.
    /// </summary>
    private DispatcherTimer? controlloCavo;
    private int controlliCavoRestanti;
    private bool cavoInEsame;

    private bool tentativoInCorso;

    private bool InAttesa => attesa?.IsEnabled == true;

    // ---- apertura ----

    /// <summary>
    /// Fa vedere la finestra, o no, e fa partire il collegamento scelto.
    ///
    /// Nascosta solo se e' partita da sola all'accesso: aperta a mano, la
    /// finestra si vuole vedere. E solo se l'icona accanto all'orologio c'e',
    /// altrimenti non ci sarebbe niente da premere per farla comparire.
    /// </summary>
    public void Avvia(bool daAccesso)
    {
        bool nascosta = daAccesso && Comportamento.PartiNascosto && vassoio is { Acceso: true };
        if (nascosta)
            Registra("Partito all'accesso a Windows: la finestra resta accanto all'orologio.");
        else
            Show();

        // Quando la finestra ha finito di disegnarsi: prima si vede, poi si
        // cerca il tablet.
        if (Comportamento.CollegaAllApertura != "no")
        {
            Dispatcher.BeginInvoke(new Action(() => CollegaDaSolo(Comportamento.CollegaAllApertura, Occasione.Apertura)),
                DispatcherPriority.ApplicationIdle);
        }
    }

    private void PreparaImpostazioni()
    {
        engine.Caduto += CollegamentoCaduto;

        if (vassoio is not null)
        {
            vassoio.Impostazioni += ApriImpostazioni;
            vassoio.DispositiviCambiati += DispositiviCambiati;
        }

        bool prima = caricamento;
        caricamento = true;

        SpuntaAccesso.IsChecked = Accesso.Presente();
        SpuntaNascosto.IsChecked = Comportamento.PartiNascosto;
        ElencoApertura.SelectedIndex = Comportamento.CollegaAllApertura switch
        {
            "usb" => 1,
            "wifi" => 2,
            "auto" => 3,
            _ => 0,
        };
        SpuntaCavo.IsChecked = Comportamento.CollegaColCavo;
        SpuntaRiprova.IsChecked = Comportamento.Riprova;
        SpuntaChiamata.IsChecked = Comportamento.AccettaChiamata;
        SpuntaNotCollegato.IsChecked = Comportamento.NotificaCollegato;
        SpuntaNotCaduto.IsChecked = Comportamento.NotificaCaduto;
        SpuntaNotTentativi.IsChecked = Comportamento.NotificaTentativi;
        SpuntaNotNascosta.IsChecked = Comportamento.NotificheSoloNascosta;

        caricamento = prima;
        DiciAccesso();

        foreach (var spunta in new[]
                 {
                     SpuntaNascosto, SpuntaCavo, SpuntaRiprova, SpuntaChiamata,
                     SpuntaNotCollegato, SpuntaNotCaduto, SpuntaNotTentativi, SpuntaNotNascosta,
                 })
        {
            spunta.Click += (_, _) => SalvaComportamento();
        }
        ElencoApertura.SelectionChanged += (_, _) => { if (!caricamento) SalvaComportamento(); };
    }

    private void SalvaComportamento()
    {
        if (caricamento) return;

        Comportamento.PartiNascosto = SpuntaNascosto.IsChecked == true;
        Comportamento.CollegaAllApertura = ElencoApertura.SelectedIndex switch
        {
            1 => "usb",
            2 => "wifi",
            3 => "auto",
            _ => "no",
        };
        Comportamento.CollegaColCavo = SpuntaCavo.IsChecked == true;
        Comportamento.AccettaChiamata = SpuntaChiamata.IsChecked == true;
        Comportamento.NotificaCollegato = SpuntaNotCollegato.IsChecked == true;
        Comportamento.NotificaCaduto = SpuntaNotCaduto.IsChecked == true;
        Comportamento.NotificaTentativi = SpuntaNotTentativi.IsChecked == true;
        Comportamento.NotificheSoloNascosta = SpuntaNotNascosta.IsChecked == true;

        bool riprovava = Comportamento.Riprova;
        Comportamento.Riprova = SpuntaRiprova.IsChecked == true;
        // Spenta la spunta, i tentativi in corso si fermano adesso: lasciarli
        // andare fino al prossimo collegamento vorrebbe dire che la spunta non
        // fa quello che dice.
        if (riprovava && !Comportamento.Riprova && InAttesa)
        {
            FermaAttesa();
            Registra("Smesso di riprovare.");
        }

        SalvaImpostazioni();
    }

    private void ApriImpostazioni()
    {
        MostraFinestra();
        Schede.SelectedItem = SchedaImpostazioni;
    }

    // ---- avvio con Windows ----

    private void SpuntaAccesso_Click(object sender, RoutedEventArgs e)
    {
        bool voluto = SpuntaAccesso.IsChecked == true;

        Mouse.OverrideCursor = Cursors.Wait;
        string errore;
        try
        {
            errore = voluto ? Accesso.Metti() : Accesso.Togli();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (errore.Length > 0)
            Registra($"Avvio con Windows non cambiato: {errore}", true);
        else
            Registra(voluto
                ? $"TabDeck si aprira' all'accesso a Windows (attivita' « {Accesso.NomeAttivita} »)."
                : "TabDeck non si apre piu' all'accesso a Windows.");

        // Si rilegge invece di fidarsi della spunta: se schtasks ha detto no,
        // la spunta deve tornare com'era.
        SpuntaAccesso.IsChecked = Accesso.Presente();
        DiciAccesso();
    }

    private void DiciAccesso()
    {
        bool acceso = SpuntaAccesso.IsChecked == true;
        SpuntaNascosto.IsEnabled = acceso;
        TestoAccesso.Text = acceso
            ? $"Parte dieci secondi dopo l'accesso, gia' con i permessi pieni: nessuna domanda di Windows. "
              + $"E' l'attivita' « {Accesso.NomeAttivita} » nell'Utilita' di pianificazione, e togliendo la spunta sparisce."
            : "TabDeck si apre quando lo apri tu. Con la spunta, Windows lo avvia all'accesso con un'attivita' "
              + "pianificata: la chiave Run non basterebbe, perche' salta i programmi che chiedono l'amministratore.";
    }

    // ---- notifiche ----

    /// <param name="voluta">La spunta di quella notifica.</param>
    /// <param name="guaio">Icona gialla: qualcosa non e' andato.</param>
    private void Notifica(bool voluta, string titolo, string testo, bool guaio = false)
    {
        if (!voluta || vassoio is not { Acceso: true }) return;

        bool davanti = IsVisible && WindowState != WindowState.Minimized;
        if (Comportamento.NotificheSoloNascosta && davanti) return;

        vassoio.Avviso(titolo, testo, guaio);
    }

    private void ProvaNotifica_Click(object sender, RoutedEventArgs e)
    {
        if (vassoio is not { Acceso: true })
        {
            Registra("L'area di notifica non ha accettato l'icona: senza icona Windows non mostra notifiche.", true);
            return;
        }
        vassoio.Avviso("TabDeck", "Le notifiche arrivano. Premendone una si apre la finestra.");
        Registra("Notifica di prova mandata.");
    }

    // ---- tentativi automatici ----

    private static string Strada(string trasporto) => trasporto switch
    {
        "usb" => "col cavo",
        "wifi" => "in rete",
        _ => "col cavo o in rete",
    };

    /// <summary>
    /// Un tentativo che nessuno ha premuto. Uno alla volta, e mai se il
    /// collegamento c'e' gia'.
    /// </summary>
    private async void CollegaDaSolo(string trasporto, Occasione occasione)
    {
        if (tentativoInCorso || engine.Connected) return;
        tentativoInCorso = true;

        string errore;
        try
        {
            bool silenzioso = occasione == Occasione.Riprova;
            errore = await Task.Run(() => ProvaStrade(trasporto, silenzioso));
        }
        finally
        {
            tentativoInCorso = false;
        }

        if (errore.Length == 0)
        {
            // Avvisi e fine dei tentativi li fa CollegamentoCambiato, che
            // arriva anche per i collegamenti a mano.
            AllineaIndirizzo();
            return;
        }

        // Chi riprova ogni quindici secondi non scrive a ogni giro: « non
        // risponde » cento volte in fila non dice niente di nuovo.
        if (occasione == Occasione.Riprova) return;

        string come = occasione == Occasione.Apertura ? "all'apertura" : "al cavo attaccato";
        Registra($"Collegamento {come} non riuscito: {errore}", true);
        Notifica(Comportamento.NotificaTentativi, "TabDeck non si e' collegato", errore, true);

        if (Comportamento.Riprova) CominciaAttesa(trasporto);
    }

    /// <summary>Thread a parte. "auto" vuol dire prima il cavo, e se non va la rete.</summary>
    private string ProvaStrade(string trasporto, bool silenzioso)
    {
        if (trasporto != "auto") return engine.Connect(trasporto, adbPath, silenzioso);

        string cavo = engine.Connect("usb", adbPath, silenzioso);
        if (cavo.Length == 0) return "";

        string rete = engine.Connect("wifi", adbPath, silenzioso);
        return rete.Length == 0 ? "" : $"col cavo, {cavo}; in rete, {rete}";
    }

    /// <summary>
    /// Collegandosi in rete il motore puo' aver trovato il tablet a un
    /// indirizzo nuovo: va rimesso nel campo, altrimenti il campo racconta
    /// quello vecchio e al prossimo salvataggio lo riscrive nel file.
    /// </summary>
    private void AllineaIndirizzo()
    {
        if (CampoIndirizzo.Text.Equals(settings.Host, StringComparison.Ordinal)) return;
        caricamento = true;
        CampoIndirizzo.Text = settings.Host;
        caricamento = false;
        SalvaImpostazioni();
    }

    /// <summary>Il motore ha perso il tablet senza che nessuno premesse « Scollega ».</summary>
    private void CollegamentoCaduto()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => CollegamentoCaduto());
            return;
        }

        Notifica(Comportamento.NotificaCaduto, "Tablet scollegato",
            Comportamento.Riprova
                ? $"Il collegamento e' caduto. Riprovo ogni {PassoAttesaSecondi} secondi."
                : "Il collegamento e' caduto.",
            true);

        if (Comportamento.Riprova) CominciaAttesa(settings.Transport);
    }

    private void CominciaAttesa(string trasporto)
    {
        attesaTrasporto = trasporto;

        if (attesa is null)
        {
            attesa = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PassoAttesaSecondi) };
            attesa.Tick += (_, _) =>
            {
                if (engine.Connected) FermaAttesa();
                else CollegaDaSolo(attesaTrasporto, Occasione.Riprova);
            };
        }

        if (!attesa.IsEnabled)
        {
            attesa.Start();
            Registra($"Riprovo a collegarmi {Strada(trasporto)} ogni {PassoAttesaSecondi} secondi.");
        }
        AggiornaAttesa();
    }

    private void FermaAttesa()
    {
        if (!InAttesa) return;
        attesa!.Stop();
        AggiornaAttesa();
    }

    /// <summary>
    /// La dashboard mentre si aspetta il tablet: lo dice, e il pulsante che a
    /// collegamento aperto e' « Scollega » diventa il modo di smettere.
    /// </summary>
    private void AggiornaAttesa()
    {
        if (engine.Connected) return;

        BtnScollega.Visibility = InAttesa ? Visibility.Visible : Visibility.Collapsed;
        BtnScollega.Content = InAttesa ? "Smetti di riprovare" : "Scollega";
        if (InAttesa)
        {
            DashboardDettaglio.Text = $"Aspetto il tablet: riprovo {Strada(attesaTrasporto)} ogni "
                                      + $"{PassoAttesaSecondi} secondi. Puoi anche collegarlo a mano.";
        }
    }

    // ---- il cavo ----

    /// <summary>
    /// Windows ha visto comparire o sparire un dispositivo. Non dice quale, e
    /// potrebbe essere un mouse: lo si chiede ad adb, ma solo adesso e solo
    /// qualche volta, invece che di continuo.
    /// </summary>
    private void DispositiviCambiati()
    {
        if (!Comportamento.CollegaColCavo || engine.Connected) return;

        controlliCavoRestanti = ControlliCavo;
        if (controlloCavo is null)
        {
            controlloCavo = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            controlloCavo.Tick += (_, _) => ControllaCavo();
        }
        controlloCavo.Start();
    }

    private async void ControllaCavo()
    {
        if (engine.Connected || controlliCavoRestanti <= 0)
        {
            controlloCavo!.Stop();
            return;
        }
        // adb che parte per la prima volta ci mette piu' di un giro: senza
        // questo, i controlli si accavallerebbero.
        if (cavoInEsame || tentativoInCorso) return;

        controlliCavoRestanti--;
        cavoInEsame = true;
        bool visto;
        try
        {
            visto = await Task.Run(() => Link.UsbDevices(adbPath).Count > 0);
        }
        finally
        {
            cavoInEsame = false;
        }

        if (!visto || engine.Connected) return;

        controlloCavo!.Stop();
        Registra("Cavo attaccato: mi collego.");
        CollegaDaSolo("usb", Occasione.Cavo);
    }
}
