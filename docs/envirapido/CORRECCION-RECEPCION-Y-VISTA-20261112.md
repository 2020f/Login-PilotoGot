# Corrección de recepción y vista deslizante de la app

12 de noviembre de 2026 · Validación local con SQL Server y Android `emulator-5554`.

Se corrigió el entorno QA que dejamos conectado a una base distinta de la que el usuario utilizaba en el navegador, y se restauró la prioridad visual del resumen de entrega. No se hizo push, despliegue de producción ni instalación en el Samsung. Este recibo complementa la [corrección anterior de los 23 hallazgos](CORRECCION-23-HALLAZGOS-20261112.md); conserva su evidencia histórica y sus límites.

## Causa y entorno recuperado

El navegador creaba y asignaba pedidos en el host **5019**, base `PilotGoTest_4612d89bd20d464394bd3724a9fea91a`. Durante nuestra validación anterior dejamos el puerto 5019 del emulador redirigido al host **5021**, base `PilotGoTest_549aabba9bdc4061bf017dcf5e73b530`. Por eso las órdenes del navegador no aparecían en esa app. Fue un error de conexión del entorno local que dejamos preparado.

Se unificó la conexión en **emulador 5019 → host 5019**. El host QA se actualizó sobre la base original mediante conexión a datos existentes: sin seed, sin ejecutar migraciones y sin borrar la base al cerrar el arnés. El proceso registrado al reiniciar fue PID 21588. Un respaldo `COPY_ONLY` con `CHECKSUM` pasó `RESTORE VERIFYONLY`; las once filas de pedidos comparadas antes y después del reinicio se conservaron idénticas.

- [Entorno original y filas conservadas](../../artifacts/regresion-recepcion-20261112/entorno-usuario-restaurado.json).
- [Respaldo verificado](../../artifacts/regresion-recepcion-20261112/respaldo-5019.json).
- [Filas antes](../../artifacts/regresion-recepcion-20261112/pedidos-antes-reinicio.json) y [después del reinicio](../../artifacts/regresion-recepcion-20261112/pedidos-despues-reinicio.json).
- [Conexión final comprobada](../../artifacts/regresion-recepcion-20261112/conexion-guardada.json).

El helper [Connect-QaEmulator.ps1](../../tools/Envirapido.Qa/Connect-QaEmulator.ps1) verifica entorno QA, base, puerto, paquete y emulador antes de conectar. La configuración queda limitada a `com.pilotgo.app.qa` en `emulator-5554`.

## Vista restaurada y regresiones cubiertas

El resumen original **#orden, comercio o destinatario, Ver entrega y disponibilidad** vuelve a aparecer primero en la tarjeta deslizante. Se conservan `BottomSheetBase`, tema, asa, radios, alturas de arrastre y el menú inferior solicitado. Las paradas de ruta se consultan debajo mediante una expansión compacta; se mantienen selección de ruta, llegadas, incidencias y reordenamiento según permisos.

Cuando hay una ruta operativa, la acción principal abre su próxima orden y parada autorizadas. Una ruta planificada no oculta una orden manual ajena ni le transfiere su parada; las demás entregas activas siguen accesibles. El banner superior GPS se reserva para errores, pausa o posición vencida; el estado saludable continúa disponible en Perfil. Se corrigió el texto obsoleto que pedía ubicación «Siempre».

La primera suite completa, de 88 casos, detectó que el ajuste visual ocultaba el aviso de detención tras un 403 GPS tardío. Se corrigió esa condición **sin relajar el aserto existente**. La suite final, con la regresión adicional de ruta planificada más orden manual ajena, aprobó 89/89.

## Comprobación real de recepción

En la misma base del navegador, Piloto A2 recuperó sus tres órdenes activas: IDs 3, 4 y 9, visibles como 0003, 0004 y 0008. Se abrió la 0008 manual y el botón principal de Home abrió la próxima recogida 0003 de R1. Piloto A2 no recibió ofertas automáticas por la regla existente de piloto ocupado; esa regla se conservó.

