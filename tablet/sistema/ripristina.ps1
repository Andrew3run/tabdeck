# Rimette in servizio i pacchetti bloccati da alleggerisci, tutti o alcuni.
#
#   .\ripristina.ps1                    sblocca tutto quello in bloccati.txt
#   .\ripristina.ps1 -Filtro usb        sblocca solo quelli che contengono "usb"
#
# Serve a tornare indietro: niente di quello che e' stato fatto e' definitivo,
# "pm block" nasconde il pacchetto all'utente ma non lo disinstalla.

param([string]$Filtro = '')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$adb = Join-Path $root '..\..\tools\platform-tools\adb.exe'
$elenco = Join-Path $root 'bloccati.txt'

if (-not (Test-Path $elenco)) { throw "Manca $elenco" }

# Lo stesso tablet di alleggerisci.ps1, e nessun altro.
. (Join-Path $root '..\..\tools\dispositivo.ps1')
$serial = Get-TabletDelProgetto -Adb $adb
if (-not $serial) { exit 1 }

$pacchetti = Get-Content $elenco | Where-Object { $_.Trim() -ne '' }
if ($Filtro) { $pacchetti = $pacchetti | Where-Object { $_ -like "*$Filtro*" } }

Write-Host "Sblocco $($pacchetti.Count) pacchetti"
foreach ($p in $pacchetti) {
    & $adb -s $serial shell "pm unblock $p" | Out-Null
    Write-Host "  $p"
}

Write-Host "`nFatto. Riavvia il tablet per vederli tornare." -ForegroundColor Green
