# Compila y prueba Rama con el SDK .NET 10 local (GitClient/.dotnet) o, si no existe, con el dotnet del PATH.
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path (Split-Path -Parent $repo) '.dotnet'
if (Test-Path (Join-Path $localSdk 'dotnet.exe')) {
    $env:DOTNET_ROOT = $localSdk
    $dotnet = Join-Path $localSdk 'dotnet.exe'
} else {
    $dotnet = 'dotnet'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

& $dotnet build (Join-Path $repo 'SourceGit.slnx') -c $Configuration -p:DisableAOT=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    foreach ($tests in @('tests/Rama.Core.Tests/Rama.Core.Tests.csproj', 'tests/Rama.Terminal.Tests/Rama.Terminal.Tests.csproj')) {
        & $dotnet test (Join-Path $repo $tests) -c $Configuration --no-build --collect:"XPlat Code Coverage"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
