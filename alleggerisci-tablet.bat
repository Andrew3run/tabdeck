@echo off
setlocal EnableExtensions
title TabDeck - alleggerisci il tablet

rem Mette a riposo i pacchetti Android elencati in tablet\sistema\bloccati.txt:
rem sono quasi tutti, cioe' tutto quello che non serve a un tablet che fa lo
rem schermo e il deck. Il lavoro lo fa tablet\sistema\alleggerisci.ps1, e chi
rem resta acceso e' scritto in tablet\sistema\tenere.txt, con il perche'.
rem
rem "pm block" nasconde il pacchetto e gli impedisce di ripartire all'avvio:
rem non disinstalla niente, e ripristina-tablet.bat rimette tutto com'era.
rem Su Android 4.4 qualche pacchetto privilegiato rifiuta il blocco - Google
rem Play Services su tutti - e lo script lo dice a fine corsa, con dove
rem spegnerlo a mano.
rem
rem Non e' un passaggio dell'installazione apposta: si fa una volta, sul
rem proprio tablet, guardando cosa succede.
rem L'app sul tablet la mette reinstalla-tablet.bat, che non tocca il resto.

cd /d "%~dp0"

if not exist "tablet\sistema\alleggerisci.ps1" (
    echo.
    echo   Manca tablet\sistema\alleggerisci.ps1. Questo file va tenuto in cima
    echo   alla cartella, quella dove ci sono tablet\ e tools\.
    echo.
    pause
    exit /b 1
)

echo.
echo   TabDeck - alleggerisci il tablet
echo   --------------------------------------------------
echo.
echo   Sto per mettere a riposo sul tablet tutti i pacchetti Android che non
echo   servono: Google, Samsung, widget, riproduttori, stampa, cloud. Restano
echo   accesi il sistema, le impostazioni, la tastiera, adb e TabDeck.
echo.
echo   Non disinstalla niente: ripristina-tablet.bat rimette tutto com'era.
echo   Ma dopo, il tablet non e' piu' un tablet normale: e' quello che vuoi.
echo.

echo   Scrivi si e premi INVIO per continuare, o chiudi la finestra.
echo.
set "RISPOSTA="
set /p "RISPOSTA=  > "
if /i not "%RISPOSTA%"=="si" goto :annulla

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "tablet\sistema\alleggerisci.ps1"
echo.
echo   Premi un tasto per chiudere.
pause >nul
exit /b

:annulla
echo.
echo   Non ho toccato niente.
echo.
echo   Premi un tasto per chiudere.
pause >nul
