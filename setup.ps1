# Operazioni sul tablet: ispezione, backup e diagnosi del collegamento.
#
#   .\setup.ps1 -Info      cosa c'e' sul tablet adesso
#   .\setup.ps1 -Backup    scarica /sdcard in .\backup-tablet
#   .\setup.ps1 -Check     verifica il collegamento pezzo per pezzo
#   .\setup.ps1 -Bloat     elenca le app Samsung disattivabili a mano
#   .\setup.ps1 -Ram       chi sta occupando la memoria, in ordine
#   .\setup.ps1 -Lighten   prova a disattivare il superfluo (reversibile)
#   .\setup.ps1 -Restore   riattiva tutto quello che -Lighten aveva spento
#   .\setup.ps1 -Provision configurazione completa dopo un reset di fabbrica
#
# Per compilare e installare l'app si usa tablet\build.ps1.

param(
    [switch]$Info,
    [switch]$Backup,
    [switch]$Check,
    [switch]$Bloat,
    [switch]$Ram,
    [switch]$Lighten,
    [switch]$Restore,
    [switch]$Provision
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$adb = Join-Path $root 'tools\platform-tools\adb.exe'
if (-not (Test-Path $adb)) { throw "adb non trovato in $adb" }
. (Join-Path $root 'tools\dispositivo.ps1')

# adb restituisce l'output riga per riga: PowerShell lo consegna come array e
# .Trim() su un array non fa quello che sembra. Qui si riduce sempre a stringa.
function Adb-Text {
    param([string]$Serial, [string]$Command)
    $out = & $adb -s $Serial shell $Command 2>$null
    if ($null -eq $out) { return '' }
    return (($out -join "`n").Trim())
}

function Get-Device {
    $lines = & $adb devices
    $ready = $lines | Select-String -Pattern '\tdevice$'
    # Non "il primo che risponde": quello del progetto. Da quando c'e' un
    # secondo tablet nella cartella accanto, il primo puo' essere l'altro.
    if ($ready) { return (Get-TabletDelProgetto -Adb $adb) }

    $unauth = $lines | Select-String -Pattern '\tunauthorized$'
    if ($unauth) {
        Write-Host ""
        Write-Host "Il tablet e' collegato ma NON autorizzato." -ForegroundColor Yellow
        Write-Host "Sblocca lo schermo: c'e' una richiesta 'Consenti debug USB'."
        Write-Host "Spunta 'Consenti sempre da questo computer' e tocca OK."
        return $null
    }

    Write-Host ""
    Write-Host "Nessun tablet visto da adb." -ForegroundColor Yellow
    Write-Host "  1. Impostazioni > Info sul dispositivo"
    Write-Host "  2. tocca 'Numero build' sette volte"
    Write-Host "  3. Impostazioni > Opzioni sviluppatore > Debug USB"
    Write-Host "  4. ricollega il cavo (attenzione: molti cavi sono di sola ricarica)"
    return $null
}

function Show-Info {
    $serial = Get-Device
    if (-not $serial) { return }

    Write-Host "`nTablet collegato: $serial" -ForegroundColor Green

    $props = @{
        'Modello'          = 'ro.product.model'
        'Nome in codice'   = 'ro.product.device'
        'Android'          = 'ro.build.version.release'
        'Livello API'      = 'ro.build.version.sdk'
        'Build'            = 'ro.build.display.id'
        'Stato bootloader' = 'ro.boot.verifiedbootstate'
    }
    foreach ($k in $props.Keys | Sort-Object) {
        $v = Adb-Text $serial "getprop $($props[$k])"
        if ($v) { Write-Host ("  {0,-16} {1}" -f $k, $v) }
    }

    # La memoria libera decide quanto margine ha l'app: il frame a piena area
    # occupa circa 1,2 MB, ma il decoder JPEG ne vuole altrettanti a picco.
    $mem = (Adb-Text $serial 'cat /proc/meminfo') -split "`n"
    foreach ($line in $mem) {
        if ($line -match '^(MemTotal|MemFree|MemAvailable):\s+(\d+)') {
            Write-Host ("  {0,-16} {1} MB" -f $matches[1], [math]::Round([int]$matches[2] / 1024))
        }
    }

    $installed = Adb-Text $serial 'pm list packages dev.tabdeck'
    Write-Host ("  {0,-16} {1}" -f 'TabDeck', $(if ($installed) { 'installato' } else { 'non installato' }))

    $size = Adb-Text $serial 'wm size'
    $density = Adb-Text $serial 'wm density'
    if ($size) { Write-Host ("  {0,-16} {1}" -f 'Schermo', ($size -replace 'Physical size:', '').Trim()) }
    if ($density) { Write-Host ("  {0,-16} {1}" -f 'Densita''', ($density -replace 'Physical density:', '').Trim()) }

    # Quante applicazioni di sistema girano davvero: e' la misura onesta del
    # peso di TouchWiz, piu' della RAM libera in un istante.
    $procs = (Adb-Text $serial 'ps') -split "`n" | Where-Object { $_ -match 'com\.(samsung|sec|android)' }
    Write-Host ("  {0,-16} {1}" -f 'Processi sistema', $procs.Count)
}

function Invoke-Backup {
    $serial = Get-Device
    if (-not $serial) { return }

    $dest = Join-Path $root 'backup-tablet'
    New-Item -ItemType Directory -Force $dest | Out-Null
    Write-Host "`nScarico /sdcard in $dest" -ForegroundColor Cyan
    Write-Host "Puo' volerci qualche minuto: USB 2.0 e schede microSD lente.`n"
    & $adb -s $serial pull /sdcard $dest
    if ($LASTEXITCODE) {
        Write-Warning "Il trasferimento non e' andato a buon fine. Controlla lo spazio libero sul PC."
    } else {
        $count = (Get-ChildItem $dest -Recurse -File -ErrorAction SilentlyContinue).Count
        Write-Host "`nFatto: $count file salvati." -ForegroundColor Green
        Write-Host "Adesso il reset di fabbrica non ti fa perdere niente."
    }
}

function Invoke-Check {
    Write-Host "`nDiagnosi del collegamento" -ForegroundColor Cyan
    Write-Host ("-" * 46)

    $serial = Get-Device
    if (-not $serial) { Write-Host "[1] tablet visibile          NO" -ForegroundColor Red; return }
    Write-Host "[1] tablet visibile          si ($serial)" -ForegroundColor Green

    $installed = Adb-Text $serial 'pm list packages dev.tabdeck'
    if (-not $installed) {
        Write-Host "[2] TabDeck installato       NO" -ForegroundColor Red
        Write-Host "    -> cd tablet; .\build.ps1 -Install -SetHome"
        return
    }
    Write-Host "[2] TabDeck installato       si" -ForegroundColor Green

    $focus = (Adb-Text $serial 'dumpsys window windows') -split "`n" |
        Select-String 'mCurrentFocus' | Select-Object -First 1
    $inFront = $focus -match 'dev\.tabdeck'
    if ($inFront) {
        Write-Host "[3] app in primo piano       si" -ForegroundColor Green
    } else {
        Write-Host "[3] app in primo piano       NO" -ForegroundColor Yellow
        Write-Host "    -> la avvio"
        & $adb -s $serial shell am start -n dev.tabdeck/.MainActivity | Out-Null
        Start-Sleep -Milliseconds 1500
    }

    # La socket astratta compare in /proc/net/unix con la chiocciola davanti:
    # se non c'e', il server dentro l'app non e' partito.
    $unix = (Adb-Text $serial 'cat /proc/net/unix') -split "`n" | Select-String 'tabdeck'
    if ($unix) {
        Write-Host "[4] socket in ascolto        si" -ForegroundColor Green
    } else {
        Write-Host "[4] socket in ascolto        NO" -ForegroundColor Red
        Write-Host "    -> l'app e' installata ma il suo server non parte."
        Write-Host "       Guarda il log:  tools\platform-tools\adb.exe logcat -s TabDeck.Link"
        return
    }

    # 18765 e non 8765: la 8765 del PC e' gia' occupata da un altro programma.
    & $adb -s $serial forward tcp:18765 localabstract:tabdeck | Out-Null
    if ($LASTEXITCODE) { Write-Host "[5] adb forward              NO" -ForegroundColor Red; return }
    Write-Host "[5] adb forward              si" -ForegroundColor Green

    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect('127.0.0.1', 18765)
        $tcp.Close()
        Write-Host "[6] connessione TCP          si" -ForegroundColor Green
        Write-Host "`nTutto a posto: apri l'applicazione con" -ForegroundColor Green
        Write-Host "  dotnet run --project pc\TabDeck.App"
    } catch {
        Write-Host "[6] connessione TCP          NO" -ForegroundColor Red
        Write-Host "    -> $($_.Exception.Message)"
        Write-Host "       Se la porta 18765 e' occupata, cambia UsbPort in config\tabdeck.json."
    }
}


