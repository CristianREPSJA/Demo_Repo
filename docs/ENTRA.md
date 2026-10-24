# Iniciar sesión con Microsoft (Entra ID) en Azure DevOps

Alternativa al PAT para la cuenta de Azure DevOps de Rama. Microsoft retira los PAT globales el
**1 de diciembre de 2026**; con Entra el equipo usa la misma cuenta con la que ya entra a `dev.azure.com`.

## Qué hace Rama

- **Login**: cliente público de MSAL, autoridad `https://login.microsoftonline.com/organizations`, flujo
  *authorization code + PKCE* en el navegador del sistema, redirección `http://localhost` (puerto aleatorio).
  Scope: `499b84ac-1321-427f-aa17-267ca6975798/.default` (recurso Azure DevOps).
- **Renovación**: primero `AcquireTokenSilent` (refresh token en caché); el navegador solo se abre al pulsar
  "Iniciar sesión con Microsoft". Si la sesión caduca, la API devuelve `ApiAuthException` con el aviso
  "Inicia sesión con Microsoft otra vez" y la UI lo muestra en Cuentas.
- **Caché de tokens**: MSAL Extensions, cifrado por el SO (DPAPI en Windows, Keychain en macOS, libsecret en
  Linux) en `<directorio de datos de Rama>/entra/rama-entra.msalcache`. En `rama-accounts.json` solo queda
  `orgUrl`, `project`, `auth: "Entra"`, `userName` (UPN) y `entraClientId`; nunca tokens.
- **API REST**: cabecera `Authorization: Bearer <token>` por petición. Ante `401` o la página HTML de inicio de
  sesión que Azure DevOps devuelve con `203` cuando la credencial es inválida, se fuerza una renovación y se
  reintenta una vez.
- **git** (`clone`, `fetch`, `pull`, `push`, `ls-remote`, `submodule`, `remote`, `lfs`): antes del comando se
  renueva el token en silencio y se inyecta en el proceso hijo vía
  `GIT_CONFIG_COUNT` / `GIT_CONFIG_KEY_n=http.https://dev.azure.com/.extraheader` /
  `GIT_CONFIG_VALUE_n=Authorization: Bearer <token>`. Nunca va en la línea de comandos, en disco ni en el log.
  El `credential.helper` configurado sigue activo como respaldo (y para remotos que no son Azure DevOps).
- **PAT**: sin cambios. Cada cuenta elige su método; al pasar una organización a Entra se borra su PAT del llavero
  si ninguna otra cuenta de esa organización lo usa.

## Registro de la aplicación (lo hace un administrador del tenant, una sola vez)

1. Portal de Entra (`entra.microsoft.com`) → **Aplicaciones** → **Registros de aplicaciones** → **Nuevo registro**.
   - Nombre: `Rama` (o el que prefieran).
   - Tipos de cuenta admitidos: *Solo cuentas de este directorio organizativo* (un solo tenant).
   - Redirección: plataforma **Aplicaciones móviles y de escritorio**, URI `http://localhost`.
2. **Autenticación** → *Configuración avanzada* → **Permitir flujos de cliente público**: **Sí**.
3. **Permisos de API** → **Agregar permiso** → **Azure DevOps** → *Permisos delegados* → `user_impersonation`.
   Si el tenant exige consentimiento de administrador, pulsar **Conceder consentimiento de administrador**.
4. Copiar el **Id. de aplicación (cliente)** de la página *Información general*: ese GUID es el Client ID.
5. Si el acceso condicional lo exige, agregar la app a las políticas correspondientes. No hace falta secreto de
   cliente: Rama es un cliente público (no puede guardar secretos).

No se usa ningún Client ID de Microsoft (p. ej. el de Azure CLI): Rama solo arranca el login con un Client ID
registrado por el equipo.

## Dónde se configura el Client ID

| Prioridad | Dónde | Uso |
|---|---|---|
| 1 | Variable de entorno `RAMA_ENTRA_CLIENT_ID` | Equipos gestionados (GPO/Intune/script de instalación). Si está definida, manda sobre lo guardado. |
| 2 | Cuentas → Azure DevOps → *Cuenta Microsoft (Entra ID)* → **Client ID** | Se guarda en `rama-accounts.json` (no es secreto). |

