#!/usr/bin/env bash
# Gate de cobertura: línea >= umbral para un paquete en archivos cobertura.
# Uso: coverage-gate.sh [umbral=80] [paquete=Rama.Core] [directorio=tests]
# Toma el mayor line-rate del paquete entre todos los coverage.cobertura.xml.
set -euo pipefail
THRESHOLD="${1:-80}"
PACKAGE="${2:-Rama.Core}"
DIR="${3:-tests}"

best=""
while IFS= read -r f; do
    rate=$(grep -o "<package name=\"$PACKAGE\"[^>]*" "$f" | head -n1 | sed -n 's/.*line-rate="\([0-9.]*\)".*/\1/p' || true)
    [ -n "$rate" ] || continue
    if [ -z "$best" ] || awk -v a="$rate" -v b="$best" 'BEGIN{exit !(a>b)}'; then best="$rate"; fi
done < <(find "$DIR" -name 'coverage.cobertura.xml' -type f)

if [ -z "$best" ]; then
    echo "coverage-gate: no se encontro el paquete $PACKAGE en $DIR" >&2
    exit 2
fi
pct=$(awk -v r="$best" 'BEGIN{printf "%.2f", r*100}')
echo "coverage-gate: $PACKAGE line coverage = $pct% (umbral $THRESHOLD%)"
if awk -v p="$pct" -v t="$THRESHOLD" 'BEGIN{exit !(p>=t)}'; then exit 0; fi
echo "coverage-gate: FALLO, cobertura bajo el umbral" >&2
exit 1
