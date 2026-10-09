using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TabDeck;

/// <summary>
/// Le postazioni: Casa, Ufficio. Si passa dall'una all'altra dall'elenco in cima alla
/// finestra, e si creano, rinominano e tolgono in Gestione › Postazioni.
///
/// Quella attiva si ricorda a ogni salvataggio quel che si sceglie nelle pagine di sempre
/// (profilo del deck, indirizzo in rete, collegamento all'apertura, sezioni e schermo del
/// tablet, estensioni accese). Nella pagina Postazioni si sceglie anche a mano, per
/// qualunque postazione: deck, luci di casa, sezioni ed estensioni. Vedi <see cref="Postazione"/>.
/// </summary>
public partial class MainWindow
{
    /// <summary>"nuova" o "rinomina" mentre la riga del nome e' aperta.</summary>
    private string modoPostazione = "";

    /// <summary>Quella da rinominare: la scelta nell'elenco puo' cambiare mentre si scrive.</summary>
    private string postazioneInNome = "";

    private void PreparaPostazioni()
    {
        // Un file di prima non ne ha: quel che c'e' diventa la prima, e la seconda
        // nasce quando la si chiede.
        if (settings.Postazioni.Count == 0)
        {
            settings.Postazioni.Add(new Postazione { Nome = "Casa" });
            settings.PostazioneAttiva = "Casa";
            ScriviImpostazioni();
        }
        if (PostazioneAttiva() is null) settings.PostazioneAttiva = settings.Postazioni[0].Nome;
        ApplicaLuci(manda: false);
        RiempiPostazioni();
    }

    /// <summary>
    /// Le luci della postazione attiva: sul PC la pagina Casa e le routine della dashboard,
    /// sul tablet l'elenco delle lampade e la sezione Casa.
    /// </summary>
    private void ApplicaLuci(bool manda)
    {
        engine.LuciSpente = !(PostazioneAttiva()?.Luci ?? true);
        SchedaCasa.Visibility = engine.LuciSpente ? Visibility.Collapsed : Visibility.Visible;
        if (engine.LuciSpente && Schede.SelectedItem == SchedaCasa) Schede.SelectedItem = SchedaDashboard;
        CostruisciPannello();
        if (manda && engine.Connected)
        {
            engine.SendLuci();
            engine.SendTabletConfig();
        }
    }

    /// <summary>Gli id delle estensioni accese adesso.</summary>
    private List<string> EstensioniAccese() =>
        estensioni?.Elenco.Where(i => i.Accesa).Select(i => i.Manifesto.Id).ToList() ?? new();

    private Postazione? CercaPostazione(string nome) =>
        settings.Postazioni.FirstOrDefault(p => string.Equals(p.Nome, nome.Trim(), StringComparison.OrdinalIgnoreCase));

    private Postazione? PostazioneAttiva() => CercaPostazione(settings.PostazioneAttiva);

    /// <summary>
    /// Scrive tabdeck.json, dopo aver messo nella postazione attiva le scelte di adesso.
    /// Tutti i salvataggi delle impostazioni passano da qui: e' cosi' che la postazione
    /// resta al passo senza un « Salva » suo.
    /// </summary>
    private void ScriviImpostazioni()
    {
        if (PostazioneAttiva() is { } p)
        {
            p.Profilo = deckFile.Attivo;
            p.Host = settings.Host;
            p.Collega = settings.Comportamento.CollegaAllApertura;
            p.KeepAwake = settings.Tablet.KeepAwake;
            p.Brightness = settings.Tablet.Brightness;
            p.Internet = settings.Tablet.Internet;
            p.Sezioni = settings.Tablet.Sezioni.Copia();
            p.SenzaPc = settings.Tablet.SenzaPc.Copia();
            if (estensioni is not null) p.Estensioni = EstensioniAccese();
        }

        try
        {
            ConfigFile.Save(settingsPath, settings);
        }
        catch (IOException e)
        {
            Registra($"Impostazioni non salvate: {e.Message}", true);
        }

        RiempiPostazioni();
    }

