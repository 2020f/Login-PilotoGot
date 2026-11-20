# PilotGo 1.2.0 — primer lote de confiabilidad

Fecha: 11 de noviembre de 2026. Backend y app en ramas
`codex/confiabilidad-panel-app`, desde `da48f52` y `0f42e39` respectivamente.

## Qué queda implementado

- **Creación recuperable:** clave de operación persistida por cuenta/API/tienda;
  el servidor devuelve la misma orden ante reintentos, sin consumir otra cuota,
  secuencia ni códigos. Conflictos de payload devuelven 409. Solo un rechazo
  explícito y definitivo permite descartar la intención pendiente.
- **Comprobante:** un POST confirmado se conserva aunque falle el QR; se
  recupera ese comprobante sin crear otra orden. En web se redirige por ID y
  tienda, sin incluir PIN en la URL. La imagen QR comprueba la pertenencia actual.
- **Datos de entrega congelados:** recogida, destino, contactos, mapa y coordenadas
  se guardan con la orden. Editar la agenda no modifica nuevas órdenes ya creadas.
  Se conserva la restricción de destino antes de recoger.
- **Bajas seguras:** editar, desactivar o eliminar lógicamente un piloto o comercio
  con órdenes abiertas se rechaza. La baja de una tienda no bloquea la cuenta
  Identity completa. Asignaciones y bajas comparten orden de bloqueos por tenant.
- **Cuotas:** resolución de límites efectivos del cliente, cero como ilimitado,
  alta/reactivación serializada y conteo de identidades distintas vinculadas.
- **Despacho/panel:** carga real, candidatos activos separados de filtros,
  actualización periódica sin solapamiento, pausa durante el modal, historial
  paginado y detalle permanente con eventos y datos congelados.
- **Administración:** Dashboard real, búsquedas/paginación, scripts conectados,
  identidad/salida correctas, formularios separados y rangos de estadísticas
  validados con día comercial dominicano.
- **Comercio móvil:** contexto de tienda explícito, respuestas antiguas aisladas,
  sincronización en primer plano/reanudación y resumen calculado en servidor;
  Incidente no aparece Entregado y la fecha de entrega gobierna el total del día.
- **Piloto:** GPS de órdenes manuales independiente de la recepción automática,
  estado ocupado derivado de carga, menú básico y selección de perfil autorizado.
- **Acceso y diagnóstico:** fallos de login web contabilizados para bloqueo;
  errores inesperados de API registrados con `traceId` y sin detalles internos.
- **Fechas de API:** todos los instantes `DateTime` enviados como JSON MVC
  incluyen UTC explícito (`Z`), incluso al leer `datetime2` de SQL Server.
  Así la app convierte una sola vez a la hora local. No cambia fechas de la base,
  interpretación JSON entrante, formularios ni el contrato `DateTimeOffset`.

Las órdenes nuevas conservan A/B/C de cuatro dígitos. No se regeneran códigos
anteriores. El APK mantiene `com.pilotgo.app`, la firma original y la URL pública
`https://pilotogoapp.zapflow77.dpdns.org`.

## Contrato y compatibilidad

El cliente 1.2.0 consulta `GET /api/tienda/capacidades` antes de enviar una
creación nueva o reintentar una creación incierta. Requiere contrato 2,
`creacionIdempotente=true` y `contextoTienda=true`. Si el servidor anterior no
lo admite, no envía el POST. Una operación ya confirmada recupera el QR por ID.

Las solicitudes comerciales llevan `X-Tienda-Id`, validado contra pertenencia,
actividad y tenant. La preferencia global se mantiene para clientes antiguos.
Cambiar la selección desde otro dispositivo no altera solicitudes con contexto
explícito. En selección legacy, el bloqueo sigue Tenant → Usuario.

Los clientes anteriores pueden continuar usando los endpoints existentes; al
no enviar clave, sus creaciones no adquieren la garantía nueva de idempotencia.
Actualizar primero servidor/base y después la app.

## Migración y datos existentes

Migración: **20261111222204_ConfiabilidadOrdenesSnapshot**.
Script: [confiabilidad-1.2.0.sql](qa/confiabilidad-1.2.0.sql).

