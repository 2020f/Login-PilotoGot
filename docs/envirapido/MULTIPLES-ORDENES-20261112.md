# Recepción de varias órdenes por piloto

12 de noviembre de 2026 · Cambio solicitado por el usuario · Validación local con SQL Server y Android `emulator-5554`.

El piloto puede recibir y aceptar otras órdenes automáticas y recibir nuevas asignaciones manuales aunque tenga entregas activas. Se retiró la restricción anterior de una sola automática, sin añadir un límite nuevo ni cambiar el aislamiento por empresa. Este cambio de regla sucede a la [restauración de recepción y vista deslizante](CORRECCION-RECEPCION-Y-VISTA-20261112.md); los informes anteriores conservan su alcance histórico.

## Comportamiento y guardas

- Las ofertas automáticas siguen exigiendo piloto activo, empresa activa y recepción en línea. Sólo incluyen órdenes disponibles de esa empresa, sin asignar, no rechazadas por ese piloto ni bloqueadas por programación, pausa o reserva de ruta.
- Las asignaciones manuales pueden añadirse con otras activas y con recepción automática OFF. El gestor y el piloto deben pertenecer a la empresa de la orden.
- Dos órdenes distintas pueden asignarse concurrentemente al mismo piloto. Dos pilotos que compiten por la misma orden producen una sola asignación y un solo evento de historial. Los bloqueos y el `UPDATE` condicional por orden se conservan.
- Una orden adicional permanece independiente: no se inserta automáticamente en R1 ni cambia sus puntos. El CRM sigue controlando la agrupación, la secuencia y los permisos para reordenar. Las guardas de B y C continúan exigiendo la próxima visita autorizada de la ruta.
- Terminar una de varias entregas no detiene el seguimiento de las restantes. La regresión SQL exige autorización GPS mientras queda trabajo activo y su retirada al terminar la última.

La app conserva el estilo restaurado de la vista deslizante. «Continuar mis entregas» permite seguir trabajando sin rechazar la oferta; la oferta pendiente puede reabrirse desde una banda en Ruta y no tapa las otras pestañas. La confirmación de una aceptación ya aprobada no queda esperando una consulta de ofertas anterior que se haya retenido.

## Entorno y datos conservados

Panel y app usan **5019 → 5019**, base `PilotGoTest_4612d89bd20d464394bd3724a9fea91a`. El servidor actualizado se adjuntó a la base existente, sin seed ni migración y sin eliminarla al cerrar. El respaldo pasó verificación y las **13 filas** comparadas durante el reinicio fueron idénticas.

- [Respaldo verificado](../../artifacts/multiples-ordenes-20261112/respaldo.json).
- [Reinicio preservado](../../artifacts/multiples-ordenes-20261112/host-restart.json), [filas antes](../../artifacts/multiples-ordenes-20261112/host-before.json) y [filas después](../../artifacts/multiples-ordenes-20261112/host-after.json).
- [Identidad del entorno y túnel](../../artifacts/multiples-ordenes-20261112/conexion-guardada.json).
- [Lectura autenticada de ofertas con trabajo activo](../../artifacts/multiples-ordenes-20261112/servidor-regla-activa.json): A y A2 obtienen ofertas propias; B mantiene sólo su empresa. Es una lectura previa a las aceptaciones visuales posteriores.

## Recorrido observado en el emulador

Piloto A conservaba la orden manual 0010 activa. Recibió la oferta 0009, usó «Continuar mis entregas», reabrió la oferta y la aceptó sin terminar 0010. Abrió el detalle de la nueva orden; después Home mostró dos activas y una oferta pendiente.

Con esas dos activas, el CRM asignó manualmente la orden QA 0012, ID 13, al mismo piloto. La app mostró **0012, 0009 y 0010** en Activas, sin cerrar sesión ni esperar entregas anteriores. Este recorrido no confirmó recogidas, entregas o cancelaciones ni consumió B/C.

| Paso | Evidencia |
| --- | --- |
| Oferta con 0010 activa | [01-oferta-con-0010-activa.png](../../artifacts/multiples-ordenes-20261112/visual/01-oferta-con-0010-activa.png) |
| Continuar sin rechazar | [02-continuar-sin-rechazar-oferta.png](../../artifacts/multiples-ordenes-20261112/visual/02-continuar-sin-rechazar-oferta.png) |
| Segunda automática aceptada sin terminar la anterior | [03-segunda-auto-aceptada-sin-terminar-primera.png](../../artifacts/multiples-ordenes-20261112/visual/03-segunda-auto-aceptada-sin-terminar-primera.png) |
| Dos activas y oferta pendiente | [04-dos-activas-y-oferta-pendiente.png](../../artifacts/multiples-ordenes-20261112/visual/04-dos-activas-y-oferta-pendiente.png) |
| Tres activas en la misma sesión | [05-tres-activas-auto-manual-misma-sesion.png](../../artifacts/multiples-ordenes-20261112/visual/05-tres-activas-auto-manual-misma-sesion.png) |
| Asignación manual desde CRM con dos activas | [06-crm-manual-con-dos-activas.png](../../artifacts/multiples-ordenes-20261112/visual/06-crm-manual-con-dos-activas.png) |

