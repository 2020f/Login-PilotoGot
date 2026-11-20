# Envirapido: implementación y validación local

Estado de trabajo del 12 de noviembre de 2026 (República Dominicana). **Implementación local y cierre de QA; no es una autorización de despliegue ni una versión distribuida.** No se han realizado commits, push, cambios en GitHub ni migraciones sobre la base de producción en esta etapa.

## Alcance implementado en fuentes

- Una orden conserva su identidad y recogida B; admite varios destinatarios con parada e identificación permanentes y un C individual. La entrega utiliza solamente C. Las órdenes antiguas conservan los códigos emitidos.
- Rutas con órdenes de una empresa, precedencia de recogidas, reserva de órdenes, secuencia autorizada, llegada, cancelación previa a recogida y cierre por resultados reales.
- Programación futura separada de pedidos listos sin asignar. No se aceptan, asignan ni recogen envíos antes de habilitarse.
- Incidencias con política configurable por categoría, pausa de parada o ruta, notas, atención, responsable, escalación y resolución. Resolver no confirma C. La detección programada de demoras utiliza compromisos registrados.
- Solicitud, autorización y trazabilidad de reintentos o devolución al comercio original. Devuelta y finalizada con devoluciones son resultados distintos de entregada.
- Liquidaciones de pilotos y comercios, reglas por servicio/ruta/parada, bases históricas congeladas y protección contra doble remuneración. Aprobar/programar/registrar un pago externo no ejecuta transferencias.
- El efectivo declarado por el piloto se verifica y recibe por la empresa. No se descuenta automáticamente de sus ganancias.
- Centro de operaciones, detalle, pedidos, zonas/rutas, incidencias, liquidaciones y vistas derivadas en Razor. Marca Envirapido y navegación móvil Ruta/Entregas/Ganancias/Perfil conservando el diseño existente de Flutter.
- Seguimiento Android con servicio de ubicación, autorización temporal, cola cifrada y contexto de cuenta. La validación nativa continúa; no se ha demostrado todavía continuidad física en Samsung.

Las fotografías, firmas y comprobantes de entrega permanecen atenuados y no funcionales, como solicitó el propietario.

## Protección del ámbito

El servidor deriva empresa y piloto de la identidad autenticada, revalida acceso y estado activo, aplica restricciones de relaciones por empresa y comprueba de nuevo cada comando bajo transacción. El CRM pertenece al gestor principal de cada empresa; el SuperAdmin conserva su administración separada. Los datos privados, las notas CRM y la geometría del destinatario se protegen antes de recoger B y ante reasignaciones.

Las escrituras conservan claves de operación y resultados. Una respuesta incierta conserva el intento; un rechazo con rollback confirmado lo identifica explícitamente. Las revisiones de rutas, zonas, casos y liquidaciones impiden sobrescribir silenciosamente ediciones concurrentes.

## Evidencia obtenida

| Comprobación | Resultado y alcance |
| --- | --- |
| Suite completa SQL Server | **122/122 aprobadas, sin omitidas**. Incluye ruta de incidencia, separación de claves, migraciones y rollback protegido. Bases GUID aisladas, sin producción. TRX `envirapido-cierre-sqlserver.trx`. |
| Suite completa SQLite | **117 aprobadas, 5 omitidas específicas de SQL Server, total 122**, cero fallos. TRX `envirapido-cierre-sqlite.trx`. Las cinco omitidas sí se ejecutaron en SQL Server. |
| Indicadores y regresiones SQL/SQLite | 4/4 por proveedor; incluye 1.005 incidencias, 507 liquidaciones, decimales, fechas RD y aislamiento por piloto/empresa. |
| Migraciones reales | Backfill de parada única sin cambiar códigos; conversión de incidencia heredada conservando pausa y advertencias; downgrade que perdería datos rechazado. |
| ETA | 10/10 por proveedor; recorridos por tramos, progreso, precedencia, caché compartida y protección de datos antes de B. Sin llamada real a Routes API en QA. |
| Flutter | **68/68 y análisis limpio** tras el ajuste de rutas e historial. APK QA x64 y ARM64 2.0.0+5 con API loopback y firma QA; x64 actualizado y validado en el emulador conservando sesión y datos. ARM64 no instalado en teléfono. |
| Navegador con SQL Server | Detalle multiparada en sus cinco pestañas; rutas creadas/iniciadas/canceladas, zonas, incidencias con responsable y resolución, caja declarada/conciliada/recibida, liquidaciones separadas, vista adaptable y aislamiento de empresa B. |
| Emulador | Dos ofertas automáticas consecutivas y una ruta manual en la misma sesión; B, C por destinatario, rechazo del C de otra parada, incidencia con pausa/resolución, devolución autorizada y recibida en origen. Login con contraseña recordada y cash recibido visible. |
| GPS nativo | Muestras simuladas recibidas con pantalla bloqueada y servicio foreground; seguimiento detenido al terminar la última actividad y contador SQL estable. No prueba física Samsung. |

