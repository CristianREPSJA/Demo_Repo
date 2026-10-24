# Estado de Rama — 2026-10-22

Fork de SourceGit v2026.22 (`base/v2026.22`), rama `main`. Remotos de prueba: GitHub `CristianREPSJA/Demo_Repo` (`origin`) y Azure DevOps `csanjuan/Rama-Pruebas` (`azdo`, `main` protegida: cambios por PR).

## Validado contra Azure DevOps real (2026-10-23, `csanjuan/Rama-Pruebas`)
- Cliente F7 completo con PAT: usuario, repos, PR por revisor, detalle, commits, políticas, votos y aprobación de etapas (`tests/Rama.Core.Tests/Azure/AzureLiveTests.cs`, categoría `Live`, se omite sin `RAMA_AZDO_ORG`/`RAMA_AZDO_PROJECT`/`RAMA_AZDO_PAT_FILE`).
- Corregido: revisores con `isRequired`/`isContainer` en `null` rompían la deserialización; `connectionData` trae `providerDisplayName` en vez de `displayName`.
- Confirmado: la API de approvals no trae las etapas, pero sí `pipeline.owner.id` (id del build), que serviría para leer la timeline.
- Login con cuenta Microsoft (Entra, navegador + PKCE) funciona con el tenant del equipo. Implementado y fusionado (MSAL detrás de `IEntraAuthenticator`, Bearer renovable en la API, extraheader para git); ver `docs/ENTRA.md`. Validado en vivo el cliente con Bearer (4/4) y `git ls-remote` con el token inyectado; pendiente la prueba manual del login dentro de la app con una app registrada en Entra.
- Etapas de la ejecución en el detalle de aprobación (timeline del build), validado en vivo, incluido el estado "esperando aprobación".
- Corregido: credenciales inválidas/vencidas devuelven `203 + HTML` (no `401`); ahora se reportan como `ApiAuthException` en vez de fallar al leer JSON.

## Validado contra Jira Cloud real (2026-10-23, solo lectura)
- Lecturas de Rama OK: usuario, tableros, configuración, búsqueda JQL, asignables, transiciones (GET). Sin escrituras (`tests/Rama.Core.Tests/Jira/JiraLiveTests.cs`, categoría `Live`).
- Corregido: los tokens con alcance solo funcionan vía `api.atlassian.com/ex/jira/{cloudId}`; Rama detecta el 401, obtiene el `cloudId` desde `{site}/_edge/tenant_info` y guarda el modo.
- Corregido: el campo de story points se lee de la configuración del tablero (antes fijo `customfield_10016`).
- Pendiente: probar mover tarjetas (transición real), cuando el usuario autorice escrituras.

## Verificado automáticamente
- Build Debug y Release (`-p:DisableAOT=true -p:DisableUpdateDetection=true`): 0 errores, 0 advertencias.
- Pruebas (2026-10-23): 499 en verde (Rama.Core 491 + 4 en vivo omitidas sin credenciales, Rama.App 4, Rama.Terminal 4). Build Release 0 advertencias.
- Cobertura de líneas Rama.Core: ≈89 % (`scripts/coverage-gate.sh`, umbral 80 %).
- Auditoría de seguridad del login Entra (Fable 5.1): medios corregidos; riesgo conocido del token en el entorno de git documentado en `docs/ENTRA.md`.
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
- Probar contra servicios reales: Bitbucket (API token) y escrituras de Jira. Azure DevOps y lecturas de Jira ya validados.
- macOS y Linux: compilan, sin ejecutar (llavero, PTY, notificaciones).
- Velopack/actualización automática y AppImage.
- Chip del ticket en la cabecera del repo (`VEN-412 En curso`).

## Cómo compilar
Al clonar desde un remoto: `git clone --recurse-submodules <url>` (o `git submodule update --init`); sin `depends/AvaloniaEdit` no compila.
`bash scripts/build.sh` (usa el SDK local `GitClient/.dotnet`). Ejecutable Debug: `src/bin/Debug/net10.0/SourceGit.exe`.
