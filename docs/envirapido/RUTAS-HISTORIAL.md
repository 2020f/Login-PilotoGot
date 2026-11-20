# Rutas finalizadas e historial móvil

12 de noviembre de 2026. Decisión del propietario: «Ocultar finalizadas de Ruta y conservar historial».

## Comportamiento

La app mostraba todas las rutas del snapshot en su espacio de trabajo, incluida una ruta finalizada. Al tocar sus paradas intentaba abrir el detalle activo de una orden ya terminada y mostraba «Actividad ya no activa».

La versión QA 2.0.0+5 separa ambos usos:

- **Ruta** muestra rutas planificadas, recogiendo, en ruta o en retorno. Al finalizar o cancelar una ruta, selecciona otra vigente o vuelve a la vista libre.
- Las rutas planificadas esperan el inicio autorizado desde CRM para habilitar acciones. Los puntos de órdenes cerradas dejan de abrir trabajo activo. Una recogida B completada permite continuar con sus C pendientes cuando la orden sigue activa.
- **Entregas → Historial → Rutas** conserva la consulta reciente de rutas finalizadas y canceladas, con sus puntos y resultados. Esa consulta no ejecuta acciones operativas ni abre pedidos liberados o reasignados.
- El historial paginado de pedidos conserva su detalle histórico autorizado. Los destinos protegidos siguen protegidos; la fecha de ruta no se presenta como fecha de entrega.

En el CRM se consulta en **Rutas y zonas → Historial**. Una ruta finalizada con todas sus entregas resueltas ya está cerrada y conserva su auditoría. No se requiere una limpieza administrativa de la base.

El historial móvil de rutas utiliza la consulta reciente del snapshot existente. Los registros completos permanecen en la base; la consulta de pedidos conserva su paginación.

## Verificación

- Análisis Flutter sin incidencias y **68/68 pruebas aprobadas**, incluidas cinco regresiones nuevas: cierre de última ruta, cambio de selección, permisos de puntos, historial protegido y respuesta tardía de otra cuenta.
- Revisión independiente de los filtros de mapa, tarjetas, selección y accesos históricos sin fallos bloqueantes detectados.
- Los 331 archivos fuente del servidor coinciden con el manifiesto de su validación anterior; el servidor y sus datos no se modificaron en este ajuste.
- APK x64 2.0.0+5 instalado con actualización sobre la QA anterior, conservando sesión y datos. El mapa carga y «Ruta» queda libre, sin R-002 finalizada ni sus marcadores de trabajo.
- En el emulador, **Entregas → Historial → Rutas** conserva R-002 y muestra recogidas 1/1 y entregas 1/1. Al tocar una parada terminada permanece la consulta histórica, sin abrir «Actividad ya no activa».
- Los pedidos #0006 y #0007 siguen visibles en el historial del piloto. El CRM conserva la ruta en **Rutas y zonas → Historial**.
- Capturas y recibo: [evidencia local](../../artifacts/envirapido-rutas-historial-20261112/VERIFICACION.json). El APK ARM64 también está preparado y verificado; no se instaló en un teléfono.

El alcance es QA local. No se realizó push, despliegue en producción ni instalación en el teléfono físico.
