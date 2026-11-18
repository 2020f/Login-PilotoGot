# Handoff de despliegue · Portal comercial Envirapido

> **Actualización posterior de Inicio:** consultar
> [DESPLEGAR-PORTADA.md](DESPLEGAR-PORTADA.md). Si el portal base ya está operativo,
> esa entrega actualiza sólo el bundle Next, sin repetir los pasos de instalación
> inicial de CRM, API, SQL o SMTP de este documento.

> **Registro manual aprobado posteriormente:** ver
> [REGISTRO-MANUAL-20261117.md](docs/portal/REGISTRO-MANUAL-20261117.md).
> Si el portal ya existe, esta corrección actualiza API/Contracts, CRM para la
> vista SuperAdmin y Next; no crea bases ni añade migraciones. Conservar datos,
> conexiones, claves y secretos. Seguir la actualización puntual de
> [DESPLEGAR-REGISTRO-MANUAL.md](DESPLEGAR-REGISTRO-MANUAL.md) desde el SHA entregado;
> su publicación en GitHub no acredita que ya esté desplegada.

Entrega autorizada por el propietario para que el agente del servidor publique el
portal en **envirapido.com**, configure **SQL separado y activación manual**, y conserve la
operación del CRM y las aplicaciones instaladas. La publicación en GitHub no
acredita que el servidor ya haya sido actualizado.

## 1. Identificar la versión y el entorno existente

- Repositorio: `https://github.com/2020f/Login-PilotoGot`.
- Rama de entrega: **`codex/confiabilidad-panel-app`**, no `master`.
- Utilizar el **SHA completo entregado por el propietario**. Este documento viaja
  dentro de ese commit. Verificar `git rev-parse HEAD` antes de compilar o publicar.
- Base anterior de esta entrega: `3533116961dea8f3d1e60d9f282a6474a4c471c8`.
- Si el checkout actual del servidor tiene cambios, preservarlo y preparar otro
  checkout limpio en una carpeta de release. No ejecutar reset, clean, rebase ni
  un pull que descarte trabajo o archivos privados.
- Inventariar el gestor real de procesos, puerto interno actual del CRM, proxy o
  túnel, hosts atendidos, conexiones SQL y rutas de claves persistentes. Los puertos
  5000/5031/3000 de las plantillas son ejemplos; no detener otro servicio para usarlos.
- Respaldar base y configuración CRM y conservar el release anterior. No imprimir
  secretos en terminal compartida, recibos, capturas, argumentos o GitHub.

## 2. Qué actualizar y qué preservar

| Componente | Acción |
| --- | --- |
| CRM `Login/Login.csproj` | Publicar el nuevo binario completo, incluida `Envirapido.Portal.Contracts.dll`; añade SuperAdmin y catálogo interno |
| SQL CRM | **Sin migración CRM nueva por este portal**; conservar `DefaultConnection` y todas las tablas actuales |
| `Envirapido.Portal.Api` | Crear un servicio independiente en Production y con SQL/secretos/almacenamiento propios |
| `portal-web` | Compilar React/Next en el sistema destino y publicar el bundle standalone |
| Proxy/túnel | Cambiar sólo las rutas web previstas; preservar API móvil, Identity, controladores y assets CRM |
| Flutter / APK / firma | No recompilar, reinstalar ni cambiar nada por esta entrega |

No ejecutar el seed de Development en producción. No publicar `*.Tests`,
`tools/Envirapido.Portal.Qa`, LocalDB, el lector de correo `/qa/mail`, accesos QA,
archivos temporales ni el proxy responsive. Las pruebas no se ejecutan contra
`DefaultConnection` ni contra ninguna base productiva.

## 3. Crear la base comercial independiente

Crear mediante el administrador SQL una base vacía **`EnvirapidoPortal`**, con un
principal de migración y otro principal de ejecución exclusivos de esa base. El
principal de ejecución necesita operaciones de datos del portal, no permisos
sobre CRM ni capacidad de crear/modificar esquemas. No usar `sa` para el servicio.

Conexión nueva: **`ConnectionStrings__PortalConnection`**, exclusivamente en el
servicio comercial. No reemplazar `ConnectionStrings__DefaultConnection` del CRM.
La API rechaza un nombre de base que no comience con `EnvirapidoPortal` y no
ejecuta migraciones o `EnsureCreated` automáticamente.

