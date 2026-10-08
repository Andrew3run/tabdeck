using System.Windows.Threading;
using TabDeck.Estensioni;

namespace TabDeck;

/// <summary>
/// La cartella d'arrivo delle estensioni: un .tabdeck messo in config\estensioni\arrivo si
/// installa da solo, come « Installa da file ».
///
/// Serve a chi costruisce un'estensione: TabDeck gira come amministratore, e un programma
/// normale non puo' ne' premergli il bottone ne' chiuderlo per fargli rileggere le cartelle.
/// Non apre una porta nuova: chi scrive qui dentro scrive gia' in config\estensioni, che
/// TabDeck carica a ogni avvio.
/// </summary>
public partial class MainWindow
{
    private FileSystemWatcher? arrivi;
    private readonly DispatcherTimer attesaArrivi = new() { Interval = TimeSpan.FromSeconds(1.5) };

    private void PreparaArrivi()
    {
        string cartella = Path.Combine(configDir, "estensioni", "arrivo");
        Directory.CreateDirectory(cartella);
        attesaArrivi.Tick += (_, _) =>
        {
            attesaArrivi.Stop();
            InstallaArrivi(cartella);
        };

        arrivi = new FileSystemWatcher(cartella, "*" + GestoreEstensioni.EstensioneFile)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        // Un file si scrive a pezzi e ogni pezzo e' un evento: si installa quando smette di cambiare.
        arrivi.Created += (_, _) => RimandaArrivi();
        arrivi.Changed += (_, _) => RimandaArrivi();
        arrivi.Renamed += (_, _) => RimandaArrivi();
        arrivi.EnableRaisingEvents = true;

        // Quello arrivato mentre TabDeck era chiuso.
        InstallaArrivi(cartella);
    }

    private void RimandaArrivi() => Dispatcher.BeginInvoke(() =>
    {
        attesaArrivi.Stop();
        attesaArrivi.Start();
    });

    private void InstallaArrivi(string cartella)
    {
        if (estensioni is null) return;
        foreach (var file in Directory.GetFiles(cartella, "*" + GestoreEstensioni.EstensioneFile))
        {
            try
            {
                estensioni.Installa(file);
                File.Delete(file);
            }
            catch (IOException)
            {
                // Ancora in scrittura o tenuto aperto da chi lo copia: si riprova fra poco.
                attesaArrivi.Start();
                return;
            }
            catch (Exception e)
            {
                Registra($"Estensione arrivata ma non installata ({Path.GetFileName(file)}): {e.Message}", true);
                // Messo da parte, o si riproverebbe a ogni evento della cartella.
                try { File.Move(file, file + ".scartato", overwrite: true); }
                catch (IOException) { }
            }
        }
    }
}
