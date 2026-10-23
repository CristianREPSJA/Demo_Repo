# Terminal integrada: dependencias y binarios nativos

Por restricción de trabajo sin red (F5), las librerías NO se vendorizan como código fuente:
se consumen por `PackageReference` desde `lib/Rama.Terminal/Rama.Terminal.csproj`
(versiones fijas, todas MIT; ver `THIRD-PARTY-LICENSES.md`).

| Paquete | Versión | Commit de origen (nuspec) |
|---|---|---|
| Iciclecreek.Avalonia.Terminal | 1.0.12 | tomlm/Iciclecreek.Avalonia.Terminal@3da5aad71e02517afa40f187461349ffafb2497b |
| XTerm.NET | 1.0.12 | tomlm/XTerm.NET@18eeafcb37b1a7584ac11c45264a037e17158b8f |
| Porta.Pty | 1.0.7 | tomlm/Porta.Pty@54684ba55148ed6bcd0c827ad7e8841a3289a466 |

## Binarios nativos de Porta.Pty 1.0.7 (sin fuente compilada por nosotros)

Windows usa ConPTY del sistema (sin binario). Linux/macOS cargan `libporta_pty` incluido en el paquete:

| RID | Archivo | SHA256 |
|---|---|---|
| linux-arm64 | runtimes/linux-arm64/native/libporta_pty.so | 523b1b7a5ebbd7c289522462f227070981e4e17882f7806ecc7da126090d681f |
| linux-x64 | runtimes/linux-x64/native/libporta_pty.so | 1b8b33dfc3f7837c1d90cfc585ddb6c3d537577a50dd533dfe804d9976b56f26 |
| osx-arm64 | runtimes/osx-arm64/native/libporta_pty.dylib | 0ae476fedbf9d3b0599279c264313bfe0f5f9e85238cec12a4411e99644ecd50 |
| osx-x64 | runtimes/osx-x64/native/libporta_pty.dylib | 10f14c7f5a7a8c7cc7aa9b70332a9fb72d28398e1dda96521f9f5a6f51d72e5c |

Pendiente (F10): compilar `porta_pty.c` en CI y comparar, o vendorizar el fuente cuando haya red.

## Correcciones aplicadas desde nuestro código (sin parchear la librería)

- `Rama.Terminal/TerminalBridge.cs`: único uso de reflexión (`TerminalView._ptyConnection`,
  `TerminalView.SendToPtyAsync`). Prueba de contrato en `tests/Rama.Terminal.Tests`.
- `TerminalSession.WriteInputAsync(text, submit=false)`: bracketed paste si la app lo activó; Enter solo con `submit`.
- UTF-8 partido entre lecturas: la librería hace `Encoding.UTF8.GetString` por bloque. Tras lanzar, se reemplaza
  la conexión por `Utf8PtyConnection` (lecturas alineadas con `Rama.Core.Terminal.Utf8BoundaryStream`).
  Limitación: la primera lectura (ya en curso al instalar el wrapper) no queda cubierta.
- Entorno: la librería no acepta entorno propio; `Porta.Pty` fusiona `Environment.GetEnvironmentVariables()`.
  `TerminalSession` aplica `ChildEnvironment.Sanitize` al proceso solo durante el spawn (con lock) y lo restaura.

## Problemas conocidos

- Desplazamiento vertical de caracteres box-drawing: está en el render de `TerminalView` (privado); no es
  corregible sin parchear la librería. Queda documentado.
- Si la vista se quita del árbol lógico, la librería mata el proceso (`CleanupProcess`); el panel oculta
  pestañas con `IsVisible` en vez de quitarlas.