# Solo roba rivolta all'utente: mai servizi di sistema, mai framework. Se una
# di queste sparisce, al massimo perdi un'app che non aprivi comunque.
$script:Superfluo = @(
    'com.sec.chaton',
    'com.vlingo.midas',                       # S Voice
    'com.sec.android.app.samsungapps',
    'com.samsung.groupcast',                  # Group Play
    'com.dropbox.android',
    'com.sec.android.app.storycam',           # Story Album
    'com.sec.android.app.videoplayer',
    'com.samsung.everglades.video',           # Video hub
    'com.sec.android.app.music',
    'com.infraware.polarisoffice5',
    'com.sec.android.widgetapp.diotek.smemo',
    'com.sec.android.app.kidsmode',
    'com.samsung.helphub',
    'com.sec.android.widgetapp.samsungapps',
    'com.sec.android.app.snsimagecache',
    'com.samsung.android.app.assistantmenu'
)

function Show-Ram {
    $serial = Get-Device
    if (-not $serial) { return }

    Write-Host "`nChi occupa la memoria, dal piu' ingombrante" -ForegroundColor Cyan
    Write-Host ("-" * 52)

    # dumpsys meminfo da' il PSS per processo: e' la misura onesta, perche'
    # ripartisce la memoria condivisa invece di contarla piu' volte.
    $lines = (Adb-Text $serial 'dumpsys meminfo') -split "`n"
    $inSection = $false
    $rows = @()
    foreach ($line in $lines) {
        if ($line -match 'Total PSS by process') { $inSection = $true; continue }
        if ($inSection) {
            # Fra una voce e l'altra ci sono righe vuote: si salta, e si esce
            # solo quando comincia una sezione diversa.
            if ($line.Trim() -eq '') { continue }
            if ($line -match '^\s*(?:Total PSS by|Total PSS:|Objects|SQL)') { break }
            if ($line -match '^\s*([\d,]+) kB:\s+(\S+)') {
                $rows += [pscustomobject]@{
                    MB   = [math]::Round([int]($matches[1] -replace ',', '') / 1024, 1)
                    Nome = $matches[2]
                }
            }
        }
    }

    if (-not $rows) {
        Write-Warning "dumpsys meminfo non ha restituito la sezione attesa."
        return
    }

    $rows | Select-Object -First 18 | ForEach-Object {
        $tag = ''
        if ($script:Superfluo -contains $_.Nome) { $tag = '  <- disattivabile' }
        if ($_.Nome -like '*launcher*') { $tag = '  <- sparisce con TabDeck come Home' }
        if ($_.Nome -like 'com.google.android.gms*') { $tag = '  <- sparisce togliendo l''account Google' }
        Write-Host ("  {0,7:N1} MB  {1}{2}" -f $_.MB, $_.Nome, $tag)
    }

    $totale = ($rows | Measure-Object -Property MB -Sum).Sum
    Write-Host ("`n  totale attribuito: {0:N0} MB su 832 MB" -f $totale)
}

