# Portal comercial de Envirapido

Implementación local y publicación GitHub autorizadas el 17/11/2026. El agente del
servidor recibe las instrucciones de entrega en [DESPLEGAR-PORTAL.md](../../DESPLEGAR-PORTAL.md).
El push no acredita despliegue ni autoriza reemplazar datos o claves operativas.
El resultado de QA y sus límites se documentan en `QA-20261117.md`.

## Servicios y separación

| Servicio | Función | Datos y autenticación |
| --- | --- | --- |
| CRM existente (.NET 10) | Pedidos, rutas, comercios, pilotos y SuperAdmin | Conserva su conexión SQL, Identity, JWT, cookies y claves actuales |
| `Envirapido.Portal.Api` (.NET 10) | Compradores, solicitudes, pagos externos del portal, vigencias y APK privados | Base **EnvirapidoPortal**, usuario SQL y claves Data Protection propios; cookie host-only propia |
| `portal-web` (React / Next.js) | Sitio comercial y área del comprador | Consume únicamente `/portal-api/v1`; no guarda contraseñas ni tokens en localStorage |

No hay FK, identificador de empresa CRM ni usuario Identity CRM en la base comercial.
Una aprobación no crea un tenant ni entrega credenciales operativas. El propietario
crea y comunica esos accesos por su procedimiento habitual. El botón habilitado apunta
al login CRM existente, sin SSO. Las fechas del portal no cambian la cobertura CRM.

El servidor CRM publica una proyección de planes **activos**, protegida por una clave
de catálogo distinta de la administrativa. El portal decide cuáles publicar y obtiene
precio/límites del servidor al registrar cada solicitud. Cero en un límite significa
sin límite. Un cambio de precio obliga a aceptar de nuevo las condiciones; no
reescribe solicitudes anteriores. Catálogo no disponible: no se acepta una compra
con precios inventados o vencidos.

## Preparación privada

1. Respaldar configuración y base CRM actuales. No sustituir sus secretos, claves ni
   conexión. Crear una base vacía `EnvirapidoPortal` y un principal SQL exclusivo,
   sin permisos sobre CRM. Crear un principal de migración separado; el servicio
   ordinario no necesita permisos para crear bases o modificar esquemas.
2. Generar **dos secretos aleatorios diferentes**, de al menos 32 caracteres:
   integración administrativa y lectura de catálogo. Guardarlos fuera de Git y del
   frontend. Nunca usar `NEXT_PUBLIC_*` para secretos.
3. Aplicar la migración **sólo a la base nueva** usando la conexión privada de portal.
   Revisar el script de `sql/` antes de ejecutarlo. La API no ejecuta migraciones,
   EnsureCreated, seeds ni altas de usuarios al arrancar. No ejecutar `database update`
   contra DefaultConnection ni con la identidad SQL del CRM.
4. Crear directorios privados persistentes para Data Protection y APK, fuera del
   webroot y del repositorio. Permisos sólo para el usuario del servicio. Respaldar
   base, claves y objetos APK coherentemente. Mantener las claves entre reinicios:
   protegen también la cola de correo, no sólo las sesiones.
5. Configurar SMTP real con TLS y remitente autorizado. La confirmación/recuperación
   del portal es independiente del correo CRM. Si SMTP no está habilitado en producción,
   el alta y la recuperación fallan de forma segura; no se crean accesos sin validar.
6. Instalar Java, `apksigner.jar` y `aapt2` oficiales con rutas absolutas. La API ejecuta
   únicamente estos validadores mediante argumentos, nunca el APK ni un shell. El
   servicio necesita espacio para el archivo en cuarentena y para su inspección.

Ver `portal-api.env.example` y `crm-integration.env.example`. Son plantillas de
variables, **no scripts ejecutables**. Reemplazar marcadores en el almacén privado;
no reemplazar archivos completos de configuración de servicios existentes.

## Compilación y procesos

- Backend: SDK .NET 10 compatible con el RuntimeFrameworkVersion del proyecto.
  `dotnet publish Envirapido.Portal.Api/Envirapido.Portal.Api.csproj -c Release`.
- CRM: publicar el proyecto existente con la nueva referencia a Contracts, conservando
  el mismo backend y base para ambos dominios operativos.
- Web: Node compatible con `portal-web/package.json`; ejecutar `npm ci`, pruebas,
  `npm run build`. Las variables `NEXT_PUBLIC_SITE_ORIGIN=https://envirapido.com` y
  `NEXT_PUBLIC_PORTAL_ORIGIN=https://portal.envirapido.com` se fijan **al compilar**.
  `PORTAL_API_ORIGIN=http://127.0.0.1:5031` es sólo del servidor Next y debe tener
  el mismo valor al compilar y al ejecutar, porque los rewrites se fijan en el build.
