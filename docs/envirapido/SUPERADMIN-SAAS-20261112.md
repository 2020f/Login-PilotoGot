# Superadministración SaaS — validación local del 12/11/2026

Implementación funcional y revisión QA local completadas. No hubo commit, push ni despliegue de producción. Este recibo conserva el estado previo a la actualización de runtime. La [actualización posterior a .NET 10.0.15](NET10-ACTUALIZACION-20261112.md) supera el bloqueo de .NET 8 en el proyecto local, con SQL 231/231 y comprobación del emulador. Todavía falta validar y desplegar el entorno real de destino.

## Funcionalidad entregada

- Acceso al icono de perfil y rutas antiguas de gestión: pantalla española «No tienes autorización», HTTP 403 y regreso al panel correspondiente. No se amplían permisos. Las API responden 401/403 JSON; los errores inesperados conservan un mensaje genérico, sin detalles internos.
- Superpanel con estilo Envirapido, navegación de escritorio y móvil: resumen global, empresas, ficha de empresa, planes/límites, facturación/pagos, accesos/sesiones, auditoría y diagnóstico del sistema.
- Alta de empresas y gestores, edición de contacto/estado/cuotas y consumo efectivo. Los gestores nuevos requieren contraseña de al menos 12 caracteres, sin contraseña predeterminada. No se pueden reducir cuotas por debajo del uso actual ni simular un pago editando fechas del perfil.
- Cambio inmediato de plan, conservando órdenes, códigos, pilotos y rutas. Cero significa ilimitado; las excepciones por empresa se mantienen o se restablecen expresamente. Los planes retirados dejan de estar disponibles para nuevas contrataciones y conservan sus empresas actuales.
- «Mi suscripción» del gestor: consulta únicamente su empresa, facturas y solicitudes. El tenant se resuelve en el servidor. Solicitud de cambio de plan, revisión, aprobación/rechazo, motivo y trazabilidad. Una solicitud pendiente no cambia por sí sola el plan ni genera un cobro.
- Facturas en DOP con concepto, período, importe y fecha de vencimiento; pagos externos parciales o completos, método, referencia, fecha real y confirmación explícita. Ningún componente ejecuta transferencias bancarias.
- Estados de factura calculados desde pagos vigentes y fecha: pendiente, parcial, pagada, vencida o anulada. Las fechas de calendario son de Santo Domingo; el vencimiento incluye el día completo. Un pago futuro, duplicado, superior al saldo o correspondiente a otra empresa se rechaza.
- Anulación con motivo, sin borrar el recibo o factura; recalcula saldo y cobertura. La renovación requiere una factura completamente cubierta. Permite reactivar desde un período actual pagado tras un hueco no facturado y conserva las deudas vencidas pendientes. Un período futuro pagado no habilita anticipadamente el servicio.
- Auditoría de altas, ediciones, planes, solicitudes, facturas, pagos y revocaciones: actor, hora, empresa, motivo y valores anterior/nuevo cuando corresponden. Sólo admite adiciones por el contexto de la aplicación; el acceso de un DBA queda fuera de esta garantía.
- Consulta de roles/pertenencias y revocación de sesiones móviles con motivo y confirmación. Las cuentas SuperAdmin están protegidas contra esa acción. No se cambia la contraseña ni se reasignan órdenes. Las sesiones web invalidan su acceso en su siguiente comprobación del sello de seguridad.

## Fechas, vencimiento y continuidad operativa

La suspensión manual, la cobertura de suscripción y el estado de una factura son conceptos separados. Una factura pagada no levanta una suspensión manual. Una suscripción vencida impide nuevos pedidos, altas y reactivaciones; permite consultar y terminar el trabajo ya asignado. La prueba de integración confirma recogida con B y cierre sólo con C de una orden autorizada tras vencer la suscripción.