function Set-Superfluo {
    param([string]$Action)   # disable oppure enable

    $serial = Get-Device
    if (-not $serial) { return }

    $verbo = if ($Action -eq 'disable') { 'Disattivo' } else { 'Riattivo' }
    Write-Host "`n$verbo le applicazioni superflue" -ForegroundColor Cyan
    Write-Host ("-" * 52)

    $installate = (Adb-Text $serial 'pm list packages') -split "`n" |
        ForEach-Object { ($_ -replace '^package:', '').Trim() }

    $ok = 0; $negati = 0; $assenti = 0
    foreach ($pkg in $script:Superfluo) {
        if ($installate -notcontains $pkg) { $assenti++; continue }
        $esito = Adb-Text $serial "pm $Action $pkg"
        if ($esito -match 'new state') {
            Write-Host ("  ok       {0}" -f $pkg) -ForegroundColor Green
            $ok++
        } elseif ($esito -match 'Permission Denial|SecurityException') {
            $negati++
        } else {
            Write-Host ("  ?        {0}: {1}" -f $pkg, $esito) -ForegroundColor DarkGray
        }
    }

    Write-Host "`n  riuscite: $ok   permesso negato: $negati   non installate: $assenti"

    if ($negati -gt 0) {
        Write-Host "`nSu Android 4.4 adb non ha il permesso di disattivare le app di" -ForegroundColor Yellow
        Write-Host "sistema: quelle vanno spente a mano da"
        Write-Host "  Impostazioni > Gestione applicazioni > Tutte > [app] > Disattiva"
    }
}