Agrega campos de snapshot y la tabla `OperacionesOrden` con unicidad por tenant,
actor, tipo y clave. El backfill congela los datos disponibles actualmente en
las órdenes antiguas sin cambiar ID, códigos, estados, precios ni fechas.
No reconstruye direcciones que ya se hubieran sobrescrito antes de esta versión.

El script incorpora opciones SQL requeridas para índices filtrados y transacción.
Se comprobó su aplicación inicial y su segunda aplicación sobre una base QA.
El rollback que elimina snapshots/operaciones se bloquea cuando hay esos datos;
una reversión requiere el respaldo y un plan coherente de versión de cliente.

## Activación controlada

Este trabajo prepara código, pruebas, script y APK para validar. La instancia
pública y la distribución a pilotos no se actualizan con esta QA; las instalaciones
de prueba se conectan al entorno QA aislado.

1. Respaldar base, publicación y configuración privada del servidor. Conservar
   claves JWT, claves de protección de datos, cadena de conexión y configuración
   de Maps; no sustituirlas por archivos de desarrollo.
2. Revisar este lote en QA. Detener las instancias antiguas durante el cambio
   de esquema para que no creen órdenes sin snapshot mientras se congela legado.
3. Con la conexión de destino correcta y la migración 1.1.0 ya aplicada, ejecutar
   el script con herramienta compatible con `GO` y abortar ante errores:

   ```powershell
   # Ejemplo: usar la instancia y base reales aprobadas; no las bases QA de pruebas.
   sqlcmd -S SERVIDOR -d BASE -E -b -i docs/qa/confiabilidad-1.2.0.sql
   ```

   Si faltan migraciones anteriores, usar EF Core 8.0.23 para actualizar la cadena
   completa, con configuración privada y el servicio detenido.
4. Publicar e iniciar el backend nuevo en Production:

   ```powershell
   dotnet publish Login/Login.csproj -c Release -o artifacts/server-1.2.0
   ```

5. Probar cuenta comercial y piloto de QA: repetir creación con la misma clave,
   recuperar QR, asignar, recoger, entregar y recibir la siguiente orden;
   comprobar historial, un segundo tenant y una baja con entrega abierta.
6. Instalar el APK 1.2.0 en los teléfonos de validación. Confirmar actualización
   conservando sesión/credenciales, cámara, GPS, permisos y reanudación.
7. Distribuir gradualmente después de validar el recorrido físico y el panel.

APK Release local para validar:
`C:\Users\Fernelis\Desktop\pilotGOapp\build\releases\PilotGo-1.2.0-release.apk`.
Compilación nativa posterior a la corrección de GPS, 71 387 149 bytes, SHA-256
`e3f3f05ac5f7a17f2e6f631c0b96cf82241a943d20840aa565521ab0c5f4c01d`.
El metadato del APK de prueba anterior se conserva en el historial del JSON.
La compilación y firma están comprobadas; el recorrido completo en dispositivo
continúa pendiente.
Versión Android 1.2.0 (3); firma SHA-256
`773d72ba127d16a95f032d520d37162708ea8676bf2f7e3e48168f751724e487`.
La clave privada sigue fuera de Git. El artefacto anterior 1.1.0 sigue en su
release anterior y en su copia local con nombre de versión.

## Validación y límites de este lote

Resultados exactos, nombres de pruebas y hash del APK:
[confiabilidad-1.2.0.json](qa/confiabilidad-1.2.0.json).

Última batería: **71 correctas en SQL Server; 69 correctas y 2 omitidas en SQLite**.
Las omitidas corresponden a la migración y a estadísticas válidas específicas de
SQL Server; ambas pasaron en SQL Server. Las pruebas Flutter registradas conservan
32 correctas y análisis sin incidencias.

