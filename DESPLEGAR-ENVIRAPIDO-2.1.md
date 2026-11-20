# Desplegar Envirapido 2.1 — entrega del 18/11/2026

> Para la actualización de retiros desde incidencias del 20/11/2026, seguir
> [DESPLEGAR-ENVIRAPIDO-2.1.1.md](DESPLEGAR-ENVIRAPIDO-2.1.1.md).
> Este documento conserva la migración previa y el recibo histórico del APK10.

Esta es la guía vigente para este lote. Reemplaza las indicaciones históricas de
«sin migración» o «sólo portada» para esta entrega. El propietario autorizó subir
las fuentes; otro agente realiza el despliegue. GitHub no acredita producción.

## Fuentes y alcance

- Backend: repositorio `2020f/Login-PilotoGot`, rama
  `codex/confiabilidad-panel-app`. Usar el SHA completo que acompaña esta entrega,
  verificando `git rev-parse HEAD` en un checkout limpio y separado.
- App: repositorio `2020f/pilotgo-app`, commit
  **`8c91f42dbe97d4fd9ec74514cb71595ec9f9280e`**.
- Cambios: reasignación por parada antes/después de recoger, retiros autorizados,
  devolución o traspaso con recepción física, ajustes por trabajo realizado,
  rediseño móvil conservando el panel deslizante, icono de caja, login y logos CRM.
- API y fuentes React del portal no cambian respecto a la base backend
  `0f7cd32c4d27617b697217c80916188857a19247`. La base comercial permanece separada.

No ejecutar un reset, clean ni un pull que descarte trabajo local del servidor.
Preparar una carpeta de release del SHA exacto y conservar el release anterior.
No incluir archivos privados de configuración en Git.

## 1. Respaldo y migración exclusiva del CRM

1. Identificar la base real de `DefaultConnection` del CRM sin exponer credenciales.
   Verificar `SELECT DB_NAME()` usando esa conexión. **Nunca usar PortalConnection
   ni la base EnvirapidoPortal para este script.**
2. Respaldar esa base y comprobar el procedimiento de restauración. Conservar las
   configuraciones, claves Identity/JWT/Data Protection, archivos privados y release
   anterior. No cambiar conexiones, secretos ni identidades de las empresas.
3. Confirmar en `__EFMigrationsHistory` la migración previa
   `20261112201846_EnvirapidoSaasBilling`. Si falta, detenerse y revisar el esquema
   real; no aplicar este delta sobre una base sin su historial previo.
4. Detener temporalmente las escrituras del CRM, incluyendo API móvil y procesos
   de asignación, durante la ventana de migración. No operar con binarios viejos
   escribiendo mientras cambia el esquema.
5. Verificar SHA256 y aplicar el script revisado
   [`docs/envirapido/20261117-reasignacion-paradas.sql`](docs/envirapido/20261117-reasignacion-paradas.sql)
   sólo al CRM:

   ```text
   1ba71fc8efa15ab8755b2ce16bdfe6bb5f7f5a27cdcd8bb13bdc203f388b3852
   ```

   El script usa transacción e historial idempotente para
   `20261117230916_EnvirapidoReasignacionPorParadas`. Ejecutar con la herramienta
   SQL del servidor y parada ante errores; en sqlcmd usar `-b -r1`. Comprobar la
   base explícitamente antes de ejecutar: el script no valida `DB_NAME()`.
   Su atributo Git específico conserva los bytes CRLF revisados; no reformatearlo
   ni normalizar sus finales de línea antes de verificar el SHA256.
   Usar un principal de migración con DDL limitado a esa base CRM; conservar el
   usuario ordinario del servicio y la cuenta SQL independiente del portal.

6. Si se detectan visitas sin membresía de ruta, el script aborta con `THROW`.
   Investigar los datos sin borrar visitas, pedidos ni rutas para eludir la guarda.
   Validar el nuevo historial y las relaciones por empresa al terminar.