function Invoke-Provision {
    Write-Host "`nConfigurazione del tablet dopo il reset" -ForegroundColor Cyan
    Write-Host ("=" * 52)

    Write-Host "`nIn attesa del tablet (serve il debug USB riattivato)..."
    & $adb wait-for-device
    $serial = Get-Device
    if (-not $serial) { return }
    Write-Host "Tablet: $serial" -ForegroundColor Green

    # --- misura di partenza, prima di toccare qualsiasi cosa -----------------
    $liberaPrima = Get-MemFreeMB $serial
    Write-Host "`n[1/5] memoria libera a sistema appena resettato: $liberaPrima MB" -ForegroundColor Yellow

    # --- niente sospensione: e' il requisito "non si blocchi" ----------------
    Write-Host "[2/5] impedisco sospensione e blocco schermo"
    & $adb -s $serial shell "settings put global stay_on_while_plugged_in 3" | Out-Null
    & $adb -s $serial shell "settings put system screen_off_timeout 1800000" | Out-Null
    & $adb -s $serial shell "settings put secure lockscreen.disabled 1" | Out-Null
    $verifica = Adb-Text $serial 'settings get global stay_on_while_plugged_in'
    if ($verifica -ne '3') {
        Write-Warning "  stay_on_while_plugged_in vale '$verifica' invece di 3: lo schermo potrebbe ancora spegnersi."
    } else {
        Write-Host "      ok: sotto USB lo schermo non si spegne piu'" -ForegroundColor Green
    }

    # --- l'app ---------------------------------------------------------------
    $apk = Join-Path $root 'tablet\build\TabDeck.apk'
    if (-not (Test-Path $apk)) {
        Write-Host "[3/5] APK assente, la compilo"
        & (Join-Path $root 'tablet\build.ps1')
    }
    Write-Host "[3/5] installo TabDeck"
    $esito = (& $adb -s $serial install -r $apk) -join ' '
    if ($esito -notmatch 'Success') {
        Write-Warning "  installazione non riuscita: $esito"
        return
    }
    Write-Host "      installata" -ForegroundColor Green

    # --- Home ----------------------------------------------------------------
    Write-Host "[4/5] apro la scelta della schermata Home"
    Write-Host "      -> sul tablet scegli TabDeck e conferma 'Sempre'" -ForegroundColor Yellow
    & $adb -s $serial shell "am start -a android.intent.action.MAIN -c android.intent.category.HOME" | Out-Null
    Start-Sleep -Seconds 6

    # --- verdetto ------------------------------------------------------------
    & $adb -s $serial shell "am start -n dev.tabdeck/.MainActivity" | Out-Null
    Start-Sleep -Seconds 3
    $liberaDopo = Get-MemFreeMB $serial

    Write-Host "`n[5/5] risultato" -ForegroundColor Cyan
    Write-Host ("-" * 52)
    Write-Host ("  prima del reset      75 MB liberi")
    Write-Host ("  dopo il reset        {0} MB liberi" -f $liberaPrima)
    Write-Host ("  con TabDeck attivo   {0} MB liberi" -f $liberaDopo)
    $guadagno = $liberaDopo - 75
    if ($guadagno -gt 0) {
        Write-Host ("`n  guadagno netto: +{0} MB" -f $guadagno) -ForegroundColor Green
    }

    Write-Host "`nAdesso apri l'applicazione:" -ForegroundColor Green
    Write-Host "  dotnet run --project pc\TabDeck.App"
}

