# Archivos upstream modificados por Rama

Formato: `ruta` — motivo.

- `src/SourceGit.csproj` — referencia a lib/Rama.Core.
- `SourceGit.slnx` — proyectos lib/ y tests/.

F1: removed AI
- `src/AI/*`, `src/ViewModels/AIAssistant.cs`, `src/Views/AIAssistant.axaml(.cs)` — F1: removed AI (eliminados).
- `src/SourceGit.csproj`, `THIRD-PARTY-LICENSES.md`, `README.md` — F1: removed AI.
- `src/Models/RepositorySettings.cs`, `src/ViewModels/{Preferences,Repository,RepositoryConfigure}.cs`, `src/App.axaml.cs` — F1: removed AI.
- `src/Views/{CommitMessageToolBox,Preferences,RepositoryConfigure}.axaml(.cs)`, `src/Views/WorkingCopy.axaml.cs` — F1: removed AI.
- `src/Resources/Icons.axaml`, `src/Resources/Locales/*.axaml` (16 archivos) — F1: removed AI.

F2: rebrand
- `src/SourceGit.csproj` — Product/Description/Company "Rama".
- `src/Native/{Windows,MacOS,Linux}.cs` — directorio de datos `Rama`.
- `src/App.axaml`, `src/Views/About.axaml` — nombre, merge del tema grafito y es_CL, titulo About.
- `src/App.axaml.cs` — carriles del grafo de Rama (`Branding.GraphPalette`).
- `src/Models/Locales.cs`, `src/ViewModels/Preferences.cs` — locale es_CL y tema Dark por defecto.
- `src/App.ico` — icono nuevo (generado con `tools/make-icon.ps1`).

F3: shell con riel
- `src/Views/Launcher.axaml` — columna izquierda con IconRail y contenedor de paginas de destino.
- `src/ViewModels/Launcher.cs` — propiedad `Rail` (RailNavigator).
- `src/SourceGit.csproj` — recurso `Rama/Resources/Images/*.png` (marca).

F6b: cuentas
- `src/Views/Launcher.axaml` — pagina `AccountsPage` en el contenedor de destinos (destino Cuentas del riel).
F4a: restyle pantallas git
- `src/App.axaml` — merge de `Rama/Resources/Styles.Rama.axaml` tras Styles.axaml.
- `src/Views/CommitRefsPresenter.cs` — pastillas palidas por carril (Render/Measure).
- `src/Views/ChangeStatusIcon.cs` — letra de estado coloreada plana (M ambar, A menta, D rosa).
- `src/Views/Avatar.cs` — fallback de iniciales en circulo con par palido.
- `src/Views/Histories.axaml` — RowHeight 30, subject 30, clase `sha_faint` en SHA.
- `src/Views/{Repository,Compare,RevisionCompare,About,SelfUpdate}.axaml` — texto sobre ambar usa `Brush.OnAccent` (#1c1406).
- `src/ViewModels/Preferences.cs` — defaults: diff lado a lado ON, avatar de iniciales (no estilo GitHub).
- `src/SourceGit.csproj` — referencia a lib/Rama.Terminal (terminal integrada, F5).
- `SourceGit.slnx` — proyectos lib/Rama.Terminal y tests/Rama.Terminal.Tests.
- `src/App.axaml.cs` — entrada oculta `RAMA_TERMINAL_DEMO=1` (abre solo el panel de terminal).

F7/F8: pagina Clonar
- `src/Views/Launcher.axaml` — pagina `ClonePage` en el contenedor de destinos (destino Clonar del riel).