Las altas sin prueba explícita no conceden cobertura futura desde el perfil; deben renovarse por facturación. Las pruebas requieren fecha de fin, admiten hasta 90 días y no crean pagos. Los términos históricos sin fechas se conservan sin inventar pagos ni fechas de corte. Al emitir una factura, su fecha y saldo permiten controlar el vencimiento.

Un cambio de plan no modifica los importes de facturas emitidas ni aplica prorrateos automáticos. El administrador emite los nuevos períodos desde Facturación. No se realiza ningún cargo automático.

## Migración y preservación de datos

Migración: `20261112201846_EnvirapidoSaasBilling`. Agrega cinco tablas: `SaasInvoices`, `SaasPayments`, `SaasSubscriptions`, `SaasPlanRequests`, `SaasAuditLogs`. Las claves de pagos/facturas incluyen la empresa; los índices únicos controlan reintentos, referencias y solicitudes pendientes. El rollback SQL se bloquea si existe cualquier registro SaaS para impedir pérdida de facturas, pagos, solicitudes, cobertura o auditoría.

Antes de ampliar la base QA se creó y verificó un respaldo con checksum:

- Base preservada: `PilotGoTest_4612d89bd20d464394bd3724a9fea91a`, instancia LocalDB `PilotGoVisualQA_20261111`.
- Respaldo: `artifacts/saas-superadmin-20261112/qa-before-saas.bak`.
- SHA-256: `EE2C455FBAE8F19B0DE92DDCEF57FCBA52FE7A46A5D6BF234777526D86F61DA8`.
- Antes/después del esquema: 17 órdenes y 35 códigos; hashes idénticos de IDs, empresas, comercios, pilotos, estados, fechas operativas y códigos/uso/reserva.
- La primera aplicación SQL falló por opciones de índices filtrados; se confirmó rollback completo y luego se aplicó con las opciones correctas. No se modificaron registros operativos.

Esta base QA histórica se creó con `EnsureCreated` y carece de historial EF. Su script exclusivo `qa-add-saas-schema.sql` agrega las tablas sin inventar migraciones anteriores. **Ese script no es para producción.** El diagnóstico avisa «Revisar migraciones» por esta condición conocida. La migración normal desde el esquema anterior se comprobó en una base SQL nueva con historial EF real.

## Evidencia de QA

Directorio de recibos: `artifacts/saas-superadmin-20261112/`.

| Validación | Resultado | Evidencia |
| --- | --- | --- |
| Regresión completa SQL Server, base nueva por caso | 221/221; 0 fallos, 0 omitidas | `saas-regresion-aprobacion-sql.trx`, `regresion-final.log` |
| Finanzas: estados, pagos parciales, duplicados, carreras, anulaciones, cuotas y aislamiento | Incluidas en la regresión; lote previo 21/21 | `saas-billing-sql-final.trx` |
| Autorización: roles, perfil, CSRF, API, solicitud de tenant y revocación | Incluidas; lote previo 15/15 | `saas-auth-panel-sql.trx` conserva el primer lote 44/45 y el fallo corregido de cierre sólo con C |
| Migración y rollback con datos protegidos | 7/7; incluidas en la regresión final | `saas-rollback-sql-final.trx` |
| Flutter | 93/93; sin cambios nuevos de código móvil en este lote | Ejecución `flutter test`, sesión de herramienta 21932, salida final «All tests passed!» |
| Publicación QA Release | Correcta | `qa-publish-final.log` |
| SQL de la prueba visual y conexión del emulador | PASS | `qa-flow-final.json`, `emulator-connection-final.json` |

El primer intento de regresión paralela tuvo un error de conexión al preparar bases LocalDB. Se serializaron las clases del arnés; las carreras internas de negocio conservan `Task.WhenAll`. La siguiente tanda dio 217/218: un test de downgrade intentaba usar el servicio actual antes de instalar las tablas SaaS. Se ajustó su esquema inicial y se conservaron sus comprobaciones de datos y de la migración protegida de retornos. La ejecución final completa dio 221/221. Se conservan los recibos anteriores.

