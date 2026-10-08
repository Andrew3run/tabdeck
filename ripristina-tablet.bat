@echo off
setlocal EnableExtensions
title TabDeck - rimetti a posto il tablet

rem Sblocca tutto quello che alleggerisci-tablet.bat aveva messo a riposo: il
rem tablet torna quello di prima, TouchWiz compreso. Il lavoro lo fa
rem tablet\sistema\ripristina.ps1.
rem
rem "pm block" non disinstallava niente, quindi qui non si scarica e non si
rem reinstalla nulla: i pacchetti tornano visibili e ripartono all'avvio.
rem Riavvia il tablet per vederli tornare davvero.

cd /d "%~dp0"

if not exist "tablet\sistema\ripristina.ps1" (
    echo.
    echo   Manca tablet\sistema\ripristina.ps1. Questo file va tenuto in cima
    echo   alla cartella, quella dove ci sono tablet\ e tools\.
    echo.
    pause
    exit /b 1
)

echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "tablet\sistema\ripristina.ps1"
echo.
echo   Premi un tasto per chiudere.
pause >nul
