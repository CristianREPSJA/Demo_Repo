# Release de Rama

## Flujo actual (sin Velopack)
1. Subir tag `rama-vX.Y.Z` (CI: `azure-pipelines.yml` o `.github/workflows/rama-ci.yml`).
2. Tras pasar build + pruebas + gate de cobertura (>= 80% sobre `Rama.Core`), la etapa de empaquetado publica por RID
   (`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`) con `-p:DisableAOT=true -p:DisableUpdateDetection=true`.
3. Artefactos: `rama_<ver>.<rid>.zip` (Windows y `.app` macOS), `rama_<ver>-1_amd64.deb`, `rama-<ver>-1.x86_64.rpm`.
   El AppImage se omite (`RAMA_SKIP_APPIMAGE=1`): su feed zsync apuntaba a sourcegit upstream.
4. `DisableUpdateDetection` es obligatorio: el comprobador upstream consulta `sourcegit-scm.github.io`.

Local: `bash scripts/build.sh [Debug|Release]` y luego `bash scripts/coverage-gate.sh 80 Rama.Core tests`
(o `scripts/coverage-gate.ps1`).

## App sin firmar
- macOS: el `.app` va firmado ad-hoc. Tras descargar: `xattr -dr com.apple.quarantine Rama.app`.
- Windows: SmartScreen avisara ("Windows protegio su PC" > Mas informacion > Ejecutar de todas formas).
  Alternativa: distribuir desde un recurso compartido interno (no marca Mark-of-the-Web).
- Linux: `sudo dpkg -i rama_*.deb` / `sudo rpm -i rama-*.rpm`.

## Habilitar Velopack mas adelante (no incluido: requiere NuGet/red)
1. Paquete solo en CI, tras una propiedad, en `src/SourceGit.csproj`:
   ```xml
   <ItemGroup Condition="'$(RamaUseVelopack)' == 'true'">
     <PackageReference Include="Velopack" Version="x.y.z" />
   </ItemGroup>
   <PropertyGroup Condition="'$(RamaUseVelopack)' == 'true'">
     <DefineConstants>$(DefineConstants);RAMA_VELOPACK</DefineConstants>
   </PropertyGroup>
   ```
2. Primera linea de `Main` (antes de Avalonia), bajo `#if RAMA_VELOPACK`:
   `Velopack.VelopackApp.Build().Run();`
3. Reemplazar `Check4Update` (App.axaml.cs) por `UpdateManager` apuntando al feed interno
   (`new UpdateManager(feedUrl)`; `CheckForUpdatesAsync` > `DownloadUpdatesAsync` > `ApplyUpdatesAndRestart`).
   Mantener `DISABLE_UPDATE_DETECTION` para el comprobador upstream.
4. Herramienta: `dotnet tool install -g vpk` en el agente de CI (o tool local en `.config/dotnet-tools.json`).
5. Empaquetar por RID (tras `dotnet publish -r <rid> -p:RamaUseVelopack=true -p:DisableAOT=true -p:DisableUpdateDetection=true -o publish/<rid>`):
   ```
   vpk pack -u Rama -v <ver> -p publish/win-x64   -e SourceGit.exe --packTitle Rama -o releases/win
   vpk pack -u Rama -v <ver> -p publish/osx-arm64 -e SourceGit --packTitle Rama -o releases/osx-arm64
   vpk pack -u Rama -v <ver> -p publish/osx-x64   -e SourceGit --packTitle Rama -o releases/osx-x64
   vpk pack -u Rama -v <ver> -p publish/linux-x64 -e SourceGit --packTitle Rama -o releases/linux
   ```
   (Cada `pack` en su SO; usar `--channel` por RID si comparten feed. Para delta, `vpk download` la version previa antes de `pack`.)
6. Feed interno: copiar `releases/<canal>` a un recurso compartido (`\servidor\rama\releases`) o contenedor Azure Blob
   (`vpk upload az --container rama --connectionString ...` o `azcopy`). La app apunta a esa URL/ruta.
7. Sin firma: Velopack acepta instalar sin certificado; aplican igual SmartScreen y la cuarentena de macOS descritas arriba.
8. Validar update N a N+1 publicando dos versiones al feed de prueba antes de activar el canal.
