@echo off
setlocal EnableExtensions
title TabDeck - installazione sul PC

rem Doppio clic, e TabDeck si installa. Il lavoro lo fa Installa.ps1: qui c'e'
rem solo quello che serve a farlo partire senza aprire un PowerShell a mano.
rem
rem Due cose che un .ps1 da solo non fa: un doppio clic su uno script di
rem PowerShell lo apre nel Blocco note invece di eseguirlo, e Installa.ps1 vuole
rem i permessi di amministratore - il driver dello schermo virtuale e la regola
rem del firewall non si toccano altrimenti.
rem
rem Le opzioni (-Dove, -SenzaDriver, -SenzaFirewall, -SenzaCollegamenti,
rem -SenzaAttivita) restano di Installa.ps1: si passano da un PowerShell come
rem amministratore, non da qui.

cd /d "%~dp0"

if not exist "Installa.ps1" (
    echo.
    echo   Questo file va tenuto accanto a Installa.ps1, dentro la cartella
    echo   del pacchetto. Da solo non installa niente.
    echo.
    pause
    exit /b 1
)

rem "net session" riesce solo da amministratore: e' il modo piu' corto di
rem sapere da che parte del consenso siamo.
net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo   TabDeck - installazione
    echo   --------------------------------------------------
    echo.
    echo   Windows sta per chiedere il consenso: serve a installare il driver
    echo   dello schermo virtuale e ad aprire la porta nel firewall.
    echo.
    rem Il percorso passa da una variabile d'ambiente e non dalla riga di
    rem comando: Start-Process di PowerShell 5.1 non mette le virgolette
    rem attorno a quello che gli si da', e una cartella con uno spazio dentro
    rem - "Progetti Personali", "C:\Users\Mario Rossi" - arriverebbe spezzata
    rem in due. Una variabile e' una stringa sola, e resta intera.
    set "TABDECK_BAT=%~f0"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -Verb RunAs -FilePath $env:TABDECK_BAT"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "Installa.ps1"
echo.
echo   Premi un tasto per chiudere.
pause >nul
