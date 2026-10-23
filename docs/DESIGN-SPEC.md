# Rama — design spec for artboards (read fully before writing)

Product: "Rama" (Spanish for branch), internal cross-platform desktop git client (GitKraken-like) for a Chilean dev team. All UI copy in **Spanish (Chile), sentence case**. Dark theme only.

## Reference implementation
`project/Main.dc.html` is the approved first artboard. **Copy its whole `<head>`, `<helmet>` (font link + entire `<style>` block), the left rail `<nav>` and the top `<header>` verbatim**, then change only what your screen needs (rail `aria-current` item, header contents if your screen differs). Reuse its classes (`.app .mono .ico .rail-btn .tb-btn .count .sec-h .item .dot .ref .row .who .av .when .sha .file .st .path .lines .btn.primary .btn.ghost .field .vh`). You may ADD classes to the helmet style for your screen; do not change existing ones.

## Tokens (already in helmet as CSS vars on .app)
ground #17181a · surface #1d1e21 · raised #25272b · active #2c2e33 · border #2c2e33 · rule #222327 · control #3a3d43 · text #ececec · muted #a9abb1 · faint #8c8f96 · amber #e6a23c (the ONE accent: current branch/HEAD, primary button, focus ring) · amber-ink #1c1406.
Lane/identity hues (data only, never chrome): sky #5aa9e6 (main), mint #5cc58a (develop / additions), amber #e6a23c (feature/VEN-412, current), rose #e0718f (feature/VEN-398 / deletions), lilac #b294e6 (hotfix/VEN-420). Pale pill pairs: amber bg #3a2f1a ink #f0c27a; rose bg #36202a ink #ee9ab1; sky bg #1b2c3b ink #8ec5ef; mint bg #1c3326 ink #8fdcae; lilac bg #2a2438 ink #c9b3f0; slate-person bg #22293a ink #a9bff0.
Diff backgrounds: removed line #34191f (intraline #5a2533), added line #17301f (intraline #24502f), conflict marker band #2a2416.
Fonts: IBM Plex Sans (UI, 13px body, 12px labels, 11px meta minimum), JetBrains Mono (code, SHAs, ticket keys, paths in code views). Radii: 6 controls, 8 panels, 4 pills. Icons: inline SVG, 16px, stroke 1.5, `class="ico"`, outline only.

## Shared fake data (keep consistent with Main)
Repo `erp-ventas`, hosted in **Azure DevOps** (Azure Repos + Azure Pipelines), organization `comercial-sur-dev`, project `ERP`. Header subtitle reads "Azure DevOps, comercial-sur-dev". PRs and pipelines live in Azure. Current branch `feature/VEN-412-descuentos-volumen` (1 commit ahead of origin). Other branches: main, develop, feature/VEN-398-libro-ventas-sii, hotfix/VEN-420-redondeo-iva, release/3.8.
People (initials + pill pair): Diego Muñoz DM (amber pair, the current user, "Tú"), Valentina Soto VS (rose pair), Camila Rojas CR (mint pair), Matías Fuentes MF (sky pair), Ignacio Pérez IP (slate-person pair).
Jira project key VEN. Tickets: VEN-412 "Descuento por volumen en notas de venta" (Diego, En curso, Historia), VEN-398 "Libro de ventas electrónico para el SII" (Valentina, En revisión), VEN-420 "Redondeo de IVA en boletas" (Ignacio, Listo, Error), VEN-377 "Búsqueda de clientes por RUT" (Matías, En revisión).
Unstaged files: M src/Ventas/NotaVentaService.cs (+12 −4), M src/Ventas/Dto/NotaVentaDto.cs (+3 −1). Staged: A src/Ventas/Descuentos/TramoDescuento.cs (+41). Code is C# (.NET 8).

## Banned (AI-design tells)
No gradients, glows, glass/blur, neon. No colored left-border cards. No icons in tinted circles/squares. No emoji. No ALL-CAPS tracked labels, no eyebrow labels above headings, no "A · B · C" middle-dot meta strings, no "→" appended to buttons/links, no "01/02/03" numbering. No zebra rows, no uppercase table headers. No lorem ipsum / Acme / John Doe. No hero headers, no greeting. Accent amber only on: current/HEAD, the single primary button, focus. Status uses pale pill pairs. Shadows only on floating things (menus, dragged card, dialogs): `0 12px 32px rgba(0,0,0,.45)`. One h1 per screen (14–24px). Numbers tabular, right-aligned in columns.

## .dc.html format rules (fail silently if broken)
- Keep `<script src="./support.js"></script>` in head exactly. Close every non-void element, quote every attribute.
- All UI is literal markup inside `<x-dc>`; never built by script (no innerHTML/appendChild). Root is fluid (no fixed px width), `min-height: 900px`; main row uses `display:flex; flex-wrap:wrap`, panels with flex-basis so they stack at phone width; wide code/tables inside `overflow-x:auto` boxes.
- Must end with `<script type="text/x-dc" data-dc-script data-props='{"$preview":{"width":1440,"height":900}}'>class Component extends DCLogic { renderVals() { return {...}; } }</script>` — classic JS, no imports.
- `{{hole}}` = dotted lookup into renderVals() only, never expressions. Events: `onClick="{{ fn }}"` with fn returned from renderVals; per-item handlers precomputed in renderVals (`items.map(x => ({...x, pick: () => this.setState({sel:x.id})}))`). Branch with `<sc-if value="{{ flag }}" hint-placeholder-val="{{ true }}">`, loop with `<sc-for list="{{ items }}" as="item" hint-placeholder-count="3">`. Never string event handlers like onsubmit="...".
- Links between artboards: `<a href="Main.dc.html">` (siblings: Main, Diff, Conflict, Jira, Terminal, Clone .dc.html).
- No network except the Google Fonts link already in helmet. No iframes. Real `<button>`, `<a href>`, `<input>`+`<label>`; `aria-label` on icon-only buttons; decorative svg `aria-hidden="true"`.
- Text contrast ≥4.5:1 (the token greys pass on ground/surface).
- Write the file ONCE with the Write tool. Do not render, screenshot, run browsers, install anything, or publish. Do not touch other files.
