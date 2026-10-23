#!/usr/bin/env bash
# Compila y prueba Rama con el SDK .NET 10 local (GitClient/.dotnet) o, si no existe, con el dotnet del PATH.
set -euo pipefail
CONFIG="${1:-Debug}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
LOCAL_SDK="$(dirname "$REPO")/.dotnet"
if [ -x "$LOCAL_SDK/dotnet" ] || [ -x "$LOCAL_SDK/dotnet.exe" ]; then
    export DOTNET_ROOT="$LOCAL_SDK"
    DOTNET="$LOCAL_SDK/dotnet"; [ -x "$DOTNET" ] || DOTNET="$LOCAL_SDK/dotnet.exe"
else
    DOTNET=dotnet
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DOTNET" build "$REPO/SourceGit.slnx" -c "$CONFIG" -p:DisableAOT=true
if [ "${SKIP_TESTS:-0}" != "1" ]; then
    "$DOTNET" test "$REPO/tests/Rama.Core.Tests/Rama.Core.Tests.csproj" -c "$CONFIG" --no-build --collect:"XPlat Code Coverage"
fi
