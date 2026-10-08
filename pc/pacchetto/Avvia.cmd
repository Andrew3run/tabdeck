@echo off
setlocal EnableExtensions
title TabDeck - installazione

rem Questo file gira dentro TabDeck-Setup.exe.
rem
rem IExpress, che l'exe lo impacchetta, sa mettere dentro solo file sciolti:
rem niente cartelle. Percio' il pacchetto vero viaggia come un solo TabDeck.zip
rem qui accanto, e il lavoro di queste righe e' aprirlo e passare la mano a
rem Installa.ps1, che vuole i permessi di amministratore per il driver e per il
rem firewall.

set "SORGENTE=%~dp0"
set "DENTRO=%TEMP%\TabDeck-Setup"

echo.
echo   TabDeck - installazione
echo   --------------------------------------------------
echo.

if exist "%DENTRO%" rd /s /q "%DENTRO%"
mkdir "%DENTRO%" 2>nul
if not exist "%DENTRO%" goto :male

echo   Apro il pacchetto: ci vuole un momento, sono 150 megabyte.

rem tar c'e' da Windows 10 in poi ed e' molto piu' svelto; dove non c'e',
rem ci pensa PowerShell.
tar -xf "%SORGENTE%TabDeck.zip" -C "%DENTRO%" >nul 2>&1
if not exist "%DENTRO%\Installa.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%SORGENTE%TabDeck.zip' -DestinationPath '%DENTRO%' -Force"
)
if not exist "%DENTRO%\Installa.ps1" goto :male

rem Il pezzo elevato sta in un file suo invece che sulla riga di comando.
rem Start-Process di PowerShell 5.1 attacca gli argomenti senza virgolette, e un
rem percorso con uno spazio dentro - "C:\Users\Mario Rossi\..." - arriverebbe
rem spezzato in due; un file da lanciare, invece, e' un argomento solo.
> "%DENTRO%\_installa.cmd" echo @echo off
>>"%DENTRO%\_installa.cmd" echo title TabDeck - installazione
>>"%DENTRO%\_installa.cmd" echo cd /d "%%~dp0"
>>"%DENTRO%\_installa.cmd" echo powershell -NoProfile -ExecutionPolicy Bypass -File Installa.ps1
>>"%DENTRO%\_installa.cmd" echo echo.
>>"%DENTRO%\_installa.cmd" echo pause

echo   Fatto. Adesso Windows chiede il consenso: serve a installare il driver
echo   dello schermo virtuale e ad aprire la porta nel firewall.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -Verb RunAs -Wait -FilePath '%DENTRO%\_installa.cmd'"

rd /s /q "%DENTRO%" >nul 2>&1
goto :fine

:male
echo.
echo   Non sono riuscito ad aprire il pacchetto in
echo   %DENTRO%
echo.
echo   Estrai TabDeck.zip a mano e lancia Installa.ps1 da un PowerShell
echo   come amministratore.
pause

:fine
endlocal
