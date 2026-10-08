# Prepara la cartella da portare su un altro PC.
#
# Dentro ci finisce tutto quello che serve e niente che si debba scaricare:
# il programma con .NET dentro, adb, il driver dello schermo virtuale, l'APK
# del tablet, e gli script che li mettono a posto dall'altra parte.
#
#   .\pacchetto.ps1                   il pacchetto, in .\pacchetto\TabDeck
#   .\pacchetto.ps1 -Zip              e anche il .zip da mandare in giro
#   .\pacchetto.ps1 -Exe              e anche TabDeck-Setup.exe, uno solo da
#                                     dare a chi non vuole sapere niente
#   .\pacchetto.ps1 -ConConfig        ci mette dentro il deck, le icone e le luci
#   .\pacchetto.ps1 -ConChiavi        ...con le chiavi delle lampade (serve -ConConfig)
#   .\pacchetto.ps1 -ConDriverUsb     esporta da qui il driver USB del tablet
#   .\pacchetto.ps1 -Leggero          senza .NET dentro: 2 MB invece di 150
#   .\pacchetto.ps1 -Dove D:\roba     da un'altra parte
#   .\pacchetto.ps1 -Estensioni a.tabdeck,b.tabdeck   e accanto queste estensioni
#
# -ConDriverUsb vuole un PowerShell come amministratore: legge il magazzino
# driver di Windows. Tutto il resto no.

param(
    [switch]$Zip,
    [switch]$Exe,
    [switch]$ConConfig,
    [switch]$ConChiavi,
    [switch]$ConDriverUsb,
    [switch]$Leggero,
    [string]$Dove,
    [string[]]$Estensioni
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $Dove) { $Dove = Join-Path $root 'pacchetto\TabDeck' }
$sorgentiScript = Join-Path $root 'pc\pacchetto'
$CasaDriver = 'C:\VirtualDisplayDriver'

function Scrivi { param([string]$T, [string]$Colore = 'Gray') Write-Host $T -ForegroundColor $Colore }

Write-Host ""
Scrivi "TabDeck - preparazione del pacchetto" 'Cyan'
Scrivi ("-" * 52)

# Si riparte puliti: un pacchetto con dentro l'avanzo di quello di prima e' il
# modo piu' facile di portare su un altro PC un file che qui non esiste piu'.
if (Test-Path $Dove) { Remove-Item $Dove -Recurse -Force }
New-Item -ItemType Directory -Force $Dove | Out-Null

# --- 1. il programma ------------------------------------------------------

$app = Join-Path $Dove 'app'
$selfContained = (-not $Leggero).ToString().ToLower()

Scrivi "  compilo   $(if ($Leggero) { 'senza .NET dentro' } else { 'con .NET dentro (ci mette un minuto)' })"
& dotnet publish (Join-Path $root 'pc\TabDeck.App') `
    -c Release -r win-x64 --self-contained $selfContained `
    -o $app --nologo -v q | Out-Host
if ($LASTEXITCODE) { throw "la compilazione non e' andata a buon fine" }

# I .pdb servono a chi sviluppa, non a chi installa: sono meta' dei megabyte
# che restano dopo .NET.
Get-ChildItem $app -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force

