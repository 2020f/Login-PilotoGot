# Desplegar Envirapido 2.1.1 — retiros e incidencias

Entrega del 20/11/2026. El propietario autorizó publicar las fuentes y preparar el
APK firmado; el despliegue del servidor corresponde al agente de infraestructura.
La publicación en GitHub y una firma válida no acreditan QA en producción.

Esta guía describe el **delta posterior a Envirapido 2.1**. Si falta su esquema,
seguir primero las precondiciones de
[DESPLEGAR-ENVIRAPIDO-2.1.md](DESPLEGAR-ENVIRAPIDO-2.1.md).

## Fuentes y archivo que se entregan

| Elemento | Identificación |
| --- | --- |
| Backend / CRM | `2020f/Login-PilotoGot`, `codex/confiabilidad-panel-app`, `envirapido-web-2.1.1-11` |
| App Flutter | `2020f/pilotgo-app`, `codex/confiabilidad-panel-app`, `ba9411c461da82d137a6fc6121daf8b0c50f4b21` |
| APK | `Envirapido-2.1.1-11-release.apk` |
| Versión Android | 2.1.1 (11) |
| Paquete | `com.pilotgo.app` |
| URL compilada | `https://envirapido.com` |
| Tamaño APK | `75427589` bytes |
| SHA256 APK | `47acc45e7f7c4340683d12464f79579c3103bfbefdf8accac4bfabbb19f40e0d` |
| Certificado SHA256 | `773d72ba127d16a95f032d520d37162708ea8676bf2f7e3e48168f751724e487` |

El backend se fija con el tag inmutable `envirapido-web-2.1.1-11`; el mensaje de entrega identifica además su SHA completo. Resolver el tag antes de publicar y usar una carpeta de release separada y limpia:

```bash
git fetch origin tag envirapido-web-2.1.1-11
git rev-parse 'envirapido-web-2.1.1-11^{commit}'
git worktree add --detach /RUTA_RELEASE_NUEVA envirapido-web-2.1.1-11
git -C /RUTA_RELEASE_NUEVA rev-parse HEAD
```

Adaptar únicamente `/RUTA_RELEASE_NUEVA` al directorio de releases del servidor.
Los dos SHA deben coincidir con el SHA completo confirmado en la entrega.
Conservar el release anterior.
No ejecutar `reset`, `clean` ni un `pull` que descarte trabajo del servidor.
No sustituir los SHA por `master`, `main` o `latest` sin revisar qué se despliega.

## 1. Preservar configuración y comprobar el esquema

1. Conservar respaldo verificable del CRM, configuración efectiva, claves
   Identity/JWT/Data Protection, archivos privados y release anterior. Mantener
   las identidades y conexiones existentes; no copiar configuraciones de QA.
2. Identificar la base real de `DefaultConnection` sin publicar credenciales y
   comprobar `SELECT DB_NAME()`. La base `PortalConnection` es independiente y no
   recibe cambios de esquema ni datos en este lote.
3. Comprobar en el CRM que `__EFMigrationsHistory` contiene
   `20261117230916_EnvirapidoReasignacionPorParadas`. **Si ya existe, este delta no
   necesita una migración nueva.** Reutiliza incidencias, retiros, devoluciones y
   paradas existentes; agrega el valor 7 `TrasladadaAReasignacion` a la enumeración
   de devolución sin renumerar los valores anteriores.
4. Si falta ese esquema, detener la actualización de este delta y preparar el
   procedimiento de migración de la guía 2.1. No ejecutar el script antiguo sin
   verificar base, historial, hash y respaldo. No recrear tablas ni bases.

No ejecutar `SeedAsync`, `EnsureCreated`, `tools/Envirapido.Qa`, sus opciones
`--retiros` o `--database`, ni cuentas o fixtures locales en producción. Este lote
no convierte masivamente órdenes ni devoluciones existentes y no regenera A/B/C.

## 2. Desplegar primero el backend completo

- Publicar `Login/Login.csproj` y sus dependencias desde `envirapido-web-2.1.1-11`, con las
  vistas Razor y los recursos `wwwroot` correspondientes al mismo commit. Mantener
  el modo de alojamiento existente de .NET 10 y su runtime compatible; el proyecto
  conserva la referencia 10.0.15. No sustituir sólo DLL o JavaScript sueltos.