Las pruebas cubren el flujo anterior y nuevas carreras, reintentos, snapshots,
cuotas, cambio de tienda, permisos, cookies/CSRF, navegación/recursos del panel,
resumen diario e idempotencia. La prueba SQL de migración comprueba órdenes
antiguas, preservación de códigos y congelación después de editar la agenda.
Siete casos adicionales comprueban fechas JSON UTC en las lecturas de órdenes,
sus etapas, clientes y GPS, y conservan la interpretación entrante y los nulos.
El resultado de creación solo transporta identificadores/códigos, sin fechas.
Se estabilizó una prueba de aislamiento del panel que buscaba `B1` en cualquier
parte del HTML: ahora compara los nombres de comercio renderizados y evita
confundir el token CSRF aleatorio con un dato de otro tenant. La batería SQLite
completa pasó antes de ese ajuste de test y el caso ajustado pasó después;
la batería SQL completa final incluye el ajuste.
Después de esa batería se aplicó la misma precaución al test SQL de estadísticas:
compara las opciones de tienda/piloto por ID, sin buscar nombres cortos dentro del
HTML completo. Ese caso pasó nuevamente **1/1 en SQL Server**. No se repitió la
batería completa ni la publicación Release por este cambio exclusivo de test.
La batería de flota sigue usando veinte pilotos, cuarenta comercios y dos tenants
con datos ficticios; no es una medición de capacidad del servidor público.

La revisión visual se retomó en navegador con el panel conectado a una instancia
SQL Server LocalDB exclusiva, `PilotGoVisualQA_20261111`, y datos ficticios. Se
validaron los siguientes recorridos; las capturas seleccionadas están en
[visual-20261111](qa/visual-20261111/README.md):

| Recorrido web revisado | Resultado observado | Captura |
| --- | --- | --- |
| Estadísticas del gestor sin filtros | Cuatro órdenes del tenant A, dos entregadas y dos pendientes; gráfico y rankings renderizados. El filtro A2 mostró su pendiente sin mezclar A1. | [Estadísticas SQL](qa/visual-20261111/19-estadisticas-sqlserver.jpg) |
| Detalle permanente del historial | Información de la orden y cuatro eventos visibles; presentación de fechas corregida. | [Detalle](qa/visual-20261111/22-detalle-corregido.jpg) |
| SuperAdmin en ancho móvil web de 390 píxeles | Dashboard adaptado y navegación mediante menú lateral revisada. Es un viewport de navegador, no un teléfono físico. | [Dashboard móvil web](qa/visual-20261111/23-admin-movil-corregido.jpg) |
| Empresas en ancho móvil web | Búsqueda y listado accesibles, con tabla dentro de su área de desplazamiento horizontal. | [Empresas](qa/visual-20261111/25-empresas-movil.jpg) |
| Comercio manual A2 y comprobante | Orden asignada, códigos de cuatro dígitos y QR generado por identificador de orden. | [Comprobante SQL](qa/visual-20261111/27-qr-manual-sqlserver.jpg) |

El error 500 de la primera revisión de estadísticas se reprodujo con SQLite:
`EF.Functions.DateDiffMinute(...).Average()` usa traducción propia de SQL Server.
El mismo controlador con SQL Server devolvió la vista correctamente y se agregó
una prueba HTTP con rango predeterminado y filtros válidos. No se cambió el código
productivo de estadísticas para acomodar SQLite. Las capturas de incidencias
anteriores no se incluyen como evidencia de pantallas aprobadas.

**La QA completa de la app Android continúa en curso.** Se corrigió el bloqueo
nativo de GPS observado en el emulador y se reconstruyó el APK Release. La revisión
móvil detectó fechas de API sin zona al leer SQL Server; su salida ya declara UTC.
En app QA profile con GPS Android 5.2.1 se observaron estos pasos, conservando la
sesión iniciada a las 20:12:

- Automática ORD-0004: aceptación, destino oculto, recogida con B, destino
  desbloqueado y entrega con B/C. El comercio A1 pasó a tres entregadas hoy.
- Segunda automática ORD-0005 (id 6): apareció sola después de la primera entrega,
  sin logout; se rechazó para continuar la prueba por la vía manual.
- Con recepción automática apagada, A2 creó ORD-0006 (id 7) y el gestor la asignó
  al mismo piloto. La app la mostró junto a ORD-0003, con dos activas y sin logout.
