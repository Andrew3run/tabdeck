using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TabDeck;

/// <summary>
/// Le luci di casa, in due pagine diverse perche' sono due mestieri diversi.
///
/// In « Gestione › Luci » si **configura**: che luci ci sono, come si chiamano,
/// che faccia hanno sul tablet, che routine esistono. In « Uso › Casa » c'e' il
/// **pannello**, e li' si accende davvero: il PC parla alle lampade sulla rete,
/// con lo stesso protocollo del tablet. Le routine compaiono anche sulla
/// dashboard, che e' il posto dove si passa comunque.
///
/// Il pannello serve perche' una routine si scrive alla cieca, e l'unico modo
/// di sapere se « Film » fa la luce giusta e' vederla accendersi mentre la si
/// prepara - senza alzarsi e senza passare dal tablet.
///
/// Il tablet resta comunque autonomo: qui si prepara e si prova, li' si usa
/// tutti i giorni, anche a PC spento.
/// </summary>
public partial class MainWindow
{
    private readonly string luciPath = "";
    private LuciConfig luci = new();

    /// <summary>Le lampade si annunciano da sole qui, come il tablet sulla 8766.</summary>
    private static readonly int[] PorteAnnunci = { 6666, 6667 };

    /// <summary>
    /// La chiave degli annunci e' la stessa per tutte le lampade Tuya del
    /// mondo: non protegge niente, scarta solo chi parla d'altro.
    /// </summary>
    private const string ChiaveAnnunci = "yGAdlopoPVldABfn";

    /// <summary>Le azioni di un passo, e come si chiamano nella finestra.</summary>
    private static readonly (string Codice, string Nome)[] AzioniLuce =
    {
        ("off", "Spegni"),
        ("on", "Accendi"),
        ("inverti", "Inverti"),
        ("luce", "Luminosita'"),
        ("colore", "Colore"),
        ("bianco", "Bianco"),
        ("bianchezza", "Bianco caldo/freddo"),
        // Non tocca lampade: « spegni la plafoniera, aspetta un minuto, spegni
        // il comodino ». Un tablet che non sa aspettare costringe a due scene.
        ("attesa", "Aspetta"),
    };

    /// <summary>
    /// Le tinte pronte del pannello, le stesse che il tablet mostra nel
    /// dettaglio di una lampada: quel che si prova qui e' quel che si ritrova
    /// li'. Dodici perche' sei erano poche per scegliere davvero.
    /// </summary>
    private static readonly string[] TinteLuce =
    {
        "FF2D2D", "FF6A00", "FFB300", "FFE94D", "8CD832", "2ED573",
        "1ABC9C", "00C2FF", "2E7CF6", "6C5CE7", "B14BFF", "FF4FA3",
    };

    private static readonly int[] LivelliLuce = { 10, 25, 40, 55, 70, 85, 100 };

    /// <summary>Il bianco: caldo, neutro, freddo.</summary>
    private static readonly (int Quanto, string Nome)[] Bianchi =
    {
        (0, "Caldo"), (50, "Neutro"), (100, "Freddo"),
    };

    private readonly List<ToggleButton> gettoniGlifoCasa = new();
    private readonly List<ToggleButton> gettoniColoreCasa = new();

    /// <summary>Un canale per lampada, riusato: dentro tiene la mappa dei numeri.</summary>
    private readonly Dictionary<string, TuyaLan> canali = new();

    /// <summary>Vero mentre i campi vengono riempiti: non si risalva quel che si legge.</summary>
    private bool scrivendo;

    // ---- montaggio ----

    private void PreparaCasa()
    {
        // Se la finestra si era chiusa con delle luci mai mandate, lo dice
        // subito: e' una cosa che si scoprirebbe altrimenti solo premendo un
        // pulsante sul tablet e vedendolo fare quel che faceva ieri.
        TestoLuciStato.Text = settings.LuciDaMandare ? "Cambiate qui, non ancora sul tablet." : "";
        TestoLuciStato.Foreground = (Brush)FindResource(settings.LuciDaMandare ? "Attenzione" : "TestoTenue");

        foreach (var glifo in Glifi)
        {
            string quale = glifo;
            var gettone = new ToggleButton
            {
                Content = quale,
                Style = (Style)FindResource("Gettone"),
                ToolTip = quale,
            };
            gettone.Click += (_, _) => CampoGlifoVoce.Text = quale;
            gettoniGlifoCasa.Add(gettone);
            PannelloGlifiCasa.Children.Add(gettone);
        }
        foreach (var colore in Colori)
        {
            string quale = colore;
            var gettone = new ToggleButton
            {
                Style = (Style)FindResource("GettoneColore"),
                Background = new SolidColorBrush(AnteprimaDeck.Tinta(quale)),
                Width = 28,
                ToolTip = quale,
            };
            gettone.Click += (_, _) => CampoColoreVoce.Text = quale;
            gettoniColoreCasa.Add(gettone);
            PannelloColoriCasa.Children.Add(gettone);
        }

        foreach (var (_, nome) in AzioniLuce) SceltaAzionePasso.Items.Add(nome);

        scrivendo = true;
        CampoCodiceUtente.Text = luci.CodiceUtente;
        scrivendo = false;

        RiempiElencoCasa(0);
        CostruisciPannello();
    }

    // ---- elenco a sinistra ----

    /// <summary>
    /// Un elenco solo per lampade e routine. Erano due liste, ed erano due
    /// selezioni da tenere allineate per mostrare un editor alla volta: cosi'
    /// la voce scelta e' sempre una, e l'editor sotto si adatta.
    /// </summary>
    private void RiempiElencoCasa(int scelto)
    {
        scrivendo = true;
        ElencoCasa.Items.Clear();
        foreach (var l in luci.Luci) ElencoCasa.Items.Add(RigaLuce(l));
        foreach (var r in luci.Routine) ElencoCasa.Items.Add(RigaRoutine(r));
        scrivendo = false;

        if (ElencoCasa.Items.Count > 0)
            ElencoCasa.SelectedIndex = Math.Clamp(scelto, 0, ElencoCasa.Items.Count - 1);
        else
            MostraVoce();
    }

    private static string RigaLuce(LuceSpec l) =>
        $"{Segno(l.Glifo)}  {(l.Nome.Length > 0 ? l.Nome : l.Ip)}"
        + (l.Stanza.Length > 0 ? $"   ·  {l.Stanza}" : "");

    private static string RigaRoutine(RoutineSpec r)
    {
        string tipo = r.Scena ? "scena" : "routine";
        return $"{Segno(r.Glifo)}  {(r.Nome.Length > 0 ? r.Nome : tipo)}   ·  {tipo}";
    }

