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
