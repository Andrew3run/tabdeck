# Mette in servizio la build nuova di TabDeck.
#
#   dotnet publish pc\TabDeck.App -c Release -o pc\app-nuovo ; .\aggiorna-app.ps1
#
# L'eseguibile in uso non si puo' sovrascrivere mentre gira, e TabDeck gira come
# amministratore: chiuderlo da uno script non elevato non si puo'. Con TabDeck aperto
# questo lascia il segnale « pronto » in pc\app-nuovo, e TabDeck si chiude, si
# aggiorna e si riapre da solo (MainWindow.Aggiornamento). Con TabDeck chiuso copia e
# basta. Un TabDeck di prima del 15/09/2026 il segnale non lo conosce: va chiuso a mano
# una volta.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$nuovo = Join-Path $root 'pc\app-nuovo'
$attuale = Join-Path $root 'pc\app'
if (-not (Test-Path (Join-Path $nuovo 'TabDeck.exe'))) { throw "Manca pc\app-nuovo: compila prima con dotnet publish." }

if (Get-Process TabDeck -ErrorAction SilentlyContinue) {
    Set-Content -Path (Join-Path $nuovo 'pronto') -Value (Get-Date -Format o) -Encoding ascii
    Write-Host "TabDeck e' aperto: gli ho lasciato il segnale, si aggiorna e si riapre da solo." -ForegroundColor Green
    exit 0
}

Copy-Item (Join-Path $nuovo '*') $attuale -Force -Recurse -Exclude 'pronto'
Write-Host "TabDeck aggiornato. Riaprilo dal collegamento sul desktop." -ForegroundColor Green