- Conservar `wwwroot/js/envirapido-reasignaciones.js`, branding, CSS y sus rutas
  públicas hacia el CRM. La portada React y sus demás vistas no necesitan un
  rediseño ni una nueva base para este delta.
- Mantener `/api/*`, `/Identity/*`, CRM y SuperAdmin dirigidos al backend; el
  fallback React no debe capturar las API ni los assets del CRM.
- Preservar HTTP→HTTPS, cookies `Secure`/`HttpOnly`, antiforgery, TLS y los proxies
  confiables exactos de la topología ya desplegada. No reemplazar a ciegas la
  configuración del proxy ni confiar en cabeceras reenviadas de cualquier origen.
  Conservar la compatibilidad HTTPS de `pilotogoapp.zapflow77.dpdns.org`.
- Mantener las conexiones, identidades, configuración de registro manual sin
  SMTP y almacenamiento privado del portal. No unir bases, compartir cookies ni
  crear automáticamente un tenant CRM a partir de una cuenta comercial.

Después de iniciar el servicio, comprobar con una sesión válida de piloto:

```http
GET /api/piloto/capacidades
```

La respuesta debe conservar el contrato 4 y contener al menos:

```json
{
  "versionContrato": 4,
  "multiparada": true,
  "cierreSoloC": true,
  "confirmacionParadaIdempotente": true,
  "gestionPorParadas": true,
  "recogidaPorLoteIdempotente": true,
  "revisionAsignacion": true,
  "retiroVinculadoAIncidencia": true
}
```

Una respuesta sin `retiroVinculadoAIncidencia: true` significa que el servidor
todavía no ofrece la nueva solicitud vinculada: revisar el release y el
enrutamiento antes de distribuir este APK. La app muestra un aviso de actualización
cuando falta esa capacidad, en lugar de enviar campos que un backend antiguo ignore.

## 3. Verificar las acciones del CRM y la app

Usar únicamente una empresa de pruebas y cuentas/órdenes ficticias autorizadas.
No retirar, cancelar ni trasladar pedidos reales para comprobar el despliegue.
No registrar cobros o pagos reales como prueba.

1. **Incidencias → Ver → Gestión del paquete** debe mostrar **Retirar del piloto**
   y **Reasignar paradas**, o la acción correspondiente a la gestión ya abierta.
   Deben existir accesos desde listado/detalle de pedidos y desde
   **Retiros y reasignaciones**. Abrir una ficha o enlace no modifica asignaciones.
2. En la app, **Reportar incidencia → Solicitar retiro** permite seleccionar
   paradas y un motivo. Debe crear una incidencia y sus solicitudes de manera
   atómica. La petición del piloto requiere aprobación del gestor; el retiro que
   aplica directamente un gestor autorizado es inmediato y exige motivo.
3. Al aprobar, las paradas salen de la ejecución del piloto sin pedir B/C ni
   registrar una entrega. **Pedidos → Reasignación y cancelación → Por reasignar**
   permite asignarlas de nuevo, programar retorno o cancelarlas según su custodia.
   La app debe actualizarse en la sesión existente, sin obligar a volver a entrar.
4. Antes de recoger, repartir o reasignar paradas no debe inventar una recepción.
   Después de recoger, el custodio anterior conserva el paquete hasta que el
   receptor confirme: **Perfil → Traspasos y recepciones** para el piloto receptor;
   personal autorizado para un comercio o centro elegido por el gestor.
5. En devoluciones clásicas abiertas, usar la acción explícita
   **Pasar devoluciones a reasignación**, revisando el conjunto completo y las
   versiones. Debe cerrar los casos clásicos con estado 7, conservar su historial
   y crear los retiros sin simular recepción física ni consumir códigos de entrega.
   No realizar esta conversión mediante SQL directo.
6. Probar una parada cancelada y otra reasignada de la misma orden: la cancelada
   sale de la cola operativa; la otra continúa con su piloto. La parada no elegida
   conserva asignación/custodia. Un paquete aún en custodia no puede desaparecer
   por cancelar: primero corresponde su recepción autorizada cuando sea necesaria.
7. Confirmar que la actividad de retiro no resuelve automáticamente la incidencia
   ni levanta sus pausas. Una pausa de ruta continúa aunque se retire su membresía.
   Con `PausarParada`, una solicitud debe limitarse a una parada; con `PausarRuta`,
   si la selección abarca varias rutas debe dividirse por ruta.
