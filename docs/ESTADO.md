# Estado de Rama — 2026-10-22

Fork local de SourceGit v2026.22 (`base/v2026.22`), rama `main`. Sin remoto: todo local.

## Verificado automáticamente
- Build Debug y Release (`-p:DisableAOT=true -p:DisableUpdateDetection=true`): 0 errores, 0 advertencias.
- Pruebas: 414 en verde (Rama.Core 406, Rama.App 4, Rama.Terminal 4), estables en 3 corridas.
- Cobertura de líneas Rama.Core: 88,6 % (`scripts/coverage-gate.sh`, umbral 80 %).
- Revisión de seguridad (Fable 5.1) y de calidad (Sonnet 5.5): todos los hallazgos corregidos (1 medio de seguridad: validación de URL de clonado + `--` en `git clone`).

## Fases
| Fase | Estado |
|---|---|
| F0 fork, scripts, proyectos de prueba | Hecho |
| F1 eliminar IA/OpenAI | Hecho |
| F2 marca Rama, tema grafito, es_CL, carriles | Hecho (fuente: Inter, no IBM Plex — sin descarga offline) |
| F3 riel de íconos | Hecho |
| F4 historial, cambios, diff; resolvedor de conflictos 3 paneles | Hecho |
| F5 terminal integrada + botones de contexto | Hecho (librerías vía NuGet, no vendorizadas) |
| F6 llavero, HTTP, polling, notificaciones, Cuentas | Hecho (Windows sin toast nativo: aviso dentro de la app) |
| F7 Azure DevOps: clonar, revisiones, votos, aprobación de etapas | Hecho |
| F8 Bitbucket: clonar | Hecho |
| F9 Jira: tablero editable, crear rama | Hecho |
| F10 CI (Azure Pipelines + GitHub Actions), empaquetado | Hecho sin Velopack (ver `docs/RELEASE.md`) |

## Pendiente (requiere a una persona)
- Todas las verificaciones visuales y de la terminal real: `docs/MANUAL-CHECKS.md` (por regla de antivirus no se abrió la app ni se capturó pantalla).
- Probar contra servicios reales: Azure DevOps (PAT), Bitbucket (API token), Jira (API token). Las formas de API marcadas `SUPUESTO` en el código salieron de documentación, sin red.
- macOS y Linux: compilan, sin ejecutar (llavero, PTY, notificaciones).
- Velopack/actualización automática y AppImage.
- Chip del ticket en la cabecera del repo (`VEN-412 En curso`).

## Cómo compilar
`bash scripts/build.sh` (usa el SDK local `GitClient/.dotnet`). Ejecutable Debug: `src/bin/Debug/net10.0/SourceGit.exe`.
