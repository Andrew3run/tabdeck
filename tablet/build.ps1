# Compila TabDeck senza Gradle: aapt2 -> javac -> d8 -> zipalign -> apksigner.
#
# Gradle qui non servirebbe a nulla: l'app non ha una sola dipendenza esterna,
# e una build completa cosi' dura due secondi invece di un minuto.
#
#   .\build.ps1              compila
#   .\build.ps1 -Install     compila e installa sul tablet collegato
#   .\build.ps1 -Install -SetHome ...e propone TabDeck come Home
#
# Le estensioni non stanno qui: ognuna e' un progetto a se', fuori da questa
# cartella, e arriva sul tablet dal PC. Questo APK e' TabDeck e basta.

param(
    [switch]$Install,
    [switch]$SetHome,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

# Gli strumenti della JVM (keytool, apksigner) scrivono avvisi su stderr anche
# quando finiscono bene. Con $ErrorActionPreference = 'Stop', PowerShell 5.1 li
# trasforma in errori terminanti: la firma riusciva e lo script moriva subito
# dopo, senza arrivare all'installazione. Qui la preferenza si abbassa per la
# sola chiamata, e a decidere resta il codice di uscita.
#
# L'output del comando va a schermo con Out-Host e non nella pipeline: senza,
# la funzione restituirebbe le righe stampate *piu'* il codice di uscita, e chi
# chiama si troverebbe un array. Con adb, che su stdout scrive "Success", quel
# vettore non vuoto faceva dichiarare fallita un'installazione riuscita.
function Invoke-Tollerante {
    param([scriptblock]$Comando)
    $prima = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Comando | Out-Host } finally { $ErrorActionPreference = $prima }
    return $LASTEXITCODE
}

$root = $PSScriptRoot
$out  = Join-Path $root 'build'

# --- individuazione degli strumenti ---------------------------------------
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" }
if (-not (Test-Path $sdk)) { throw "SDK Android non trovato in '$sdk'. Imposta ANDROID_HOME." }

$buildTools = Get-ChildItem (Join-Path $sdk 'build-tools') -Directory |
    Sort-Object { [version]($_.Name -replace '[^0-9.].*$','') } | Select-Object -Last 1
if (-not $buildTools) { throw "Nessun build-tools nell'SDK." }

$androidJar = Join-Path $sdk 'platforms\android-19\android.jar'
if (-not (Test-Path $androidJar)) {
    throw "Manca la platform android-19. Installala con:`n  $sdk\cmdline-tools\latest\bin\sdkmanager.bat ""platforms;android-19"""
}

$aapt2     = Join-Path $buildTools.FullName 'aapt2.exe'
$d8        = Join-Path $buildTools.FullName 'd8.bat'
$zipalign  = Join-Path $buildTools.FullName 'zipalign.exe'
$apksigner = Join-Path $buildTools.FullName 'apksigner.bat'
$adb       = Join-Path $root '..\tools\platform-tools\adb.exe'
. (Join-Path $root '..\tools\dispositivo.ps1')

# keytool serve solo alla prima build, per creare il keystore. Non e' nel PATH:
# il javapath di Oracle espone java.exe ma non il resto del JDK.
$keytool = (Get-Command keytool -ErrorAction SilentlyContinue).Source
if (-not $keytool) {
    $probe = @()
    $javaExe = (Get-Command java -ErrorAction SilentlyContinue).Source
    if ($javaExe) {
        $real = (Get-Item $javaExe).Target
        if ($real) { $probe += (Split-Path $real -Parent) }
        $probe += (Split-Path $javaExe -Parent)
    }
    if ($env:JAVA_HOME) { $probe += (Join-Path $env:JAVA_HOME 'bin') }
    $probe += (Get-ChildItem 'C:\Program Files\Java' -Directory -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'bin' })
    $keytool = $probe |
        Where-Object { $_ -and (Test-Path (Join-Path $_ 'keytool.exe')) } |
        Select-Object -First 1 |
        ForEach-Object { Join-Path $_ 'keytool.exe' }
}

Write-Host "build-tools : $($buildTools.Name)"
Write-Host "platform    : android-19 (compilare contro il 19 impedisce di usare API piu' recenti di KitKat)"

# --- pulizia ---------------------------------------------------------------
if ($Clean -and (Test-Path $out)) { Remove-Item $out -Recurse -Force }
$dirs = @('res','gen','classes','dex','apk')
foreach ($d in $dirs) { New-Item -ItemType Directory -Force (Join-Path $out $d) | Out-Null }

# --- 1. risorse ------------------------------------------------------------
Write-Host "`n[1/6] compilazione risorse"
$resFiles = Get-ChildItem (Join-Path $root 'res') -Recurse -File
& $aapt2 compile -o (Join-Path $out 'res') $resFiles.FullName
if ($LASTEXITCODE) { throw "aapt2 compile fallito" }