La migración conserva órdenes anteriores con `GestionPorParadas=false`; no
convierte automáticamente pedidos existentes ni regenera A/B/C. Corrige la
relación de visitas usando empresa, ruta y orden. No ejecutar seed, EnsureCreated,
recreación de base ni conversiones masivas. **No hay migración nueva del portal.**

## 2. Publicar backend y recursos

- Publicar `Login/Login.csproj` completo, con sus dependencias, desde el SHA exacto.
  El proyecto mantiene `net10.0` y runtime/paquetes `10.0.15`; preparar el runtime
  compatible según el modo de publicación existente. No desplegar sólo DLL sueltas.
- Conservar configuración y almacenamiento persistente del servidor. No copiar
  `appsettings` de QA, bases locales ni rutas Windows de este repositorio.
- Publicar `wwwroot/branding`, CSS y JS junto al backend. En `envirapido.com`,
  **`/branding/*` debe ir al CRM**, igual que `/css`, `/js`, `/lib` e `/images`.
  Ver [ejemplo de enrutamiento](docs/portal/routing.nginx.example.conf), adaptando
  únicamente a la topología real. No reemplazar a ciegas TLS ni el proxy completo.
- Conservar `/api/*`, `/Identity/*`, CRM y SuperAdmin en el backend, y la portada
  React en `/`. En `portal.envirapido.com`, mantener los assets del frontend allí.
  Ningún fallback React debe capturar la API operativa.
- Mantener HTTP→HTTPS en ambos dominios conocidos, cookies Secure/HttpOnly,
  antiforgery y proxies confiables exactos. No confiar en cabeceras reenviadas por
  cualquier origen. Conservar compatibilidad HTTPS del dominio anterior
  `pilotogoapp.zapflow77.dpdns.org` y la API `https://envirapido.com`.
- Preservar el portal ya desplegado y sus secretos independientes:
  `Portal__RequireEmailConfirmation=false`, `Portal__Mail__Enabled=false` para
  registro manual sin SMTP. La aprobación/vigencia del portal no activa un tenant
  CRM ni crea sus credenciales. No unir las bases ni compartir cookies/identidades.
- Los APK privados siguen fuera de `wwwroot` y fuera de cualquier alias público.
  Conservar almacenamiento, herramientas de inspección y certificados permitidos.

## 3. Comprobaciones antes de habilitar operación nueva

Tras iniciar el backend y antes de distribuir la app, con un piloto autorizado:

```http
GET /api/piloto/capacidades
```

```json
{"versionContrato":4,"multiparada":true,"cierreSoloC":true,"confirmacionParadaIdempotente":true,"gestionPorParadas":true,"recogidaPorLoteIdempotente":true,"revisionAsignacion":true}
```

La app nueva usa `X-Contrato-Ordenes: 4`. La recogida gestionada usa
`POST /api/piloto/ordenes/{ordenId}/recoger-paradas`; las entregas mantienen el
endpoint de confirmación por parada y validan `RevisionAsignacion`.

Los pedidos legacy conservan el flujo anterior, B para recogida y sólo C para
cerrar cada entrega. Actualizar los pilotos involucrados antes de repartir o
traspasar trabajo gestionado. No asignar esa operación nueva a un cliente antiguo.

Validar con cuentas y órdenes ficticias autorizadas de una empresa de pruebas:

- Login, persistencia de sesión y separación entre empresas; acceso ajeno denegado.
- Varias órdenes manuales y automáticas con otra activa, sin cerrar sesión; ruta,
  estados y desglose recogiendo/en ruta sincronizados.
- Recogida B y cierre C por parada, idempotencia y permanencia de los códigos.
- Reparto antes de recoger, retiro autorizado, devolución a destino elegido por
  gestor y traspaso directo. Asignación y custodia son distintas; el receptor
  confirma físicamente, el gestor supervisa. Entregadas no vuelven al flujo.
