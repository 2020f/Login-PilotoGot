# Portal comercial: entorno QA integrado local

Este ejecutable levanta **CRM y Portal reales** mediante sus respectivas `WebApplicationFactory`, con SQL Server LocalDB desechable y dos puentes HTTP que escuchan únicamente `127.0.0.1`. No despliega ni modifica producción. La web React/Next se ejecuta aparte y utiliza el Portal por su rewrite habitual.

## Condiciones y límites

- Crea únicamente `PilotGoTest_<GUID>` y `EnvirapidoPortalTest_<GUID>` nuevos, desde `PilotGoFactory` y `PortalFactory`. No acepta una conexión, una base existente ni `DefaultConnection`.
- Cada proceso contiene sus propias identidades, sesiones y almacenamiento privado. Las factories eliminan **sus** bases y archivos al cerrar normalmente con Ctrl+C.
- El catálogo del Portal proviene por HTTP real del endpoint CRM `/api/internal/portal/catalog`. Se retira `MutableCatalog` de la factory y se publican por la API administrativa dos planes ficticios que existen en el CRM QA: RD$1,500 y gratuito.
- El CRM utiliza `PortalAdminClient` por HTTP real al Portal para SuperAdmin. Las claves internas son ficticias de Testing; no se usan secretos de producción.
- El correo se conserva cifrado en el outbox de la BD Portal. El worker se retira únicamente de este harness y un transporte de seguridad rechaza cualquier envío. No se usa SMTP real.
- Las cookies se separan por servicio/puerto. Solo este adaptador local permite cookies HTTP loopback. Las opciones del producto y sus cookies Secure permanecen intactas.
- Los cuerpos comunes se limitan a 1 MiB. Solo las dos rutas de carga APK permiten hasta 151 MiB, con una carga simultánea por puente y archivo temporal privado con eliminación al cerrar. Las descargas se transmiten por stream; no se carga el APK en RAM.
- El contrato mantiene su `crmUrl` real `https://envirapido.com/Identity/Account/Login`. **No seguir ese enlace durante QA**; utilizar el CRM local del recibo. No se reescriben respuestas del producto.
- No se inicia un emulador ni se instala/publica un APK automáticamente. Esta herramienta valida la integración web; no demuestra disponibilidad pública o despliegue.

## Iniciar

Desde la raíz del repositorio, preparar el runtime privado habitual de QA:

```powershell
. .\tools\Envirapido.Qa\Use-Net10Qa.ps1
& $env:DOTNET_HOST_PATH run --project .\tools\Envirapido.Portal.Qa -- --crm-port 5098 --portal-port 5031 --web-origin http://127.0.0.1:3000
```

En otra consola, iniciar Next con su configuración API local (el script exacto está en `portal-web/package.json`):

```powershell
$env:PORTAL_API_ORIGIN = 'http://127.0.0.1:5031'
Set-Location .\portal-web
npm run dev -- --hostname 127.0.0.1 --port 3000
```

Utilizar siempre el mismo nombre de host al navegar; por defecto `127.0.0.1`, no mezclarlo con `localhost`, porque las cookies son host-only.

El ejecutable imprime el recibo y lo escribe por defecto en `artifacts/portal-implementation-20261117/qa-local-receipt.json`. Contiene URLs, nombres de bases QA, cuentas ficticias y las condiciones de los planes. No contiene claves privadas ni datos de producción.

| Uso | Dirección predeterminada |
| --- | --- |
| Web React | `http://127.0.0.1:3000` |
| API Portal | `http://127.0.0.1:5031/portal-api/v1/catalog` |
| SuperAdmin ficticio, acceso directo QA | `http://127.0.0.1:5098/qa/login/qa-admin` |
| Portal comercial en SuperAdmin | `http://127.0.0.1:5098/SuperAdmin/Portal` |
| Gestor de empresa A | `http://127.0.0.1:5098/qa/login/manager-a` |
| Comercio A | `http://127.0.0.1:5098/qa/login/shop-a` |
| Piloto A | `http://127.0.0.1:5098/qa/login/pilot-a` |
| Información de servicios | `http://127.0.0.1:5098/qa/environment` y `http://127.0.0.1:5031/qa/environment` |

Usuario SuperAdmin: `qa-admin@example.test`; contraseña: `Test-only-Password42!`. Todas estas credenciales son ficticias y solo existen en la BD recién creada.

El Portal **no tiene un comprador precreado**. Registrar por la web, por ejemplo, `portal-qa@example.test` con `Portal-Test-Only-Password42!`; así se comprueba el formulario y la creación real de una solicitud. Consultar después:

```text
http://127.0.0.1:5031/qa/mail?email=portal-qa%40example.test
```

La respuesta JSON contiene enlaces de confirmación/restablecimiento correspondientes únicamente a esa cuenta ficticia de la BD QA actual. La utilidad solo admite correos `@example.test`; no envía mensajes. Abrir el enlace `link` más reciente para el asunto deseado. No guardar tokens de confirmación en evidencia pública.

## Validar un APK real de forma optativa

### Vista adaptable de sólo lectura

Si la capacidad de viewport del navegador no cambia el ancho observado, ejecutar
`node tools/Envirapido.Portal.Qa/responsive-preview.mjs` desde la raíz, con Next
disponible en 3000. Abrir `http://127.0.0.1:5089/__qa_responsive` o añadir
`?page=/acceso` o `?page=/cuenta/descargas`. El iframe mide realmente 390 px.
Sólo admite GET, rutas enumeradas y Host/IP de loopback. Omite las cabeceras que
impiden enmarcar únicamente en este fixture QA; Next original conserva DENY y
no debe publicarse este proxy. No sirve para probar envío de formularios ni headers
de producción. Ctrl+C cierra únicamente este servidor Node.

### Herramientas Android

Para habilitar las herramientas oficiales en el servicio QA, añadir:

```powershell
& $env:DOTNET_HOST_PATH run --project .\tools\Envirapido.Portal.Qa -- --android-tools 'C:\Users\Fernelis\AppData\Local\Android\Sdk\build-tools\36.0.0' --java 'C:\Program Files\Android\Android Studio\jbr\bin\java.exe'
```

El harness configura el mínimo QA en 8 para comprobar el APK 9 existente sin cambiar los controles de producción. La carga, validación, aprobación QA y publicación privada siguen siendo acciones explícitas de SuperAdmin. El APK nunca se ejecuta desde el servidor y el enlace histórico de GitHub conserva su carácter público.

## Argumentos

`--crm-port`, `--portal-port`, `--web-origin` (solo HTTP loopback), `--sql-instance` (nombre LocalDB), `--receipt` (archivo local), `--android-tools` y `--java` (ambas rutas absolutas, opcionales). `--verify-seed` prepara y comprueba las dos APIs y el catálogo, escribe el recibo y cierra eliminando las bases QA; sus URLs dejan de estar disponibles al terminar.

Si el proceso termina forzosamente, puede no ejecutar la limpieza de sus bases GUID. No se adjuntan ni borran bases ajenas automáticamente.
