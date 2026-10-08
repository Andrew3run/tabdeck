# Dove se ne va la batteria del tablet.
#
#   .\batteria.ps1              fotografia: stato, impostazioni, chi tiene sveglio
#   .\batteria.ps1 -Misura 30   misura vera: 30 minuti di consumo, a cavo staccato
#
# Non tocca niente: legge e basta.
#
# La fotografia serve a trovare le impostazioni sbagliate; la misura serve a
# sapere se cambiarle e' servito. Fare la seconda senza la prima e' guardare un
# numero senza sapere da cosa dipende.
#
# Sul contatore di corrente: /sys/class/power_supply/battery/current_now su
# questo tablet e' in mA con segno, ma viene da un fuel gauge lento e non serve
# a confrontare due stati a un minuto di distanza - provato, i numeri di due
# stati diversi si sovrappongono. L'unica misura che tiene e' la percentuale
# che scende in mezz'ora, ed e' quella che fa -Misura.

param([int]$Misura = 0)

$ErrorActionPreference = 'Stop'
$adb = Join-Path $PSScriptRoot '..\..\tools\platform-tools\adb.exe'
if (-not (Test-Path $adb)) { throw "Manca adb in $adb" }

# Legge la batteria del tablet di questo progetto: con due tablet attaccati,
# "il primo" darebbe i numeri dell'altro senza dirlo.
. (Join-Path $PSScriptRoot '..\..\tools\dispositivo.ps1')
$serial = Get-TabletDelProgetto -Adb $adb
if (-not $serial) { exit 1 }

function Sh([string]$cmd) {
    $out = (& $adb -s $serial shell $cmd | Out-String)
    return $out.Replace("`r", '').Trim()
}

function Nodo([string]$nome) { Sh "cat /sys/class/power_supply/battery/$nome 2>/dev/null" }

function Riga($etichetta, $valore, $colore) {
    Write-Host ("  {0,-32}" -f $etichetta) -NoNewline
    Write-Host $valore -ForegroundColor $colore
}

if ((& $adb -s $serial get-state 2>&1) -notmatch 'device') { throw "Il tablet non risponde ad adb." }

# Letto una volta e riusato in due punti: quanto costa il pannello ha senso
# dirlo solo mentre il pannello e' acceso.
$power = @((Sh 'dumpsys power') -split "`n" | Where-Object { $_ -match 'mWakefulness|mStayOn=|mWakeLockSummary|mHoldingDisplaySuspendBlocker' })
$sveglio = [bool]($power -match 'mWakefulness=Awake')

# --- stato della batteria ---------------------------------------------------

$cap   = [int](Nodo 'capacity')
$volt  = [double](Nodo 'voltage_now') / 1000000
$stato = Nodo 'status'
$tempC = [double](Nodo 'temp') / 10
$corr  = [int](Nodo 'current_now')

Write-Host "`nBATTERIA" -ForegroundColor Cyan
Riga 'carica'       ("{0} %" -f $cap)      $(if ($cap -lt 20) { 'Red' } elseif ($cap -lt 50) { 'Yellow' } else { 'Green' })
Riga 'tensione'     ("{0:N2} V" -f $volt)  'Gray'
Riga 'stato'        $stato                 'Gray'
Riga 'temperatura'  ("{0:N1} C" -f $tempC) $(if ($tempC -gt 40) { 'Yellow' } else { 'Gray' })
Riga 'corrente ora' ("{0} mA (indicativo)" -f $corr) 'DarkGray'
Riga 'pannello'     $(if ($sveglio) { 'acceso' } else { 'spento' }) 'Gray'

# Il fuel gauge e' lento di qualche decina di secondi: appena dopo che lo
# schermo si spegne legge ancora il consumo di prima. Per non raccontare
# frottole l'avviso esce solo a pannello acceso.
if ($sveglio -and ($stato -eq 'Charging') -and $corr -lt 50) {
    Write-Host ''
    Write-Host "  Dice 'in carica' ma la corrente non sale: quello che entra dalla" -ForegroundColor Yellow
    Write-Host "  USB se ne va tutto nel pannello acceso. Cosi' il tablet non si" -ForegroundColor Yellow
    Write-Host "  ricarica - se il numero e' negativo si sta perfino scaricando," -ForegroundColor Yellow
    Write-Host "  attaccato alla corrente." -ForegroundColor Yellow
}