8. Verificar reintentos con el mismo intento sin duplicación, recuperación de la
   incidencia original, rechazo de revisiones antiguas tras reasignar y acceso
   ajeno denegado. Una gestión rechazada/cerrada no bloquea una nueva solicitud
   vinculada si el piloto conserva una parada activa.
9. Conservar el flujo principal: comercio crea orden → CRM → asignación manual o
   automática → piloto recibe aun con otra activa → recogida B → entrega C por
   parada. Verificar rutas y actualización de estados sin cerrar sesión.

La asignación y la custodia son independientes. Los retiros y traspasos no
consumen C; el efectivo ya cobrado sigue a cargo de su responsable hasta entregarlo
a la empresa. El ajuste por trabajo previo lo decide el gestor con el mecanismo
existente. Las validaciones de empresa, actor, revisión e idempotencia permanecen.

## 4. Cargar el APK por el canal protegido

El APK se entrega localmente al propietario. **No se agrega a Git ni se publica
como asset público de GitHub.** Se conserva el enlace histórico existente.

En **SuperAdmin → Portal comercial → Versiones Android**:

- Archivo: `Envirapido-2.1.1-11-release.apk`.
- Commit del build: `ba9411c461da82d137a6fc6121daf8b0c50f4b21`.
- URL pública del build: `https://envirapido.com`.
- Notas sugeridas: «Envirapido 2.1.1 (11). Retiro desde incidencias, reasignación
  por paradas y recepción física trazable, conservando rutas y multitenant».

Subir a cuarentena y validar paquete, versión, firma, tamaño y SHA256 contra la
tabla de esta guía. El código 11 debe superar al máximo ya registrado en el canal;
si existe una versión igual o superior, preparar un nuevo binario con otro código.
No editar la versión declarada de un APK ya compilado ni cambiar su certificado.

Confirmar la QA funcional y la URL en el formulario únicamente después de probar
ese binario contra el backend público actualizado. Entonces publicar la versión
en el canal privado para las cuentas del portal que tengan acceso vigente.

## Evidencia local y cierre del despliegue

La revisión local del lote aprobó **371/371 pruebas SQL Server** (incluye 25
regresiones nuevas), **129/129 pruebas Flutter** y analizador sin problemas.
Los logs y el informe completos se conservan localmente en
`artifacts/retiros-integrados-20261119/`; no se deben publicar bases, credenciales,
códigos ni fixtures de QA como configuración del servidor.

El recorrido nativo usó un APK debug QA y SQL Server local: retirada sin B/C,
aprobación sin relogin, reparto/cancelación por parada, traslado de devoluciones
clásicas y recepción por el piloto receptor. Ese APK precede a los dos últimos
ajustes UX, cubiertos después por cuatro pruebas widget: nueva solicitud tras
rechazo y contexto original del intento recuperado. La evidencia nativa anterior
no se atribuye a esas dos ramas ni al nuevo APK release firmado.

El APK release 2.1.1 (11) se instaló después en un emulador independiente.
El SHA256 del archivo instalado coincide con la tabla de entrega; se observaron
el icono de caja y el login. No se inició sesión en producción ni se alteraron
órdenes públicas. También terminó correctamente `dotnet publish -c Release`.
Estas comprobaciones no sustituyen los recorridos autenticados posteriores al
despliegue. Ver [recibo y evidencias](docs/qa/retiros-incidencias-20261120/README.md).

El agente del servidor debe devolver el SHA desplegado, la capacidad autenticada
observada, el resultado de HTTPS/cookies, el identificador/hash del APK probado y
los resultados de los recorridos ficticios autorizados, indicando cualquier
pendiente. Hasta completar ese cierre, este documento no certifica producción ni
autoriza marcar como realizada una QA funcional que todavía no se haya ejecutado.

## Recuperación

Conservar un release compatible y los respaldos. No ejecutar `Down`, borrar
historial o restaurar la base comercial para revertir el CRM. Tras registrar
retiros o estados nuevos, no volver a un binario que desconozca esas operaciones:
preferir corrección hacia delante o un release compatible con los datos actuales.
Una restauración necesita revisar las escrituras posteriores y una ventana
controlada; nunca eliminar pedidos, rutas o custodia para eludir una guarda.