- ORD-0006 terminó por UI con B/C; apareció la confirmación, quedó solo ORD-0003
  activa y A2 reflejó la orden entregada y una entregada hoy.
- El panel mostró GPS actualizado con recepción automática apagada y entregas
  activas. El mapa web de QA no tiene clave Maps; no acredita navegación real.
- Tras force-stop y arranque frío sin borrar datos, se restauró el inicio con
  ORD-0003 y recepción automática apagada, sin pedir login. Luego, al hacer logout,
  el formulario conservó email, contraseña enmascarada y Recordar activo;
  Iniciar sesión funcionó sin reescribirlos.

Las capturas seleccionadas [30–42](qa/visual-20261111/README.md) conservan estos
estados. El escáner abrió y se concedió permiso WhileUsing; cancelarlo conservó el
código C ya escrito y permitió finalizar por entrada manual. Tras un primer
intento negro, volver a abrirlo en ORD-0003 mostró la habitación virtual con TV
y libros. Se valida vista previa solo en el emulador. Se importó un QR a la pared
virtual, pero no se logró orientar la cámara hacia él: no se valida decodificación
ni cámara física.
Quedan pendientes decodificación real con cámara, ruta real y recorrido físico
completo. En un Samsung físico con app QA, después de corregir UTC, se inició
sesión A2 sin reescribir las credenciales recordadas. Se observaron ORD-0006
entregada, ORD-0003 pendiente y contadores 1 asignada / 0 en ruta / 1 entregada hoy.
La captura 42 muestra horas locales correctas: creación 20:21, asignación 20:22,
recogida 20:23 y entrega 20:30, con reloj del teléfono a las 20:38. Esta comprobación
no acredita ciclo físico de reparto, GPS ni cámara física. No se conservaron
imágenes personales. La release pública anterior no se sustituyó.

Las bases efímeras de los tests se limpiaron al terminar. El servidor visual, su
instancia SQL y la proyección se conservan activos por petición del usuario.
No se programa limpieza ni apagado de ese entorno.

Las pruebas automatizadas de almacenamiento, GPS, cámara y ciclo de vida usan simulaciones.
El reporte de GPS y las consultas siguen siendo en primer plano; este lote no
implementa push, tracking con proceso cerrado ni cola general offline.

Quedan para módulos siguientes: transferencia con custodia, incidencias/retornos,
evidencias avanzadas, cotizador, dinero contra entrega/liquidaciones, correo real,
facturación SaaS, auditoría global, vencimiento/gracia y planificación de rutas.
Finanzas y Auditoría Global no se anuncian como pantallas disponibles.

## Reproducción de pruebas

```powershell
dotnet test Login.sln
# SQL Server: usar una instancia local aislada, nunca la conexión de producción.
# Crear/iniciar la instancia siguiente solo si todavía no está disponible.
SqlLocalDB create PilotGoVisualQA_20261111
SqlLocalDB start PilotGoVisualQA_20261111
$env:PILOTGO_TEST_SQLSERVER='1'
$env:PILOTGO_TEST_SQLSERVER_INSTANCE='PilotGoVisualQA_20261111'
dotnet test Login.sln
Remove-Item Env:\PILOTGO_TEST_SQLSERVER
Remove-Item Env:\PILOTGO_TEST_SQLSERVER_INSTANCE

# Desde la app
flutter analyze --no-pub
flutter test --no-pub
flutter build apk --release --no-pub --dart-define=API_BASE_URL=https://pilotogoapp.zapflow77.dpdns.org
```

Las pruebas emplean SQLite o bases `PilotGoTest_*` aisladas, nunca la conexión
de producción. Cookies/CSRF usan claves efímeras por host de pruebas; el reloj
de autenticación se alinea con el reloj controlado y el pool SQLite se desactiva
para aislar fixtures paralelos. Estas opciones solo afectan a `Login.Tests`.
El nombre de instancia se configura mediante `PILOTGO_TEST_SQLSERVER_INSTANCE`,
validado en la factoría de pruebas. Si se omite, sigue usándose la instancia QA
anterior `PilotGoCodexQA_20261109`. No detener ni eliminar una instancia mientras
el host de revisión visual la esté utilizando.
