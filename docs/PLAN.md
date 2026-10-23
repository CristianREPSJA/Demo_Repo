# Rama — plan de implementación (fork de SourceGit v2026.22)

Fecha: 2026-10-22. Base: SourceGit `cb11d68` (net10.0, Avalonia 11.3.22). SDK local: `GitClient/.dotnet` (10.0.402).
Maquetas aprobadas: https://claude.ai/artifact/3f8qaNha2wKGNMNPwm2zbN

## Decisiones tomadas
- Fork de SourceGit; se conservan `Commands/`, `Models/`, `ViewModels/` y MVVM. Código propio en `src/Rama/` (UI) y `src/Rama.Core/` (sin Avalonia, testeable).
- Azure DevOps principal (Repos, PRs, Pipelines); Bitbucket secundario; Jira Cloud.
- Claude solo vía binario `claude` oficial en terminal integrada; sin llamadas a API de IA. Se elimina el módulo OpenAI de SourceGit.
- Terminal: vendor de Iciclecreek.Avalonia.Terminal 1.0.12 + XTerm.NET 1.0.12 + Porta.Pty 1.0.7 (MIT) con fixes propios.
- v1 sin AOT (`-p:DisableAOT=true`); upstream usa `PublishAot`/`PublishTrimmed`.

## Estimación
- Alcance completo: **27–31 días de sesión (~6 semanas)**.
- MVP propuesto (F0–F7 + F10 mínima): **~17–19 días (~4 semanas)**. Bitbucket (F8), Jira (F9) y login Entra pasan a v1.1 si se acepta el corte.

## Mapa de SourceGit relevante (verificado)
| Área | Archivos/clases |
|---|---|
| Flujo feature | `Commands/Command.cs` → `Models/*` → `ViewModels/Repository.cs`, `WorkingCopy.cs`, `Histories.cs` → `Views/*.axaml` |
| Shell | `Views/Launcher.axaml`, `ViewModels/Launcher.cs`, `LauncherPage.cs`, `Workspace.cs` |
| Settings | `ViewModels/Preferences.cs` (`preference.json`), `Models/RepositorySettings.cs` (`sourcegit.settings` por repo), `App.JsonCodeGen.cs` |
| Credenciales | La app no guarda secretos; fuerza `-c credential.helper=` (`Command.cs:169`, default `manager`) |
| Hosting/PR | Solo URLs: `Models/Remote.cs:94 TryGetCreatePullRequestURL`; no hay clientes REST |
| AI a eliminar | `src/AI/*`, `ViewModels/AIAssistant.cs`, `Views/AIAssistant.axaml`, miembros OpenAI en `Preferences`, `RepositorySettings`, `Repository.cs`, `CommitMessageToolBox`, `WorkingCopy`; paquetes `Azure.AI.OpenAI` y `OpenAI` |
| Theming | `Resources/Themes.axaml`, `Styles.axaml`, `Fonts.axaml`; `App.SetTheme` + `Models/ThemeOverrides`; lanes vía `Models/CommitGraph.SetPens` |
| Diff | Side-by-side e intraline ya existen (`TextDiffView`, `Models/TextInlineChange.cs`); stage por chunk (`OnStageChunk`) |
| Conflictos | `ViewModels/MergeConflictEditor.cs`; `Models/Conflict.cs ConflictResolution` (Ours, Theirs, BothMineFirst, BothTheirsFirst) |
| Locale | `es_ES` existe (`Resources/Locales/es_ES.axaml`) |
| Tests | No hay proyecto de tests de la app |
| CI/packaging | `.github/workflows/*`, `build/scripts/package.*` |

## Fases

### F0 — Fork y baseline (0,5 d)
- [ ] Ramas `upstream` (espejo) y `main`; tag `base/v2026.22`.
- [ ] `scripts/build.ps1|.sh` usando el SDK local (`DOTNET_ROOT`).
- [ ] `src/Rama.Core` + `tests/Rama.Core.Tests` (xUnit + coverlet) en `SourceGit.slnx`.
- Acepta: build Release con `DisableAOT=true` y `dotnet test` verde.

### F1 — Eliminar AI (0,5 d)
- [ ] Borrar módulo, vistas, settings, keys de locales y paquetes OpenAI.
- Acepta: `grep -ri openai src` = 0; un `preference.json` antiguo carga sin error; sin DLLs OpenAI en publish.

### F2 — Rebrand y design tokens (2 d)
- [ ] Nombre, ícono, ConfigDir `Rama` (no compartir settings con SourceGit instalado).
- [ ] `Resources/Rama/Theme.Graphite.axaml` sobre `Themes.axaml`; acento `#e6a23c`; lanes sky/mint/amber/rose/lilac.
- [ ] IBM Plex Sans (OFL) + JetBrains Mono.
- [ ] `es_CL` sobre `es_ES`, forzado por defecto.
- Acepta: captura vs maqueta; sin textos en inglés en pantallas MVP.

### F3 — Shell con riel de íconos (2 d)
- [ ] `src/Rama/Views/IconRail.axaml` + `RailNavigator`: Repositorio, Revisiones pendientes, Tablero Jira, Clonar, Terminal, Ajustes.
- Acepta: navegación por teclado, foco visible, 6 destinos.

