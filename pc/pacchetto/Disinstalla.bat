@echo off
setlocal EnableExtensions
title TabDeck - disinstallazione

rem Doppio clic, e del passaggio di Installa.bat non resta niente: la cartella,
rem il driver dello schermo virtuale, le regole del firewall, l'attivita'
rem pianificata e i collegamenti. Il lavoro lo fa Disinstalla.ps1.
rem
rem Chiudi prima TabDeck davvero - icona accanto all'orologio, tasto destro,
rem "Esci" - altrimenti lo script si ferma e lo dice: la croce della finestra
rem non spegne, ritira.
rem
rem Sul tablet l'app resta: si toglie da li', o con "adb uninstall dev.tabdeck".
rem
rem Per tenere il deck e le icone (-TieniConfig) o lo schermo virtuale
rem (-TieniDriver) si passa da Disinstalla.ps1, in un PowerShell come
rem amministratore.

cd /d "%~dp0"

if not exist "Disinstalla.ps1" (
    echo.
    echo   Questo file va tenuto accanto a Disinstalla.ps1.
    echo.
    pause
    exit /b 1
)

net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo   TabDeck - disinstallazione
    echo   --------------------------------------------------
    echo.
    echo   Windows sta per chiedere il consenso: serve a togliere il driver
    echo   dello schermo virtuale e le regole del firewall.
    echo.
    set "TABDECK_BAT=%~f0"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -Verb RunAs -FilePath $env:TABDECK_BAT"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "Disinstalla.ps1"
echo.
echo   Premi un tasto per chiudere.
pause >nul
