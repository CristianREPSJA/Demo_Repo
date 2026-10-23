# Brief común para agentes (Rama)

Rama = fork interno de SourceGit (MIT). Lee `docs/PLAN.md` (fases) y, si tocas UI, `docs/DESIGN-SPEC.md` + `docs/mockups/*.html` (maquetas aprobadas, HTML de referencia; no son código a copiar).

## Entorno (obligatorio)
- Repo: `C:\Users\Cris\Desktop\Personal\GitClient\rama` (o el worktree que te indiquen). Trabaja SOLO dentro de esa carpeta.
- SDK: .NET 10 local en `C:\Users\Cris\Desktop\Personal\GitClient\.dotnet\dotnet.exe` con `DOTNET_ROOT` apuntando ahí. El `dotnet` del sistema es v9: no lo uses.
- Build + tests: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1` (Debug, `-p:DisableAOT=true`, corre `tests/Rama.Core.Tests`).
- PROHIBIDO cambiar el sistema: nada de instalaciones globales (winget, `dotnet tool install -g`, npm -g), ni tocar `~/.claude`, PATH, registro o config git global. Paquetes NuGet vía PackageReference están permitidos (restauran a la caché normal). Herramientas dotnet solo como tool local (`.config/dotnet-tools.json`).
- TODO LOCAL Y OFFLINE (regla del usuario): nada de operaciones git de red (clone/fetch/pull/push/submodule remoto), nada de descargas de internet (GitHub, curl, WebFetch de código). NuGet: solo paquetes ya presentes en `~/.nuget/packages`. Si algo requiere red, no lo hagas y repórtalo.
- No cambies los remotes. Commits locales en tu rama, formato `<tipo>: <descripción>` (feat, fix, refactor, chore, test, docs, style) en español, terminando con la línea:
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`

## Convenciones de código
- Respeta el estilo de SourceGit (namespace `SourceGit.*`, MVVM con CommunityToolkit.Mvvm, `Commands/` = wrappers git CLI, compiled bindings Avalonia).
- Código nuevo de lógica sin UI va en `lib/Rama.Core` (namespace `Rama.Core.*`, sin Avalonia) con pruebas xUnit en `tests/Rama.Core.Tests` (TDD: prueba primero; meta 80% de cobertura de Rama.Core). Sin FluentAssertions (licencia comercial).
- Código nuevo de UI va en `src/Rama/` (Views/ViewModels/Resources propios) cuando sea posible; si editas un archivo upstream, marca el bloque con `// RAMA:` (o `<!-- RAMA: -->` en XAML) y agrégalo a `docs/UPSTREAM-TOUCHPOINTS.md` (archivo + motivo, una línea).
- Comentarios mínimos en español. Funciones < 50 líneas, archivos < 800, sin secretos hardcodeados, sin `Console.WriteLine` de depuración.
- Textos de UI en español de Chile vía recursos de locale (no hardcodear strings visibles si el área usa `Text.*`).
- Claude: solo se lanza el binario `claude` oficial en la terminal; NUNCA llamadas a APIs de IA ni manejo de credenciales de Claude.

## Antes de terminar
1. `scripts/build.ps1` en verde (0 errores; no introducir advertencias nuevas).
2. Commit(s) en tu rama.
3. Reporte final ≤ 200 palabras: qué hiciste, archivos clave, pruebas agregadas (n.º), qué quedó pendiente o SUPUESTO, riesgos. Sin relleno.