Sin Client ID válido (GUID), el botón muestra el aviso de que falta registrar la aplicación y no abre el navegador.

## Uso

1. Cuentas → Azure DevOps: URL de la organización y proyecto; elegir **Cuenta Microsoft (Entra ID)**.
2. Escribir el Client ID (si no viene por variable de entorno) y pulsar **Iniciar sesión con Microsoft**.
3. Se abre el navegador; al terminar, Rama consulta `connectionData`, muestra "Conectado como …", guarda la
   cuenta y muestra "Sesión iniciada como <UPN>".
4. **Probar conexión** usa solo la sesión en caché (no abre el navegador). **Quitar** borra la cuenta y cierra la
   sesión de MSAL si ninguna otra cuenta usa ese usuario.

## Riesgos conocidos

- **El Bearer viaja en el entorno del proceso git.** Rama lo inyecta en `GIT_CONFIG_VALUE_n` del `git` hijo
  (solo para `clone/fetch/pull/push/ls-remote/submodule/remote/lfs`). Todo lo que ese `git` ejecute hereda ese
  entorno: *hooks* del repositorio (`post-checkout`, `post-merge`, `pre-push`…), filtros de LFS/`clean`/`smudge`,
  `fsmonitor` y helpers externos. Un repositorio malicioso puede leer el token desde un hook.
- **Paridad con PAT/GCM.** Con el PAT clásico un hook ya puede ejecutar `git credential fill` y obtener la
  credencial; y el caché de MSAL (DPAPI en Windows) lo descifra cualquier proceso del mismo usuario. Entra no
  empeora el modelo frente a un atacante activo, pero sí añade una **fuga pasiva** (variable de entorno visible,
  p. ej. en `env`/volcados de un hook).
- **Endurecimiento futuro.** Un *credential helper* propio (`credential.helper=<rama> --entra`) evitaría la fuga
  pasiva por entorno; no detendría a un hook activo, porque git propaga `-c` a los subprocesos vía
  `GIT_CONFIG_PARAMETERS` y el helper responde a cualquier `git credential fill`. Queda como mejora pendiente.
- **Recomendación:** no abrir ni clonar repositorios no confiables con una cuenta Entra activa; revisar
  `.git/hooks`, `.lfsconfig` y `core.fsmonitor` antes de hacer `fetch`/`pull` en repos de terceros.
- **Caché cifrado obligatorio.** Si el llavero del SO no está disponible (p. ej. Linux sin GNOME Keyring/KWallet),
  `MsalCacheHelper.VerifyPersistence()` falla y Rama muestra "No se pudo proteger el caché de sesión de Microsoft…"
  en vez de guardar tokens en texto plano. Alternativa: PAT.
- **Al quitar la cuenta o pasarla a PAT** se invalida el token en memoria y se cierra la sesión en el caché de
  MSAL (si ninguna otra cuenta usa ese usuario). Los clientes ya creados fallan con "Inicia sesión con Microsoft
  otra vez".

## Limitaciones conocidas

- El token de Azure DevOps dura ~1 h; la renovación es silenciosa mientras el refresh token sea válido (días a
  semanas según la política del tenant). Si falla, hay que volver a pulsar el botón.
- Los permisos efectivos son los del usuario en Azure DevOps (no hay scopes de PAT): los mensajes "Falta el permiso
  del PAT…" ante `403` indican, en modo Entra, que al usuario le falta ese permiso en la organización/proyecto.
- Linux necesita un *secret service* activo (GNOME Keyring/KWallet); sin él el login se rechaza con un mensaje claro
  (ver "Riesgos conocidos").
- Pendiente de prueba manual (ver `docs/MANUAL-CHECKS.md`): login real, clon/fetch/push con Entra y
  comportamiento tras caducar la sesión. Sin red ni GUI en el entorno de desarrollo no se pudo ejecutar.
