# Mette TabDeck su questo PC.
#
# Non c'e' un servizio, non c'e' un avvio automatico e non resta niente in
# esecuzione: questo script copia una cartella, accende il driver dello schermo
# virtuale, apre la porta del richiamo nel firewall e fa i collegamenti. Tutto
# quello che fa si disfa con Disinstalla.ps1.
#
#   .\Installa.ps1                      in C:\TabDeck
#   .\Installa.ps1 -Dove D:\TabDeck     da un'altra parte
#   .\Installa.ps1 -SenzaDriver         niente schermo virtuale (resta il deck)
#   .\Installa.ps1 -SenzaFirewall       niente regola per il richiamo del tablet
#   .\Installa.ps1 -SenzaCollegamenti   niente icone nel menu Start e sul desktop
#   .\Installa.ps1 -SenzaAttivita       niente attivita' pianificata: il
#                                       collegamento va dritto all'exe, e
#                                       Windows chiede il consenso ogni volta
#
# Va lanciato da un PowerShell **come amministratore**: il driver e il firewall
# non si toccano altrimenti.

param(
    [string]$Dove = 'C:\TabDeck',
    [switch]$SenzaDriver,
    [switch]$SenzaFirewall,
    [switch]$SenzaCollegamenti,
    [switch]$SenzaAttivita
)

$ErrorActionPreference = 'Stop'
$qui = $PSScriptRoot

# Il driver dello schermo virtuale sta dentro TabDeck, in driver\schermo: lo
# si dice al driver con la chiave VDDPATH (vedi SchermoVirtuale.ps1), senza la
# quale cercherebbe vdd_settings.xml in C:\VirtualDisplayDriver.
$IdSchermo  = 'Root\MttVDD'
. (Join-Path $qui 'SchermoVirtuale.ps1')

function Scrivi { param([string]$T, [string]$Colore = 'Gray') Write-Host $T -ForegroundColor $Colore }

function Test-Amministratore {
    $io = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $io).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Amministratore)) {
    Write-Host ""
    Write-Warning "Serve un PowerShell come amministratore."
    Write-Host "Tasto destro su 'Windows PowerShell' > Esegui come amministratore, poi rilancia:"
    Write-Host "    cd '$qui'"
    Write-Host "    .\Installa.ps1"
    exit 1
}

Write-Host ""
Scrivi "TabDeck - installazione" 'Cyan'
Scrivi ("-" * 46)

# --- 1. la cartella -------------------------------------------------------

$sorgenteApp = Join-Path $qui 'app'
if (-not (Test-Path $sorgenteApp)) {
    throw "Nel pacchetto manca la cartella 'app'. Ricrealo con pacchetto.ps1."
}

New-Item -ItemType Directory -Force $Dove | Out-Null

# 'tablet' e' l'APK: viaggia con l'installazione perche' l'app sul tablet si
# rimette piu' volte - a ogni versione nuova, dopo un reset, dopo un cambio di
# tablet - e la cartella del pacchetto, un mese dopo, non la ritrova nessuno.
foreach ($pezzo in @('app', 'tools', 'driver', 'tablet')) {
    $da = Join-Path $qui $pezzo
    if (-not (Test-Path $da)) { continue }
    $a = Join-Path $Dove $pezzo
    # Il contenuto e non la cartella: reinstallando sopra, Copy-Item di una
    # cartella dentro una che esiste gia' ne creerebbe una annidata - app\app.
    New-Item -ItemType Directory -Force $a | Out-Null
    Copy-Item (Join-Path $da '*') $a -Recurse -Force
}

# E accanto all'APK quello che serve a rimetterlo, e a rifare la pulizia del
# sistema del tablet dopo un reset: gli script, i loro doppi clic, e le due
# letture. adb e dispositivo.ps1 stanno gia' in $Dove\tools, dove li cercano.
foreach ($f in @('Tablet.ps1', 'Tablet.bat', 'Alleggerisci.bat', 'Ripristina.bat',
                 'LEGGIMI.txt', 'GUIDA.md')) {
    $da = Join-Path $qui $f
    if (Test-Path $da) { Copy-Item $da $Dove -Force }
}

