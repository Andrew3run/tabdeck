# Mette TabDeck sul tablet, dal PC dove il pacchetto e' appena stato installato.
#
# Non serve l'SDK di Android: l'APK e' gia' compilato dentro il pacchetto, e adb
# sta accanto. Serve solo il cavo, e il debug USB acceso sul tablet.
#
#   .\Tablet.ps1                cerca il tablet, installa, e propone la Home
#   .\Tablet.ps1 -SenzaHome     installa e basta
#   .\Tablet.ps1 -Info          dice solo cosa vede
#
# Il tablet di questo progetto e' un Samsung Galaxy Tab 3 7.0, modello SM-T210.
# Con due tablet attaccati, "il primo che adb elenca" installerebbe sul tablet
# sbagliato senza dire niente: qui il modello si guarda.

param(
    [switch]$SenzaHome,
    [switch]$Info
)

$ErrorActionPreference = 'Stop'
$qui = $PSScriptRoot

$Modello = 'SM-T210'
$Pacchetto = 'dev.tabdeck'

function Scrivi { param([string]$T, [string]$Colore = 'Gray') Write-Host $T -ForegroundColor $Colore }

function Trova-File {
    param([string[]]$Candidati)
    foreach ($c in $Candidati) { if (Test-Path $c) { return (Resolve-Path $c).Path } }
    return $null
}

# Lo script vive nel pacchetto, ma dopo l'installazione lo si rilancia spesso
# dalla cartella installata: si guarda in tutte e due.
$adb = Trova-File @(
    (Join-Path $qui 'tools\platform-tools\adb.exe'),
    (Join-Path $qui '..\tools\platform-tools\adb.exe'),
    (Join-Path 'C:\TabDeck' 'tools\platform-tools\adb.exe'))

if (-not $adb) { throw "adb non trovato. Cercavo in tools\platform-tools accanto a questo script." }

$apk = Trova-File @(
    (Join-Path $qui 'tablet\TabDeck.apk'),
    (Join-Path $qui '..\tablet\TabDeck.apk'))

Write-Host ""
Scrivi "TabDeck - il tablet" 'Cyan'
Scrivi ("-" * 46)

$righe = & $adb devices | Select-String -Pattern '\tdevice$'
if (-not $righe) {
    $sospesi = & $adb devices | Select-String -Pattern '\tunauthorized$'
    if ($sospesi) {
        Write-Host ""
        Scrivi "Il tablet e' attaccato ma non autorizzato." 'Yellow'
        Write-Host "Sblocca lo schermo: c'e' una richiesta 'Consenti debug USB'."
        Write-Host "Spunta 'Consenti sempre da questo computer' e tocca OK."
        exit 1
    }
    Write-Host ""
    Scrivi "adb non vede nessun tablet." 'Yellow'
    Write-Host "  1. Impostazioni > Info sul dispositivo"
    Write-Host "  2. tocca 'Numero build' sette volte"
    Write-Host "  3. Impostazioni > Opzioni sviluppatore > Debug USB"
    Write-Host "  4. ricollega il cavo - molti cavi sono di sola ricarica"
    exit 1
}

$serial = $null
$altri = @()
foreach ($riga in $righe) {
    $s = ($riga -split "`t")[0].Trim()
    $m = ((& $adb -s $s shell getprop ro.product.model 2>$null) -join '') -replace '\s', ''
    if ($m -eq $Modello) { $serial = $s; break }
    $altri += "$s ($m)"
}

if (-not $serial) {
    Write-Host ""
    Scrivi "Questo pacchetto e' per un $Modello, e non e' fra quelli attaccati." 'Yellow'
    Write-Host "Vedo: $($altri -join ', ')"
    exit 1
}

$versione = ((& $adb -s $serial shell getprop ro.build.version.release) -join '').Trim()
$installato = ((& $adb -s $serial shell pm list packages $Pacchetto) -join '').Trim()
Scrivi "  tablet    $serial - Android $versione" 'Green'
Scrivi "  TabDeck   $(if ($installato) { 'installato' } else { 'non installato' })"

if ($Info) { exit 0 }

if (-not $apk) {
    Write-Host ""
    Scrivi "Nel pacchetto non c'e' tablet\TabDeck.apk." 'Yellow'
    Write-Host "Rifallo con pacchetto.ps1 dopo aver compilato l'app (tablet\build.ps1)."
    exit 1
}

Write-Host ""
Scrivi "Installo $apk"
# -r reinstalla tenendo i dati: il deck, le luci e le icone che il tablet si e'
# salvato restano dov'erano.
& $adb -s $serial install -r "$apk" | Out-Host
if ($LASTEXITCODE) { throw "installazione non riuscita" }

# WRITE_SETTINGS su Android 4.4 si concede all'installazione, ma le due voci che
# contano davvero per la batteria stanno fra le impostazioni protette e si danno
# da qui, una volta sola.
& $adb -s $serial shell settings put global stay_on_while_plugged_in 0 2>$null | Out-Null
& $adb -s $serial shell settings put global wifi_sleep_policy 2 2>$null | Out-Null
Scrivi "  corrente  'Rimani attivo' spento, WiFi che dorme a schermo spento" 'Green'

if ($SenzaHome) {
    & $adb -s $serial shell am start -n "$Pacchetto/.MainActivity" | Out-Null
    Scrivi "  aperto." 'Green'
} else {
    Write-Host ""
    Scrivi "Adesso sul tablet scegli TabDeck come schermata Home e conferma 'Sempre'." 'Cyan'
    & $adb -s $serial shell am start -a android.intent.action.MAIN -c android.intent.category.HOME | Out-Null
}

Write-Host ""
Scrivi "Fatto." 'Green'
