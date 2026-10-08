using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace TabDeck;

/// <summary>
/// TabDeck che parte da solo quando si accede a Windows.
///
/// <para><b>Perche' un'attivita' pianificata e non la chiave Run.</b> TabDeck
/// chiede l'amministratore nel manifest, e Windows all'accesso non fa partire
/// dalla chiave Run i programmi che vogliono l'elevazione: li salta in
/// silenzio. Un'attivita' con « privilegi piu' elevati » invece parte gia'
/// elevata, senza la domanda — la stessa ragione per cui esiste l'attivita'
/// « TabDeck » del collegamento sul desktop.</para>
///
/// <para><b>Perche' un'altra attivita' e non un orario su quella.</b> Quella
/// del collegamento e' promessa senza orario in README e in Avvio.ps1, e chi la
/// toglie con <c>avvio.ps1 -Togli</c> non deve portarsi via anche questa, ne'
/// il contrario. Due nomi, due scelte.</para>
///
/// <para>Si crea e si toglie da qui, e non c'e' nessuna copia della scelta nel
/// file delle impostazioni: la spunta legge l'Utilita' di pianificazione ogni
/// volta, cosi' dice la verita' anche se l'attivita' e' stata tolta a mano.</para>
/// </summary>
public static class Accesso
{
    public const string NomeAttivita = "TabDeck Accesso";

    /// <summary>Con questo argomento TabDeck sa di essere partito all'accesso.</summary>
    public const string Argomento = "--accesso";

    /// <summary>Vero se l'attivita' c'e'.</summary>
    public static bool Presente() => Schtasks($"/Query /TN \"{NomeAttivita}\"").Uscita == 0;

    /// <summary>
    /// Crea l'attivita', o la rifa' se c'era: un eseguibile spostato lascerebbe
    /// un'attivita' che non apre niente e non lo dice. Stringa vuota se e'
    /// andata, altrimenti il motivo.
    /// </summary>
    public static string Metti()
    {
        string exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0) return "non so dove sta TabDeck.exe";

        string file = Path.Combine(Path.GetTempPath(), "tabdeck-accesso.xml");
        try
        {
            // UTF-16, come dichiara l'intestazione: schtasks rifiuta un file che
            // dice una codifica e ne usa un'altra.
            File.WriteAllText(file, Xml(exe), Encoding.Unicode);
            var (uscita, testo) = Schtasks($"/Create /F /TN \"{NomeAttivita}\" /XML \"{file}\"");
            return uscita == 0 ? "" : Pulito(testo, "schtasks non ha creato l'attivita'");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>Toglie l'attivita'. Stringa vuota se e' andata.</summary>
    public static string Togli()
    {
        var (uscita, testo) = Schtasks($"/Delete /F /TN \"{NomeAttivita}\"");
        return uscita == 0 ? "" : Pulito(testo, "schtasks non ha tolto l'attivita'");
    }

    /// <summary>
    /// L'attivita' scritta per intero. Tre scelte, contro i valori di fabbrica:
    /// <list type="bullet">
    /// <item>nessun limite di durata — quello di tre giorni chiuderebbe TabDeck
    ///   mentre sta accanto all'orologio;</item>
    /// <item>priorita' normale — quella di fabbrica e' « sotto il normale », e
    ///   lo schermo mandato al tablet ne risentirebbe;</item>
    /// <item>parte anche a batteria, e non si ferma se la corrente se ne va.</item>
    /// </list>
    /// Dieci secondi dopo l'accesso: l'area di notifica deve esserci gia', se no
    /// l'icona non ha dove andare.
    /// </summary>
    private static string Xml(string exe)
    {
        string utente = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string comando = SecurityElement.Escape(exe);
        string cartella = SecurityElement.Escape(Path.GetDirectoryName(exe) ?? "");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Apre TabDeck all'accesso a Windows. Si toglie da TabDeck, in Gestione &gt; Impostazioni.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{utente}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{utente}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{comando}</Command>
                  <Arguments>{Argomento}</Arguments>
                  <WorkingDirectory>{cartella}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Pulito(string testo, string altrimenti)
    {
        string t = testo.Trim();
        return t.Length > 0 ? t : altrimenti;
    }

    /// <summary>
    /// schtasks scrive nella codifica della console, non in UTF-8: senza dirlo,
    /// « attivita' » nei suoi messaggi d'errore uscirebbe coi segni sbagliati.
    /// </summary>
    private static (int Uscita, string Testo) Schtasks(string argomenti)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding console;
        try
        {
            console = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            console = Encoding.Default;
        }

        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), argomenti)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = console,
            StandardErrorEncoding = console,
        };

        try
        {
            using var p = Process.Start(info)!;
            string fuori = p.StandardOutput.ReadToEnd();
            string errore = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(10000)) return (-1, "schtasks non ha risposto");
            return (p.ExitCode, errore.Length > 0 ? errore : fuori);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, e.Message);
        }
    }
}