# --- le impostazioni che contano --------------------------------------------
#
# Ognuna ha un valore atteso e il motivo per cui e' quello. Il tablet esce di
# fabbrica con l'opposto: e' regolato per stare in mano dieci minuti, non
# appoggiato alla scrivania tutto il giorno.

$controlli = @(
    @{ Ambito = 'system'; Chiave = 'screen_off_timeout'
       Atteso = { param($v) [int]$v -le 60000 }
       Mostra = { param($v) '{0} s' -f ([int]$v / 1000) }
       Perche = 'pannello e retroilluminazione sono il primo consumo: mezz''ora accesa dopo un tocco e'' mezz''ora buttata' }

    @{ Ambito = 'global'; Chiave = 'stay_on_while_plugged_in'
       Atteso = { param($v) [int]$v -eq 0 }
       Mostra = { param($v) if ([int]$v -eq 0) { 'no' } else { "si (maschera $v)" } }
       Perche = 'a 3 lo schermo non si spegne MAI mentre il cavo e'' attaccato, e la scelta "tieni sveglio" dell''app non conta piu'' niente' }

    @{ Ambito = 'system'; Chiave = 'screen_brightness'
       Atteso = { param($v) [int]$v -le 100 }
       Mostra = { param($v) '{0}/255 ({1} %)' -f $v, [math]::Round([int]$v * 100 / 255) }
       Perche = 'la retroilluminazione scala quasi con il valore: da 74 a 30 per cento e'' circa meta'' corrente sul pannello' }

    @{ Ambito = 'global'; Chiave = 'wifi_sleep_policy'
       Atteso = { param($v) [int]$v -eq 0 }
       Mostra = { param($v) switch ([int]$v) { 0 { 'dorme con lo schermo' } 1 { 'dorme solo a batteria' } 2 { 'MAI' } default { "? ($v)" } } }
       Perche = 'a 2 la radio resta agganciata e sveglia sempre; a 0 dorme col pannello e si riaggancia in qualche secondo al tocco' }

    @{ Ambito = 'global'; Chiave = 'wifi_scan_always_enabled'
       Atteso = { param($v) [int]$v -eq 0 }
       Mostra = { param($v) if ([int]$v -eq 0) { 'no' } else { 'si' } }
       Perche = 'la scansione continua serve alla localizzazione, che qui non c''e''' }

    @{ Ambito = 'global'; Chiave = 'bluetooth_on'
       Atteso = { param($v) [int]$v -eq 0 }
       Mostra = { param($v) if ([int]$v -eq 0) { 'spento' } else { 'ACCESO' } }
       Perche = 'niente lo usa' }

    @{ Ambito = 'system'; Chiave = 'screen_brightness_mode'
       Atteso = { param($v) [int]$v -eq 0 }
       Mostra = { param($v) if ([int]$v -eq 0) { 'manuale' } else { 'automatica' } }
       Perche = 'l''automatica insegue il sensore e alza la luce piu'' del necessario; qui la luce giusta e'' una sola' }
)

Write-Host "`nIMPOSTAZIONI DI SISTEMA" -ForegroundColor Cyan
$daSistemare = @()
foreach ($c in $controlli) {
    $v = Sh "settings get $($c.Ambito) $($c.Chiave)"
    if ($v -eq 'null' -or $v -eq '') { $v = '0' }
    $ok = & $c.Atteso $v
    Riga $c.Chiave (& $c.Mostra $v) $(if ($ok) { 'Green' } else { 'Yellow' })
    if (-not $ok) { $daSistemare += $c }
}

if ($daSistemare.Count -gt 0) {
    Write-Host "`n  Da sistemare:" -ForegroundColor Yellow
    foreach ($c in $daSistemare) {
        Write-Host ("    {0}" -f $c.Chiave) -ForegroundColor Yellow
        Write-Host ("      {0}" -f $c.Perche) -ForegroundColor DarkGray
    }
}

# --- chi impedisce al processore di dormire ---------------------------------
#
# /sys/kernel/debug/wakeup_sources tiene il conto, dall'accensione, di quanto
# ogni sorgente ha tenuto il SoC fuori dalla sospensione. La colonna che conta
# e' prevent_suspend_time: millisecondi in cui il tablet avrebbe potuto dormire
# e non ha dormito.

$uptimeMs = [double](((Sh 'cat /proc/uptime') -split '\s+')[0]) * 1000
$righe = (Sh 'cat /sys/kernel/debug/wakeup_sources') -split "`n" | Select-Object -Skip 1

