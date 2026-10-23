# Gate de cobertura: línea >= umbral para un paquete en archivos cobertura.
# Uso: coverage-gate.ps1 [-Threshold 80] [-Package Rama.Core] [-Dir tests]
param([double]$Threshold = 80, [string]$Package = 'Rama.Core', [string]$Dir = 'tests')
$ErrorActionPreference = 'Stop'
$best = $null
Get-ChildItem -Path $Dir -Recurse -Filter coverage.cobertura.xml | ForEach-Object {
    [xml]$xml = Get-Content -Raw $_.FullName
    $pkg = $xml.SelectNodes("//package[@name='$Package']") | Select-Object -First 1
    if ($pkg) {
        $rate = [double]::Parse($pkg.'line-rate', [Globalization.CultureInfo]::InvariantCulture)
        if ($null -eq $best -or $rate -gt $best) { $best = $rate }
    }
}
if ($null -eq $best) { Write-Error "coverage-gate: no se encontro el paquete $Package en $Dir"; exit 2 }
$pct = [math]::Round($best * 100, 2)
Write-Host "coverage-gate: $Package line coverage = $pct% (umbral $Threshold%)"
if ($pct -lt $Threshold) { Write-Error 'coverage-gate: FALLO, cobertura bajo el umbral'; exit 1 }