- Cola de reasignaciones, cancelación autorizada e historial. Revisiones antiguas
  del piloto retirado no permiten recoger ni entregar una parada reasignada.
- Ajuste manual de ganancias; el efectivo cobrado sigue a cargo de quien lo cobró
  hasta recepción en la empresa. No registrar cobros/pagos reales como prueba.
- Logos en login, layouts CRM/SuperAdmin/tienda/piloto, errores y etiquetas; portal
  de registro manual y descargas protegidas sin mezclar permisos con el CRM.

## 4. APK firmado: carga por el propietario

El APK nuevo **no se publica como asset público de GitHub**. Se conserva el enlace
histórico del APK 2.0.1 (9); ese archivo no tiene el rediseño ni el nuevo icono.
El propietario recibe localmente `Envirapido-2.1.0-10-release.apk` y lo sube a la
sección protegida de SuperAdmin → Portal comercial → Versiones Android.

| Campo | Valor |
| --- | --- |
| Versión | 2.1.0 (10) |
| Paquete | `com.pilotgo.app` |
| Tamaño | 75,427,589 bytes |
| SHA256 APK | `d1fc8278288adc75390af9baca9998158c7ee07559f979ed6aeaeed8e7dd4be3` |
| Certificado SHA256 | `773d72ba127d16a95f032d520d37162708ea8676bf2f7e3e48168f751724e487` |
| Commit del build | `8c91f42dbe97d4fd9ec74514cb71595ec9f9280e` |
| URL pública del build | `https://envirapido.com` |

Notas sugeridas: «Envirapido 2.1.0 (10). Identidad Envirapido, icono de caja,
mejoras de vistas y reasignación por paradas con recepción trazable».

Subir a cuarentena, validar firma/paquete/versión y comprobar que versionCode 10
supera al máximo del canal. No cambiar la versión declarada de un binario si ya
existiera una versión igual o superior: en ese caso preparar otro build firmado.
Confirmar QA funcional y URL **sólo después de realizarla**; entonces publicar.
La firma válida por sí sola no acredita funcionamiento contra producción.

## Evidencia y límites

- Ejecución final local: **340/340 pruebas SQL Server, 0 fallos, 0 omitidas**;
  `artifacts/entrega-2.1-20261118/crm-release-final.trx`. Compilación sin errores.
- Evidencia móvil retenida del mismo árbol de fuentes: **120/120 pruebas** y
  `flutter analyze --no-pub` sin incidencias. No es una nueva ejecución en esta
  entrega Git. El APK conservado pasó firma V2, alineación 16 KiB e inspección de
  URL pública en sus tres ABI; launcher/login release comprobados en emulador.
- Las pantallas autenticadas y el panel deslizante se probaron con variante QA y
  SQL local. No equivale a QA del release contra producción ni a una actualización
  desde APK 9 conservando sesión. No se modificó el Samsung ni se validó iOS.
- Los 71 archivos del manifiesto del build conservan sus hashes raw; sus blobs Git
  corresponden al commit móvil, con seis normalizaciones CRLF→LF de sólo finales
  de línea. Build previo al commit, atribuido por correspondencia comprobada;
  no se afirma recompilación ni reproducibilidad binaria. Ver
  [recibo APK](docs/envirapido/APK-2.1.0-10.json).
- **Despliegue y QA pública del lote pendientes del agente del servidor.**

## Recuperación

Conservar respaldos y un release compatible. La reversión de la migración está
bloqueada cuando existen órdenes gestionadas o retiros registrados. No borrar
historial para forzar `Down`, ni arrancar un backend legacy que no interprete los
datos nuevos. Después de usar la nueva gestión, preferir una corrección hacia
adelante o un release compatible; cualquier restauración requiere una ventana
controlada y revisión de las escrituras posteriores. No restaurar ni tocar la
base comercial para revertir un cambio del CRM.