$sorgenti = foreach ($r in $righe) {
    $campi = @($r -split '\s+' | Where-Object { $_ -ne '' })
    if ($campi.Count -lt 11) { continue }
    [pscustomobject]@{
        Nome   = $campi[0]
        Attiva = [int]$campi[1] -ne 0
        Attesa = [double]$campi[10]   # prevent_suspend_time
    }
}

Write-Host "`nCHI TIENE SVEGLIO IL TABLET" -ForegroundColor Cyan
Write-Host ("  su {0:N1} ore di accensione" -f ($uptimeMs / 3600000)) -ForegroundColor DarkGray
$top = @($sorgenti | Where-Object { $_.Attesa -gt 0 } | Sort-Object Attesa -Descending | Select-Object -First 8)
if ($top.Count -eq 0) {
    Write-Host "  nessuna: il tablet si sospende quando deve" -ForegroundColor Green
} else {
    foreach ($s in $top) {
        $quota = if ($uptimeMs -gt 0) { $s.Attesa * 100 / $uptimeMs } else { 0 }
        $coda = if ($s.Attiva) { '  <-- adesso' } else { '' }
        Riga $s.Nome ("{0,7:N1} min svegli  ({1:N0}% del tempo){2}" -f ($s.Attesa / 60000), $quota, $coda) `
             $(if ($quota -gt 20) { 'Yellow' } else { 'Gray' })
    }
    Write-Host ''
    Write-Host "  mv-udc e' la porta USB: finche' il cavo e' attaccato il tablet non" -ForegroundColor DarkGray
    Write-Host "  si sospende mai, ed e' cosi' per forza. mmc2 e' la radio WiFi." -ForegroundColor DarkGray
}

# Il filtro si fa in PowerShell e non con grep sul tablet: adb rimonta gli
# argomenti in una riga sola e le virgolette del pattern si perdono per strada.
if ($power.Count -gt 0) {
    Write-Host ''
    foreach ($r in $power) { Write-Host ("  " + $r.Trim()) -ForegroundColor DarkGray }
}

# --- misura -----------------------------------------------------------------

if ($Misura -le 0) {
    Write-Host "`nPer sapere quanto dura davvero:  .\batteria.ps1 -Misura 30" -ForegroundColor Cyan
    Write-Host "(alimentazione staccata, WiFi acceso, tablet lasciato com'e')`n" -ForegroundColor DarkGray
    return
}

if ($stato -eq 'Charging' -or $stato -eq 'Full') {
    Write-Host "`nIl tablet e' sotto carica: la misura non direbbe niente." -ForegroundColor Red
    Write-Host "Serve il cavo dati senza alimentazione, oppure adb sul WiFi" -ForegroundColor Red
    Write-Host "(adb tcpip 5555, poi adb connect IP:5555 e cavo staccato).`n" -ForegroundColor Red
    return
}

Write-Host ("`nMISURA - {0} minuti. Non toccare il tablet." -f $Misura) -ForegroundColor Cyan
$capIniziale = $cap
$inizio = Get-Date

for ($m = 1; $m -le $Misura; $m++) {
    Start-Sleep -Seconds 60
    Write-Host ("  {0,3} min   {1} %   {2:N2} V" -f $m, [int](Nodo 'capacity'), ([double](Nodo 'voltage_now') / 1000000)) -ForegroundColor DarkGray
}

$capFinale = [int](Nodo 'capacity')
$ore = ((Get-Date) - $inizio).TotalHours
$scesa = $capIniziale - $capFinale

Write-Host ("-" * 52)
Write-Host ("  scesa da {0}% a {1}% in {2:N0} minuti" -f $capIniziale, $capFinale, ($ore * 60))
if ($scesa -le 0) {
    Write-Host "  meno di un punto percentuale: ottimo, ma per un numero serio" -ForegroundColor Green
    Write-Host "  rilancia con -Misura 60." -ForegroundColor Green
} else {
    $perOra = $scesa / $ore
    Write-Host ("  consumo: {0:N1} % all'ora" -f $perOra) -ForegroundColor Cyan
    Write-Host ("  autonomia da pieno: {0:N1} ore" -f (100 / $perOra)) -ForegroundColor Cyan
    Write-Host ("  (batteria da 4000 mAh: circa {0:N0} mA medi)" -f ($perOra * 40)) -ForegroundColor DarkGray
}
Write-Host ''
