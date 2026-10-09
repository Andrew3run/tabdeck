# Il driver dello schermo virtuale: dove si prende e come si prepara.
#
# Lo usano pacchetto.ps1, per metterlo nel pacchetto, e Installa.ps1, che lo
# scarica da se' quando il pacchetto non ce l'ha. Si carica con ". .\SchermoVirtuale.ps1".
#
# E' il Virtual Display Driver (github.com/VirtualDrivers/Virtual-Display-Driver),
# firmato da SignPath Foundation con CA GlobalSign: Windows lo accetta senza
# toccare nessun archivio di certificati. La versione e' fissata, con la sua
# impronta: uno zip diverso da quello provato qui non si installa.
#
# Lo zip della 25.7.23 si chiama "x86" ma dentro c'e' il driver amd64
# (MttVDD.inf dichiara NTamd64, MttVDD.dll e' PE x64): e' un nome sbagliato
# della release, non il pacchetto sbagliato.

$VddVersione = '25.7.23'
$VddUrl      = 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip'
$VddSha256   = 'E24210692B442B39AF763536330CE78B423F19342B7A7792C26DE3944E418B3A'

# Dove il driver cerca vdd_settings.xml: questa chiave, e se manca
# C:\VirtualDisplayDriver. Letta dentro MttVDD.dll, non documentata altrove.
$VddChiave   = 'HKLM:\SOFTWARE\MikeTheTech\VirtualDisplayDriver'

# Mette in $Cartella i file del driver, scaricandoli se non ci sono. Vero se
# alla fine ci sono.
function Get-DriverSchermo {
    param([string]$Cartella)

    if (Test-Path (Join-Path $Cartella 'MttVDD.inf')) { return $true }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("tabdeck-vdd-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $zip = Join-Path $tmp 'vdd.zip'
        # PowerShell 5.1 parte con TLS 1.0, che GitHub rifiuta.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $vecchio = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -Uri $VddUrl -OutFile $zip -UseBasicParsing }
        finally { $ProgressPreference = $vecchio }

        $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($hash -ne $VddSha256) { throw "lo zip scaricato non e' quello atteso (SHA-256 $hash)" }

        $fuori = Join-Path $tmp 'x'
        Expand-Archive $zip $fuori -Force
        $inf = Get-ChildItem $fuori -Recurse -Filter 'MttVDD.inf' | Select-Object -First 1
        if (-not $inf) { throw "nello zip non c'e' MttVDD.inf" }

        $firma = Get-AuthenticodeSignature (Join-Path $inf.DirectoryName 'mttvdd.cat')
        if ($firma.Status -ne 'Valid') { throw "la firma del driver non e' valida ($($firma.Status))" }

        New-Item -ItemType Directory -Force $Cartella | Out-Null
        Copy-Item (Join-Path $inf.DirectoryName '*') $Cartella -Force
        Set-RisoluzioneTablet -Cartella $Cartella
        return $true
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Il vdd_settings.xml di serie non ha 1024x600, e senza quella modalita'
# TabDeck non puo' portare lo schermo ai pixel del pannello. La si mette prima
# delle altre: e' quella con cui lo schermo nasce.
function Set-RisoluzioneTablet {
    param([string]$Cartella)

    $xml = Join-Path $Cartella 'vdd_settings.xml'
    if (-not (Test-Path $xml)) { return }
    $testo = [IO.File]::ReadAllText($xml)
    if ($testo -match '<width>1024</width>\s*<height>600</height>') { return }

    $nuova = "<resolutions>`r`n        <resolution>`r`n            <width>1024</width>`r`n" +
             "            <height>600</height>`r`n            <refresh_rate>60</refresh_rate>`r`n" +
             "        </resolution>"
    $testo = ([regex]'<resolutions>').Replace($testo, $nuova, 1)
    [IO.File]::WriteAllText($xml, $testo, (New-Object Text.UTF8Encoding $false))
}
