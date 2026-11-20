# Retiros desde incidencias y reasignación por paradas

Estado: implementado y validado localmente; publicación de fuentes y preparación
de APK autorizadas para la entrega 2.1.1. Despliegue y QA pública pendientes.

Guía de entrega: [DESPLEGAR-ENVIRAPIDO-2.1.1.md](../../DESPLEGAR-ENVIRAPIDO-2.1.1.md).

QA final: SQL Server 371/371 (incluye 25 regresiones nuevas), Flutter 129/129 y analizador sin problemas. Recorrido visual en CRM y emulador con datos ficticios. Las últimas dos ramas UX del móvil tienen cuatro regresiones widget; el informe identifica por separado el APK de la ejecución nativa y el build de cierre.

## Operación del gestor

1. En **Incidencias**, abrir el caso. La ficha muestra **Gestión del paquete**: retirar del piloto, reasignar o revisar la solicitud pendiente, según su estado.
2. **Retirar del piloto** permite elegir las paradas afectadas y exige un motivo. No requiere B/C y no registra una entrega al cliente. El retiro que aplica directamente el gestor es inmediato; la solicitud del piloto exige aprobación.
3. Las entregas retiradas aparecen en **Pedidos → Reasignación y cancelación → Por reasignar**. Desde esa bandeja se pueden asignar a otro piloto, programar su retorno o cancelarlas.
4. Si el paquete ya fue recogido, el piloto anterior conserva la custodia hasta la recepción física. El nuevo piloto confirma desde **Traspasos y recepciones**. Para un centro o comercio, confirma el personal autorizado. La aprobación administrativa nunca sustituye la recepción física.
5. La cancelación respeta la custodia: no permite dar por desaparecido un paquete en poder de un piloto. Si corresponde, primero se registra la devolución y recepción.
6. Las devoluciones antiguas abiertas requieren la acción explícita **Pasar devoluciones a reasignación**. Se confirma el conjunto completo de casos pendientes y sus versiones. Se conserva el historial, con estado `TrasladadaAReasignacion`, y se liberan sólo las membresías de ruta afectadas.

También hay accesos desde el listado y el detalle de pedidos. La bandeja incluye órdenes asignadas normales, además de las que ya están pendientes de reasignar.

## App del piloto

En **Reportar incidencia** se puede elegir **Solo reportar** o **Solicitar retiro**. La segunda opción registra una incidencia y las solicitudes de las paradas elegidas en una sola operación idempotente; no crea primero un caso separado que pueda quedar sin gestión.

El panel deslizante se conserva. Las solicitudes existentes se pueden recuperar con el mismo intento y payload, incluso tras un error de red o una respuesta incierta. Los nuevos intentos enlazados requieren la capacidad de servidor `retiroVinculadoAIncidencia`; un servidor anterior provoca un aviso de actualización en lugar de ignorar silenciosamente los campos nuevos.

Una gestión histórica cerrada o rechazada no impide solicitar otro retiro cuando el piloto aún tiene una parada activa. Las gestiones abiertas relacionadas se muestran antes de crear otra solicitud. Al recuperar un intento se presenta la incidencia original del payload conservado, aunque se haya abierto desde otra ficha; no se cambian sus datos ni su identificador de operación.

Las revisiones ausentes de órdenes clásicas se admiten únicamente antes de convertir la orden a gestión por paradas, comprobando la revisión vacía almacenada. Después de la conversión se exige coincidencia exacta. No se modifica la exigencia de revisión en la recogida gestionada.

## Reglas y aislamiento

- Los comandos, consultas, incidencias, destinatarios de traspaso y versiones se validan dentro de la misma empresa. Los GET de contexto no modifican asignaciones.
- Los cambios se ejecutan con bloqueo transaccional por empresa e idempotencia por actor y operación. Se conservan los hashes de solicitudes anteriores sin los nuevos campos opcionales.
- La actividad del retiro no resuelve una incidencia ni levanta sus pausas. Se conserva la ruta donde se originó la incidencia, aun si se retiran sus membresías.
- Con política `PausarParada`, varias paradas requieren solicitudes separadas. Con `PausarRuta`, una selección que abarque varias rutas requiere dividirse por ruta. Se evita aplicar una pausa parcial o más amplia de lo solicitado.
- Los códigos B/C conservan su significado. Retirar, aprobar, reasignar y recibir un traspaso no consumen C ni confirman entrega al destinatario.
- El efectivo sigue bajo el responsable que lo cobró. El trabajo previo del piloto se ajusta por decisión del gestor mediante el mecanismo existente.

## Entrega futura al servidor

Este lote no añade migraciones: reutiliza las tablas existentes de incidencias, devoluciones y retiros. El nuevo estado de devolución se añade como valor 7 a su enumeración, sin cambiar los valores anteriores.

La publicación de fuentes está autorizada y el APK release 2.1.1 (11) ya está preparado con la firma definitiva y `https://envirapido.com`. Desplegar primero el backend y sus vistas/JavaScript juntos; verificar la capacidad autenticada del piloto y los accesos del gestor. Después completar la QA pública de ese APK antes de publicarlo en el canal protegido. El APK público anterior no incorpora las nuevas opciones móviles. Conservar las configuraciones de HTTPS, proxy, SQL Server, portal separado y almacenamiento privado del servidor.

No ejecutar el seed de QA ni sus cuentas de prueba en producción. El modo `--retiros` de la herramienta QA sólo crea escenarios en una base efímera nueva de SQL Server local; rechaza su uso con `--database`.

La evidencia y el resultado final de QA se registran en `artifacts/retiros-integrados-20261119/INFORME.md` dentro del entorno local. No se deben publicar credenciales, códigos de prueba ni bases locales.