$peso = [math]::Round((Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Scrivi "  app       $((Get-ChildItem $app -Recurse -File).Count) file, $peso MB" 'Green'

# --- 2. adb ---------------------------------------------------------------

$adbDa = Join-Path $root 'tools\platform-tools'
if (Test-Path $adbDa) {
    $adbA = Join-Path $Dove 'tools\platform-tools'
    New-Item -ItemType Directory -Force $adbA | Out-Null
    Copy-Item (Join-Path $adbDa '*') $adbA -Recurse -Force
    Scrivi "  adb       tools\platform-tools" 'Green'
} else {
    Scrivi "  adb       non trovato in tools\platform-tools: il cavo non funzionera'" 'Yellow'
}

# Chi sceglie il tablet giusto. Viaggia col pacchetto perche' di la' gli script
# di tablet\sistema lo cercano dove sta qui, un piano sopra: stessa cartella,
# stesso percorso relativo, nessuna riga da cambiare in mezzo.
$scelta = Join-Path $root 'tools\dispositivo.ps1'
if (Test-Path $scelta) {
    New-Item -ItemType Directory -Force (Join-Path $Dove 'tools') | Out-Null
    Copy-Item $scelta (Join-Path $Dove 'tools') -Force
}

# --- 3. il driver dello schermo virtuale ---------------------------------

# Senza, il tablet resta un deck: Windows non lascia trascinare una finestra
# dove non c'e' un monitor, e il monitor lo crea questo driver.
$driverA = Join-Path $Dove 'driver\schermo'
if (Test-Path (Join-Path $CasaDriver 'MttVDD.inf')) {
    New-Item -ItemType Directory -Force $driverA | Out-Null
    Copy-Item (Join-Path $CasaDriver '*') $driverA -Force
    Scrivi "  schermo   Virtual Display Driver, da $CasaDriver" 'Green'
} else {
    Scrivi "  schermo   $CasaDriver non c'e': il pacchetto restera' senza driver" 'Yellow'
    Scrivi "            si prende da github.com/VirtualDrivers/Virtual-Display-Driver" 'Yellow'
}

# --- 4. il driver USB del tablet -----------------------------------------

# Su Windows 11 il tablet spesso viene riconosciuto da solo; su una macchina
# dove non succede serve il driver Samsung, e il modo onesto di portarselo
# dietro e' esportare quello che qui funziona gia'.
if ($ConDriverUsb) {
    $io = [Security.Principal.WindowsIdentity]::GetCurrent()
    $capo = (New-Object Security.Principal.WindowsPrincipal $io).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $capo) {
        Scrivi "  usb       -ConDriverUsb vuole un PowerShell come amministratore: salto" 'Yellow'
    } else {
        $usbA = Join-Path $Dove 'driver\usb'
        New-Item -ItemType Directory -Force $usbA | Out-Null

        $quanti = 0
        try {
            $trovati = @(Get-WindowsDriver -Online -ErrorAction Stop |
                Where-Object {
                    $_.ProviderName -match 'Samsung|Google|Android' -or
                    $_.ClassName -match 'AndroidUsbDeviceClass'
                })
            foreach ($d in $trovati) {
                $nome = [IO.Path]::GetFileNameWithoutExtension($d.Driver)
                $fuori = Join-Path $usbA $nome
                New-Item -ItemType Directory -Force $fuori | Out-Null
                & pnputil.exe /export-driver $d.Driver "$fuori" | Out-Null
                if ($LASTEXITCODE -eq 0) { $quanti++ } else { Remove-Item $fuori -Recurse -Force }
            }
        } catch {
            Scrivi "  usb       non riesco a leggere i driver installati: $($_.Exception.Message)" 'Yellow'
        }

        if ($quanti -gt 0) {
            Scrivi "  usb       $quanti driver esportati" 'Green'
        } else {
            Remove-Item $usbA -Recurse -Force -ErrorAction SilentlyContinue
            Scrivi "  usb       niente da esportare: qui il tablet lo riconosce Windows da solo" 'Yellow'
        }
    }
}

# --- 5. l'app del tablet --------------------------------------------------

$apk = Join-Path $root 'tablet\build\TabDeck.apk'
if (Test-Path $apk) {
    $apkA = Join-Path $Dove 'tablet'
    New-Item -ItemType Directory -Force $apkA | Out-Null
    Copy-Item $apk $apkA -Force
    $kb = [math]::Round((Get-Item $apk).Length / 1KB)
    Scrivi "  tablet    TabDeck.apk, $kb KB" 'Green'
} else {
    Scrivi "  tablet    nessun APK compilato: prima tablet\build.ps1" 'Yellow'
}

# --- 5ter. le estensioni --------------------------------------------------

# Accanto, non dentro: TabDeck parte senza, e chi le vuole le installa dalla
# pagina Estensioni. Le estensioni sono progetti a se', fuori da questa cartella:
# si portano solo i file .tabdeck passati con -Estensioni.
if ($Estensioni) {
    $estA = Join-Path $Dove 'estensioni'
    New-Item -ItemType Directory -Force $estA | Out-Null
    foreach ($e in $Estensioni) {
        if (-not (Test-Path $e)) { Scrivi "  estensione manca $e" 'Yellow'; continue }
        Copy-Item $e $estA -Force
        Scrivi "  estensione $(Split-Path $e -Leaf)" 'Green'
    }
}

# --- 5bis. la pulizia del sistema del tablet ------------------------------

# alleggerisci.ps1, ripristina.ps1, batteria.ps1 e i due elenchi. Vanno di la'
# com'e' qui - tablet\sistema, con adb e dispositivo.ps1 due piani sopra - cosi'
# gli script sono gli stessi file, non una copia da tenere allineata.
$sistemaDa = Join-Path $root 'tablet\sistema'
if (Test-Path $sistemaDa) {
    $sistemaA = Join-Path $Dove 'tablet\sistema'
    New-Item -ItemType Directory -Force $sistemaA | Out-Null
    Copy-Item (Join-Path $sistemaDa '*') $sistemaA -Recurse -Force
    $quanti = (Get-Content (Join-Path $sistemaDa 'bloccati.txt') |
               Where-Object { $_.Trim() -ne '' }).Count
    Scrivi "  sistema   alleggerisci e ripristina, $quanti pacchetti in elenco" 'Green'
} else {
    Scrivi "  sistema   manca tablet\sistema: il pacchetto restera' senza pulizia" 'Yellow'
}

# --- 6. la configurazione -------------------------------------------------

# Di suo il pacchetto parte vuoto: il deck di chi lo riceve e' il suo. Con
# -ConConfig si porta dietro questo, che e' quello che si vuole quando il PC
# nuovo e' il proprio.
if ($ConConfig) {
    $configDa = Join-Path $root 'config'
    $configA = Join-Path $Dove 'config'
    New-Item -ItemType Directory -Force $configA | Out-Null
    Copy-Item (Join-Path $configDa '*') $configA -Recurse -Force

    # La password di OBS e il gettone di Twitch valgono quanto le chiavi delle
    # lampade: restano qui con loro, e di la' si ricollega dall'editor.
    $streaming = Join-Path $configA 'streaming.json'
    if ((Test-Path $streaming) -and (-not $ConChiavi)) {
        Remove-Item $streaming -Force
        Scrivi "  config    streaming.json lasciato fuori (OBS e Twitch si ricollegano di la')" 'Gray'
    }

    $luci = Join-Path $configA 'luci.json'
    if ((Test-Path $luci) -and (-not $ConChiavi)) {
        # La chiave locale di una lampada e' l'unica cosa segreta del progetto:
        # con quella chiunque sia sulla rete di casa accende le luci. Fuori di
        # qui non ci va, a meno di chiederlo apposta.
        $dentro = Get-Content $luci -Raw | ConvertFrom-Json
        $tolte = 0
        foreach ($l in @($dentro.Luci)) {
            if ($l.Chiave) { $l.Chiave = ''; $tolte++ }
        }
        ($dentro | ConvertTo-Json -Depth 12) | Set-Content $luci -Encoding UTF8
        Scrivi "  config    copiata; $tolte chiavi delle lampade tolte" 'Green'
        Scrivi "            (di la' si rifanno con 'Rileva chiavi', o rifai con -ConChiavi)" 'Gray'
    } else {
        Scrivi "  config    copiata$(if ($ConChiavi) { ', chiavi delle lampade comprese' })" 'Green'
        if ($ConChiavi) {
            Scrivi "            attento a dove finisce: dentro ci sono le chiavi di casa" 'Yellow'
        }
    }
} else {
    Scrivi "  config    non copiata: di la' si parte da un deck vuoto" 'Gray'
}

# --- 7. gli script e il leggimi ------------------------------------------

# I .bat sono la porta d'ingresso: di la' un doppio clic su un .ps1 lo apre nel
# Blocco note, e l'installazione vuole l'amministratore. I .ps1 restano, sotto,
# per le opzioni.
foreach ($f in @('Installa.ps1', 'Disinstalla.ps1', 'Avvio.ps1', 'Tablet.ps1',
                 'Installa.bat', 'Disinstalla.bat', 'Tablet.bat',
                 'Alleggerisci.bat', 'Ripristina.bat',
                 'LEGGIMI.txt', 'GUIDA.md')) {
    $da = Join-Path $sorgentiScript $f
    if (Test-Path $da) { Copy-Item $da $Dove -Force }
    else { Scrivi "  manca     $f in pc\pacchetto" 'Yellow' }
}

$quando = Get-Date -Format 'yyyy-MM-dd HH:mm'
@(
    "TabDeck",
    "creato il $quando su $env:COMPUTERNAME",
    "programma:  $(if ($Leggero) { 'leggero, vuole .NET Desktop Runtime 10 sul PC' } else { '.NET compreso, non serve altro' })",
    "driver:     $(if (Test-Path $driverA) { 'Virtual Display Driver compreso' } else { 'nessuno' })",
    "config:     $(if ($ConConfig) { if ($ConChiavi) { 'compresa, chiavi comprese' } else { 'compresa, senza chiavi' } } else { 'non compresa' })"
) | Set-Content (Join-Path $Dove 'versione.txt') -Encoding UTF8

Scrivi "  script    Installa, Disinstalla, Avvio, Tablet (.ps1 e .bat)," 'Green'
Scrivi "            Alleggerisci e Ripristina, LEGGIMI, GUIDA" 'Green'

# --- 8. lo zip ------------------------------------------------------------

$totale = [math]::Round((Get-ChildItem $Dove -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)

# Non Compress-Archive: su PS 5.1 tiene in memoria quello che comprime, e con
# quattrocento file e centocinquanta megabyte ci mette minuti quando non muore.
# L'archivio non si chiama $zip: PowerShell non distingue le maiuscole, e
# $zip sarebbe l'interruttore -Zip qui sopra.
function Comprimi {
    param([string]$Cartella, [string]$Archivio)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path $Archivio) { Remove-Item $Archivio -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $Cartella, $Archivio, [IO.Compression.CompressionLevel]::Optimal, $false)
}

$archivio = "$Dove.zip"

if ($Zip -or $Exe) {
    Scrivi "  comprimo  (un paio di minuti)"
    Comprimi -Cartella $Dove -Archivio $archivio
    $zipMb = [math]::Round((Get-Item $archivio).Length / 1MB)
    Scrivi "  zip       $archivio, $zipMb MB" 'Green'
}

# --- 9. l'exe -------------------------------------------------------------

# Un file solo, da dare a chi non vuole sapere niente di cartelle e di
# PowerShell: doppio clic, Windows chiede il consenso, e' installato.
#
# Lo impacchetta IExpress, che sta dentro Windows da sempre: nessuno strumento
# da installare per fare l'installatore, che sarebbe un giro un po' comico.
# IExpress sa mettere dentro solo file sciolti, quindi quello che ci va e' il
# .zip di sopra piu' Avvia.cmd, che lo apre e chiama Installa.ps1.
$setup = Join-Path (Split-Path $Dove -Parent) 'TabDeck-Setup.exe'

if ($Exe) {
    $iexpress = Join-Path $env:SystemRoot 'System32\iexpress.exe'
    $avvia = Join-Path $sorgentiScript 'Avvia.cmd'

    if (-not (Test-Path $iexpress)) {
        Scrivi "  exe       iexpress.exe non c'e' su questa macchina: salto" 'Yellow'
    } elseif (-not (Test-Path $avvia)) {
        Scrivi "  exe       manca pc\pacchetto\Avvia.cmd: salto" 'Yellow'
    } else {
        # Il banco sta in una cartella senza spazi, e non accanto al pacchetto:
        # IExpress vuole il percorso della ricetta nudo, senza virgolette
        # attorno - se ce le trova, cerca un file che si chiama proprio cosi',
        # non lo trova, ed esce con zero senza scrivere niente e senza dire
        # niente. Nudo pero' vuol dire che uno spazio lo spezzerebbe in due, e
        # questa cartella si chiama "Progetti Personali".
        $banco = ''
        foreach ($posto in @((Join-Path $env:TEMP 'tabdeck-banco'),
                             (Join-Path $env:SystemDrive 'tabdeck-banco'))) {
            if ($posto -notmatch ' ') { $banco = $posto; break }
        }
        if (-not $banco) { $banco = Join-Path $env:SystemDrive 'tabdeck-banco' }

        if (Test-Path $banco) { Remove-Item $banco -Recurse -Force }
        New-Item -ItemType Directory -Force $banco | Out-Null

        Copy-Item $archivio (Join-Path $banco 'TabDeck.zip') -Force
        Copy-Item $avvia $banco -Force

        # Anche l'exe nasce li' e viene portato qui dopo, per la stessa ragione.
        $nato = Join-Path $banco 'TabDeck-Setup.exe'
        if (Test-Path $setup) { Remove-Item $setup -Force }

        # Il .sed e' la ricetta che IExpress legge al posto della procedura
        # guidata. Va scritto in ANSI: con la firma dell'UTF-8 davanti, la
        # prima riga non viene riconosciuta e l'exe non si fa.
        $sed = Join-Path $banco 'TabDeck.sed'
        @(
            '[Version]',
            'Class=IEXPRESS',
            'SEDVersion=3',
            '[Options]',
            'PackagePurpose=InstallApp',
            'ShowInstallProgramWindow=1',
            'HideExtractAnimation=1',
            'UseLongFileName=1',
            'InsideCompressed=0',
            'CAB_FixedSize=0',
            'CAB_ResvCodeSigning=0',
            'RebootMode=N',
            'InstallPrompt=%InstallPrompt%',
            'DisplayLicense=%DisplayLicense%',
            'FinishMessage=%FinishMessage%',
            'TargetName=%TargetName%',
            'FriendlyName=%FriendlyName%',
            'AppLaunched=%AppLaunched%',
            'PostInstallCmd=%PostInstallCmd%',
            'AdminQuietInstCmd=',
            'UserQuietInstCmd=',
            'SourceFiles=SourceFiles',
            '[Strings]',
            'InstallPrompt=Installare TabDeck su questo PC?',
            'DisplayLicense=',
            'FinishMessage=',
            "TargetName=$nato",
            'FriendlyName=TabDeck',
            'AppLaunched=cmd.exe /c Avvia.cmd',
            'PostInstallCmd=<None>',
            'FILE0="TabDeck.zip"',
            'FILE1="Avvia.cmd"',
            '[SourceFiles]',
            "SourceFiles0=$banco\",
            '[SourceFiles0]',
            '%FILE0%=',
            '%FILE1%='
        ) | Set-Content $sed -Encoding Ascii

        Scrivi "  impacchetto (IExpress ricomprime: ci mette qualche minuto)"
        # Start-Process e non &: iexpress e' un programma con finestra, e
        # PowerShell non aspetta i programmi con finestra. Lanciato con &,
        # la riga dopo cercava un exe che makecab stava ancora scrivendo, e
        # il pacchetto sembrava fallito ogni volta pur riuscendo sempre.
        Start-Process -FilePath $iexpress -ArgumentList '/N', '/Q', $sed -Wait

        if (Test-Path $nato) {
            Move-Item $nato $setup -Force
            $mb = [math]::Round((Get-Item $setup).Length / 1MB)
            Scrivi "  exe       $setup, $mb MB" 'Green'
            Remove-Item $banco -Recurse -Force
        } else {
            Scrivi "  exe       IExpress non l'ha prodotto; il banco resta in $banco" 'Red'
        }
    }
}

Write-Host ""
Scrivi "Fatto: $Dove, $totale MB" 'Green'
Write-Host ""
if ($Exe -and (Test-Path $setup)) {
    Write-Host "Sull'altro PC basta TabDeck-Setup.exe: doppio clic, consenso, fatto."
    Write-Host "La cartella e il .zip restano per chi preferisce vedere cosa installa."
} else {
    Write-Host "Sull'altro PC: copia la cartella e fai doppio clic su Installa.bat, poi,"
    Write-Host "col tablet attaccato, su Tablet.bat. Chi vuole le opzioni apre un PowerShell"
    Write-Host "come amministratore dentro la cartella e lancia .\Installa.ps1."
}