Migración: **`20261117144550_PortalInitial`**. Script revisado:
[`docs/portal/sql/PortalInitial.idempotent.sql`](docs/portal/sql/PortalInitial.idempotent.sql).
Es idempotente y contiene guardas para rechazar bases con nombre incorrecto o
tablas propias del CRM. No eliminar esas guardas ni generar otro script sin
revisar cómo conserva la separación.

Ejemplo Linux, con `PORTAL_SQL_SERVER`, `PORTAL_MIGRATION_USER` y
`SQLCMDPASSWORD` inyectados por el almacén privado del operador, no escritos aquí:

```bash
sqlcmd -S "$PORTAL_SQL_SERVER" -U "$PORTAL_MIGRATION_USER" \
  -d EnvirapidoPortal -b -r1 \
  -i docs/portal/sql/PortalInitial.idempotent.sql
```

**`-b` es obligatorio** y se debe detener el despliegue si la salida no es cero.
No pegar el script en un ejecutor que ignore errores entre lotes `GO`. No usar
una conexión CRM para probar que una guarda funciona. Verificar después, desde
esa misma conexión explícita al portal, `DB_NAME()` y el registro de la migración
en `dbo.__EFMigrationsHistory`.

## 4. Configuración privada de los servicios

Plantillas que deben completarse fuera del repositorio:

- [API comercial](docs/portal/portal-api.env.example).
- [Overrides que se añaden al CRM](docs/portal/crm-integration.env.example).
- [Variables React/Next](portal-web/.env.example).

No copiar los marcadores como secretos ni reemplazar todo el archivo de entorno
del CRM por estas plantillas. Son archivos de variables, no scripts para `source`.

Para la API comercial:

- `ASPNETCORE_ENVIRONMENT=Production`; escuchar sólo en loopback/red privada.
- `Portal__AdminKey` y `Portal__CatalogKey`: **dos secretos aleatorios distintos**
  de al menos 32 caracteres. Compartir cada uno sólo con su integración de servidor.
- `Portal__PublicBaseUrl=https://portal.envirapido.com`.
- `Portal__RequireEmailConfirmation=false` y `Portal__Mail__Enabled=false` para
  registrar sin SMTP. SuperAdmin controla aprobación, pago aplicable y vigencia.
- `Portal__CatalogUrl`: URL privada **real** del CRM y ruta
  `/api/internal/portal/catalog`, no asumir que el CRM usa el puerto 5000.
- `Portal__DataProtectionDirectory` y `PortalArtifacts__StoragePath`: directorios
  privados, persistentes y fuera del código/webroot, con permisos para el usuario
  del servicio. No compartir las claves Data Protection del CRM.
- `AllowedHosts` incluye `portal.envirapido.com` y los nombres internos exactos
  necesarios. Respetar la lista de proxies inmediatos exactos, nunca comodines/CIDR.

Para el CRM, añadir `PortalIntegration__Enabled=true`, `AdminBaseUrl` privado,
`AdminKey` idéntico al administrativo y `CatalogReadKey` idéntico al de catálogo.
La propiedad de moneda es **`PortalIntegration__CatalogCurrency=DOP`**.
Añadir loopback a `AllowedHosts` conservando todos los hosts públicos/internos
vigentes. No cambiar JWT, emisor/audiencia, cookies o claves históricas del CRM.

HTTP se admite únicamente en saltos privados loopback. Los clientes de integración
mantienen el esquema público mediante `X-Forwarded-Proto: https` en ese caso; el
receptor sólo confía en su peer exacto. Entre máquinas distintas usar HTTPS real
con certificado validado. No seguir redirecciones, desactivar validación TLS ni
abrir los endpoints internos en Internet para solucionar una configuración errónea.

## 5. Registro manual y correo opcional

La política vigente del propietario permite registrar correo y contraseña sin
validar un enlace. Configurar `Portal__RequireEmailConfirmation=false`; SMTP no es
requisito para abrir registros. Con `Portal__Mail__Enabled=false` no se generan
tokens de confirmación ni mensajes de registro. `EmailConfirmed` sigue false:
el panel muestra correo de contacto y no afirma verificación. La cuenta recién
creada sólo consulta su solicitud; no obtiene tenant, credenciales CRM ni recursos.

SuperAdmin revisa los datos, aprueba la solicitud, confirma pagos externos cuando
corresponda y fija la vigencia independiente del portal. Sin SMTP, la recuperación
automática no se ofrece: se muestran los contactos de administración.

El resto de esta sección sólo aplica si se habilita correo para recuperación
automática o si se elige en otra instalación el modo opcional
`Portal__RequireEmailConfirmation=true`.