Piloto A2 inició sesión con las órdenes 0003, 0004 y 0008 activas y R1 en curso. Recibió la oferta QA 0011, ID 12, continuó sin rechazarla y Home mantuvo la próxima recogida 0003 de R1. Después reabrió y aceptó 0011 sin terminar ninguna de sus tres entregas previas. El detalle mostró 0011; al volver, Home conservó la próxima visita 0003 y Entregas mostró cuatro activas: **0011, 0008, 0004 y 0003**. No hizo falta otro login para aceptar el trabajo adicional.

| Ruta activa y trabajo adicional | Evidencia |
| --- | --- |
| Oferta con R1 y tres entregas previas | [07-oferta-piloto-con-ruta-y-tres-activas.png](../../artifacts/multiples-ordenes-20261112/visual/07-oferta-piloto-con-ruta-y-tres-activas.png) |
| Continuar mantiene la próxima visita de R1 | [08-ruta-conserva-proxima-visita-con-oferta.png](../../artifacts/multiples-ordenes-20261112/visual/08-ruta-conserva-proxima-visita-con-oferta.png) |
| 0011 aceptada con la ruta activa | [09-auto-adicional-aceptada-con-ruta-activa.png](../../artifacts/multiples-ordenes-20261112/visual/09-auto-adicional-aceptada-con-ruta-activa.png) |
| Home conserva la secuencia con cuatro activas | [10-cuatro-activas-ruta-conserva-proxima-visita.png](../../artifacts/multiples-ordenes-20261112/visual/10-cuatro-activas-ruta-conserva-proxima-visita.png) |
| Cuatro activas en Entregas | [11-cuatro-activas-auto-manuales-y-ruta.png](../../artifacts/multiples-ordenes-20261112/visual/11-cuatro-activas-auto-manuales-y-ruta.png) |
| R1 conservada en CRM | [12-crm-ruta-preservada.png](../../artifacts/multiples-ordenes-20261112/visual/12-crm-ruta-preservada.png) |

La [captura SQL inicial](../../artifacts/multiples-ordenes-20261112/multi-order-before.json) pasó **9/9 controles**. La [comparación SQL posterior](../../artifacts/multiples-ordenes-20261112/multi-order-after.json) pasó **28/28**, mediante el [script de lectura](../../artifacts/multiples-ordenes-20261112/Read-MultiOrderProof.ps1). Confirma las 13 órdenes, A con IDs 10/11/13 activos, A2 con 3/4/9/12 activos, R1 con los mismos cinco puntos y sus mismas pertenencias, B/C sin consumo y los controles programado y de empresa B intactos. Las órdenes adicionales no se incorporaron a R1 ni duplicaron sus asignaciones. La continuidad del login y las interacciones de pantalla son observaciones CUA; SQL acredita los estados y relaciones guardados.

## Pruebas y artefacto

- **SQL Server: 169/169**, cero fallidas u omitidas: [TRX final](../../artifacts/multiples-ordenes-20261112/sql/multiples-sqlserver-final.trx) y [log](../../artifacts/multiples-ordenes-20261112/sql-suite.log). Incluye seis casos nuevos de [MultipleOrderAssignmentTests.cs](../../Login.Tests/MultipleOrderAssignmentTests.cs) y la carrera actualizada de órdenes distintas en [OrderFlowTests.cs](../../Login.Tests/OrderFlowTests.cs). El [grupo focal backend](../../artifacts/multiples-ordenes-20261112/backend/multiple-backend.trx) pasó 19/19.
- **Flutter: 93/93**, análisis completo limpio. Logs originales en `C:\Users\Fernelis\Desktop\pilotGOapp\build\envirapido-qa\multiples-ordenes-20261112`. Se preservó el fallo que reprodujo la aceptación retenida por un GET antiguo; el código se corrigió y pasó la regresión sin eliminar su aserto.
- **APK QA 2.0.0+8, profile x64**, paquete `com.pilotgo.app.qa`, instalado sólo en `emulator-5554`. API embebida `http://127.0.0.1:5019`; firma QA conservada. Archivo: `C:\Users\Fernelis\Desktop\pilotGOapp\build\envirapido-qa\Envirapido-2.0.0-8-qa-x64.apk`. SHA-256: `f5f9d1681211582fe940d53349292e11cc82cbccbc8a2e59ad18c6c3c0d7d26e`.

El resultado acredita el cambio de regla y las condiciones descritas en local. No equivale a distribución del APK, validación en Samsung físico o producción, ni amplía las pruebas de batería, Waze, reinicio o SMTP real. No se hizo commit, push ni despliegue de producción.
