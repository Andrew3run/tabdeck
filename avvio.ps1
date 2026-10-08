# Fa in modo che il collegamento sul desktop apra TabDeck senza la domanda di
# Windows, e viceversa.
#
#   .\avvio.ps1          crea l'attivita' pianificata e rifa' i collegamenti
#   .\avvio.ps1 -Togli   toglie l'attivita': la domanda torna a ogni apertura
#
# Il lavoro lo fa pc\pacchetto\Avvio.ps1, che e' lo stesso file che finisce nel
# pacchetto per gli altri PC: una regola sola, in un posto solo.
#
# Serve l'amministratore, e se non ce l'ha se lo fa dare da solo: una domanda
# adesso, una volta, al posto di una a ogni apertura di TabDeck.

param([switch]$Togli)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$dentro = Join-Path $root 'pc\pacchetto\Avvio.ps1'
$ese = Join-Path $root 'pc\app\TabDeck.exe'

if (-not (Test-Path $dentro)) { throw "Manca $dentro" }

$io = [Security.Principal.WindowsIdentity]::GetCurrent()
$capo = (New-Object Security.Principal.WindowsPrincipal $io).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $capo) {
    Write-Host "Serve l'amministratore: Windows lo chiede adesso, una volta sola." -ForegroundColor Cyan

    # Le virgolette attorno al percorso vanno messe a mano: Start-Process
    # attacca gli argomenti con uno spazio in mezzo e non ne mette nessuna, e
    # questa cartella si chiama "Progetti Personali".
    $riga = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"")
    if ($Togli) { $riga += '-Togli' }

    try {
        Start-Process powershell -Verb RunAs -Wait -ArgumentList $riga
    } catch {
        Write-Warning "Permesso negato: niente e' cambiato."
        exit 1
    }

    # La finestra elevata si e' chiusa portandosi via quello che ha scritto:
    # il risultato si guarda qui, che e' dove sta chi ha lanciato il comando.
    $c = Get-ScheduledTask -TaskName 'TabDeck' -ErrorAction SilentlyContinue
    if ($Togli) {
        if ($c) { Write-Warning "L'attivita' c'e' ancora: qualcosa non e' andato." }
        else { Write-Host "Fatto: il collegamento va dritto all'eseguibile, con la domanda di Windows." -ForegroundColor Green }
    } else {
        if ($c) { Write-Host "Fatto: da adesso il collegamento apre TabDeck senza chiedere niente." -ForegroundColor Green }
        else { Write-Warning "L'attivita' non e' stata creata: qualcosa non e' andato." }
    }
    exit 0
}

if ($Togli) {
    & $dentro -Togli
} else {
    if (-not (Test-Path $ese)) {
        throw "Manca $ese. Compila e metti in servizio con: dotnet publish pc\TabDeck.App -c Release -o pc\app-nuovo ; .\aggiorna-app.ps1"
    }
    & $dentro -Ese $ese
}