    /// <summary>
    /// Passa a un'altra postazione: le sue scelte tornano nelle pagine, il deck cambia
    /// profilo e, col tablet collegato, gli arriva tutto subito. Il collegamento in corso
    /// non si tocca: un indirizzo nuovo vale dal prossimo.
    /// </summary>
    private void PassaAPostazione(string nome)
    {
        var p = CercaPostazione(nome);
        if (p is null || ReferenceEquals(p, PostazioneAttiva()))
        {
            RiempiPostazioni();
            return;
        }

        // Quella che si lascia si tiene le ultime scelte.
        ScriviImpostazioni();
        // Copiata subito: da qui ogni salvataggio fotografa le estensioni accese nella
        // postazione nuova, e lo farebbe prima di averle accese.
        var volute = p.Estensioni?.ToList();

        settings.PostazioneAttiva = p.Nome;
        settings.Host = p.Host;
        settings.Comportamento.CollegaAllApertura = p.Collega;
        settings.Tablet.KeepAwake = p.KeepAwake;
        settings.Tablet.Brightness = p.Brightness;
        settings.Tablet.Internet = p.Internet;
        settings.Tablet.Sezioni = p.Sezioni.Copia();
        settings.Tablet.SenzaPc = p.SenzaPc.Copia();
        // Le sezioni si scelgono anche sul tablet, e al collegamento vince la scelta piu'
        // recente: quella della postazione e' di adesso, non di quando la si era lasciata.
        settings.Tablet.Sezioni.Cambiate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        MostraImpostazioni();
        if (deckFile.Cerca(p.Profilo) is not null) CambiaProfilo(p.Profilo);
        if (volute is not null && estensioni is not null)
        {
            // Prima si spegne: si accende una estensione alla volta.
            foreach (var i in estensioni.Elenco.Where(i => i.Accesa && !volute.Contains(i.Manifesto.Id)))
                estensioni.Accendi(i.Manifesto.Id, false);
            foreach (var i in estensioni.Elenco.Where(i => !i.Accesa && volute.Contains(i.Manifesto.Id)))
                if (estensioni.Accendi(i.Manifesto.Id, true) is { } errore)
                    Registra($"{i.Manifesto.Nome} non si accende: {errore}", true);
        }
        SalvaImpostazioni();
        ApplicaLuci(manda: true);
        RiempiPostazioni();
        Registra($"Postazione « {p.Nome} »: profilo « {deckFile.Attivo} »"
                 + (p.Host.Length > 0 ? $", tablet in rete a {p.Host}." : "."));
    }

    /// <summary>Il nome di un profilo del deck e' cambiato: le postazioni lo seguono.</summary>
    private void ProfiloRinominato(string prima, string adesso)
    {
        foreach (var p in settings.Postazioni)
            if (string.Equals(p.Profilo, prima, StringComparison.OrdinalIgnoreCase))
                p.Profilo = adesso;
        ScriviImpostazioni();
    }

    // ---- elenchi ----

    private void RiempiPostazioni()
    {
        bool prima = caricamento;
        caricamento = true;

        string scelta = (ListaPostazioni.SelectedItem as ListBoxItem)?.Tag as string ?? settings.PostazioneAttiva;
        var attiva = PostazioneAttiva();

        ElencoPostazioni.Items.Clear();
        ListaPostazioni.Items.Clear();
        foreach (var p in settings.Postazioni)
        {
            ElencoPostazioni.Items.Add(p.Nome);
            ListaPostazioni.Items.Add(VocePostazione(p, ReferenceEquals(p, attiva)));
        }
        // Sempre in vista, anche con una sola: e' il posto dove si capisce che le postazioni
        // esistono, e da dove se ne crea un'altra.
        ElencoPostazioni.Items.Add(new ComboBoxItem
        {
            Content = "+  Nuova postazione…",
            Tag = VoceNuova,
            Foreground = (Brush)FindResource("TestoTenue"),
        });
        ElencoPostazioni.SelectedItem = attiva?.Nome;

        ListaPostazioni.SelectedItem =
            ListaPostazioni.Items.Cast<ListBoxItem>().FirstOrDefault(v => string.Equals((string)v.Tag, scelta, StringComparison.OrdinalIgnoreCase))
            ?? ListaPostazioni.Items.Cast<ListBoxItem>().FirstOrDefault(v => string.Equals((string)v.Tag, attiva?.Nome, StringComparison.OrdinalIgnoreCase));

        caricamento = prima;
        MostraSceltaPostazione();
    }