Configurar **`Portal__Mail__*`** en el servicio nuevo, no sustituir el correo del CRM:
Enabled, Host, Port, UseSsl, UserName, Password, FromAddress y FromName. Utilizar
credenciales privadas y un remitente autorizado por el proveedor. La dirección
de contacto visible de Fernelis no es una credencial SMTP ni demuestra autorización
para enviar desde un dominio.

El transporte implementado usa **STARTTLS**, normalmente puerto 587, con
`UseSsl=true`. No funciona con TLS implícito de puerto 465. Comprobar salida de
red, autenticación, remitente y configuración DNS que requiera el proveedor antes
de ofrecer recuperación o confirmación automática. [Referencia oficial de SmtpClient](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.enablessl?view=net-10.0).

La entrega usa un outbox cifrado con reintentos; una respuesta de registro exitosa
no demuestra que el correo llegó. Verificar recepción real, confirmación de enlace
de un uso y recuperación de acceso. Si faltan credenciales válidas, informar el
pendiente de ese servicio opcional. Si se eligió exigir confirmación, no afirmar
que ese modo está listo sin validarlo. Nunca usar el lector de correo QA en producción.

## 6. Compilar desde el SHA correcto

Requisitos: SDK .NET 10 compatible y runtime **10.0.15** indicado por los proyectos;
Node compatible con `portal-web/package.json` (QA se hizo con Node 26.12.1).
No copiar `node_modules` o `.next` generados en Windows al servidor Linux.

Desde el checkout limpio, con `RELEASE_DIR` resuelto y elegido por el operador:

```bash
dotnet restore Login.sln
dotnet publish Login/Login.csproj -c Release --no-restore -o "$RELEASE_DIR/crm"
dotnet publish Envirapido.Portal.Api/Envirapido.Portal.Api.csproj \
  -c Release --no-restore -o "$RELEASE_DIR/portal-api"
```

En `portal-web`, fijar estas variables **al compilar**:

```text
NEXT_PUBLIC_SITE_ORIGIN=https://envirapido.com
NEXT_PUBLIC_PORTAL_ORIGIN=https://portal.envirapido.com
PORTAL_API_ORIGIN=http://127.0.0.1:5031
```

Usar el puerto privado real elegido para la API en lugar de 5031 si cambia.
`PORTAL_API_ORIGIN` también se fija en el entorno del servicio Node; Next utiliza
ese origen al consultar el catálogo y al generar rewrites durante el build.
Ninguna clave interna ni conexión SQL debe estar en `NEXT_PUBLIC_*`.

```bash
npm ci --no-audit --no-fund
npm run typecheck
npm test
npm run build
```

El comando build ya incorpora `public` y `.next/static` a `.next/standalone`.
Publicar **todo** ese bundle como directorio `web` del release. Arrancar con
`node server.js` desde el bundle, `NODE_ENV=production`, `HOSTNAME=127.0.0.1`
y `PORT` privado. Desde el checkout completo también existe `npm start`.
No usar `next start` para este output standalone.

Crear dos servicios nuevos (API comercial y Node), con reinicio controlado y
usuario sin privilegios, usando el gestor ya disponible en el servidor. Reutilizar
el servicio CRM existente actualizando su release y conservando entorno/volúmenes.
Instalar Java, apksigner y AAPT2 oficiales para Linux y configurar sus rutas
absolutas si se habilitará la carga de APK. No subir un keystore al servidor web.

## 7. Orden de arranque y rutas públicas

1. Preparar copia de seguridad y release limpio; crear/migrar sólo el SQL comercial.
2. Preparar entorno privado, política manual y directorios; publicar CRM actualizado con su
   mismo SQL, Identity y JWT. Añadir la configuración de integración.
3. Arrancar API comercial y verificar salud interna, catálogo privado y sección
   SuperAdmin sin cambiar todavía la raíz pública. Arrancar Next y comprobar HTML.
4. Configurar DNS/TLS de **`portal.envirapido.com`** sobre el ingreso real existente.
   La configuración anterior del dominio raíz no crea este subdominio por sí sola.
5. Adaptar el proxy/túnel conforme a la tabla y al
   [ejemplo de rutas](docs/portal/routing.nginx.example.conf), comprobar sintaxis y
   hacer recarga controlada, conservando el archivo anterior para rollback.

