# Tema grafito, fuentes y locale (F2)

## Archivos
- `src/Rama/Resources/Theme.Graphite.axaml`: se mezcla en `App.axaml` justo despues de `Themes.axaml`; sobrescribe las claves `Color.*` y `SystemAccent*` de la variante **Dark**. Light queda con el tema original de SourceGit (Rama es solo oscuro; el default es `Dark`).
- `src/Rama/GraphPalette.cs`: carriles del grafo; `App.SetTheme` lo aplica en vez de `SetDefaultPens`.
- Un archivo `ThemeOverrides` del usuario sigue ganando sobre el tema (se mezcla despues).

## Mapeo Color.* (Dark) -> token Rama
| Clave | Valor | Token |
|---|---|---|
| Window, TitleBar | #17181A | ground |
| ToolBar, Contents | #1D1E21 | surface |
| Popup, SubPanel | #25272B | raised |
| Badge | #2C2E33 | active (BadgeFG = text #ECECEC) |
| WindowBorder, Border2 | #2C2E33 | border |
| Border0 | #222327 | rule |
| Border1, PopupBorder, FlatButton.FloatingBorder | #3A3D43 | control |
| FlatButton.Background / Hovered | #25272B / #2C2E33 | raised / active |
| FG1 / FG2 | #ECECEC / #A9ABB1 | text / muted (faint #8C8F96 sin clave propia; F4 lo usara en metadatos) |
| Conflict / Conflict.Foreground | #2A2416 / #F0C27A | banda de conflicto / tinta ambar palida |
| Conflict.MineBG / TheirsBG | #405AA9E6 / #40E6A23C | sky / amber translucido |
| Diff.AddedBG / AddedHighlight | #17301F / #24502F | linea / intralinea agregada |
| Diff.DeletedBG / DeletedHighlight | #34191F / #5A2533 | linea / intralinea eliminada |
| Diff.EmptyBG | #40000000 | relleno de hueco |
| Diff.BlockBorderHighlight | #E6A23C | amber |
| Link | #5AA9E6 | sky |
| InlineCode / InlineCodeFG | #2C2E33 / #ECECEC | active / text |
| SystemAccentColor (+Light1-3, Dark1-3) | #E6A23C | acento unico; ListLow/ListMedium = raised/active (hover/seleccion) |

Carriles del grafo: sky #5AA9E6, mint #5CC58A, amber #E6A23C, rose #E0718F, lilac #B294E6, luego Turquoise, Khaki, Olive, RoyalBlue, Teal.

Pendiente (F3/F4): texto blanco sobre fondos de acento ambar (contraste), estilos de lista/seleccion propios.

## Fuentes
IBM Plex Sans no esta disponible offline. Por ahora: UI = Inter (`Avalonia.Fonts.Inter`, `Fonts.Default` en `Resources/Fonts.axaml`), monoespaciada = JetBrains Mono NL (`Resources/Fonts/`). Los defaults no usan `App.SetFonts` salvo que el usuario configure fuentes.

Para cambiar a Plex mas adelante: copiar `IBMPlexSans-*.ttf` (OFL) a `src/Resources/Fonts/` (ya se incluyen como AvaloniaResource) y cambiar en `Resources/Fonts.axaml` la linea `Fonts.Default` a `fonts:SourceGit#IBM Plex Sans` (la coleccion `fonts:SourceGit` carga todo `Resources/Fonts`). Cambiar tambien `DefaultFamilyName` en `App.BuildAvaloniaApp` si se quiere como fuente de respaldo.

## Locale
`es_CL` (`src/Rama/Resources/Locales/es_CL.axaml`) incluye es_ES y solo redefine marca, giros chilenos y claves nuevas `Text.Rama.*`. Es el default de instalaciones nuevas (`Preferences._locale`). Al estar fuera de `src/Resources/Locales`, el chequeo de localizacion upstream no lo toca; las claves faltantes caen a es_ES/en_US por herencia.

## Brechas vs maqueta (captura `docs/evidence/f2-main.png`)
Esperadas; el layout llega en F3/F4: sin riel de iconos propio ni cabecera Rama, pestañas/tabs y toolbar de SourceGit, encabezados de seccion y de columnas en MAYUSCULAS (la spec los prohibe), seleccion/pestaña activa con acento ambar subrayado (ok) pero filas sin pastillas de ramas pales, avatares genericos en vez de iniciales con par pale, badge amarillo de cambios, tarjeta de "sin seleccion" gris, fuente Inter en vez de Plex, titulo de ventana con nombre de carpeta (no "Rama"), densidad de filas (24 px vs 30 px), tipografia mono sin tabular en columnas.
