using System.Diagnostics;
using System.Text;

namespace TabDeck;

/// <summary>
/// TabDeck che si aggiorna da solo. L'eseguibile in uso non si sovrascrive, e TabDeck gira
/// come amministratore: uno script normale non puo' chiuderlo, e aggiornarlo voleva dire
/// chiedere a una persona di farlo a mano ogni volta.
///
/// Chi costruisce mette la build nuova in pc\app-nuovo e, per ultimo, il file « pronto ».
/// TabDeck se ne accorge, lancia uno script (con i suoi stessi permessi) che aspetta la sua
/// chiusura, copia la build al suo posto e lo riapre, e poi esce come da « Esci ».
/// Chi puo' scrivere in pc\app-nuovo puo' gia' scrivere in pc\app: nessun permesso in piu'.
/// </summary>
public partial class MainWindow
{
    private const string Segnale = "pronto";
    private FileSystemWatcher? aggiornamento;
    private bool inAggiornamento;

    private void PreparaAggiornamento()
    {
        string attuale = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string? padre = Path.GetDirectoryName(attuale);
        if (padre is null) return;
        string nuovo = Path.Combine(padre, "app-nuovo");
        Directory.CreateDirectory(nuovo);

        aggiornamento = new FileSystemWatcher(nuovo, Segnale) { NotifyFilter = NotifyFilters.FileName };
        aggiornamento.Created += (_, _) => Dispatcher.BeginInvoke(() => Aggiorna(attuale, nuovo));
        aggiornamento.EnableRaisingEvents = true;

        // Un segnale rimasto da prima: una build pronta mentre TabDeck era chiuso.
        if (File.Exists(Path.Combine(nuovo, Segnale))) Dispatcher.BeginInvoke(() => Aggiorna(attuale, nuovo));
    }

    private void Aggiorna(string attuale, string nuovo)
    {
        if (inAggiornamento || !File.Exists(Path.Combine(nuovo, Segnale))) return;
        if (!File.Exists(Path.Combine(nuovo, "TabDeck.exe")))
        {
            Registra("Segnale di aggiornamento senza TabDeck.exe in app-nuovo: ignorato.", true);
            File.Delete(Path.Combine(nuovo, Segnale));
            return;
        }
        inAggiornamento = true;

        string script = Path.Combine(Path.GetTempPath(), "tabdeck-aggiorna.ps1");
        // robocopy torna 0-7 quando va bene: 8 e oltre e' un errore, e allora non si riapre
        // una copia a meta' ma quella di prima, e l'errore resta scritto accanto.
        File.WriteAllText(script, $$"""
            $ErrorActionPreference = 'Continue'
            try { Wait-Process -Id {{Environment.ProcessId}} -Timeout 60 } catch { }
            Start-Sleep -Milliseconds 500
            robocopy "{{nuovo}}" "{{attuale}}" /E /XF {{Segnale}} /R:5 /W:1 /NFL /NDL /NJH /NJS | Out-Null
            if ($LASTEXITCODE -ge 8) { "robocopy $LASTEXITCODE $(Get-Date)" | Out-File "{{Path.Combine(nuovo, "errore.txt")}}" }
            Remove-Item "{{Path.Combine(nuovo, Segnale)}}" -Force -ErrorAction SilentlyContinue
            Start-Process "{{Path.Combine(attuale, "TabDeck.exe")}}"
            """, new UTF8Encoding(false));

        Registra("Build nuova in app-nuovo: TabDeck si chiude, si aggiorna e si riapre.");
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        EsciDavvero();
    }
}