    private ListBoxItem VocePostazione(Postazione p, bool attiva)
    {
        var riga = new Grid();
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        riga.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var spia = new Ellipse
        {
            Width = 7,
            Height = 7,
            Margin = new Thickness(2, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)FindResource(attiva ? "Acceso" : "Spento"),
        };
        var nome = new TextBlock { Text = p.Nome, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TestoForte") };
        var cosa = new TextBlock
        {
            Text = string.Join("  ·  ", new[]
            {
                p.Profilo,
                p.Host,
                p.Collega switch
                {
                    "usb" => "cavo all'apertura",
                    "wifi" => "rete all'apertura",
                    "auto" => "cavo o rete all'apertura",
                    _ => "",
                },
            }.Where(s => s.Length > 0)),
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TestoTenue"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nome, 1);
        Grid.SetColumn(cosa, 2);
        riga.Children.Add(spia);
        riga.Children.Add(nome);
        riga.Children.Add(cosa);

        var voce = new ListBoxItem { Tag = p.Nome, Content = riga, Padding = new Thickness(10, 9, 12, 9) };
        voce.MouseDoubleClick += (_, _) => PassaAPostazione(p.Nome);
        return voce;
    }

    private string? PostazioneScelta() => (ListaPostazioni.SelectedItem as ListBoxItem)?.Tag as string;

    private void MostraSceltaPostazione()
    {
        string? scelta = PostazioneScelta();
        BtnUsaPostazione.IsEnabled = scelta is not null
            && !string.Equals(scelta, settings.PostazioneAttiva, StringComparison.OrdinalIgnoreCase);
        MostraDettaglioPostazione();
    }

    // ---- cosa c'e' nella postazione scelta ----

    private static readonly (string Chiave, string Nome)[] SezioniPostazione =
    {
        ("dashboard", "Dashboard"), ("deck", "Deck"), ("schermo", "Schermo remoto"),
        ("casa", "Casa"), ("orologio", "Timer e sveglie"),
    };

    private static bool Sezione(SezioniTablet s, string chiave) => chiave switch
    {
        "dashboard" => s.Dashboard,
        "deck" => s.Deck,
        "schermo" => s.Schermo,
        "casa" => s.Casa,
        _ => s.Orologio,
    };

    private static void Sezione(SezioniTablet s, string chiave, bool valore)
    {
        switch (chiave)
        {
            case "dashboard": s.Dashboard = valore; break;
            case "deck": s.Deck = valore; break;
            case "schermo": s.Schermo = valore; break;
            case "casa": s.Casa = valore; break;
            default: s.Orologio = valore; break;
        }
    }

    /// <summary>La postazione scelta nell'elenco della pagina, e se e' quella in cui si sta.</summary>
    private (Postazione P, bool Attiva)? PostazioneInPagina()
    {
        if (PostazioneScelta() is not { } nome || CercaPostazione(nome) is not { } p) return null;
        return (p, ReferenceEquals(p, PostazioneAttiva()));
    }

    private void MostraDettaglioPostazione()
    {
        if (PostazioneInPagina() is not var (p, attiva))
        {
            GruppoPostazione.Visibility = Visibility.Collapsed;
            return;
        }
        GruppoPostazione.Visibility = Visibility.Visible;
        GruppoPostazione.Header = p.Nome.ToUpperInvariant();

        bool prima = caricamento;
        caricamento = true;

        ElencoPostProfilo.Items.Clear();
        foreach (var profilo in deckFile.Profili) ElencoPostProfilo.Items.Add(profilo.Nome);
        ElencoPostProfilo.SelectedItem = deckFile.Cerca(attiva ? deckFile.Attivo : p.Profilo)?.Nome;

        SpuntaPostLuci.IsChecked = p.Luci;

        // Sulla attiva i valori veri sono quelli in uso; la postazione ne e' la fotografia.
        var col = attiva ? settings.Tablet.Sezioni : p.Sezioni;
        var senza = attiva ? settings.Tablet.SenzaPc : p.SenzaPc;
        PostSezioni.Children.Clear();
        PostSenzaPc.Children.Clear();
        foreach (var (chiave, nome) in SezioniPostazione)
        {
            PostSezioni.Children.Add(SpuntaSezione(nome, chiave, Sezione(col, chiave), false));
            if (chiave != "schermo") PostSenzaPc.Children.Add(SpuntaSezione(nome, chiave, Sezione(senza, chiave), true));
        }

        PostEstensioni.Children.Clear();
        var elenco = estensioni?.Elenco.OrderBy(i => i.Manifesto.Nome, StringComparer.CurrentCultureIgnoreCase).ToList() ?? new();
        if (elenco.Count == 0)
            PostEstensioni.Children.Add(new TextBlock { Text = "Nessuna installata", Style = (Style)FindResource("Aiuto"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var i in elenco)
        {
            string id = i.Manifesto.Id;
            var spunta = new CheckBox
            {
                Content = i.Manifesto.Nome,
                IsChecked = attiva ? i.Accesa : p.Estensioni?.Contains(id) ?? i.Accesa,
                Margin = new Thickness(0, 0, 22, 8),
            };
            spunta.Click += (_, _) => PostEstensione(id, spunta.IsChecked == true);
            PostEstensioni.Children.Add(spunta);
        }

        caricamento = prima;
    }

    private CheckBox SpuntaSezione(string nome, string chiave, bool acceso, bool senzaPc)
    {
        var spunta = new CheckBox { Content = nome, IsChecked = acceso, Margin = new Thickness(0, 0, 22, 8) };
        spunta.Click += (_, _) => PostSezione(chiave, senzaPc, spunta.IsChecked == true);
        return spunta;
    }

    private void PostProfilo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento || ElencoPostProfilo.SelectedItem is not string nome || PostazioneInPagina() is not var (p, attiva)) return;
        if (attiva)
        {
            CambiaProfilo(nome);
            return;
        }
        p.Profilo = nome;
        ScriviImpostazioni();
    }

    private void PostLuci_Click(object sender, RoutedEventArgs e)
    {
        if (PostazioneInPagina() is not var (p, attiva)) return;
        p.Luci = SpuntaPostLuci.IsChecked == true;
        if (attiva) ApplicaLuci(manda: true);
        ScriviImpostazioni();
    }

    private void PostSezione(string chiave, bool senzaPc, bool valore)
    {
        if (PostazioneInPagina() is not var (p, attiva)) return;
        if (!attiva)
        {
            Sezione(senzaPc ? p.SenzaPc : p.Sezioni, chiave, valore);
            ScriviImpostazioni();
            return;
        }
        // Come una spunta della pagina Tablet: si scrive nelle impostazioni e parte al tablet.
        Sezione(senzaPc ? settings.Tablet.SenzaPc : settings.Tablet.Sezioni, chiave, valore);
        settings.Tablet.Sezioni.Cambiate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MostraImpostazioni();
        SalvaImpostazioni();
    }

    private void PostEstensione(string id, bool accesa)
    {
        if (PostazioneInPagina() is not var (p, attiva)) return;
        if (attiva)
        {
            if (estensioni?.Accendi(id, accesa) is { } errore) Registra($"Estensione non accesa: {errore}", true);
            return;
        }
        var lista = p.Estensioni ?? EstensioniAccese();
        lista.Remove(id);
        if (accesa) lista.Add(id);
        p.Estensioni = lista;
        ScriviImpostazioni();
    }

    private void ListaPostazioni_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!caricamento) MostraSceltaPostazione();
    }

