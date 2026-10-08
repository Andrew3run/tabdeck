# Mette a riposo tutto quello che e' elencato in bloccati.txt.
#
#   .\alleggerisci.ps1              blocca tutto
#   .\alleggerisci.ps1 -Filtro sec  blocca solo quelli che contengono "sec"
#
# "pm block" nasconde il pacchetto e gli impedisce di ripartire all'avvio: non
# disinstalla niente, e .\ripristina.ps1 rimette tutto com'era.
#
# Su qualche pacchetto privilegiato (/system/priv-app) Android 4.4 rifiuta il
# blocco e risponde "new blocked state: false": su questo tablet capita solo a
# Google Play Services. Non c'e' un giro da adb che lo aggiri —
#   - "pm disable-user" fa cadere il package manager e produce un bugreport;
#   - "pm block pacchetto/componente" sembra funzionare ma e' una presa in
#     giro: pm legge la stringa intera come nome di pacchetto, non lo trova, e
#     stampa "true" perche' un pacchetto inesistente per lui e' gia' bloccato.
#     Si verifica in un secondo: "pm block pippo.pluto" risponde true anche lui.
# Restano le Impostazioni sul tablet, a mano; lo script lo dice a fine corsa.

param([string]$Filtro = '')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$adb = Join-Path $root '..\..\tools\platform-tools\adb.exe'
$elenco = Join-Path $root 'bloccati.txt'

if (-not (Test-Path $elenco)) { throw "Manca $elenco" }

# Il tablet giusto, non il primo che risponde. Qui si mette a riposo meta' del
# sistema: sbagliare dispositivo, con l'E960 dell'altro progetto attaccato allo
# stesso PC, vorrebbe dire rimetterlo insieme a mano pacchetto per pacchetto.
. (Join-Path $root '..\..\tools\dispositivo.ps1')
$serial = Get-TabletDelProgetto -Adb $adb
if (-not $serial) { exit 1 }

$pacchetti = Get-Content $elenco | Where-Object { $_.Trim() -ne '' }
if ($Filtro) { $pacchetti = $pacchetti | Where-Object { $_ -like "*$Filtro*" } }

# Quello che il tablet ha davvero installato, bloccati compresi (-u): senza -u
# i pacchetti gia' messi a riposo sembrerebbero disinstallati e lo script li
# salterebbe, rendendosi inutile dalla seconda esecuzione in poi.
$installati = (& $adb -s $serial shell 'pm list packages -u') -split "`n" |
    ForEach-Object { ($_ -replace '^package:', '').Trim() } | Where-Object { $_ }

function Get-MemLiberaMB {
    $mem = (& $adb -s $serial shell 'cat /proc/meminfo') -split "`n"
    $libera = 0
    foreach ($riga in $mem) {
        if ($riga -match '^MemFree:\s+(\d+)') { $libera = [int]$matches[1] }
    }
    return [math]::Round($libera / 1024)
}

$primaMB = Get-MemLiberaMB
Write-Host "`nMemoria libera adesso: $primaMB MB" -ForegroundColor Cyan
Write-Host "Blocco $($pacchetti.Count) pacchetti"
Write-Host ("-" * 52)

$fatti = 0; $assenti = 0; $ostinati = @()
foreach ($p in $pacchetti) {
    if ($installati -notcontains $p) { $assenti++; continue }
    $esito = (& $adb -s $serial shell "pm block $p") -join ' '
    if ($esito -match 'new blocked state: true') {
        $fatti++
    } else {
        $ostinati += $p
        Write-Host ("  rifiutato  {0}" -f $p) -ForegroundColor Yellow
    }
}
Write-Host "`n  bloccati: $fatti   non installati: $assenti   rifiutati: $($ostinati.Count)"

# --- chiudo quello che era gia' in esecuzione -------------------------------
Write-Host "`nChiudo i processi gia' avviati" -ForegroundColor Cyan
foreach ($p in $pacchetti) {
    if ($installati -contains $p) { & $adb -s $serial shell "am force-stop $p" | Out-Null }
}
Start-Sleep -Seconds 3

$dopoMB = Get-MemLiberaMB
Write-Host ("-" * 52)
Write-Host ("  prima  {0} MB liberi" -f $primaMB)
Write-Host ("  dopo   {0} MB liberi" -f $dopoMB)
$guadagno = $dopoMB - $primaMB
if ($guadagno -gt 0) { Write-Host ("  guadagno: +{0} MB" -f $guadagno) -ForegroundColor Green }

if ($ostinati.Count -gt 0) {
    Write-Host "`nQuesti vanno spenti a mano sul tablet:" -ForegroundColor Yellow
    $ostinati | ForEach-Object { Write-Host "  $_" }
    Write-Host "  Impostazioni > Gestione applicazioni > Tutte > [app] > Disattiva"
    Write-Host "  (la schermata giusta si apre anche da qui:"
    Write-Host "   adb shell am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:NOME)"
}

Write-Host "`nRiavvia il tablet per la misura vera: e' all'avvio che questi si ripresentano."