| Host/ruta | Servicio |
| --- | --- |
| `envirapido.com/` y páginas comerciales | Next |
| `envirapido.com/api/*` | CRM existente, sin redirección a otro host |
| `envirapido.com/Identity/*` | Identity CRM existente |
| `/Operaciones`, `/SuperAdmin`, `/Supervisor`, `/Suscripcion`, `/Tienda`, `/Piloto`, `/Qr`, `/Bloqueo`, `/Gestor`, `/Home`, `/Envirapido` y sus subrutas | CRM |
| `/css`, `/js`, `/lib` y assets operativos | CRM |
| `portal.envirapido.com/portal-api/v1/*` | API comercial, antes del fallback Next |
| Resto de páginas de `portal.envirapido.com` | Next |
| `/portal-api/internal/*` y `/api/internal/portal/*` | Rechazados en el ingreso público; sólo comunicación privada |
| `pilotogoapp.zapflow77.dpdns.org` | Mantener CRM/API y HTTPS existentes |

No permitir que un fallback React capture una API desconocida y devuelva HTML200.
No almacenar descargas privadas ni respuestas autenticadas en caché/CDN. Preservar
los POST, cuerpos multipart, Range/HEAD, cookies Secure/HttpOnly y consulta en las
redirecciones HTTP→HTTPS. Ajustar límites de carga del ingreso al APK permitido
por la aplicación; no desactivar autenticación ni CSRF para conseguir una carga.

## 8. Criterios de aceptación antes de anunciar producción

- HTTPS correcto en ambos dominios operativos y el subdominio nuevo. Raíz comercial
  HTML200; login CRM200; API móvil anónima401 JSON. Usar
  `tools/Verify-PublicHttps.ps1 -RootMode Marketing` para el dominio raíz nuevo,
  y el modo `CrmRedirect` para el dominio anterior.
- SuperAdmin del CRM conserva empresas, planes, facturación y permisos. En
  **Portal comercial → Catálogo web**, publicar sólo los planes comerciales
  deseados; un plan QA no se publica automáticamente por estar activo.
- Alta ficticia autorizada sin SMTP; solicitud pendiente visible en SuperAdmin,
  sin tenant/usuario CRM automático y sin recursos desbloqueados. Consultar
  `/portal-api/v1/auth/options` en `portal.envirapido.com`: ambos flags false
  cuando confirmación y correo automático están deshabilitados.
- Reportar pago no equivale a confirmarlo. Aprobar, confirmar un pago externo real
  cuando corresponda y fijar fechas independientes. En QA usar sólo pagos ficticios.
- Acceso activo habilita descarga y botón de login CRM, con credenciales operativas
  entregadas manualmente por el propietario. Vencimiento/suspensión/anulación del
  pago bloquean recursos comerciales sin cambiar pedidos ni cobertura CRM.
- APK: paquete `com.pilotgo.app`, firma definitiva, SHA esperado, cuarentena,
  validación y publicación manual. Piso inclusivo9; después códigos crecientes.
  Comprobar descarga autorizada y denegación anónima/expirada, también en Range.
  Conservar el enlace GitHub del APK ya distribuido; no puede volverse privado.
- Con cuentas ficticias autorizadas, comprobar recepción manual y automática de
  varias órdenes, recogida B, entrega C y rutas. Verificar aislamiento entre dos
  empresas y que el conjunto de datos reales anteriores permanezca igual.
- No mover pedidos reales ni modificar usuarios, pagos o modos de asignación reales
  para simular QA. No crear fixtures productivos sin el alcance autorizado.

## 9. Rollback y recibo de entrega

Ante una regresión, restaurar las rutas anteriores del proxy y, si es necesario,
el release CRM anterior, manteniendo su configuración/base/claves. Deshabilitar
`PortalIntegration` y parar sólo los servicios nuevos. **Conservar** base comercial,
outbox, claves y APK privados para retomar o investigar; no ejecutar Down ni borrar
datos como parte del rollback. No desinstalar apps ni restablecer contraseñas reales.

Al terminar, comunicar: SHA completo desplegado y árbol limpio; versiones/runtime;
servicios y hosts atendidos; base comercial y migración aplicada; política de registro,
HTTPS, cuentas, pagos, APK, multitenant y pedidos; rollback disponible y cualquier
pendiente. No incluir secretos, tokens, conexiones completas ni datos personales.
Las pruebas locales de [QA-20261117](docs/portal/QA-20261117.md) son evidencia
previa; no sustituir con ellas la verificación del servidor.
El resultado nuevo del modo sin SMTP está en
[REGISTRO-MANUAL-20261117.md](docs/portal/REGISTRO-MANUAL-20261117.md).