# La configurazione non si sovrascrive mai. Chi reinstalla sopra
# un'installazione che c'e' gia' vuole il programma nuovo, non il deck di
# qualcun altro al posto del suo.
$sorgenteConfig = Join-Path $qui 'config'
$destConfig = Join-Path $Dove 'config'
if (Test-Path $sorgenteConfig) {
    if (Test-Path $destConfig) {
        Scrivi "  config    c'era gia': lasciata com'e'" 'Yellow'
    } else {
        Copy-Item $sorgenteConfig $destConfig -Recurse -Force
        Scrivi "  config    copiata dal pacchetto" 'Green'
    }
} elseif (-not (Test-Path $destConfig)) {
    # Senza, TabDeck se la creerebbe da solo accanto all'eseguibile, cioe'
    # dentro app\: funziona, ma poi il deck sta in un posto che non si trova.
    New-Item -ItemType Directory -Force $destConfig | Out-Null
    Scrivi "  config    vuota, la riempira' il programma" 'Green'
}

$ese = Join-Path $Dove 'app\TabDeck.exe'
Scrivi "  programma $ese" 'Green'

if (Test-Path (Join-Path $Dove 'tablet\TabDeck.apk')) {
    Scrivi "  tablet    APK e Tablet.bat in $Dove, per rimetterlo sul tablet" 'Green'
}
if (Test-Path (Join-Path $Dove 'tablet\sistema\alleggerisci.ps1')) {
    Scrivi "  sistema   Alleggerisci.bat e Ripristina.bat, per la pulizia del tablet" 'Green'
}

# Il pacchetto normale si porta dentro .NET e non chiede niente a nessuno.
# Quello fatto con -Leggero no: li' il runtime deve esserci gia', e accorgersene
# adesso costa una riga, accorgersene dopo costa una finestra che non si apre.
if (-not (Test-Path (Join-Path $Dove 'app\System.Private.CoreLib.dll'))) {
    $runtime = & { try { & dotnet.exe --list-runtimes 2>$null } catch { @() } }
    $desktop = $runtime | Select-String -Pattern 'Microsoft\.WindowsDesktop\.App 10\.'
    if (-not $desktop) {
        Write-Host ""
        Write-Warning "Questo pacchetto e' leggero: dentro non c'e' .NET, e su questo PC non lo trovo."
        Write-Host "Scarica '.NET Desktop Runtime 10' da https://dotnet.microsoft.com/download"
        Write-Host "oppure rifai il pacchetto senza -Leggero. L'installazione va avanti lo stesso."
        Write-Host ""
    }
}

# --- 2. lo schermo virtuale ----------------------------------------------

function Install-SchermoVirtuale {
    param([string]$Cartella)

    # Prima il controllo: uno schermo virtuale gia' installato - da TabDeck o
    # a mano - si lascia com'e', file e impostazioni compresi.
    $gia = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue |
             Where-Object { $_.FriendlyName -like '*Virtual Display Driver*' })
    if ($gia.Count -gt 0) {
        Scrivi "  schermo   c'era gia': $($gia[0].FriendlyName) - $($gia[0].Status)" 'Green'
        return
    }

    # I file li ha portati il pacchetto in $Cartella; se non ci sono, si scaricano.
    $scaricato = -not (Test-Path (Join-Path $Cartella 'MttVDD.inf'))
    if ($scaricato) { Scrivi "  schermo   manca: scarico Virtual Display Driver $VddVersione" }
    try {
        [void](Get-DriverSchermo -Cartella $Cartella)
    } catch {
        Scrivi "  schermo   non riesco a scaricarlo: $($_.Exception.Message)" 'Red'
        Scrivi "            Il deck e le luci funzionano lo stesso; manca solo il secondo monitor." 'Yellow'
        Scrivi "            Con internet, rilancia Installa.ps1." 'Yellow'
        return
    }
    Set-RisoluzioneTablet -Cartella $Cartella
    Scrivi "  driver    file in $Cartella"

    New-Item -Path $VddChiave -Force | Out-Null
    Set-ItemProperty -Path $VddChiave -Name 'VDDPATH' -Value $Cartella
    $inf = Join-Path $Cartella 'MttVDD.inf'

    # Prima nel magazzino driver di Windows, poi il nodo del dispositivo. Lo
    # schermo virtuale non e' una scheda che si attacca: nessun bus lo
    # annuncia, quindi il dispositivo va creato a mano sotto la radice.
    & pnputil.exe /add-driver "$inf" | Out-Host

    Aggiungi-NodoDispositivo
    try {
        Crea-Nodo -Classe 'Display' `
                  -GuidClasse '4d36e968-e325-11ce-bfc1-08002be10318' `
                  -HardwareId $IdSchermo -Inf $inf
    } catch {
        Scrivi "  schermo   il dispositivo non si e' creato: $($_.Exception.Message)" 'Red'
        Scrivi "            Il deck e le luci funzionano lo stesso; manca solo il secondo monitor." 'Yellow'
        return
    }

    $ora = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue |
             Where-Object { $_.FriendlyName -like '*Virtual Display Driver*' })
    if ($ora.Count -gt 0) {
        # Appena creato e' acceso: un monitor in piu' sul desktop anche a tablet
        # staccato. Lo si spegne; lo accende TabDeck quando serve, e trovandolo
        # spento sa che e' suo da rispegnere.
        foreach ($d in $ora) { Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }
        Scrivi "  schermo   $($ora[0].FriendlyName), spento finche' TabDeck non lo accende" 'Green'
    } else {
        Scrivi "  schermo   installato, ma Windows non lo elenca ancora: riavvia se non compare" 'Yellow'
    }
}

