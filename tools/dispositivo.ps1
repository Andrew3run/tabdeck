# Il tablet di QUESTO progetto, e nessun altro.
#
# Da quando esiste un secondo progetto su un secondo tablet, "il primo
# dispositivo che adb vede" non e' piu' una risposta buona: con tutti e due i
# tablet attaccati, `adb devices` ne elenca due e chi non guarda il modello
# installa TabDeck sul tablet sbagliato. Qui il modello si guarda.
#
# L'E960 ha per giunta un numero di serie segnaposto (0123456789ABCDEF), quindi
# il riconoscimento non puo' passare dal seriale: passa da ro.product.model.

$ProgettoModello = 'SM-T210'
$ProgettoNome    = 'TabDeck (Samsung Galaxy Tab 3 7.0)'

function Get-TabletDelProgetto {
    param([string]$Adb)

    $righe = & $Adb devices | Select-String -Pattern '\tdevice$'
    if (-not $righe) { return $null }

    $seriali = $righe | ForEach-Object { ($_ -split "`t")[0].Trim() }
    $altri = @()

    foreach ($s in $seriali) {
        $modello = (& $Adb -s $s shell getprop ro.product.model 2>$null)
        $modello = (($modello -join '') -replace '\s', '')
        if ($modello -eq $ProgettoModello) { return $s }
        $altri += "$s ($modello)"
    }

    Write-Host ""
    Write-Warning "Questo e' il progetto $ProgettoNome e vuole un $ProgettoModello."
    Write-Host "Collegati invece: $($altri -join ', ')"
    Write-Host "Il DUODUOGO E960 ha una cartella sua: '..\Tablet DUODUOGO E960'." -ForegroundColor Yellow
    return $null
}
