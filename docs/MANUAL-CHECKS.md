# Pruebas manuales pendientes (verificación visual)

Lo que los agentes no pueden verificar sin abrir la GUI. Marcar con fecha y quién lo probó.

## F4b — Resolvedor de conflictos (`src/Rama/Views/ConflictResolver.axaml`)

Preparación (repo desechable): crear un repo con un archivo y 3 regiones en conflicto separadas por ≥ 8 líneas comunes (rama `feature` vs `main`), `git merge feature`, abrir el repo en Rama.

- [ ] Clic derecho en el archivo en conflicto → "Merge" (o botón Merge del panel de conflicto) abre la ventana "Resolver conflictos" (no el editor antiguo).
- [ ] Banda superior: "Merge de feature en main en curso. 1 archivo con conflicto."
- [ ] Panel izquierdo "Actual: main" con punto ámbar y SHA corto; derecho "Entrante: feature" con punto menta.
- [ ] Cada región tiene una casilla por lado; marcar Actual y luego Entrante deja ambas en ese orden en Resultado; desmarcar quita el lado.
- [ ] La región actual tiene borde ámbar; "Conflicto i de n" y los botones ↑/↓ (Ctrl+↑/↓) navegan y desplazan los tres paneles.
- [ ] "Usar ambas (actual primero)" aplica a la región actual.
- [ ] Resultado (AvaloniaEdit) se actualiza en vivo; regiones sin resolver con fondo ámbar, resueltas con fondo menta.
- [ ] Escribir dentro de una región en Resultado la marca como editada a mano (casillas se desmarcan) y el contador de resueltos se actualiza.
- [ ] Con regiones pendientes, "Marcar archivo como resuelto" (Ctrl+S) muestra "Quedan N conflictos sin resolver" y no guarda.
- [ ] Con todo resuelto, guarda, cierra la ventana y el archivo aparece en Preparados (`git diff --cached` lo muestra, sin marcadores).
- [ ] Cerrar con cambios sin guardar pide confirmación.
- [ ] Archivo con CRLF: tras resolver, los fines de línea se conservan (`git diff --cached` sin cambios de EOL espurios).
- [ ] Contraste y tipografía coinciden con `docs/mockups/Conflict.html` (tema grafito).