# Il nodo si crea con le stesse tre chiamate di devcon e di nefcon, prese qui
# direttamente da setupapi: cosi' nel pacchetto non serve portarsi dietro un
# eseguibile di terze parti per un lavoro da venti righe.
function Aggiungi-NodoDispositivo {
    if ('TabDeck.Nodo' -as [type]) { return }

    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TabDeck
{
    public static class Nodo
    {
        const int DICD_GENERATE_ID = 0x00000001;
        const int SPDRP_HARDWAREID = 0x00000001;
        const int DIF_REGISTERDEVICE = 0x00000019;
        const int INSTALLFLAG_FORCE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr parent);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiCreateDeviceInfo(IntPtr set, string name, ref Guid classGuid,
            string description, IntPtr parent, int flags, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiSetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data,
            int property, byte[] buffer, int size);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiCallClassInstaller(int func, IntPtr set, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr parent, string hardwareId,
            string inf, int flags, out bool reboot);

        public static bool Crea(string classe, Guid guidClasse, string hardwareId, string inf)
        {
            IntPtr set = SetupDiCreateDeviceInfoList(ref guidClasse, IntPtr.Zero);
            if (set == new IntPtr(-1)) throw new Exception("SetupDiCreateDeviceInfoList: errore " + Marshal.GetLastWin32Error());

            try
            {
                SP_DEVINFO_DATA dati = new SP_DEVINFO_DATA();
                dati.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                if (!SetupDiCreateDeviceInfo(set, classe, ref guidClasse, null, IntPtr.Zero, DICD_GENERATE_ID, ref dati))
                    throw new Exception("SetupDiCreateDeviceInfo: errore " + Marshal.GetLastWin32Error());

                byte[] multi = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
                if (!SetupDiSetDeviceRegistryProperty(set, ref dati, SPDRP_HARDWAREID, multi, multi.Length))
                    throw new Exception("SetupDiSetDeviceRegistryProperty: errore " + Marshal.GetLastWin32Error());

                if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, ref dati))
                    throw new Exception("SetupDiCallClassInstaller: errore " + Marshal.GetLastWin32Error());
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }

            bool riavvio;
            if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, hardwareId, inf, INSTALLFLAG_FORCE, out riavvio))
                throw new Exception("UpdateDriverForPlugAndPlayDevices: errore " + Marshal.GetLastWin32Error());

            return riavvio;
        }
    }
}
'@
}

function Crea-Nodo {
    param([string]$Classe, [string]$GuidClasse, [string]$HardwareId, [string]$Inf)
    [void][TabDeck.Nodo]::Crea($Classe, [Guid]$GuidClasse, $HardwareId, $Inf)
}

if ($SenzaDriver) {
    Scrivi "  schermo   saltato (-SenzaDriver)" 'Yellow'
} else {
    Install-SchermoVirtuale -Cartella (Join-Path $Dove 'driver\schermo')
}

# --- 3. il driver USB del tablet -----------------------------------------

# Serve solo al collegamento col cavo: senza, adb non vede il tablet. Sul WiFi
# non c'entra niente. Ce n'e' uno nel pacchetto solo se chi l'ha creato ne
# aveva uno installato da esportare.
$driverUsb = Join-Path $Dove 'driver\usb'
if (Test-Path $driverUsb) {
    $inf = @(Get-ChildItem $driverUsb -Recurse -Filter '*.inf' -ErrorAction SilentlyContinue)
    foreach ($f in $inf) { & pnputil.exe /add-driver "$($f.FullName)" /install | Out-Null }
    Scrivi "  usb       $($inf.Count) driver messi nel magazzino di Windows" 'Green'
} else {
    Scrivi "  usb       nessun driver nel pacchetto: vedi LEGGIMI.txt se il cavo non va" 'Yellow'
}