    private static string Segno(string glifo) => glifo.Length > 0 ? glifo : "·";

    private LuceSpec? LuceScelta =>
        ElencoCasa.SelectedIndex >= 0 && ElencoCasa.SelectedIndex < luci.Luci.Count
            ? luci.Luci[ElencoCasa.SelectedIndex]
            : null;

    private RoutineSpec? RoutineScelta
    {
        get
        {
            int i = ElencoCasa.SelectedIndex - luci.Luci.Count;
            return i >= 0 && i < luci.Routine.Count ? luci.Routine[i] : null;
        }
    }

    /// <summary>
    /// Riscrivere la riga scelta la toglie e la rimette, e questo conta come un
    /// cambio di selezione: senza questa guardia i campi venivano riletti a
    /// ogni lettera battuta, il cursore tornava a inizio riga e il nome si
    /// scriveva al contrario.
    /// </summary>
    private void ElencoCasa_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (scrivendo) return;
        MostraVoce();
    }

    private void MostraVoce()
    {
        var luce = LuceScelta;
        var routine = RoutineScelta;

        PannelloVoce.IsEnabled = luce is not null || routine is not null;
        RiquadroLampada.Visibility = luce is not null ? Visibility.Visible : Visibility.Collapsed;
        RigaStanza.Visibility = RiquadroLampada.Visibility;
        PannelloStanzeCasa.Visibility = RiquadroLampada.Visibility;
        RiquadroPassiCasa.Visibility = routine is not null ? Visibility.Visible : Visibility.Collapsed;
        RiquadroPassiCasa.Header = routine?.Scena == true ? "PASSI DELLA SCENA" : "PASSI DELLA ROUTINE";
        TestoCattura.Text = "";

        scrivendo = true;
        CampoNomeVoce.Text = luce?.Nome ?? routine?.Nome ?? "";
        CampoGlifoVoce.Text = luce?.Glifo ?? routine?.Glifo ?? "";
        CampoColoreVoce.Text = luce?.Colore ?? routine?.Colore ?? "";
        CampoStanzaVoce.Text = luce?.Stanza ?? "";
        CampoIpLuce.Text = luce?.Ip ?? "";
        CampoChiaveLuce.Text = luce?.Chiave ?? "";
        TestoProtocollo.Text = luce is null ? "" : $"protocollo {luce.Versione}";
        TestoChiaveLuce.Text = luce is null ? "" : SpiegaChiave(luce);
        scrivendo = false;

        SegnaGettoni();
        RiempiStanze();
        RiempiPassi(routine, 0);
    }

    private static string SpiegaChiave(LuceSpec l) => l.Chiave.Length switch
    {
        16 => "Chiave a posto.",
        0 => "Senza chiave la lampada non risponde a nessuno: premi « Rileva chiavi ».",
        _ => $"La chiave e' di 16 caratteri, questa ne ha {l.Chiave.Length}.",
    };

    private void SegnaGettoni()
    {
        foreach (var g in gettoniGlifoCasa)
            g.IsChecked = (string?)g.Content == CampoGlifoVoce.Text;
        foreach (var g in gettoniColoreCasa)
            g.IsChecked = string.Equals((string?)g.ToolTip, CampoColoreVoce.Text,
                StringComparison.OrdinalIgnoreCase);
    }

    // ---- editor della voce ----

    private void CasaVoce_Changed(object sender, TextChangedEventArgs e)
    {
        if (scrivendo) return;

        if (LuceScelta is { } l)
        {
            l.Nome = CampoNomeVoce.Text.Trim();
            l.Glifo = CampoGlifoVoce.Text;
            l.Colore = CampoColoreVoce.Text.Trim();
            l.Ip = CampoIpLuce.Text.Trim();
            l.Chiave = CampoChiaveLuce.Text.Trim();
            l.Stanza = CampoStanzaVoce.Text.Trim();
            RiempiStanze();
            TestoChiaveLuce.Text = SpiegaChiave(l);
            // Cambiando indirizzo o chiave, il canale di prima non vale piu'.
            canali.Remove(l.Id);
        }
        else if (RoutineScelta is { } r)
        {
            r.Nome = CampoNomeVoce.Text.Trim();
            r.Glifo = CampoGlifoVoce.Text;
            r.Colore = CampoColoreVoce.Text.Trim();
        }
        else
        {
            return;
        }

        SegnaGettoni();
        RinominaVoce();
        SalvaLuci();
        CostruisciPannello();
    }

    /// <summary>
    /// Riscrive solo la riga scelta invece di rifare la lista: rifarla a ogni
    /// tasto premuto la farebbe lampeggiare e sposterebbe il cursore.
    /// </summary>
    private void RinominaVoce()
    {
        int i = ElencoCasa.SelectedIndex;
        if (i < 0) return;
        string testo = LuceScelta is { } l ? RigaLuce(l)
                     : RoutineScelta is { } r ? RigaRoutine(r)
                     : "";
        scrivendo = true;
        ElencoCasa.Items[i] = testo;
        ElencoCasa.SelectedIndex = i;
        scrivendo = false;
    }

    private void CasaNuovaRoutine_Click(object sender, RoutedEventArgs e)
    {
        luci.Routine.Add(new RoutineSpec { Nome = NomeLibero("Nuova routine") });
        SalvaLuci();
        RiempiElencoCasa(luci.Luci.Count + luci.Routine.Count - 1);
        CostruisciPannello();
    }

    /// <summary>
    /// Una scena nasce gia' piena: le luci come sono adesso. E' il modo in cui una
    /// scena si pensa — « cosi' va bene, tienimela » — invece di scriverla passo a passo.
    /// </summary>
    private async void CasaNuovaScena_Click(object sender, RoutedEventArgs e)
    {
        var scena = new RoutineSpec { Nome = NomeLibero("Nuova scena"), Scena = true, Glifo = "◐", Colore = "#3B4E9B" };
        luci.Routine.Add(scena);
        SalvaLuci();
        RiempiElencoCasa(luci.Luci.Count + luci.Routine.Count - 1);
        CostruisciPannello();
        if (luci.Luci.Any(l => l.Completa)) await Cattura(scena);
    }

    /// <summary>Il nome deve restare unico: e' con il nome che una sveglia del tablet dice cosa far partire.</summary>
    private string NomeLibero(string radice)
    {
        string nome = radice;
        for (int n = 2; luci.Routine.Any(x => string.Equals(x.Nome, nome, StringComparison.OrdinalIgnoreCase)); n++)
            nome = $"{radice} {n}";
        return nome;
    }

    private async void CasaCattura_Click(object sender, RoutedEventArgs e)
    {
        if (RoutineScelta is not { } r) return;
        if (!r.Scena && r.Passi.Count > 0 && MessageBox.Show(this,
                "Sostituire i passi di questa routine con le luci com'e' adesso?\nLe attese e l'ordine di adesso si perdono.",
                "TabDeck", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await Cattura(r);
    }

    /// <summary>
    /// Fa di una voce lo stato delle luci di adesso: accesa o spenta, e la
    /// luminosita'. Il colore no: lo stato letto dalle lampade non lo porta, e
    /// inventarlo sarebbe peggio che lasciarlo fuori.
    /// </summary>
    private async Task Cattura(RoutineSpec r)
    {
        PulsanteCattura.IsEnabled = false;
        TestoCattura.Text = "Leggo le luci...";
        var passi = new List<PassoLuce>();
        var mute = new List<string>();
        int prese = 0;
        // Una ricerca in rete per cattura, alla prima lampada muta, come quando si esegue una routine.
        bool ritrovate = false;

        foreach (var l in luci.Luci.Where(x => x.Completa).ToList())
        {
            if (Canale(l) is not { } c) continue;
            var s = await c.Leggi();
            if (!s.Raggiunta && !ritrovate)
            {
                ritrovate = true;
                string prima = l.Ip;
                await RitrovaLampade(aggiungiNuove: false);
                if (l.Ip != prima && Canale(l) is { } ritrovata) s = await ritrovata.Leggi();
            }

            if (!s.Raggiunta)
            {
                mute.Add(l.Nome.Length > 0 ? l.Nome : l.Ip);
                continue;
            }
            prese++;
            if (!s.Accesa)
            {
                passi.Add(new PassoLuce { Luce = l.Id, Azione = "off" });
                continue;
            }
            passi.Add(new PassoLuce { Luce = l.Id, Azione = "on" });
            if (s.HaLuminosita && s.Luminosita > 0)
                passi.Add(new PassoLuce { Luce = l.Id, Azione = "luce", Valore = s.Luminosita.ToString() });
        }
        PulsanteCattura.IsEnabled = true;

        if (prese == 0)
        {
            TestoCattura.Text = mute.Count == 0
                ? "Nessuna luce con la chiave da leggere."
                : "Nessuna luce ha risposto: la voce resta com'era.";
            return;
        }

        r.Passi = passi;
        SalvaLuci();
        if (RoutineScelta == r) RiempiPassi(r, 0);
        CostruisciPannello();
        TestoCattura.Text = (prese == 1 ? "Presa una luce" : $"Prese {prese} luci") + ": accesa o spenta, e luminosita'."
            + (mute.Count > 0 ? $" Non hanno risposto: {string.Join(", ", mute)}." : "")
            + " Il colore non si legge dalle lampade: se serve, aggiungi un passo « colore ».";
    }

    /// <summary>Le stanze gia' date alle altre lampade: un tocco invece di riscriverle, magari diverse di una lettera.</summary>
    private void RiempiStanze()
    {
        PannelloStanzeCasa.Children.Clear();
        if (LuceScelta is not { } scelta) return;
        string attuale = CampoStanzaVoce.Text.Trim();
        foreach (var stanza in luci.Luci
                     .Where(l => l != scelta && l.Stanza.Length > 0)
                     .Select(l => l.Stanza)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            string quale = stanza;
            var gettone = new ToggleButton
            {
                Content = quale,
                Style = (Style)FindResource("Gettone"),
                Width = double.NaN,
                MinWidth = 0,
                Padding = new Thickness(10, 3, 10, 3),
                IsChecked = string.Equals(quale, attuale, StringComparison.CurrentCultureIgnoreCase),
            };
            gettone.Click += (_, _) => CampoStanzaVoce.Text = quale;
            PannelloStanzeCasa.Children.Add(gettone);
        }
    }

    /// <summary>
    /// Una scena nuova si fa quasi sempre partendo da una che c'e': « Film » e
    /// « Film, luce bassa » differiscono di un passo, e riscriverli tutti da
    /// capo e' il modo di sbagliarne uno.
    /// </summary>
    private void CasaDuplicaRoutine_Click(object sender, RoutedEventArgs e)
    {
        if (RoutineScelta is not { } r) return;

        var copia = r.Copia();
        string radice = (r.Nome.Length > 0 ? r.Nome : "routine") + " (copia)";
        copia.Nome = radice;
        // Il nome deve restare unico: e' con il nome che una sveglia del
        // tablet dice quale scena far partire.
        for (int n = 2; luci.Routine.Any(x => string.Equals(x.Nome, copia.Nome, StringComparison.OrdinalIgnoreCase)); n++)
            copia.Nome = $"{radice} {n}";

        int i = luci.Routine.IndexOf(r) + 1;
        luci.Routine.Insert(i, copia);
        SalvaLuci();
        RiempiElencoCasa(luci.Luci.Count + i);
        CostruisciPannello();
    }

    private void CasaSuRoutine_Click(object sender, RoutedEventArgs e) => SpostaRoutine(-1);

    private void CasaGiuRoutine_Click(object sender, RoutedEventArgs e) => SpostaRoutine(+1);

    /// <summary>L'ordine delle routine e' quello dei pulsanti sul tablet e sulla dashboard.</summary>
    private void SpostaRoutine(int verso)
    {
        if (RoutineScelta is not { } r) return;
        int i = luci.Routine.IndexOf(r);
        int j = i + verso;
        if (j < 0 || j >= luci.Routine.Count) return;

        (luci.Routine[i], luci.Routine[j]) = (luci.Routine[j], luci.Routine[i]);
        SalvaLuci();
        RiempiElencoCasa(luci.Luci.Count + j);
        CostruisciPannello();
    }

    /// <summary>
    /// Prova la scena da dove la si scrive. Era gia' possibile dalla dashboard
    /// e dalla pagina Casa, cioe' a due pagine di distanza dai passi che si
    /// stavano cambiando.
    /// </summary>
    private async void CasaProvaRoutine_Click(object sender, RoutedEventArgs e)
    {
        if (RoutineScelta is not { } r) return;
        TestoCasa.Text = $"Provo « {(r.Nome.Length > 0 ? r.Nome : "routine")} » dal PC: il risultato e' nella pagina Casa.";
        await EseguiRoutine(r);
        TestoCasa.Text = TestoPannello.Text;
    }

    private void CasaTogli_Click(object sender, RoutedEventArgs e)
    {
        int i = ElencoCasa.SelectedIndex;
        if (LuceScelta is { } l) { canali.Remove(l.Id); luci.Luci.Remove(l); }
        else if (RoutineScelta is { } r) luci.Routine.Remove(r);
        else return;

        SalvaLuci();
        RiempiElencoCasa(i);
        CostruisciPannello();
        TestoCasa.Text = "Tolta. Premi « Manda al tablet » per allinearlo.";
    }

    // ---- passi di una routine ----

    private void RiempiPassi(RoutineSpec? r, int scelto)
    {
        ElencoPassiLuce.Items.Clear();
        if (r is not null)
            foreach (var p in r.Passi) ElencoPassiLuce.Items.Add(DescriviPasso(p));

        TestoPassiVuoto.Visibility = ElencoPassiLuce.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // L'elenco delle lampade nel menu del passo si rifa' qui: le lampade
        // cambiano nome mentre si scrive la routine.
        SceltaLucePasso.Items.Clear();
        SceltaLucePasso.Items.Add("Tutte le luci");
        foreach (var l in luci.Luci) SceltaLucePasso.Items.Add(l.Nome.Length > 0 ? l.Nome : l.Ip);

        if (ElencoPassiLuce.Items.Count > 0)
            ElencoPassiLuce.SelectedIndex = Math.Clamp(scelto, 0, ElencoPassiLuce.Items.Count - 1);
        else
            MostraPassoCasa();
    }

    private string DescriviPasso(PassoLuce p)
    {
        if (p.Azione == "attesa") return $"Aspetta {PassoLuce.SecondiAttesa(p.Valore)} s";

        string chi = p.Luce.Length == 0
            ? "Tutte"
            : luci.Luci.FirstOrDefault(l => l.Id == p.Luce) is { } l
                ? (l.Nome.Length > 0 ? l.Nome : l.Ip)
                : "(luce sparita)";
        string cosa = AzioniLuce.FirstOrDefault(a => a.Codice == p.Azione).Nome ?? p.Azione;
        return p.Valore.Length > 0 ? $"{chi} · {cosa} {p.Valore}" : $"{chi} · {cosa}";
    }

    private PassoLuce? PassoCasaScelto
    {
        get
        {
            var r = RoutineScelta;
            int i = ElencoPassiLuce.SelectedIndex;
            return r is not null && i >= 0 && i < r.Passi.Count ? r.Passi[i] : null;
        }
    }

    private void ElencoPassiLuce_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (scrivendo) return;
        MostraPassoCasa();
    }

    private void MostraPassoCasa()
    {
        var p = PassoCasaScelto;
        PannelloPassoCasa.IsEnabled = p is not null;

        scrivendo = true;
        if (p is null)
        {
            SceltaLucePasso.SelectedIndex = -1;
            SceltaAzionePasso.SelectedIndex = -1;
            CampoValorePassoCasa.Text = "";
        }
        else
        {
            int quale = p.Luce.Length == 0 ? 0 : luci.Luci.FindIndex(l => l.Id == p.Luce) + 1;
            SceltaLucePasso.SelectedIndex = Math.Max(0, quale);
            SceltaAzionePasso.SelectedIndex = Math.Max(0,
                Array.FindIndex(AzioniLuce, a => a.Codice == p.Azione));
            CampoValorePassoCasa.Text = p.Valore;
        }
        scrivendo = false;
        SpiegaValore();
    }

    /// <summary>Il valore serve solo a due azioni: dirlo evita di lasciarlo pieno per sbaglio.</summary>
    private void SpiegaValore()
    {
        string azione = SceltaAzionePasso.SelectedIndex >= 0
            ? AzioniLuce[SceltaAzionePasso.SelectedIndex].Codice : "";
        // Aspettare non riguarda nessuna lampada: la scelta resta, spenta, cosi'
        // tornando a un'altra azione si ritrova quella di prima.
        SceltaLucePasso.IsEnabled = azione != "attesa";
        (CampoValorePassoCasa.IsEnabled, TestoValorePasso.Text) = azione switch
        {
            "attesa" => (true, "secondi, da 1 a 600"),
            "luce" => (true, "da 1 a 100"),
            "bianchezza" => (true, "da 0 (caldo) a 100 (freddo)"),
            "colore" => (true, "RRGGBB, per esempio ff8800"),
            _ => (false, ""),
        };
    }

    private void CasaPasso_Changed(object sender, RoutedEventArgs e)
    {
        if (scrivendo || PassoCasaScelto is not { } p) return;

        int quale = SceltaLucePasso.SelectedIndex;
        p.Luce = quale <= 0 ? "" : luci.Luci[quale - 1].Id;
        if (SceltaAzionePasso.SelectedIndex >= 0)
            p.Azione = AzioniLuce[SceltaAzionePasso.SelectedIndex].Codice;
        p.Valore = CampoValorePassoCasa.Text.Trim();

        SpiegaValore();
        int i = ElencoPassiLuce.SelectedIndex;
        scrivendo = true;
        ElencoPassiLuce.Items[i] = DescriviPasso(p);
        ElencoPassiLuce.SelectedIndex = i;
        scrivendo = false;
        SalvaLuci();
        CostruisciPannello();
    }

    private void CasaAggiungiPasso_Click(object sender, RoutedEventArgs e)
    {
        if (RoutineScelta is not { } r) return;
        r.Passi.Add(new PassoLuce());
        SalvaLuci();
        RiempiPassi(r, r.Passi.Count - 1);
        CostruisciPannello();
    }

    private void CasaTogliPasso_Click(object sender, RoutedEventArgs e)
    {
        if (RoutineScelta is not { } r || PassoCasaScelto is not { } p) return;
        int i = ElencoPassiLuce.SelectedIndex;
        r.Passi.Remove(p);
        SalvaLuci();
        RiempiPassi(r, i);
        CostruisciPannello();
    }

    private void CasaSuPasso_Click(object sender, RoutedEventArgs e) => SpostaPassoCasa(-1);

    private void CasaGiuPasso_Click(object sender, RoutedEventArgs e) => SpostaPassoCasa(+1);

    private void SpostaPassoCasa(int verso)
    {
        if (RoutineScelta is not { } r) return;
        int i = ElencoPassiLuce.SelectedIndex;
        int j = i + verso;
        if (i < 0 || j < 0 || j >= r.Passi.Count) return;

        (r.Passi[i], r.Passi[j]) = (r.Passi[j], r.Passi[i]);
        SalvaLuci();
        RiempiPassi(r, j);
    }

    // ---- il pannello: qui si accende davvero ----

    /// <summary>
    /// Rifa' il pannello della pagina Casa, e con lui la fila di routine che
    /// sta sulla dashboard. E' volutamente lo stesso disegno della sezione Casa
    /// sul tablet: quel che si vede qui e' quel che ci si trova li', senza
    /// doverlo immaginare.
    /// </summary>
    private void CostruisciPannello()
    {
        Pannello.Children.Clear();

        // Le routine finiscono in due posti - il pannello e la dashboard - e i
        // pulsanti sono due serie distinte: un elemento della finestra sta in
        // un genitore solo, non perche' le due file siano cose diverse.
        RoutineVive.Children.Clear();
        RiquadroRoutineVive.Visibility =
            luci.Routine.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Comparendo o sparendo cambia la forma della dashboard: senza routine
        // il deck si prende anche la sua colonna.
        SistemaDashboard();

        // Prima le scene e poi le routine, come sul tablet: una scena si preme
        // entrando in una stanza, una routine si prepara.
        var scene = luci.Routine.Where(r => r.Scena).ToList();
        var sequenze = luci.Routine.Where(r => !r.Scena).ToList();
        foreach (var r in scene.Concat(sequenze)) RoutineVive.Children.Add(GettoneRoutine(r));
        foreach (var (titolo, elenco) in new[] { ("SCENE", scene), ("ROUTINE", sequenze) })
        {
            if (elenco.Count == 0) continue;
            Pannello.Children.Add(Titoletto(titolo));
            var riga = new WrapPanel();
            foreach (var r in elenco) riga.Children.Add(GettoneRoutine(r));
            Pannello.Children.Add(riga);
        }

        if (luci.Luci.Count == 0)
        {
            Pannello.Children.Add(new TextBlock
            {
                Text = "Nessuna luce. In « Gestione › Luci » premi « Cerca le lampade », "
                       + "poi « Rileva chiavi ».",
                Style = (Style)FindResource("Aiuto"),
                Margin = new Thickness(2, 6, 0, 0),
            });
            return;
        }

        // Le lampade sono schede affiancate e non una colonna: la pagina e'
        // larga quanto la finestra, e in colonna resterebbe vuota per due terzi.
        // Raggruppate per stanza, nell'ordine in cui le stanze compaiono: una luce
        // si cerca da dove sta, non dal suo nome.
        var stanze = luci.Luci
            .Select(l => l.Stanza)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var stanza in stanze)
        {
            Pannello.Children.Add(Titoletto(stanza.ToUpper(System.Globalization.CultureInfo.CurrentCulture)));
            var muro = new WrapPanel();
            foreach (var l in luci.Luci.Where(l => string.Equals(l.Stanza, stanza, StringComparison.CurrentCultureIgnoreCase)))
                muro.Children.Add(SchedaLuce(l));
            Pannello.Children.Add(muro);
        }

        var senzaStanza = luci.Luci.Where(l => l.Stanza.Length == 0).ToList();
        if (senzaStanza.Count > 0)
        {
            Pannello.Children.Add(Titoletto(stanze.Count > 0 ? "ALTRE LUCI" : "LUCI"));
            var muro = new WrapPanel();
            foreach (var l in senzaStanza) muro.Children.Add(SchedaLuce(l));
            Pannello.Children.Add(muro);
        }
    }

    private TextBlock Titoletto(string testo) => new()
    {
        Text = testo,
        FontSize = 11,
        FontWeight = FontWeights.Normal,
        Foreground = (Brush)FindResource("TestoTenue"),
        Margin = new Thickness(2, 10, 0, 6),
    };

    private UIElement GettoneRoutine(RoutineSpec r)
    {
        var contenuto = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        contenuto.Children.Add(new TextBlock
        {
            Text = Segno(r.Glifo),
            FontSize = 20,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        contenuto.Children.Add(new TextBlock
        {
            Text = r.Nome.Length > 0 ? r.Nome : "routine",
            FontSize = 12,
            Foreground = (Brush)FindResource("TestoForte"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var b = new Button
        {
            Content = contenuto,
            Width = 108,
            Margin = new Thickness(0, 0, 8, 8),
            Background = new SolidColorBrush(AnteprimaDeck.Tinta(r.Colore)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        b.Click += async (_, _) => await EseguiRoutine(r);
        return b;
    }

    private UIElement SchedaLuce(LuceSpec l)
    {
        var scheda = new Border
        {
            Background = (Brush)FindResource("VeloScheda"),
            BorderBrush = (Brush)FindResource("Bordo"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 12, 12),
            // Larghezza fissa: le schede vanno a capo da sole, e restano
            // larghe uguali invece di seguire il nome della lampada.
            Width = 360,
        };

        // Griglia e non StackPanel orizzontale: uno StackPanel offre ai figli
        // larghezza infinita, quindi le file di comandi non andavano mai a capo
        // e sparivano oltre il bordo della colonna - dei dodici colori se ne
        // vedevano tre. La griglia da' alla seconda colonna una larghezza vera.
        var riga = new Grid();
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var interruttore = new Button
        {
            Content = new TextBlock
            {
                Text = Segno(l.Glifo),
                FontSize = 20,
                Foreground = Brushes.White,
            },
            Width = 52,
            Height = 52,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(AnteprimaDeck.Tinta(l.Colore)),
            BorderThickness = new Thickness(0),
        };
        interruttore.Click += async (_, _) => await Comanda(l, c => c.Inverti(), "inverti");
        Grid.SetColumn(interruttore, 0);
        riga.Children.Add(interruttore);

        var destra = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(destra, 1);
        destra.Children.Add(new TextBlock
        {
            Text = l.Nome.Length > 0 ? l.Nome : l.Ip,
            Foreground = (Brush)FindResource("Testo"),
            FontSize = 13,
        });

        var comandi = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (int livello in LivelliLuce)
        {
            int quanto = livello;
            comandi.Children.Add(Comandino($"{quanto}%",
                () => Comanda(l, c => c.Luminosita(quanto), $"luce {quanto}%")));
        }
        destra.Children.Add(comandi);

        var bianchi = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        bianchi.Children.Add(Comandino("Bianco", () => Comanda(l, c => c.Bianco(), "bianco")));
        foreach (var (quanto, nome) in Bianchi)
        {
            int quale = quanto;
            bianchi.Children.Add(Comandino(nome,
                () => Comanda(l, c => c.Temperatura(quale), "bianco " + nome.ToLowerInvariant())));
        }
        destra.Children.Add(bianchi);

        var tinte = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (string rgb in TinteLuce)
        {
            string quale = rgb;
            var p = new Button
            {
                Width = 27,
                Height = 24,
                MinWidth = 0,
                Margin = new Thickness(0, 0, 5, 5),
                Background = new SolidColorBrush(AnteprimaDeck.Tinta("#" + quale)),
                BorderThickness = new Thickness(0),
                ToolTip = "#" + quale,
            };
            p.Click += async (_, _) => await Comanda(l,
                c => c.Colore(Convert.ToInt32(quale, 16)), "colore " + quale);
            tinte.Children.Add(p);
        }
        destra.Children.Add(tinte);

        riga.Children.Add(destra);
        scheda.Child = riga;
        return scheda;
    }

    private Button Comandino(string testo, Func<Task> azione)
    {
        var b = new Button
        {
            Content = testo,
            MinWidth = 0,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 4, 4),
            Style = (Style)FindResource("Tenue"),
        };
        b.Click += async (_, _) => await azione();
        return b;
    }

    /// <summary>Il canale verso una lampada, tenuto da parte: dentro c'e' la mappa dei numeri.</summary>
    private TuyaLan? Canale(LuceSpec l)
    {
        if (!l.Completa) return null;
        if (!canali.TryGetValue(l.Id, out var c))
        {
            c = new TuyaLan(l.Id, l.Ip, l.Chiave, l.Versione);
            canali[l.Id] = c;
        }
        return c;
    }

    private async Task Comanda(LuceSpec l, Func<TuyaLan, Task<TuyaLan.Stato>> cosa, string nome)
    {
        string chi = l.Nome.Length > 0 ? l.Nome : l.Ip;
        if (Canale(l) is not { } c)
        {
            TestoPannello.Text = $"{chi}: manca la chiave locale.";
            return;
        }
        TestoPannello.Text = $"{chi}: {nome}...";
        var s = await cosa(c);
        if (!s.Raggiunta)
        {
            // Puo' essersi solo spostata: si ascolta chi si annuncia e, se
            // l'indirizzo e' cambiato, si riprova una volta sola.
            TestoPannello.Text = $"{chi}: non risponde, la cerco in rete...";
            string prima = l.Ip;
            await RitrovaLampade(aggiungiNuove: false);
            if (l.Ip != prima && Canale(l) is { } ritrovata)
            {
                TestoPannello.Text = $"{chi}: si era spostata a {l.Ip}, {nome}...";
                s = await cosa(ritrovata);
            }
        }
        TestoPannello.Text = s.Raggiunta
            ? $"{chi}: {(s.Accesa ? "accesa" : "spenta")}"
              + (s.HaLuminosita && s.Luminosita > 0 ? $", {s.Luminosita}%" : "")
            : $"{chi}: non risponde - {s.Errore}";
    }

    /// <summary>
    /// Esegue una routine dal PC, un passo alla volta e in ordine: e' cosi' che
    /// la esegue il tablet, quindi provarla qui vale anche per li'.
    /// </summary>
    private async Task EseguiRoutine(RoutineSpec r)
    {
        string nome = r.Nome.Length > 0 ? r.Nome : "routine";
        TestoPannello.Text = $"{nome}: in corso...";

        // Una ricerca per esecuzione, alla prima lampada muta: se dopo quella
        // un'altra non risponde, e' spenta davvero.
        bool ritrovate = false;

        foreach (var passo in r.Passi)
        {
            if (passo.Azione == "attesa")
            {
                int secondi = PassoLuce.SecondiAttesa(passo.Valore);
                TestoPannello.Text = $"{nome}: aspetto {secondi} s...";
                await Task.Delay(secondi * 1000);
                continue;
            }

            var bersagli = passo.Luce.Length == 0
                ? luci.Luci.ToList()
                : luci.Luci.Where(l => l.Id == passo.Luce).ToList();

            foreach (var l in bersagli)
            {
                if (Canale(l) is not { } c) continue;
                var s = await FaiPasso(c, passo);
                if (s.Raggiunta || ritrovate) continue;

                ritrovate = true;
                string prima = l.Ip;
                await RitrovaLampade(aggiungiNuove: false);
                if (l.Ip != prima && Canale(l) is { } ritrovata) await FaiPasso(ritrovata, passo);
            }
        }
        TestoPannello.Text = $"{nome}: fatta.";
    }

    private static async Task<TuyaLan.Stato> FaiPasso(TuyaLan c, PassoLuce passo) => passo.Azione switch
    {
        "on" => await c.Accendi(true),
        "off" => await c.Accendi(false),
        "inverti" => await c.Inverti(),
        "bianco" => await c.Bianco(),
        "luce" => await c.Luminosita(int.TryParse(passo.Valore, out int n) ? n : 100),
        "bianchezza" => await c.Temperatura(int.TryParse(passo.Valore, out int b) ? b : 50),
        "colore" => await c.Colore(Colore(passo.Valore)),
        _ => await c.Leggi(),
    };

    private static int Colore(string testo)
    {
        string pulito = testo.TrimStart('#');
        return int.TryParse(pulito, System.Globalization.NumberStyles.HexNumber, null, out int v)
            ? v : 0xFFFFFF;
    }

    // ---- account e chiavi ----

    private void CasaAccount_Changed(object sender, TextChangedEventArgs e)
    {
        if (scrivendo) return;
        luci.CodiceUtente = CampoCodiceUtente.Text.Trim();
        SalvaLuci();
    }

    private CancellationTokenSource? attesaQr;

    /// <summary>
    /// Mostra un codice da inquadrare con l'app Smart Life e aspetta la
    /// conferma. E' la strada di Home Assistant: niente account da
    /// sviluppatore, niente progetto cloud, niente prova che scade.
    /// </summary>
    private async void CasaRileva_Click(object sender, RoutedEventArgs e)
    {
        if (luci.CodiceUtente.Length == 0)
        {
            TestoCasa.Text = "Serve il codice utente dell'app Smart Life.";
            return;
        }

        TestoCasa.Text = "Chiedo un codice a Tuya...";
        var sessione = new TuyaSharing.Sessione();
        var (codice, errore) = await TuyaSharing.ChiediCodice(luci.CodiceUtente);
        if (errore.Length > 0 || codice.Length == 0)
        {
            TestoCasa.Text = "Non arriva il codice: " + (errore.Length > 0 ? errore : "risposta vuota");
            Registra(TestoCasa.Text);
            return;
        }
        sessione.Codice = codice;

        var matrice = Qr.Genera(TuyaSharing.TestoQr(codice));
        if (matrice is null)
        {
            TestoCasa.Text = "Il codice mandato da Tuya e' troppo lungo per essere disegnato.";
            return;
        }
        ImmagineQr.Source = Disegno(matrice);
        RiquadroQr.Visibility = Visibility.Visible;
        TestoCasa.Text = "";

        attesaQr?.Cancel();
        attesaQr = new CancellationTokenSource();
        await Aspetta(sessione, attesaQr.Token);
    }

    /// <summary>
    /// Chiede ogni due secondi se qualcuno ha inquadrato. Il codice scade da
    /// solo dopo pochi minuti: oltre i tre si smette, invece di insistere su un
    /// codice morto.
    /// </summary>
    private async Task Aspetta(TuyaSharing.Sessione sessione, CancellationToken stop)
    {
        var scadenza = DateTime.UtcNow.AddMinutes(3);
        while (!stop.IsCancellationRequested && DateTime.UtcNow < scadenza)
        {
            var (entrato, errore) = await TuyaSharing.Esito(sessione, luci.CodiceUtente);
            if (errore.Length > 0)
            {
                TestoQrStato.Text = errore;
            }
            else if (entrato)
            {
                TestoQrStato.Text = "Entrato. Prendo le chiavi...";
                await Chiavi(sessione);
                return;
            }
            else
            {
                int restano = (int)(scadenza - DateTime.UtcNow).TotalSeconds;
                TestoQrStato.Text = $"In attesa della scansione ({restano / 60}:{restano % 60:00})";
            }

            try { await Task.Delay(2000, stop); }
            catch (TaskCanceledException) { return; }
        }

        if (!stop.IsCancellationRequested)
        {
            RiquadroQr.Visibility = Visibility.Collapsed;
            TestoCasa.Text = "Il codice e' scaduto senza essere inquadrato. Riprova.";
        }
    }

    private async Task Chiavi(TuyaSharing.Sessione sessione)
    {
        var (trovate, errore) = await TuyaSharing.Dispositivi(sessione);
        RiquadroQr.Visibility = Visibility.Collapsed;

        if (errore.Length > 0)
        {
            TestoCasa.Text = "Entrato, ma l'elenco non arriva: " + errore;
            Registra(TestoCasa.Text);
            return;
        }

        int con = 0, nuove = 0;
        foreach (var t in trovate)
        {
            var riga = luci.Luci.FirstOrDefault(l => l.Id == t.Id);
            if (riga is null)
            {
                riga = new LuceSpec { Id = t.Id, Ip = t.Ip };
                luci.Luci.Add(riga);
                nuove++;
            }
            if (t.Chiave.Length > 0) { riga.Chiave = t.Chiave; con++; }
            // Il nome scritto a mano vince: se l'hai cambiato qui, non deve
            // tornare quello dell'app al prossimo giro.
            if (riga.Nome.Length == 0) riga.Nome = t.Nome;
            if (riga.Ip.Length == 0) riga.Ip = t.Ip;
            canali.Remove(riga.Id);
        }

        SalvaLuci();
        RiempiElencoCasa(ElencoCasa.SelectedIndex);
        CostruisciPannello();

        // La categoria dice cosa sono davvero: e' cosi' che si scopre che un
        // apparecchio e' una presa e non una lampada.
        foreach (var t in trovate) Registra($"  {t.Nome} - {t.Tipo} - {t.Id}");
        TestoCasa.Text = $"{con} chiavi prese su {trovate.Count} dispositivi ({nuove} righe nuove). "
                         + "Nel Registro c'e' cosa sono.";
        Registra(TestoCasa.Text);
    }

    private void CasaAnnullaQr_Click(object sender, RoutedEventArgs e)
    {
        attesaQr?.Cancel();
        RiquadroQr.Visibility = Visibility.Collapsed;
        TestoCasa.Text = "";
    }

    /// <summary>Un pixel per modulo, piu' il bordo chiaro che i lettori pretendono.</summary>
    private static System.Windows.Media.Imaging.BitmapSource Disegno(bool[,] m)
    {
        const int bordo = 4;
        int lato = m.GetLength(0);
        int n = lato + bordo * 2;

        var pixel = new byte[n * n];
        Array.Fill(pixel, (byte)255);
        for (int r = 0; r < lato; r++)
            for (int c = 0; c < lato; c++)
                if (m[r, c]) pixel[(r + bordo) * n + (c + bordo)] = 0;

        return System.Windows.Media.Imaging.BitmapSource.Create(
            n, n, 96, 96, PixelFormats.Gray8, null, pixel, n);
    }

    // ---- scoperta e invio ----

    private async void CasaCerca_Click(object sender, RoutedEventArgs e)
    {
        TestoCasa.Text = "Ascolto gli annunci delle lampade...";
        var (trovate, nuove, aggiornate) = await RitrovaLampade(aggiungiNuove: true);

        TestoCasa.Text = trovate == 0
            ? "Nessuna lampada si e' annunciata: sono accese al muro? Il PC e' sulla loro rete?"
            : $"{trovate} lampade in rete: {nuove} nuove, {aggiornate} con l'indirizzo cambiato.";
        Registra(TestoCasa.Text);
    }

    private Task<(int Trovate, int Nuove, int Aggiornate)>? ricercaLampade;

    /// <summary>
    /// Ascolta gli annunci e riallinea gli indirizzi. La chiama « Cerca le
    /// lampade », e la chiama da sola una lampada che non risponde: spenta al
    /// muro per qualche giorno torna spesso con un altro indirizzo, e il canale
    /// tenuto da parte bussava ancora a quello vecchio finche' qualcuno non
    /// premeva « Cerca ».
    ///
    /// Una ricerca alla volta: due lampade mute in una routine non diventano
    /// due ascolti da sette secondi sulle stesse porte.
    /// </summary>
    /// <param name="aggiungiNuove">Solo a mano: una lampada sconosciuta non entra nell'elenco senza che lo si chieda.</param>
    private Task<(int Trovate, int Nuove, int Aggiornate)> RitrovaLampade(bool aggiungiNuove)
    {
        if (ricercaLampade is { IsCompleted: false } inCorso) return inCorso;
        ricercaLampade = Ritrova(aggiungiNuove);
        return ricercaLampade;
    }

    private async Task<(int Trovate, int Nuove, int Aggiornate)> Ritrova(bool aggiungiNuove)
    {
        var trovate = await Task.Run(() => CercaLampade(TimeSpan.FromSeconds(7)));

        int nuove = 0, aggiornate = 0;
        foreach (var t in trovate)
        {
            var esistente = luci.Luci.FirstOrDefault(l => l.Id == t.Id);
            if (esistente is null)
            {
                if (!aggiungiNuove) continue;
                luci.Luci.Add(new LuceSpec { Id = t.Id, Ip = t.Ip, Versione = t.Versione });
                nuove++;
            }
            else if (esistente.Ip != t.Ip || esistente.Versione != t.Versione)
            {
                Registra($"{(esistente.Nome.Length > 0 ? esistente.Nome : esistente.Id)} si e' spostata: {esistente.Ip} -> {t.Ip}.");
                esistente.Ip = t.Ip;
                esistente.Versione = t.Versione;
                canali.Remove(esistente.Id);
                aggiornate++;
            }
        }

        if (nuove > 0 || aggiornate > 0)
        {
            SalvaLuci();
            RiempiElencoCasa(ElencoCasa.SelectedIndex);
            CostruisciPannello();
        }
        return (trovate.Count, nuove, aggiornate);
    }

    /// <summary>
    /// Scrive luci.json e ricorda che il tablet non l'ha ancora visto.
    ///
    /// Le luci sono l'unica cosa che il tablet sa fare da solo, e proprio per
    /// questo se le tiene: quello che ha in mano puo' essere di un mese fa. Da
    /// quando il collegamento non gliele rimanda piu' da solo, chi cambia una
    /// riga qui deve poter vedere che di la' non e' ancora arrivata.
    /// </summary>
    private void SalvaLuci()
    {
        ConfigFile.Save(luciPath, luci);
        LuciDaMandare(true);
    }

    private void LuciDaMandare(bool si)
    {
        settings.LuciDaMandare = si;
        SalvaImpostazioni();

        TestoLuciStato.Text = si ? "Cambiate qui, non ancora sul tablet." : "";
        TestoLuciStato.Foreground = (Brush)FindResource(si ? "Attenzione" : "TestoTenue");
    }

    private void CasaManda_Click(object sender, RoutedEventArgs e)
    {
        SalvaLuci();

        int pronte = luci.Luci.Count(l => l.Completa);
        if (!engine.Connected)
        {
            TestoCasa.Text = "Tablet non collegato: collegalo col cavo o in rete, poi rimanda.";
            Registra(TestoCasa.Text);
            return;
        }

        // Si manda sempre, anche vuoto: questo pulsante allinea il tablet a quel
        // che c'e' qui, e senza il caso vuoto una riga sbagliata resterebbe sul
        // tablet per sempre.
        engine.UseLuci(luci);
        engine.SendLuci();
        LuciDaMandare(false);
        int scartate = luci.Luci.Count - pronte;
        int routine = luci.Routine.Count(r => r.Passi.Count > 0);
        TestoCasa.Text = pronte == 0
            ? "Tablet allineato: nessuna luce, perche' nessuna ha la chiave."
            : $"Mandate {pronte} luci e {routine} routine."
              + (scartate > 0 ? $" {scartate} senza chiave, lasciate indietro." : "");
        Registra(TestoCasa.Text);
    }

    // ---- ascolto degli annunci ----

    private sealed record Annuncio(string Id, string Ip, string Versione);

    /// <summary>
    /// Le lampade parlano da sole ogni pochi secondi: aspettare costa meno che
    /// bussare a 254 indirizzi per trovarne tre.
    /// </summary>
    private static List<Annuncio> CercaLampade(TimeSpan quanto)
    {
        var viste = new Dictionary<string, Annuncio>();
        var fine = DateTime.UtcNow + quanto;
        var sockets = new List<UdpClient>();

        try
        {
            foreach (int porta in PorteAnnunci)
            {
                try
                {
                    var s = new UdpClient();
                    s.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    s.Client.Bind(new IPEndPoint(IPAddress.Any, porta));
                    s.Client.ReceiveTimeout = 500;
                    sockets.Add(s);
                }
                catch (SocketException)
                {
                    // Porta gia' occupata: si prova l'altra.
                }
            }

            while (DateTime.UtcNow < fine && sockets.Count > 0)
            {
                foreach (var s in sockets)
                {
                    try
                    {
                        var da = new IPEndPoint(IPAddress.Any, 0);
                        byte[] dati = s.Receive(ref da);
                        if (Leggi(dati) is { } a) viste[a.Id] = a;
                    }
                    catch (SocketException)
                    {
                        // Nessuno ha parlato entro il mezzo secondo: si riprova.
                    }
                }
            }
        }
        finally
        {
            foreach (var s in sockets) s.Dispose();
        }

        return viste.Values.OrderBy(a => a.Ip).ToList();
    }

    private static Annuncio? Leggi(byte[] dati)
    {
        try
        {
            if (InChiaro(dati) is not { } testo) return null;

            using var doc = JsonDocument.Parse(testo);
            var root = doc.RootElement;
            string id = Campo(root, "gwId") ?? Campo(root, "devId") ?? "";
            string ip = Campo(root, "ip") ?? "";
            if (id.Length == 0 || ip.Length == 0) return null;
            return new Annuncio(id, ip, Campo(root, "version") ?? "3.3");
        }
        catch
        {
            // Sulle stesse porte parlano telefoni e altre marche: un pacchetto
            // che non si capisce e' rumore, non un guasto.
            return null;
        }
    }

    private static string? Campo(JsonElement o, string nome) =>
        o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? InChiaro(byte[] dati)
    {
        int da = 0, quanti = dati.Length;

        // Gli annunci viaggiano nella stessa busta dei comandi: intestazione di
        // 16 byte piu' l'esito, e in coda firma e suffisso.
        if (dati.Length > 24 && BinaryPrimitives.ReadUInt32BigEndian(dati) == 0x000055AA)
        {
            int dichiarata = BinaryPrimitives.ReadInt32BigEndian(dati.AsSpan(12));
            int limite = Math.Min(dati.Length, 16 + dichiarata) - 8;
            da = 20;
            quanti = limite - da;
            if (quanti <= 0) return null;
        }

        // Qualche lampada annuncia in chiaro: se e' gia' JSON non c'e' niente
        // da decifrare.
        if (dati[da] == (byte)'{') return Encoding.UTF8.GetString(dati, da, quanti);

        using var aes = Aes.Create();
        aes.Key = MD5.HashData(Encoding.UTF8.GetBytes(ChiaveAnnunci));
        return Encoding.UTF8.GetString(aes.DecryptEcb(dati.AsSpan(da, quanti), PaddingMode.PKCS7));
    }
}