function Get-MemFreeMB {
    param([string]$Serial)
    $mem = (Adb-Text $Serial 'cat /proc/meminfo') -split "`n"
    $free = 0
    foreach ($line in $mem) {
        if ($line -match '^(MemFree|Cached|Buffers):\s+(\d+)') { $free += [int]$matches[2] }
    }
    return [math]::Round($free / 1024)
}

function Show-Bloat {
    $serial = Get-Device
    if (-not $serial) { return }

    # Su Android 4.4 il comando "pm disable" da adb non ha i permessi: la
    # disattivazione va fatta a mano. Qui si elenca solo cosa cercare.
    $targets = @(
        'chaton', 'svoice', 'samsungapps', 'hub', 'groupplay', 'dropbox',
        'storyalbum', 'gamehub', 'videohub', 'musichub', 'readershub',
        'polarisoffice', 'smemo', 'kieslibrary', 'allshare'
    )

    Write-Host "`nApp Samsung presenti fra quelle disattivabili:`n" -ForegroundColor Cyan
    $packages = (Adb-Text $serial 'pm list packages') -split "`n" |
        ForEach-Object { ($_ -replace '^package:', '').Trim() } | Where-Object { $_ }

    $found = $packages | Where-Object {
        $p = $_.ToLower()
        $targets | Where-Object { $p -like "*$_*" }
    }

    if ($found) {
        $found | Sort-Object | ForEach-Object { Write-Host "  $_" }
        Write-Host "`nSu Android 4.4 adb non ha il permesso di disattivarle."
        Write-Host "Vai su: Impostazioni > Gestione applicazioni > Tutte > [app] > Disattiva"
    } else {
        Write-Host "  nessuna trovata: gia' pulito, o gia' disattivate."
    }

    Write-Host "`nIl risparmio piu' grosso lo fa TabDeck come Home: TouchWiz non"
    Write-Host "viene proprio caricato, e sono decine di MB."
}

if (-not ($Info -or $Backup -or $Check -or $Bloat -or $Ram -or $Lighten -or $Restore -or $Provision)) {
    Write-Host "Uso:"
    Write-Host "  .\setup.ps1 -Info      cosa c'e' sul tablet adesso"
    Write-Host "  .\setup.ps1 -Backup    scarica /sdcard in .\backup-tablet"
    Write-Host "  .\setup.ps1 -Check     verifica il collegamento pezzo per pezzo"
    Write-Host "  .\setup.ps1 -Bloat     elenca le app Samsung disattivabili"
    Write-Host "  .\setup.ps1 -Ram       chi sta occupando la memoria, in ordine"
    Write-Host "  .\setup.ps1 -Lighten   prova a disattivare il superfluo (reversibile)"
    Write-Host "  .\setup.ps1 -Restore   riattiva quello che -Lighten aveva spento"
    Write-Host "  .\setup.ps1 -Provision configurazione completa dopo un reset"
    exit 0
}

if ($Info) { Show-Info }
if ($Backup) { Invoke-Backup }
if ($Check) { Invoke-Check }
if ($Bloat) { Show-Bloat }
if ($Ram) { Show-Ram }
if ($Lighten) { Set-Superfluo -Action 'disable' }
if ($Restore) { Set-Superfluo -Action 'enable' }
if ($Provision) { Invoke-Provision }