- Next usa `output: standalone`. Para ese modo copiar también `public/` y
  `.next/static/` a los directorios correspondientes del bundle. Ejecutar el
  `server.js` generado detrás del proxy con HOSTNAME loopback y puerto privado.
- La API comercial sólo debe escuchar en loopback o red privada. Configurar
  `ASPNETCORE_ENVIRONMENT=Production`; no reutilizar Testing/Development en producción.

## Enrutamiento HTTPS sin romper la app

El proxy/túnel existente conserva DNS/TLS, renovación y reglas de confianza. No
reemplazar su configuración por un ejemplo completo. `routing.nginx.example.conf`
documenta las reglas que deben adaptarse a su topología real:

- `envirapido.com/`: Next, página comercial.
- `portal.envirapido.com`: Next y `/portal-api/v1/*` hacia la API comercial. Cookies
  `__Host-` host-only, Secure, HttpOnly; sin Domain compartido con el CRM.
- Apex: `/api/*`, `/Identity/*` y controladores operativos/administrativos existentes
  siguen en el **CRM original**. Nunca redirigir POST API al portal ni devolver HTML
  de Next para una ruta API inexistente. Mantener `/css`, `/js`, `/lib` y assets CRM.
- `pilotogoapp.zapflow77.dpdns.org`: continuar atendiendo el mismo CRM/API, con sus
  reglas y HTTPS actuales. No introducir un segundo CRM ni duplicar su base.
- `/portal-api/internal/*` y `/api/internal/portal/*` no se publican en Internet:
  las conexiones servidor a servidor usan direcciones privadas y claves diferentes.
- El proxy debe **sobrescribir** Host/X-Forwarded-Proto, aceptar sólo hosts configurados
  y confiar únicamente en IPs exactas de los saltos inmediatos conocidos. No confiar
  en una cabecera enviada libremente por un visitante.
- Las llamadas HTTP privadas en loopback añaden `X-Forwarded-Proto: https` para
  conservar el esquema público sin entrar en un bucle de redirección. Ambos
  servidores deben confiar sólo en ese salto loopback exacto y el CRM debe incluir
  `127.0.0.1` en AllowedHosts, conservando sus nombres públicos actuales. No significa
  que el salto interno esté cifrado. Si los servicios están en equipos diferentes,
  usar HTTPS real validado; el cliente no admite un origen HTTP remoto.
- Sin caché CDN/proxy para auth, cuenta, API comercial o descargas. Sin caché pública
  para páginas CRM autenticadas. No desactivar Secure ni CSRF por problemas de proxy.

No ampliar CORS para la app nativa. El APK mantiene `https://envirapido.com`, paquete,
firma y contratos; este cambio no requiere reinstalarlo ni borrar datos de los pilotos.

Después de desplegar, `tools/Verify-PublicHttps.ps1 -RootMode Marketing` exige
HTML 200 en la raíz nueva y conserva las verificaciones de HTTPS/cookies/API.
Para el dominio anterior usar el modo predeterminado `CrmRedirect`.

## Puesta en servicio comercial

1. Verificar acceso SuperAdmin, abrir **Portal comercial → Catálogo web** y publicar
   sólo los planes comerciales deseados. Los planes QA activos no se publican solos.
2. Registrar un comprador ficticio por el sitio, confirmar correo por SMTP real y
   comprobar que esté pendiente y sin descarga. Revisar datos desde SuperAdmin.
3. El reporte del comprador nunca confirma dinero. Registrar únicamente pagos externos
   comprobados; no existe pasarela ni transferencia bancaria automática. Un pago
   anulado debe cerrar el permiso si deja importe pendiente.
4. Aprobar solicitud y fijar sus fechas RD inclusivas. Probar futuro, vencimiento,
   suspensión, revocación y separación entre cuenta web y acceso operativo CRM.
5. Subir un APK firmado con el certificado definitivo y versionCode igual o mayor
   al piso configurado. La primera publicación puede usar el APK 9 actual; las
   siguientes deben superar todas las versiones publicadas. Revisar firma, SHA,
   paquete, versión, URL y QA. Publicarlo
   expresamente; no basta con subir o validar. No subir keystore ni contraseña de firma.
6. Verificar GET, HEAD y Range del APK con comprador autorizado y denegación para
   anónimo, otra sesión suspendida y vigencia vencida. Conservar el enlace GitHub
   histórico ya distribuido: su carácter público no puede protegerse retroactivamente.
7. Repetir pedidos manuales/automáticos y rutas con cuentas ficticias autorizadas,
   multitenant, sesiones web/móvil y HTTPS antes de anunciar producción.

Rollback de enrutamiento: restaurar el proxy anterior y deshabilitar `PortalIntegration`
sin revertir ni tocar la base operativa. Conservar base/archivos comerciales para
investigar o retomar; no eliminarlos como parte del rollback.
