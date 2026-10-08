@echo off
setlocal EnableExtensions
title TabDeck - l'app sul tablet

rem Doppio clic, e l'app va sul tablet: la prima volta e tutte le volte dopo.
rem Il lavoro lo fa Tablet.ps1, che trova adb e l'APK qui accanto.
rem
rem Non serve l'amministratore e non serve l'SDK di Android: l'APK e' gia'
rem compilato dentro il pacchetto. Serve il cavo, e il debug USB acceso sul
rem tablet - se manca, Tablet.ps1 lo dice e spiega dove si accende.
rem
rem Reinstalla tenendo i dati (adb install -r): il deck, le luci e le icone che
rem il tablet si e' salvato restano dov'erano.
rem
rem Dopo Installa.bat questo file sta anche in C:\TabDeck, con l'APK accanto:
rem per rimettere sul tablet una versione nuova non serve piu' ritrovare il
rem pacchetto.

cd /d "%~dp0"

if not exist "Tablet.ps1" (
    echo.
    echo   Questo file va tenuto accanto a Tablet.ps1 e alla cartella tablet\,
    echo   dove sta TabDeck.apk. Da solo non installa niente.
    echo.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "Tablet.ps1"
echo.
echo   Premi un tasto per chiudere.
pause >nul