# --- 4. il firewall -------------------------------------------------------

# L'annuncio del tablet e' l'unico pacchetto non richiesto che TabDeck riceve:
# le risposte alla ricerca dritta rientrano da sole, perche' Windows le
# riconosce come ritorno di un pacchetto uscito. Questo no, e senza regola il
# firewall lo butta via in silenzio.
function Apri-Firewall {
    param([string]$Programma)

    $regole = @(
        @{ Nome = 'TabDeck - richiamo del tablet'; Porta = 8767 },
        @{ Nome = 'TabDeck - ricerca del tablet';  Porta = 8766 }
    )

    foreach ($r in $regole) {
        try {
            Get-NetFirewallRule -DisplayName $r.Nome -ErrorAction SilentlyContinue |
                Remove-NetFirewallRule -ErrorAction SilentlyContinue
            New-NetFirewallRule -DisplayName $r.Nome -Direction Inbound -Protocol UDP `
                -LocalPort $r.Porta -Action Allow -Profile Any | Out-Null
        } catch {
            & netsh.exe advfirewall firewall delete rule name="$($r.Nome)" | Out-Null
            & netsh.exe advfirewall firewall add rule name="$($r.Nome)" dir=in action=allow `
                protocol=UDP localport=$($r.Porta) | Out-Null
        }
    }

    # E la regola per il programma, cosi' Windows non chiede niente al primo
    # avvio: chi risponde di corsa a quella finestra spesso dice 'annulla'.
    $nome = 'TabDeck'
    try {
        Get-NetFirewallRule -DisplayName $nome -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue
        New-NetFirewallRule -DisplayName $nome -Direction Inbound -Program $Programma `
            -Action Allow -Profile Any | Out-Null
    } catch {
        & netsh.exe advfirewall firewall delete rule name="$nome" | Out-Null
        & netsh.exe advfirewall firewall add rule name="$nome" dir=in action=allow `
            program="$Programma" enable=yes | Out-Null
    }
}

if ($SenzaFirewall) {
    Scrivi "  firewall  saltato (-SenzaFirewall)" 'Yellow'
} else {
    Apri-Firewall -Programma $ese
    Scrivi "  firewall  aperte udp 8766 e 8767, piu' il programma" 'Green'
}

# --- 5. come si apre, e i collegamenti ------------------------------------

# Lo fa Avvio.ps1, che sa le due forme: il collegamento che passa da
# un'attivita' pianificata - e allora Windows non chiede piu' il consenso - e
# quello che va dritto all'eseguibile, che lo chiede ogni volta.
$avvio = Join-Path $qui 'Avvio.ps1'
if (-not (Test-Path $avvio)) {
    Scrivi "  icone     manca Avvio.ps1 nel pacchetto: salto" 'Yellow'
} else {
    $argomenti = @{ Ese = $ese }
    if ($SenzaAttivita) { $argomenti['Diretto'] = $true }
    if ($SenzaCollegamenti) { $argomenti['SenzaCollegamenti'] = $true }
    & $avvio @argomenti
}

# --- fine -----------------------------------------------------------------

Write-Host ""
Scrivi "Fatto." 'Green'
Write-Host ""
Write-Host "TabDeck chiede i permessi di amministratore all'apertura: servono ad accendere"
Write-Host "e spegnere lo schermo virtuale e a mandare i clic del tablet anche alle finestre"
Write-Host "che girano elevate."
Write-Host ""
Write-Host "Sul tablet ci vuole l'app: attaccalo col cavo e fai doppio clic su"
Write-Host "    Tablet.bat"
Write-Host "che sta qui e anche in $Dove, con l'APK accanto: e' da li' che si rimette"
Write-Host "l'app sul tablet tutte le volte dopo. Da PowerShell: .\Tablet.ps1"
Write-Host ""
Write-Host "Per togliere dal tablet le applicazioni Android che non servono - Google,"
Write-Host "Samsung, widget, riproduttori, stampa - c'e' Alleggerisci.bat, e Ripristina.bat"
Write-Host "per tornare indietro. Non fanno parte dell'installazione: si fanno una volta,"
Write-Host "guardando cosa succede. Cosa resta acceso e' in tablet\sistema\tenere.txt."
Write-Host ""
Write-Host "Per togliere tutto: Disinstalla.bat, oppure .\Disinstalla.ps1"
Write-Host "Per rimettere la domanda di Windows: .\Avvio.ps1 -Ese '$ese' -Diretto"