Write-Host "[2/6] link risorse e manifest"
$flat = Get-ChildItem (Join-Path $out 'res') -Filter *.flat -File
$baseApk = Join-Path $out 'apk\base.apk'
& $aapt2 link `
    -I $androidJar `
    --manifest (Join-Path $root 'AndroidManifest.xml') `
    --java (Join-Path $out 'gen') `
    --min-sdk-version 19 `
    --target-sdk-version 19 `
    -o $baseApk `
    $flat.FullName
if ($LASTEXITCODE) { throw "aapt2 link fallito" }

# --- 2. java ---------------------------------------------------------------
Write-Host "[3/6] compilazione Java"
# La cartella delle classi si svuota: senza, una build -SenzaPlugin dopo una
# normale si porterebbe dietro le classi del plugin rimaste li'.
Remove-Item (Join-Path $out 'classes\*') -Recurse -Force -ErrorAction SilentlyContinue
$sources = @()
$sources += (Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.java).FullName
$sources += (Get-ChildItem (Join-Path $out 'gen') -Recurse -Filter *.java -ErrorAction SilentlyContinue).FullName
$srcList = Join-Path $out 'sources.txt'
# Argfile di javac: niente BOM (verrebbe letto come parte del primo percorso) e
# percorsi fra virgolette con i backslash raddoppiati, perche' dentro le
# virgolette javac tratta il backslash come carattere di escape.
$quoted = $sources | ForEach-Object { '"' + ($_ -replace '\\', '\\') + '"' }
[System.IO.File]::WriteAllLines($srcList, $quoted, (New-Object System.Text.UTF8Encoding($false)))

# -source/-target 8 invece di --release 8: --release imporrebbe le librerie del
# JDK, mentre qui la piattaforma e' android.jar e basta.
& javac -source 8 -target 8 -nowarn -encoding UTF-8 `
    -bootclasspath $androidJar -classpath $androidJar `
    -d (Join-Path $out 'classes') "@$srcList"
if ($LASTEXITCODE) { throw "javac fallito" }

# --- 3. dex ----------------------------------------------------------------
Write-Host "[4/6] conversione in DEX"
# Le classi entrano in d8 dentro un jar invece che una per una sulla riga di
# comando. Ogni classe interna e ogni classe anonima e' un file a se': passati
# tutti insieme sfondano gli 8191 caratteri di Windows, e l'errore che ne esce
# ("La riga di comando e' troppo lunga") non nomina d8 e sembra un guasto suo.
# L'argfile "@lista", che con javac funziona, qui no: d8 non toglie le
# virgolette e muore con "internal error".
#
# Il jar lo fa .NET e non lo strumento "jar" del JDK: quello non e' nel PATH per
# lo stesso motivo per cui non lo e' keytool - il javapath di Oracle espone
# java.exe e nient'altro. Un jar e' uno zip, e qui dentro non serve altro.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$jar = Join-Path $out 'classes.jar'
if (Test-Path $jar) { Remove-Item $jar }
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $out 'classes'), $jar)
& $d8 --min-api 19 --lib $androidJar --output (Join-Path $out 'dex') $jar
if ($LASTEXITCODE) { throw "d8 fallito" }

# --- 4. impacchettamento ---------------------------------------------------
Write-Host "[5/6] inserimento classes.dex nell'APK"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($baseApk, 'Update')
try {
    $existing = $zip.GetEntry('classes.dex')
    if ($existing) { $existing.Delete() }
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, (Join-Path $out 'dex\classes.dex'), 'classes.dex')
} finally { $zip.Dispose() }

$aligned = Join-Path $out 'apk\aligned.apk'
& $zipalign -f 4 $baseApk $aligned
if ($LASTEXITCODE) { throw "zipalign fallito" }

# --- 5. firma --------------------------------------------------------------
Write-Host "[6/6] firma"
$ks = Join-Path $root 'keystore\tabdeck.jks'
if (-not (Test-Path $ks)) {
    if (-not $keytool) { throw "keytool non trovato: serve un JDK per creare il keystore di firma." }
    Write-Host "      genero un keystore di sviluppo"
    $exit = Invoke-Tollerante { & $keytool -genkeypair -keystore $ks -alias tabdeck -keyalg RSA -keysize 2048 `
        -validity 10000 -storepass tabdeck -keypass tabdeck -dname 'CN=TabDeck, O=Personale' | Out-Null }
    if ($exit) { throw "keytool fallito" }
}

$final = Join-Path $out 'TabDeck.apk'
# v1 obbligatoria: KitKat non conosce gli schemi di firma successivi.
$exit = Invoke-Tollerante { & $apksigner sign --ks $ks --ks-pass pass:tabdeck --key-pass pass:tabdeck `
    --v1-signing-enabled true --v2-signing-enabled true `
    --min-sdk-version 19 --out $final $aligned }
if ($exit) { throw "apksigner fallito" }

$size = [math]::Round((Get-Item $final).Length / 1KB, 1)
Write-Host "`nFatto: $final ($size KB)" -ForegroundColor Green

# --- installazione ---------------------------------------------------------
if ($Install) {
    # Il tablet giusto, non il primo: con l'E960 dell'altro progetto attaccato
    # allo stesso PC, "il primo" e' una lotteria che si perde in silenzio.
    $serial = Get-TabletDelProgetto -Adb $adb
    if (-not $serial) {
        Write-Warning "Nessun SM-T210 autorizzato. Attiva il debug USB e accetta la richiesta sul tablet."
        exit 1
    }
    Write-Host "`nInstallazione sul tablet $serial"
    # Anche adb scrive l'avanzamento su stderr: stessa tolleranza di apksigner.
    $exit = Invoke-Tollerante { & $adb -s $serial install -r $final }
    if ($exit) { throw "installazione fallita" }

    if ($SetHome) {
        Write-Host "Apro la scelta della Home: seleziona TabDeck e conferma 'Sempre'."
        & $adb -s $serial shell am start -a android.intent.action.MAIN -c android.intent.category.HOME | Out-Null
    } else {
        & $adb -s $serial shell am start -n dev.tabdeck/.MainActivity | Out-Null
    }
}
