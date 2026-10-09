# Toglie da questo PC quello che Installa.ps1 ci aveva messo.
#
#   .\Disinstalla.ps1                      toglie tutto
#   .\Disinstalla.ps1 -Dove D:\TabDeck     se era stato messo altrove
#   .\Disinstalla.ps1 -TieniConfig         lascia config\ dov'e'
#   .\Disinstalla.ps1 -TieniDriver         lascia lo schermo virtuale installato
#
# Va lanciato da un PowerShell **come amministratore**.

param(
    [string]$Dove = 'C:\TabDeck',
    [switch]$TieniConfig,
    [switch]$TieniDriver
)

$ErrorActionPreference = 'Continue'
$CasaDriver = 'C:\VirtualDisplayDriver'

function Scrivi { param([string]$T, [string]$Colore = 'Gray') Write-Host $T -ForegroundColor $Colore }

$io = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $io).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "Serve un PowerShell come amministratore."
    exit 1
}

Write-Host ""
Scrivi "TabDeck - disinstallazione" 'Cyan'
Scrivi ("-" * 46)

# --- il programma aperto --------------------------------------------------

$vivi = @(Get-Process -Name 'TabDeck' -ErrorAction SilentlyContinue)
if ($vivi.Count -gt 0) {
    Scrivi "  TabDeck e' aperto: chiudilo prima (icona nell'area di notifica > Esci)." 'Yellow'
    exit 1
}

# --- le regole del firewall ----------------------------------------------

foreach ($nome in @('TabDeck', 'TabDeck - richiamo del tablet', 'TabDeck - ricerca del tablet')) {
    try {
        Get-NetFirewallRule -DisplayName $nome -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue
    } catch {
        & netsh.exe advfirewall firewall delete rule name="$nome" | Out-Null
    }
}
Scrivi "  firewall  regole tolte" 'Green'

# --- l'attivita' pianificata ---------------------------------------------

# Quella che faceva partire TabDeck elevato senza chiedere il consenso. Se
# restasse, resterebbe una voce che punta a un eseguibile che non c'e' piu'.
try {
    Unregister-ScheduledTask -TaskName 'TabDeck' -Confirm:$false -ErrorAction Stop
    Scrivi "  attivita' 'TabDeck' tolta dal pianificatore" 'Green'
} catch {
    Scrivi "  attivita' non c'era" 'Gray'
}

# --- i collegamenti -------------------------------------------------------

foreach ($lnk in @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\TabDeck.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TabDeck.lnk'))) {
    if (Test-Path $lnk) { Remove-Item $lnk -Force }
}
Scrivi "  icone     tolte" 'Green'

# --- lo schermo virtuale --------------------------------------------------

if ($TieniDriver) {
    Scrivi "  schermo   lasciato installato (-TieniDriver)" 'Yellow'
} else {
    $nodo = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue |
              Where-Object { $_.FriendlyName -like '*Virtual Display Driver*' })

    foreach ($d in $nodo) {
        # /remove-device c'e' da Windows 11 21H2. Dove non c'e', il dispositivo
        # resta e lo si toglie da Gestione dispositivi: si dice, non si finge.
        & pnputil.exe /remove-device "$($d.InstanceId)" | Out-Host
    }

    if ($nodo.Count -eq 0) { Scrivi "  schermo   non c'era" 'Gray' }
    else { Scrivi "  schermo   dispositivo rimosso" 'Green' }

    # E il driver dal magazzino, cercato per il file che porta il suo nome.
    $elenco = & pnputil.exe /enum-drivers
    $oem = $null
    for ($i = 0; $i -lt $elenco.Count; $i++) {
        if ($elenco[$i] -match '(oem\d+\.inf)') { $oem = $matches[1] }
        if ($elenco[$i] -match 'MttVDD\.inf' -and $oem) {
            & pnputil.exe /delete-driver $oem /uninstall /force | Out-Null
            Scrivi "  driver    $oem tolto dal magazzino" 'Green'
            break
        }
    }

    # Il percorso dei file, che Installa.ps1 scrive per tenerli dentro TabDeck.
    $chiave = 'HKLM:\SOFTWARE\MikeTheTech\VirtualDisplayDriver'
    if (Test-Path $chiave) {
        Remove-Item $chiave -Recurse -Force -ErrorAction SilentlyContinue
        $padre = Split-Path $chiave
        if (-not (Get-ChildItem $padre -ErrorAction SilentlyContinue)) {
            Remove-Item $padre -Force -ErrorAction SilentlyContinue
        }
        Scrivi "  driver    chiave VDDPATH tolta" 'Green'
    }

    # Le installazioni di prima tenevano i file qui.
    if (Test-Path $CasaDriver) {
        Remove-Item $CasaDriver -Recurse -Force -ErrorAction SilentlyContinue
        Scrivi "  driver    $CasaDriver buttata" 'Green'
    }
}

# --- la cartella ----------------------------------------------------------

if (-not (Test-Path $Dove)) {
    Scrivi "  cartella  $Dove non c'e'" 'Gray'
} elseif ($TieniConfig) {
    foreach ($pezzo in @('app', 'tools', 'driver', 'tablet',
                         'Tablet.ps1', 'Tablet.bat', 'Alleggerisci.bat', 'Ripristina.bat',
                         'LEGGIMI.txt', 'GUIDA.md')) {
        $p = Join-Path $Dove $pezzo
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Scrivi "  cartella  svuotata, config\ lasciata dov'e'" 'Green'
} else {
    Remove-Item $Dove -Recurse -Force
    Scrivi "  cartella  $Dove buttata, config compresa" 'Green'
}

Write-Host ""
Scrivi "Fatto." 'Green'
Write-Host "Sul tablet l'app resta: si toglie da li', o con"
Write-Host "    adb uninstall dev.tabdeck"
Write-Host "E se il tablet era stato alleggerito, resta alleggerito: i pacchetti Android"
Write-Host "bloccati si rimettono in servizio con Ripristina.bat, che pero' stava qui"
Write-Host "dentro ed e' appena stato buttato. Si riparte dalla cartella del pacchetto."
