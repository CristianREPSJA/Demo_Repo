# Verificaciones manuales de la terminal (F5/F5b)

Requieren lanzar la app y una sesión PTY real; no se automatizan (regla antivirus). Ejecutar en un equipo con `claude` en el PATH.

## Entradas
- App completa: abrir un repositorio, riel > Terminal (página) o botón "Terminal" de la barra del repositorio (panel inferior).
- Demo aislada (oculta): `RAMA_TERMINAL_DEMO=1` abre solo el panel en el directorio actual. `RAMA_TERMINAL_DEMO_TAB=shell` selecciona la shell; `RAMA_TERMINAL_DEMO_INPUT="texto"` escribe y envía ese texto solo en la shell (nunca en claude). Es solo para desarrollo.

## Pasos
1. **Sin repositorio**: riel > Terminal con la pestaña Bienvenida activa muestra "Abre un repositorio para usar la terminal.".
2. **Una instancia por repo**: abrir dos repos; en cada uno abrir la terminal. Escribir `echo A` en la shell del repo 1; cambiar al repo 2 y volver: el historial de la sesión sigue ahí. Cerrar la pestaña del repo 1 y reabrirla: la terminal arranca de cero (proceso anterior terminado).
3. **Una sola vista a la vez**: abrir el panel inferior, luego riel > Terminal: la sesión aparece en la página (misma sesión) y el panel inferior queda vacío; al volver al repositorio, reaparece en el panel.
4. **Eco unicode**: en la shell, `echo "ñandú áéíóú ─│┌┐ 日本語 😀"` se ve idéntico (sin `?`, sin cortes).
5. **Resize**: arrastrar el ancho de la ventana; el contador `cols×filas` de la barra de estado cambia y `tput cols`/`mode con` coincide. Texto largo reajusta sin corrupción.
6. **claude renderiza**: pestaña claude arranca `claude` en el directorio del repo; caja de entrada, colores y bordes (box-drawing) correctos; sin mensaje de binario faltante.
7. **Botones insertan sin Enter** (con claude abierto o cerrado; si está cerrado se inicia solo):
   - Seleccionar archivos en Cambios locales > "Explicar cambios seleccionados": el texto aparece en la caja de entrada y NO se envía.
   - Preparar archivos > "Redactar mensaje de commit": incluye los archivos preparados y la clave del ticket de la rama (p. ej. `VEN-412`).
   - "Revisar rama contra develop" (o main/master si no existe develop; `origin/...` si solo hay remota).
   - "Resumir VEN-xxx" solo visible si la rama tiene clave.
   - Formulario de commit > "Pedir mensaje a Claude": abre el panel inferior e inserta el pedido; nota "Escribe el pedido en la terminal. Tú lo envías con Enter.".
   - Pulsar el mismo botón dos veces: el texto se agrega, nunca se envía solo.
8. **Entorno limpio** (en la shell): `env | grep -E '^(CLAUDE|SOURCEGIT_|NO_COLOR)'` no devuelve nada; `echo $TERM $COLORTERM` = `xterm-256color truecolor`; `echo $LANG` termina en `UTF-8`. En Windows usar `Get-ChildItem Env: | Where-Object Name -match '^(CLAUDE|SOURCEGIT_|NO_COLOR)'`.
9. **Cierre**: cerrar la app y comprobar en el Administrador de tareas que no quedan procesos `claude`/shell huérfanos.
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
