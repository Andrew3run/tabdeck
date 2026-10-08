# Come TabDeck si avvia, e perche' Windows smette di chiedere il consenso.
#
# TabDeck ha bisogno dei permessi di amministratore per due cose sole: accendere
# e spegnere il monitor virtuale (sono Enable-PnpDevice e Disable-PnpDevice) e
# mandare i tocchi del tablet anche alle finestre che girano elevate. Senza
# elevazione il consenso lo chiederebbe a ogni "Manda lo schermo" e a ogni
# "Ferma": piu' richieste, non meno.
#
# La strada per non sentirselo chiedere piu' e' dirlo a Windows una volta sola,
# da amministratore, invece che ogni volta: un'attivita' pianificata **senza
# orario e senza avvio automatico**, che esiste solo per portare con se' il
# livello dei permessi. Niente parte da solo: l'attivita' si muove quando la
# chiami tu premendo il collegamento, e non un momento prima.
#
# Va detto per intero: da quel momento chiunque possa premere quel collegamento
# fa partire un programma elevato senza vedere nessuna domanda. Su una macchina
# propria e' quello che si vuole; su una condivisa e' una cosa da sapere.
#
#   .\Avvio.ps1 -Ese C:\TabDeck\app\TabDeck.exe    attivita' e collegamenti
#   .\Avvio.ps1 -Ese ... -Diretto                  collegamenti che vanno
#                                                  dritti all'exe: il consenso
#                                                  torna a essere chiesto
#   .\Avvio.ps1 -Togli                             via l'attivita' e i collegamenti
#
# Va lanciato da un PowerShell come amministratore.

param(
    [string]$Ese,
    [switch]$Diretto,
    [switch]$Togli,
    [switch]$SenzaCollegamenti
)

$ErrorActionPreference = 'Stop'

$Attivita = 'TabDeck'
$Collegamenti = @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\TabDeck.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TabDeck.lnk')
)

function Scrivi { param([string]$T, [string]$Colore = 'Gray') Write-Host $T -ForegroundColor $Colore }

$io = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $io).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Serve un PowerShell come amministratore."
    exit 1
}

# --- via quello che c'era -------------------------------------------------

# Si rifa' sempre da zero: un'attivita' che punta a un exe spostato e' peggio
# di nessuna attivita', perche' il collegamento sembra rotto senza dire niente.
try {
    Unregister-ScheduledTask -TaskName $Attivita -Confirm:$false -ErrorAction Stop
    Scrivi "  attivita' quella di prima tolta"
} catch {
    # Non c'era: e' il caso normale la prima volta.
}

if ($Togli) {
    foreach ($lnk in $Collegamenti) { if (Test-Path $lnk) { Remove-Item $lnk -Force } }
    Scrivi "  icone     tolte" 'Green'
    Scrivi "Fatto: TabDeck si apre dall'eseguibile, e Windows torna a chiedere il consenso." 'Green'
    exit 0
}

if (-not $Ese) { throw "Dimmi dov'e' TabDeck.exe: -Ese C:\TabDeck\app\TabDeck.exe" }
if (-not (Test-Path $Ese)) { throw "Non trovo $Ese" }
$Ese = (Resolve-Path $Ese).Path
$cartella = Split-Path $Ese -Parent

# --- l'attivita' ----------------------------------------------------------

$viaAttivita = -not $Diretto

if ($viaAttivita) {
    # Chi ha davanti lo schermo, non chi ha lanciato questo script: si puo'
    # installare da un PowerShell elevato con le credenziali di un altro
    # amministratore, e la finestra deve comparire sulla scrivania di chi usa
    # il tablet, non su quella di nessuno.
    $utente = ''
    try { $utente = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).UserName } catch { }
    if (-not $utente) { $utente = "$env:USERDOMAIN\$env:USERNAME" }

    $azione = New-ScheduledTaskAction -Execute $Ese -WorkingDirectory $cartella

    $principale = New-ScheduledTaskPrincipal -UserId $utente `
        -LogonType Interactive -RunLevel Highest

    # Tre cose che i valori di fabbrica sbagliano per un programma come questo:
    #   - il limite di tre giorni ammazzerebbe TabDeck mentre e' nel vassoio;
    #   - "una sola istanza" impedirebbe alla seconda copia di nascere, e la
    #     seconda copia serve: e' quella che dice alla prima di farsi vedere;
    #   - a batteria non deve fermarsi, che e' meta' dei portatili.
    $impostazioni = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -MultipleInstances Parallel

    # Nessun trigger: l'attivita' non ha un orario e non si accende all'accesso.
    # Esiste solo per dire "questo programma parte con i permessi pieni".
    $compito = New-ScheduledTask -Action $azione -Principal $principale `
        -Settings $impostazioni `
        -Description 'Apre TabDeck con i permessi pieni. Nessun orario: parte solo quando lo chiedi.'

    Register-ScheduledTask -TaskName $Attivita -InputObject $compito -Force | Out-Null
    Scrivi "  attivita' '$Attivita' per $utente, senza orario" 'Green'
}

# --- i collegamenti -------------------------------------------------------

if ($SenzaCollegamenti) {
    Scrivi "  icone     saltate (-SenzaCollegamenti)" 'Yellow'
} else {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($percorso in $Collegamenti) {
        $lnk = $shell.CreateShortcut($percorso)
        if ($viaAttivita) {
            $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\schtasks.exe'
            $lnk.Arguments = "/run /tn `"$Attivita`""
            # L'icona va detta a parte: quella di schtasks e' un ingranaggio.
            $lnk.IconLocation = "$Ese,0"
            # Ridotto a icona: schtasks e' un programma da riga di comando e la
            # sua finestra si apre comunque, ma cosi' e' un lampo e non un
            # rettangolo nero in mezzo allo schermo.
            $lnk.WindowStyle = 7
        } else {
            $lnk.TargetPath = $Ese
            $lnk.IconLocation = $Ese
        }
        $lnk.WorkingDirectory = $cartella
        $lnk.Description = 'Uno schermo in piu'' e una console da scrivania'
        $lnk.Save()
    }
    Scrivi "  icone     menu Start e desktop" 'Green'
}

if ($viaAttivita) {
    Scrivi "Fatto: da adesso il collegamento apre TabDeck senza chiedere niente." 'Green'
} else {
    Scrivi "Fatto: il collegamento va dritto all'eseguibile, con la domanda di Windows." 'Green'
}
