# Portal comercial de Envirapido

Frontend React con Next.js App Router y TypeScript, independiente de la app Flutter y del CRM. Sirve portada, funciones, planes publicados, contacto, registro y área privada comercial. El catálogo, las solicitudes, los reportes de pagos, vigencias y versiones APK provienen del servicio real; no existen datos comerciales de respaldo inventados.

## Ejecutar localmente

Requiere Node.js 22.18 o posterior. Se validó con Node 26.12.1 y npm 11.20.0. Desde esta carpeta en PowerShell:

```powershell
$env:npm_config_cache = 'D:\Envirapido-Portal-QA-20261117\npm-cache'
$env:NEXT_TELEMETRY_DISABLED = '1'
npm.cmd ci --no-audit --no-fund
$env:PORTAL_API_ORIGIN = 'http://127.0.0.1:5031'
$env:NEXT_PUBLIC_SITE_ORIGIN = 'http://127.0.0.1:3000'
$env:NEXT_PUBLIC_PORTAL_ORIGIN = 'http://127.0.0.1:3000'
npm.cmd run dev
```

El backend independiente del portal debe estar disponible en 5031 y configurado con su SQL Server, cookies locales de desarrollo y correo de pruebas. Orígenes públicos vacíos también permiten navegación relativa durante QA. Las variables `NEXT_PUBLIC_*` se fijan al compilar; no admiten secretos.

```powershell
npm.cmd run typecheck
npm.cmd test
npm.cmd run build
npm.cmd start
```

## Rutas y despliegue

En producción, `NEXT_PUBLIC_SITE_ORIGIN=https://envirapido.com` y `NEXT_PUBLIC_PORTAL_ORIGIN=https://portal.envirapido.com`. `PORTAL_API_ORIGIN` es el origen interno del nuevo servicio comercial, nunca una ruta con usuario/contraseña. Debe ser el mismo al compilar y ejecutar: Next fija los rewrites durante el build. El catálogo SSR identifica únicamente su salto HTTP de loopback como proxy privado, omite credenciales del navegador y rechaza HTTP remoto. Next solo reescribe `/portal-api/v1/*`; no intercepta `/api/*`, `/Identity/*`, SignalR ni rutas actuales de descarga. El proxy de infraestructura debe dirigir la portada y sus rutas públicas a Next, conservar las rutas operativas actuales hacia el CRM y servir la cuenta comercial en el subdominio privado. Este repositorio no cambia DNS ni publica nada automáticamente.

La salida es `standalone`. `npm.cmd run build` copia automáticamente `public` y `.next/static` dentro de `.next/standalone`, sin mover ni borrar originales. `npm.cmd start` ejecuta el `server.js` generado mediante un wrapper con `HOSTNAME=127.0.0.1` y `PORT=3000` como valores predeterminados; se pueden configurar explícitamente para el proxy del despliegue. No utiliza `next start`, incompatible con esta modalidad. Para distribuir únicamente el bundle preparado, copia `.next/standalone` completo y ejecuta `node server.js` allí con `HOSTNAME` y `PORT` definidos. El backend debe configurar su propio origen público para enlaces de confirmación y recuperación. El navegador solicita el API mediante rutas relativas; no se habilita CORS contra el CRM ni se envían cookies operativas manualmente.

## Seguridad y estados

- Cookie del portal emitida por el servicio propio, sin reutilizar Identity del CRM. Contraseñas, tokens y payloads no se escriben en localStorage, sessionStorage ni cookies desde JavaScript.
- Cada POST/PUT obtiene antiforgery y envía `X-CSRF-TOKEN`; se renueva después de autenticarse. `credentials:include`, `cache:no-store` y rechazo de redirecciones inesperadas.
- Registro, solicitud y reporte conservan UUID y payload en memoria al perder una respuesta. Reintentar usa ese mismo intento. Cerrar o recargar la pestaña pierde esa memoria: consultar primero la cuenta/historial antes de repetir; no se persisten contraseñas para restaurar un registro.
- Un reporte es información pendiente de revisión manual. No confirma pagos, no activa vigencias ni ejecuta transferencias. El acceso operativo no se modifica desde el cliente.
- Fechas de vigencia en República Dominicana, mostrando correctamente el último día a partir del extremo UTC exclusivo del backend.
- CRM únicamente con grant `ACTIVE` y URL exacta `https://envirapido.com/Identity/Account/Login`. APK únicamente con versión publicada y acceso activo; enlace relativo al ID autorizado, validado otra vez por el servidor al descargar. No se usa el APK público anterior como sustituto de una descarga privada.
- Las páginas de cuenta/acceso llevan noindex y no-store; los errores JSON se muestran sin interpretar HTML. El backend sigue siendo la autoridad para permisos, expiración y descargas.

## Marca y dependencias

Los cuatro originales aprobados en `public/branding` y `public/illustrations` se copiaron sin modificación. `branding-manifest.json` conserva SHA-256 para verificar identidad. Se usan SVG de interfaz propios y tipografías del sistema; no se descargan fuentes o imágenes remotas.

Versiones exactas fijadas en lockfile, consultadas en el registro oficial: [Next 16.4.6](https://registry.npmjs.org/next/16.4.6), [React 19.3.1](https://registry.npmjs.org/react/19.3.1), [TypeScript 7.0.2](https://registry.npmjs.org/typescript/7.0.2). Compatibilidad según [documentación oficial de TypeScript en Next](https://nextjs.org/docs/app/api-reference/config/typescript).

Las pruebas Node ejercitan transporte antiforgery, fronteras de autenticación, timeout, idempotencia tras respuesta perdida, rechazo de rutas externas, enlaces de acceso cerrados por defecto y fechas. No sustituyen pruebas SQL, SMTP, navegación integrada, DNS/TLS ni una descarga APK real; esas validaciones se registran por separado.