### Recorrido visual real

Capturas `01-` a `20-` en el directorio de recibos. Navegador de escritorio 1440 px y móvil 406 px; sin desbordamiento horizontal del documento en las vistas comprobadas. Se restauró el tamaño por defecto al terminar.

1. Alta de empresa ficticia `SaaS QA visual 20261112` (#3), separada de las empresas operativas A/B.
2. Factura ficticia #1, DOP 1.000, vencida sin pagos; abono 400 → saldo 600, aún vencida; segundo pago 600 → pagada, saldo cero y cobertura hasta 11/12/2026.
3. Creación del plan ficticio «QA Crecimiento» y mejora de la empresa #3: 50 comercios, 100 pilotos, 10.000 órdenes/mes. Factura anterior e importe conservados.
4. Solicitud del gestor A recibida y resuelta como rechazada de QA, conservando su plan operativo. Historial y decisiones visibles; empresas A/B siguen en su plan anterior.
5. Auditoría con cambio anterior/nuevo; accesos globales y protección de SuperAdmin; filtros de facturación y menú móvil.
6. Icono de perfil del gestor → pantalla 403 «No tienes autorización».
7. App QA del emulador conserva sesión y cuatro órdenes activas (#0013, #0012, #0009, #0010); vistas de Ruta y Entregas abiertas con el servidor actualizado. No se recogieron, entregaron ni cancelaron esas órdenes durante este lote.

Los importes, referencias y cuentas anteriores son ficticios de QA; no son cobros de clientes reales. Las asignaciones automáticas/manuales, aislamiento, rutas y entrega B/C están cubiertos por la regresión SQL y Flutter. La captura del emulador no sustituye un recorrido físico con GPS real.

## Estado local y pase de producción

- Panel y app QA usan `http://127.0.0.1:5019`, la misma base preservada y `adb reverse tcp:5019 tcp:5019`.
- Host: `artifacts/envirapido-qa-saas-final-host/Envirapido.Qa.dll`, modo adjunto sin seed ni migraciones al arrancar. No publicar ese arnés.
- App instalada: Envirapido QA `2.0.0+8`, paquete `com.pilotgo.app.qa`; APK x64 de emulador. Este lote no crea un APK de distribución ni modifica la API pública.
- El checkout sigue en `codex/confiabilidad-panel-app`, con los cambios previos conservados y sin commit/push.

**Pendientes para autorizar el pase real:**

1. **Proyecto actualizado posteriormente:** [recibo .NET 10](NET10-ACTUALIZACION-20261112.md), runtime local 10.0.15, SQL 231/231, Flutter 93/93 y recorrido del emulador completado. Queda instalar/verificar el runtime compatible en el alojamiento real y validar allí el pase. El diagnóstico histórico de este recibo era `net8.0`/`8.0.25`, fuera de soporte desde el 10/11/2026. Fuentes: [política oficial](https://dotnet.microsoft.com/en-us/platform/support/policy), [anuncio de fin de soporte](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).
2. Validar en el servidor de destino su configuración real, HTTPS, claves privadas persistentes, correo de recuperación y respaldo/restauración. El QA local tiene correo sin configurar; no se ha comprobado el SMTP de producción.
3. Revisar el historial EF y los datos reales, respaldarlos y aplicar la migración normal mediante un procedimiento de despliegue controlado; no usar el script especial de la base QA heredada. Con registros SaaS, el rollback exige conservar la base y ejecutar una versión compatible o restaurar un respaldo revisado.
4. Comprobar con el servidor actualizado el flujo público comercio → CRM → asignación automática/manual → piloto → B → C. Este informe acredita QA local, no despliegue ni prueba física de producción.

La administración SaaS implementada funciona en el entorno QA verificado. El pase de producción no está aprobado todavía.
