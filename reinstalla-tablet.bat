@echo off
setlocal EnableExtensions
title TabDeck - ricompila e rimette l'app sul tablet

rem Doppio clic: compila l'APK e lo rimette sul tablet attaccato col cavo.
rem Il lavoro lo fa tablet\build.ps1 -Install; qui c'e' il doppio clic e la
rem pausa che tiene aperta la finestra il tempo di leggere com'e' andata.
rem
rem E' la strada di QUESTO PC, quello dove il progetto sta e si compila: vuole
rem l'SDK di Android (aapt2, d8, apksigner) e un JDK. Su un PC dove non c'e'
rem niente di tutto questo si parte dal pacchetto, e l'APK gia' compilato lo
rem mette sul tablet pacchetto\TabDeck\Tablet.bat.
rem
rem Il tablet e' il Samsung Galaxy Tab 3 7.0 (SM-T210) e non un altro: con
rem l'E960 dell'altro progetto attaccato allo stesso PC, il modello si guarda
rem prima di installare. Se non c'e', build.ps1 si ferma e lo dice.
rem
rem Reinstalla tenendo i dati: il deck, le luci e le icone che il tablet si e'
rem salvato restano dov'erano. Alla fine l'app si riapre da sola.

cd /d "%~dp0"

if not exist "tablet\build.ps1" (
    echo.
    echo   Questo file va tenuto in cima alla cartella del progetto, quella
    echo   dove ci sono tablet\ e pc\.
    echo.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "tablet\build.ps1" -Install
echo.
echo   Premi un tasto per chiudere.
pause >nul