Los resultados pertenecen a los árboles que los ejecutaron; los cambios posteriores no heredan automáticamente esas aprobaciones.

El recorrido y sus capturas están documentados en [RECORRIDO.md](../../artifacts/envirapido-visual-qa-20261112/RECORRIDO.md). Distingue las pruebas del APK anterior a los últimos rótulos de las comprobaciones finales posteriores.

El [recibo del recorrido inicial 2.0.0+4](../../artifacts/envirapido-visual-qa-20261112/VERIFICACION-FINAL.json) vincula sus pruebas, 331 archivos fuente del servidor y ambos APK de esa fase. En el host recompilado y ese APK se repitieron conservación de sesión al actualizar, contraseña recordada después de salir, ruta manual, recogida B, cierre únicamente con C, incidencia con ruta y zona inferidas, cierre de ruta y detención del seguimiento. El ajuste JavaScript para que registrar una incidencia no reabra su formulario se verificó por sintaxis y guardado real con SQL.

El [ajuste posterior 2.0.0+5 de rutas e historial](RUTAS-HISTORIAL.md) oculta las rutas finalizadas o canceladas del trabajo móvil y conserva su consulta histórica. Se verificó en el emulador con la misma sesión y datos. La validación SQL anterior se conserva como evidencia del servidor: sus 331 archivos no cambiaron en este ajuste y no se repitió la suite SQL.

La inspección posterior de la captura detectó un rótulo adicional: el aviso contaba incidencias pero decía órdenes. Se corrigió a «incidencia(s) requiere(n) atención» y la compilación Razor aprobó sin advertencias ni errores. No cambia contadores ni comandos. El host abierto y `panel-final-operaciones.png` conservan el texto anterior; la compilación actualizada está en `artifacts/envirapido-qa-etiqueta-final`. No se reinició ese host para conservar la sesión y los fixtures del recorrido.

## Cierre y límites

- El HTTP 500 multiparada, la repetición del aviso de precisión y la recreación del mapa durante refrescos quedaron corregidos. El último lote GPS no puede reactivar seguimiento revocado; el cierre sin actividad se presenta de forma neutral y conserva de manera segura la cola incierta.
- La incidencia desde una orden captura automáticamente su ruta vigente activa, del mismo tenant y custodio; no enlaza rutas históricas o planificadas. Repetir una operación conserva el contexto original.
- La clave del servidor de mapas ya no se utiliza como fallback para el navegador. Los valores actuales no se rotaron; para producción deben configurarse claves de servidor y navegador separadas y sus restricciones reales. Las claves y la facturación de Routes no se verificaron contra Google en esta revisión.
- La prueba física de Samsung sigue pendiente de autorización específica para ubicación real. También quedan fuera de la evidencia la navegación física y la recuperación ante cortes reales de red; las pruebas de cola/reintento automatizadas no sustituyen esos ensayos.
- Los APK de QA usan paquete separado y API local. No son una distribución para los pilotos ni una actualización del servidor publicado.

Antes de habilitarlo en producción: respaldar la base, aplicar las dos migraciones revisadas antes de arrancar la nueva API, conservar la configuración JWT real, configurar mapas correctamente y distribuir la app compatible con contrato 3 antes de asignar rutas o pedidos multiparada. Las apps antiguas no reciben trabajo que no pueden ejecutar. El proyecto conserva su runtime actual; la revisión de soporte y actualización de .NET debe tratarse aparte del recorrido funcional. No se ha comprobado la configuración del servidor remoto.

El arnés reproducible está en [tools/Envirapido.Qa](../../tools/Envirapido.Qa/README.md) y escucha únicamente en loopback con cuentas ficticias. El API público original permanece `https://pilotogoapp.zapflow77.dpdns.org`; los APK QA utilizan exclusivamente `http://127.0.0.1:5019`.
