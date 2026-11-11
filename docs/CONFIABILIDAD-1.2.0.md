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

Este trabajo prepara código, pruebas, script y APK de prueba. No actualiza la
instancia pública ni instala la app en teléfonos.

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

APK de prueba local:
`C:\Users\Fernelis\Desktop\pilotGOapp\build\releases\PilotGo-1.2.0-prueba.apk`.
Versión Android 1.2.0 (3); firma SHA-256
`773d72ba127d16a95f032d520d37162708ea8676bf2f7e3e48168f751724e487`.
La clave privada sigue fuera de Git. El artefacto anterior 1.1.0 sigue en su
release anterior y en su copia local con nombre de versión.

## Validación y límites de este lote

Resultados exactos, nombres de pruebas y hash del APK:
[confiabilidad-1.2.0.json](qa/confiabilidad-1.2.0.json).

Las pruebas cubren el flujo anterior y nuevas carreras, reintentos, snapshots,
cuotas, cambio de tienda, permisos, cookies/CSRF, navegación/recursos del panel,
resumen diario e idempotencia. La prueba SQL de migración comprueba órdenes
antiguas, preservación de códigos y congelación después de editar la agenda.
La batería de flota sigue usando veinte pilotos, cuarenta comercios y dos tenants
con datos ficticios; no es una medición de capacidad del servidor público.

La revisión visual con Computer Use se detuvo al no poder verificar con suficiente
confianza la URL del navegador. No se continuó con automatización de navegador.
Las pantallas se verificaron por HTTP autenticado, renderizado Razor, recursos,
formularios y pruebas de widgets. La comprobación visual en navegador y teléfono
sigue pendiente. El servidor efímero de QA local fue detenido.

Las pruebas nativas de almacenamiento, GPS, cámara y ciclo de vida usan simulaciones.
El reporte de GPS y las consultas siguen siendo en primer plano; este lote no
implementa push, tracking con proceso cerrado ni cola general offline.

Quedan para módulos siguientes: transferencia con custodia, incidencias/retornos,
evidencias avanzadas, cotizador, dinero contra entrega/liquidaciones, correo real,
facturación SaaS, auditoría global, vencimiento/gracia y planificación de rutas.
Finanzas y Auditoría Global no se anuncian como pantallas disponibles.

## Reproducción de pruebas

```powershell
dotnet test Login.sln
# SQL Server opcional: crear/iniciar instancia local aislada de QA primero.
$env:PILOTGO_TEST_SQLSERVER='1'
dotnet test Login.sln
Remove-Item Env:\PILOTGO_TEST_SQLSERVER

# Desde la app
flutter analyze --no-pub
flutter test --no-pub
flutter build apk --release --no-pub --dart-define=API_BASE_URL=https://pilotogoapp.zapflow77.dpdns.org
```

Las pruebas emplean SQLite o bases `PilotGoTest_*` aisladas, nunca la conexión
de producción. Cookies/CSRF usan claves efímeras por host de pruebas; el reloj
de autenticación se alinea con el reloj controlado y el pool SQLite se desactiva
para aislar fixtures paralelos. Estas opciones solo afectan a `Login.Tests`.