### F4 — Pantallas git restyle (4 d)
- [ ] Historial/grafo/WIP vía estilos en `Resources/Rama/Styles.Rama.axaml`.
- [ ] Diff lado a lado por defecto; stage/descartar por bloque.
- [x] Conflicto 3 paneles: casillas por región + resultado editable (AvaloniaEdit); lógica `Rama.Core/Conflicts/ResolutionComposer`.
- [ ] Clonar: pestaña "Desde proveedor" (se completa en F7/F8).
- Acepta: stage/descartar de un bloque verificado con `git diff --cached`; conflicto mixto + edición manual queda stageado sin marcadores.

### F5 — Terminal integrada (4 d; paralela a F2–F4)
- [ ] Vendor en `depends/terminal/` con licencias en `THIRD-PARTY-LICENSES.md`.
- [ ] `WriteInputAsync(text, submit=false)` público; `Utf8StreamDecoder` con estado; corrección de box-drawing; `ChildEnvironment.Sanitize()` (quita `NO_COLOR`, `CLAUDE*`, `SOURCEGIT_*`; fija `TERM`, `COLORTERM`, locale UTF-8).
- [ ] `TerminalPanel` por repo, pestañas claude/shell; `ClaudeLocator` con mensaje si falta el binario.
- [ ] Botones de contexto (`PromptBuilder`): solo insertan texto saneado, nunca Enter.
- Acepta: acentos partidos entre lecturas correctos; `claude` sin variables `CLAUDE_*`; la app nunca toca credenciales de Claude.

### F6 — Infra de integraciones (2 d)
- [ ] `ISecretStore` (Windows Credential Manager / Keychain / libsecret); evaluar `Devlooped.CredentialManager`, si no wrappers propios.
- [ ] `ApiClientBase` (HttpClient inyectable, 429/Retry-After, sin loguear auth).
- [ ] `Poller` + `INotifier` (spike de 0,5 d para toasts en Windows; fallback bandeja + badge).
- Acepta: round-trip de secreto en 3 SO; ningún token en `preference.json` ni logs.

### F7 — Azure DevOps (5 d)
- [ ] Conexión por organización con PAT (scopes: Code R/W, Build Read & execute, Pipeline Resources Use).
- [ ] Listar y clonar repos.
- [ ] PRs: lista, detalle, políticas, diff (`refs/pull/{id}/merge`), voto `PUT .../pullRequests/{id}/reviewers/{reviewerId}?api-version=7.1`.
- [ ] Aprobación de etapas: `PATCH {org}/{project}/_apis/pipelines/approvals?api-version=7.1`.
- [ ] "Pendientes de mi aprobación": polling 60 s + notificación solo de lo nuevo.
- Acepta: voto y aprobación reflejados en la web de Azure; 401/403 indica qué permiso falta.

### F8 — Bitbucket Cloud (1 d)
- [ ] API token: listar workspaces/repos y clonar.

### F9 — Jira Cloud (4 d)
- [ ] Columnas desde configuración del board; issues con `POST /rest/api/3/search/jql`.
- [ ] Arrastrar = transición real; campos requeridos en diálogo; rollback si Jira rechaza.
- [ ] Parser rama↔ticket, "Crear rama", aviso de vencimiento del token.
- Acepta: mover tarjeta cambia el estado real; transición rechazada vuelve a su columna.

### F10 — CI, empaquetado y seguridad (3 d)
- [ ] `azure-pipelines.yml`: Windows/macOS/Linux, tests con gate 80% sobre `Rama.Core`.
- [ ] Velopack: instaladores por plataforma + actualización automática desde feed interno; sin firma (macOS: `xattr -dr com.apple.quarantine`).
- [ ] Revisión de seguridad (Fable 5.1): secretos, entorno del PTY, clientes HTTP, saneo de `PromptBuilder`.
- Acepta: instalables en 3 SO; update N→N+1; 0 hallazgos CRITICAL/HIGH.

## Riesgos
| Riesgo | Mitigación |
|---|---|
| AOT/trimming rompe dependencias | v1 sin AOT; AOT después con smoke test |
| Conflictos al mergear upstream | Overrides en archivos `Rama/*`; ediciones upstream marcadas `// RAMA:` y listadas en `docs/UPSTREAM-TOUCHPOINTS.md` |
| Borrado de AI choca con upstream | Superficie acotada (~15 archivos); resolver "mantener borrado" |
| Notificaciones nativas en Windows | Spike en F6; fallback bandeja |
| Binarios nativos de Porta.Pty | Vendor con SHA256 o compilar en CI |
| Rate limits por polling | 60 s, backoff, Retry-After, pausa tras 401 |
| Alcance ≈2× estimación previa | Corte MVP explícito |

## Pruebas y CI/CD
- Unit (xUnit, TDD, 80% sobre `Rama.Core`): clientes Azure/Bitbucket/Jira con `HttpMessageHandler` falso; mappers; `Utf8StreamDecoder`; `ChildEnvironment`; `BranchTicketParser`; `PromptBuilder`; `ResolutionComposer`; rollback del board; dedupe del poller. Sin FluentAssertions v8 (licencia comercial).
- Opcional: `Avalonia.Headless.XUnit` para ViewModels nuevos.
- Manual: checklist por pantalla vs maqueta en 3 SO por release.
- CI: build+test+cobertura por PR; empaquetado en tags `rama-v*`; merge mensual de upstream en `sync/upstream-<tag>` (nunca rebase de `main`).

## Orden
1. F0 → F1 → F2.
2. En paralelo: UI (F3 → F4), terminal (F5), Azure (F6 → F7); CI de F10 temprano.
3. F8 y F9 (paralelas), cierre de F10.