    private const string VoceNuova = "nuova";

    private void Postazione_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (caricamento) return;
        if (ElencoPostazioni.SelectedItem is ComboBoxItem { Tag: VoceNuova })
        {
            // La voce non e' una postazione: l'elenco torna su quella attiva, e la nuova
            // si battezza nella sua pagina.
            // Dopo, e non dentro l'evento: rifare le voci mentre l'elenco sta scegliendo le confonde.
            Dispatcher.BeginInvoke(() =>
            {
                RiempiPostazioni();
                Schede.SelectedItem = SchedaPostazioni;
                NuovaPostazione_Click(sender, e);
            });
            return;
        }
        if (ElencoPostazioni.SelectedItem is string nome) PassaAPostazione(nome);
    }

    private void UsaPostazione_Click(object sender, RoutedEventArgs e)
    {
        if (PostazioneScelta() is { } nome) PassaAPostazione(nome);
    }

    // ---- nuova, rinomina, togli ----

    private void NuovaPostazione_Click(object sender, RoutedEventArgs e) =>
        ChiediNomePostazione("nuova", "Nome della postazione nuova",
            NomePostazioneLibero(CercaPostazione("Ufficio") is null ? "Ufficio" : "Postazione"));

    private void RinominaPostazione_Click(object sender, RoutedEventArgs e)
    {
        if (PostazioneScelta() is { } nome) ChiediNomePostazione("rinomina", $"Nuovo nome di « {nome} »", nome);
    }

    private void TogliPostazione_Click(object sender, RoutedEventArgs e)
    {
        if (PostazioneScelta() is not { } nome || CercaPostazione(nome) is not { } p) return;
        if (settings.Postazioni.Count == 1)
        {
            Registra("E' l'unica postazione: ne resta sempre una.", true);
            return;
        }
        if (MessageBox.Show(this, $"Tolgo la postazione « {p.Nome} »?\nIl profilo del deck resta.",
                "TabDeck", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        ChiudiNomePostazione();
        // Togliendo quella in cui si sta, si passa a un'altra: senza, le scelte di adesso
        // finirebbero nella prima che capita alla prossima modifica.
        if (ReferenceEquals(p, PostazioneAttiva()))
            PassaAPostazione(settings.Postazioni.First(altra => !ReferenceEquals(altra, p)).Nome);
        settings.Postazioni.Remove(p);
        ScriviImpostazioni();
        Registra($"Postazione « {p.Nome} » tolta.");
    }

    private string NomePostazioneLibero(string radice)
    {
        if (CercaPostazione(radice) is null) return radice;
        for (int n = 2; ; n++)
            if (CercaPostazione($"{radice} {n}") is null) return $"{radice} {n}";
    }

    private void ChiediNomePostazione(string modo, string domanda, string proposta)
    {
        modoPostazione = modo;
        postazioneInNome = proposta;
        TestoNomePostazione.Text = domanda;
        CampoNomePostazione.Text = proposta;
        RigaNomePostazione.Visibility = Visibility.Visible;
        CampoNomePostazione.Focus();
        CampoNomePostazione.SelectAll();
    }

    private void ChiudiNomePostazione()
    {
        modoPostazione = "";
        RigaNomePostazione.Visibility = Visibility.Collapsed;
    }

    private void AnnullaNomePostazione_Click(object sender, RoutedEventArgs e) => ChiudiNomePostazione();

    private void ConfermaNomePostazione_Click(object sender, RoutedEventArgs e) => ConfermaNomePostazione();

    private void CampoNomePostazione_Tasto(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ConfermaNomePostazione();
        else if (e.Key == Key.Escape) ChiudiNomePostazione();
        else return;
        e.Handled = true;
    }

    private void ConfermaNomePostazione()
    {
        string modo = modoPostazione;
        if (modo.Length == 0) return;

        string nome = CampoNomePostazione.Text.Trim();
        if (nome.Length == 0)
        {
            Registra("Una postazione senza nome non si distingue dalle altre.", true);
            return;
        }

        string vecchio = postazioneInNome;
        var gia = CercaPostazione(nome);
        if (gia is not null && !(modo == "rinomina" && string.Equals(gia.Nome, vecchio, StringComparison.OrdinalIgnoreCase)))
        {
            Registra($"C'e' gia' una postazione che si chiama « {nome} ».", true);
            return;
        }

        ChiudiNomePostazione();

        if (modo == "rinomina")
        {
            if (CercaPostazione(vecchio) is not { } p) return;
            bool attiva = ReferenceEquals(p, PostazioneAttiva());
            p.Nome = nome;
            if (attiva) settings.PostazioneAttiva = nome;
            ScriviImpostazioni();
            Registra($"« {vecchio} » adesso si chiama « {nome} ».");
            return;
        }

        // La nuova parte da com'e' adesso: si entra e si cambia solo quel che e' diverso.
        ScriviImpostazioni();
        settings.Postazioni.Add(new Postazione { Nome = nome });
        settings.PostazioneAttiva = nome;
        ScriviImpostazioni();
        ListaPostazioni.SelectedItem = ListaPostazioni.Items.Cast<ListBoxItem>().FirstOrDefault(v => (string)v.Tag == nome);
        Registra($"Postazione « {nome} »: le scelte che fai adesso restano sue.");
    }
}