Piloto A inició sesión una vez. La oferta automática 0009, ID 10, apareció en la app sin aceptarla ni rechazarla. Después, el CRM asignó manualmente 0010, ID 11, a ese mismo piloto y la app la mostró sin otro login. La [lectura API de recepción](../../artifacts/regresion-recepcion-20261112/recepcion-api-final.json) es anterior a esa última asignación manual: acredita la cola y el aislamiento, no el estado posterior de 0010.

No se consumieron códigos B/C ni se cancelaron o completaron pedidos en este recorrido. Se dejaron 0010 asignada a Piloto A, 0008 a Piloto A2 y 0009 sin aceptar.

La [lectura SQL posterior a la asignación](../../artifacts/regresion-recepcion-20261112/estado-final-recepcion.json) aprobó **7/7 controles**: once pedidos conservados, una sola asignación de 0010, B/C sin consumo, 0009 disponible, 0008 con A2 y el pedido de la otra empresa intacto. El [script de lectura](../../artifacts/regresion-recepcion-20261112/Read-FinalReception.ps1) no modifica datos. La continuidad sin otro login fue observada en la app; no se deduce sólo del registro SQL.

| Comprobación visual | Evidencia |
| --- | --- |
| Resumen original restaurado | [app7-resumen-original-restaurado.png](../../artifacts/regresion-recepcion-20261112/visual/app7-resumen-original-restaurado.png) |
| Acción principal abre la próxima recogida | [app7-ver-entrega-proxima-recogida.png](../../artifacts/regresion-recepcion-20261112/visual/app7-ver-entrega-proxima-recogida.png) |
| Orden 0008 manual abierta | [app7-manual-0008-abierta.png](../../artifacts/regresion-recepcion-20261112/visual/app7-manual-0008-abierta.png) |
| Oferta automática 0009 recibida | [app7-oferta-automatica-0009-recibida.png](../../artifacts/regresion-recepcion-20261112/visual/app7-oferta-automatica-0009-recibida.png) |
| Asignación manual 0010 confirmada en CRM | [crm-5019-manual-0010-confirmada.png](../../artifacts/regresion-recepcion-20261112/visual/crm-5019-manual-0010-confirmada.png) |
| Orden manual 0010 recibida en la misma sesión | [app7-manual-0010-recibida-misma-sesion.png](../../artifacts/regresion-recepcion-20261112/visual/app7-manual-0010-recibida-misma-sesion.png) |

## Resultado y límites

- **SQL Server: 163/163**, cero fallidas u omitidas: [TRX final](../../artifacts/regresion-recepcion-20261112/sql/regresion-sqlserver-final.trx) y [log](../../artifacts/regresion-recepcion-20261112/sql-suite.log).
- **Flutter: 89/89**, con [suite final](../../artifacts/regresion-recepcion-20261112/app/flutter-suite-final.log) y [análisis completo limpio](../../artifacts/regresion-recepcion-20261112/app/analyze-final.log). Los originales de la ejecución se conservan en `C:\Users\Fernelis\Desktop\pilotGOapp\build\envirapido-qa\regresion-recepcion-20261112`.
- **APK QA 2.0.0+7, x64**, instalado sólo en `emulator-5554`. SHA-256: `866672f790492e6c637c8f46f1cdf070ae9689ad9cd32538687fb38a1b2eee79`.

La [verificación del APK](../../artifacts/regresion-recepcion-20261112/app/Envirapido-2.0.0-7-qa-verificacion.json) conserva versión, firma, arquitectura y API. El [índice de evidencia](../../artifacts/regresion-recepcion-20261112/verification.json) identifica las fuentes locales y los recibos de esta corrección. La auditoría y los manifiestos del APK QA6 anterior se conservan como antecedentes, sin atribuirles la restauración de esta sesión.

Las pruebas cubren recuperación de recepción, mismo contexto de datos, aislamiento multitenant, acción principal y vista deslizante en el entorno local descrito. No constituyen distribución del APK, prueba física del Samsung ni validación de producción. SMTP real y los demás límites de la auditoría anterior siguen separados de esta corrección.
