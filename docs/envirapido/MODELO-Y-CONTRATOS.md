# Envirapido — modelo de dominio y contratos

Fecha de revisión: 11 de noviembre de 2026. Base examinada: backend
`36d15c7696f1b0a8018251cbcdd6be9897ae7248`, rama
`codex/confiabilidad-panel-app`. Se leyó `CLAUDE.md` y el código real. Este
documento consolida las seis referencias y sus aclaraciones; no modifica código,
base de datos, migraciones ni contratos publicados. No se ejecutaron pruebas.

**Confirmado** identifica una decisión del usuario. **Propuesta** describe una
forma técnica de cumplirla, pendiente de revisión antes de programar.
**Pendiente** evita convertir una pregunta de negocio en una regla implícita.
Los nombres de entidades/DTO nuevos son de trabajo; no fijan dependencias,
proveedor, prefijos de códigos ni versión numérica de la próxima API.

## 1. Decisiones que gobiernan el modelo

| Tema | Confirmado |
| --- | --- |
| Producto | Nombre visible Envirapido; CRM operativo por empresa y SuperAdmin separado. |
| Flujo | El comercio crea pedidos; el CRM permite despacho automático o manual; el piloto ejecuta el servicio. |
| Simple y escalas | Una orden tiene uno o varios destinatarios/paradas de entrega, a cargo del mismo piloto, sin transferencias de custodia. |
| Ruta | Agrupa una o varias órdenes de la misma empresa, con un mismo piloto al ejecutarse. Una ruta puede planificarse sin piloto. |
| Códigos | Se conserva A/B/C: A identifica visualmente la orden, B confirma recogida y C confirma exclusivamente la entrega de su parada. El cierre final pide solo C del destinatario. |
| Progreso | Cerrar una parada no puede completar prematuramente toda la orden ni toda la ruta. |
| Reordenamiento | Solo con permisos y reglas autorizados; no implica saltar paradas ni omitir recogidas. |
| Incidencias | El piloto puede reportarlas. Su atención tiene estado independiente; reglas configurables por tipo permiten continuar, pausar una parada o pausar una ruta. Resolver no sustituye C. |
| Evidencias | Foto, firma y comprobante de entrega quedan opacos y no funcionales. No habrá subida, almacenamiento ni obligación de evidencia en este alcance. |
| Liquidaciones | Cada tenant registra y concilia pagos realizados fuera del sistema. No ejecuta transferencias. Remuneración configurable por servicio, ruta o parada, con origen/regla conservados y sin duplicados. |
| App | Ganancias consulta datos propios. GPS continuo durante actividad, incluso con pantalla bloqueada o Waze abierto, está aprobado; su realización técnica móvil se documenta aparte. |
| Vistas derivadas | Se autorizan las pantallas adicionales necesarias para esos flujos, manteniendo el estilo de las referencias. |

Pendientes: significado distinto de Pendiente/Por asignar; Recogido frente a En
ruta; saltos, entregas parciales, retornos, cancelaciones, SLAs, compensaciones,
programación y cuota mensual. Tampoco se ha definido el modelo comercial completo
de envíos entre provincias. Ninguna tabla posterior resuelve esos pendientes.

## 2. Qué existe y qué necesita ampliarse

| Área | Backend actual comprobado | Objetivo y diferencia |
| --- | --- | --- |
| Orden | Un comercio, un destinatario, un piloto nullable y snapshots de recogida/destino. | Una orden simple o con varias entregas, conservando su identidad. |
| Estados | Creada=1, Asignada=2, Recolectada=3, Entregada=4, Incidente=5. | Separar avance de parada/orden/ruta y atención de incidencias; decidir transiciones nuevas. |
| Códigos | Un B y un C por orden; reservas activas por tenant/tipo; cuatro dígitos nuevos. | Un B de recogida por orden y C asociado a cada parada. |
| Creación | Operación idempotente tenant/actor/tipo/clave con hash del payload. | Incluir todas las paradas y campos relevantes sin perder recuperación de reintentos. |
| Despacho | Manual por orden Creada; automática con una automática activa por piloto. | Ruta planificada/asignada y acciones de lote compatibles con las reglas existentes. |
| Geografía | Puntos de comercio/destino/piloto; distancia y precio base/km. | Zonas, puntos de ruta, planificación, recorrido, ETA y capacidad operativa. |
| GPS | Última posición/hora del piloto, sobrescrita por cada reporte. | Actividad de seguimiento, historial autorizado, recepción recuperable y antigüedad visible. |
| Incidencias | Enum y contadores; no módulo de atención. | Categorías, responsable, plazo, escalación, notas, resolución y efecto operativo configurable. |
| Finanzas | Precio guardado del envío; tarifa base/km del tenant. | Devengos, efectivo, conciliación, liquidaciones y registro de pagos externos. |

Fuentes: [OrdenEntrega.cs:15](../../Login/Domain/Entities/OrdenEntrega.cs#L15),
[Enums.cs:10](../../Login/Domain/Enums/Enums.cs#L10),
[restricciones actuales:54](../../Login/Data/ApplicationDbContext.cs#L54),
[creación:30](../../Login/Application/Services/OrdenService.cs#L30),
[automática:430](../../Login/Application/Services/OrdenService.cs#L430),
[GPS:139](../../Login/Application/Services/PilotoAppService.cs#L139) y
[distancia:61](../../Login/Application/Services/GoogleMapsService.cs#L61).

No renumerar los enums existentes: el índice filtrado de A utiliza actualmente
`Estado <> 4`. Las reglas de baja consideran abierta toda orden no Entregada.
Agregar Cancelada u otros terminales exige revisar ambos contratos y todos sus
consumidores. No basta añadir un nombre al enum.
[Índice:59](../../Login/Data/ApplicationDbContext.cs#L59),
[bajas:95](../../Login/Application/Services/QuotaPolicyService.cs#L95).

## 3. Entidades y pertenencia propuestas

`ClienteAppId` seguirá siendo el tenant. Conservar los IDs actuales de orden,
comercio, piloto y códigos; introducir IDs permanentes de parada/ruta/punto.
Los IDs operativos y financieros nunca se derivarán de A/B/C ni del orden visual.

| Entidad propuesta | Responsabilidad y claves relevantes |
| --- | --- |
| OrdenEntrega existente | Cabecera, comercio, recogida congelada, A/B, modo, piloto y estado agregado. Clave alternativa `(ClienteAppId, OrdenId)`. |
| ParadaEntrega | Destinatario y dirección/contactos/coordenadas congelados, instrucciones, estado e hitos, posición lógica y C. Clave `(ClienteAppId, OrdenId, ParadaId)`. |
| Ruta | Fecha, planificación, piloto nullable mientras no se ejecute, versión, estado y métricas. Clave alternativa `(ClienteAppId, RutaId)`. |
| RutaOrden | Pertenencia de una orden completa a una ruta y su historial. FKs compuestas hacia ruta y orden dentro del tenant. |
| PuntoRuta | Posición del recorrido: recogida de una orden o entrega de una parada. Referencia RutaOrden y, si es entrega, su ParadaEntrega. |
| Zona / versión de zona | Polígono, región, horario, capacidad y reglas versionadas. La unidad de capacidad y el efecto de editar límites quedan pendientes. |
| ActividadGPS / posición | Piloto, actividad/versión, dispositivo, secuencia, hora capturada/recibida y punto. No confundir coordenada con evento de entrega. |
| Incidencia / actividad | Objeto afectado, categoría, prioridad, responsable, política aplicada, estado de atención, efecto y resolución. |
| ReglaRemuneracion / movimiento / liquidación / pago externo | Reglas versionadas, origen económico, consumo de movimientos, aprobación, conciliación y registro de pago. |

### 3.1 Integridad de pertenencia

Propuesta: respaldar los filtros de aplicación con claves alternativas y FKs
compuestas. Ejemplos de relaciones que deben ser imposibles entre empresas:

- `(tenant, TiendaId)` de orden a comercio y `(tenant, PilotoId)` a piloto.
- `(tenant, OrdenId)` de parada a orden.
- `(tenant, RutaId)` y `(tenant, OrdenId)` de RutaOrden a sus padres.
- `(tenant, RutaId, OrdenId)` de PuntoRuta a RutaOrden; además
  `(tenant, OrdenId, ParadaId)` cuando el punto sea entrega.
- `(tenant, OrdenId)` de cualquier código a orden; C también debe referenciar
  `(tenant, OrdenId, ParadaId)` de su destinatario concreto.
- Incidencias, movimientos, liquidaciones y pagos deben referenciar sus padres
  usando también tenant, incluyendo responsable/beneficiario autorizado.

La agenda actual `UsuarioFinal` pertenece a Tienda y no lleva tenant directo.
Una FK propuesta no puede apuntar a columnas inexistentes: habrá que elegir
backfill validado de tenant o claves por tienda, manteniendo coherencia
`orden → tienda → destinatario`. La parada conserva snapshot aunque se edite o
desactive el contacto original; el ID de agenda no será la fuente histórica.

Propuesta: ParadaEntrega será la fuente autoritativa de destino. Los actuales
UsuarioFinalId/Destino* de cabecera solo podrán conservarse como compatibilidad
de una parada, con revisión de su nulabilidad para multiparada. No elegir el
primer destinatario como destino de toda la orden ni crear uno ficticio para
satisfacer la FK. Todos los lectores deben distinguir ambas representaciones.
Para referencias polimórficas de incidencias/finanzas, un par tipo/ID no basta
para la integridad: usar relaciones tipadas y restricciones de ámbito apropiadas.

En EF Core una FK compuesta puede apuntar a una clave principal o alternativa.
Su número y orden de columnas deben coincidir. Esto sustenta la propuesta, no
significa que el modelo actual ya tenga esas restricciones.
[Documentación primaria de claves EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/foreign-and-principal-keys).

**Caso nullable importante:** una FK compuesta con un componente null se considera
sin relación. Por eso B, cuyo ParadaId será null, necesita además una FK obligatoria
tenant/orden. Una restricción de tipo debe exigir ParadaId para C y excluirlo para
B; el dato nulo no puede eludir la pertenencia de orden. Igualmente, una relación
requerida no garantiza por sí sola que una ruta tenga al menos una orden: esa
cardinalidad se valida transaccionalmente.
[Relaciones compuestas y cardinalidad EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many).

Si `PilotoId` se conserva en ruta y orden por compatibilidad, las FKs de tenant
no garantizan que ambos sean el mismo piloto. La activación debe comprobarlo y
actualizarlo atómicamente. Propuesta: una única pertenencia operativa vigente por
orden, preservando membresías históricas; los traslados y rutas sucesivas requieren
regla de negocio antes de habilitarse. Borrados físicos en cadena no son apropiados
para historia, códigos consumidos ni movimientos financieros.

## 4. Recogidas, entregas, privacidad y estado

### 4.1 Punto de ruta no equivale a parada de entrega

Una ruta que agrupa comercios necesita **una recogida por orden** y sus entregas.
Ejemplo conceptual: recogida de orden 1 mediante B1; entregas 1a/1b mediante sus C;
recogida de orden 2 mediante B2; entrega 2a mediante C. Este ejemplo no fija el
orden óptimo ni obliga a completar la primera orden antes de recoger la segunda.

Invariante: la recogida de cada orden precede a todas sus entregas. Reordenar con
permiso no elimina esa precedencia. Un PuntoRuta de recogida referencia la orden,
no crea un destinatario ni otro C. Los puntos de entrega referencian ParadaId.

Propuesta de métricas separadas: `recogidasCompletadas/totalRecogidas`,
`entregasCompletadas/totalEntregas` y `puntosCompletados/totalPuntos`. La UI debe
indicar cuál muestra; el “3/6” de referencia no autoriza mezclar denominadores.
Una entrega simple es una orden con una parada de entrega, aunque su recorrido
tenga también un punto de recogida.

### 4.2 Preservar la privacidad actual

Hoy el servidor oculta dirección, teléfono, mapa y coordenadas de destino al
piloto hasta confirmar B; el nombre del destinatario ya aparece en la proyección.
[DTO piloto:206](../../Login/Application/Services/PilotoAppService.cs#L206).

Confirmado para la evolución: el CRM autorizado puede calcular la ruta completa;
la proyección del piloto conserva la restricción por cada orden no recogida.
No enviar destinos futuros ocultos en JSON y esconderlos solamente con widgets.
La polilínea, bounds, markers, enlaces a Waze/Maps, exportaciones y cachés también
pueden revelar esas posiciones. Antes de B, devolver únicamente la información
autorizada, conteos o estimaciones que no revelen el destino; después, la parte
habilitada por la recogida correspondiente. Limpiar proyecciones al perder acceso.

Pendiente: qué representación/estimaciones puede ver el piloto al planificar una
ruta optimizada con recogidas de varios comercios. Hasta decidirlo no se amplía
su acceso a direcciones o coordenadas, aunque el motor del servidor las use.

### 4.3 Estados independientes y cierre por C

Propuesta: representar por separado avance de parada, agregado de orden, avance
de ruta y atención de incidencia. Una incidencia puede mantener entrega en curso
y pausarla según política, sin sustituir el estado logístico ni cerrarla.
Actualmente usar `EstadoOrden.Incidente` excluiría la orden de la consulta activa
del piloto y bloquearía sus confirmaciones.
[Consulta activa:196](../../Login/Application/Services/PilotoAppService.cs#L196).

Al validar C: comprobar tenant, orden, parada, piloto actual, recogida previa,
política de pausa, versión y código vigente/no consumido. Consumir una sola vez,
registrar el hito de esa parada y recalcular agregados en la misma transacción.
La orden completa solo cuando todas sus entregas exigibles estén satisfechas;
qué hacer con fallidas, devueltas o canceladas sigue pendiente. La ruta agrega
sus órdenes/puntos, sin convertirse en otro código de entrega. No equiparar
Recogido y En ruta como dos hechos independientes hasta definir su disparador.

## 5. Códigos y reservas: objetivo frente a legado

El sistema actual guarda un B y un C por orden, códigos nuevos de cuatro dígitos,
valores históricos de hasta 80 caracteres y reservas activas por tenant/tipo.
La asignación/recogida/cierre utilizan ID de orden para los códigos cortos.
[Entidad:10](../../Login/Domain/Entities/CodigoEntrega.cs#L10),
[índices:64](../../Login/Data/ApplicationDbContext.cs#L64),
[validación:605](../../Login/Application/Services/OrdenService.cs#L605).

| Aspecto | Propuesta de evolución |
| --- | --- |
| A y número interno | Conservar CodigoA, NumeroOrdenA e Id emitidos; A no identifica una fila financiera ni garantiza unicidad histórica. |
| B | Una fila de recogida por orden; UsadoAt conserva el hito ya registrado. No volver a pedir B al entregar. |
| C | Una fila por parada; el consumo de C afecta exclusivamente esa parada. |
| Unicidad estructural | Sustituir “un código por orden/tipo” por unicidad B por orden y C por parada, con predicados explícitos para nullable. |
| Reserva de dígitos | Mantener unicidad activa tenant/tipo/código. Un C no puede cerrar otra parada ni otra empresa. |
| Liberación | Propuesta: C se libera al confirmar su parada; A/B se mantienen hasta cierre de orden. Política de cancelación/retorno y vida de reservas programadas pendiente. |
| Formato | Strings, conservando ceros iniciales; no regenerar ni convertir códigos emitidos. No inventar un prefijo de ruta que cambie A/B/C. |

Los índices filtrados permiten aplicar restricciones a las filas que cumplen el
predicado; el índice propuesto y los estados terminales deben evolucionar juntos.
[Referencia primaria SQL Server](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes?view=sql-server-ver17).

Hay 10.000 combinaciones por tipo/tenant bajo el formato actual. Multiparada
consume tantos C activos como entregas pendientes, no uno por orden. Reservar un
lote de códigos debe ser atómico: si no alcanza el espacio, no crear una orden
parcial ni ocupar cuota/secuencia sin resultado. El generador actual termina con
error al agotar combinaciones; esa protección debe conservarse.
[Generador:642](../../Login/Application/Services/OrdenService.cs#L642).

El conjunto reservado durante la generación debe incluir también los códigos de
paradas del mismo lote aún no persistidos; consultar solo las filas ya guardadas
no protege contra duplicados internos de una creación multiparada.

C es el requisito que aporta el destinatario. No incluirlo en DTO del piloto,
logs, URLs, exportaciones generales ni etiquetas que transporta el repartidor.
Hoy el comprobante autorizado del comercio muestra B y C; la distribución de C
por destinatario y sus permisos deben concretarse, sin asumir SMS u otro proveedor.
El QR B por ID autorizado es antecedente reutilizable, no una etiqueta C pública.
[QR autorizado:22](../../Login/Controllers/QrController.cs#L22).

El comprobante QR de creación/recogida existente se diferencia del comprobante
de entrega con foto/firma postergado; este último sigue sin funcionalidad.

## 6. Contratos propuestos y convivencia de clientes

### 6.1 Punto de partida y negociación

Actualmente `capacidades` anuncia `versionContrato=2`, creación idempotente y
contexto de tienda. Crear recibe un único UsuarioFinalId; cerrar entrega exige
B+C. Los DTO de comercio/piloto contienen un único destino. No alterar en silencio
su significado para aceptar múltiples destinatarios.
[Capacidades/crear:24](../../Login/Controllers/Api/TiendaApiController.cs#L24),
[cierre actual:25](../../Login/Controllers/Api/PilotoApiController.cs#L25),
[DTO piloto:47](../../Login/Application/Interfaces/IPilotoAppService.cs#L47).

Propuesta: negociar capacidades separadas para multiparada, C por parada, cierre
solo C, rutas, reordenamiento, incidencias, liquidaciones propias y GPS por
actividad. El número definitivo de contrato y las URLs se acordarán al implementar;
los siguientes tipos son ilustrativos, no endpoints publicados.

| DTO conceptual | Campos mínimos y reglas |
| --- | --- |
| CrearOrden | OperacionId, modalidad, nota y lista de destinatarios con identificador de fila del cliente; referencia externa/servicio/programación cuando se definan. Tienda por contexto autorizado, tenant derivado del actor. |
| OrdenDetalle | OrdenId, A/número interno, versión, recogida, piloto/ruta, estado agregado, conteos separados y colección de paradas autorizada según rol. |
| ParadaEntrega | ParadaId, OrdenId, orden lógico, snapshot, instrucciones, estado, hitos, pausa y versión. C únicamente en respuesta/canal comercial autorizado. |
| ConfirmarRecogida | OrdenId, B, OperacionId y versión esperada. El servidor conserva la precedencia de sus entregas. |
| ConfirmarEntregaParada | OrdenId, ParadaId, C, OperacionId y versión esperada; sin B. Devuelve parada y agregados confirmados, no un éxito local supuesto. |
| RutaDetalle | RutaId, versión, piloto nullable planificada, órdenes incluidas, puntos tipados y conteos/estimaciones con proyección por permisos. |
| ReordenarRuta | RutaId, versión, lista de PuntoRutaId y OperacionId; permisos/reglas/precedencias verificadas en servidor. |
| ReportarIncidencia | Scope autorizado, categoría, descripción y OperacionId. Estado de atención/efecto devueltos según política versionada. |
| PosicionesActividad | ActividadId, dispositivo/secuencia, muestras UTC capturadas y clave de envío; recepción/último punto separado de hora capturada. |
| Ganancias / liquidación | Período, unidad/origen, conceptos, importes congelados y estado de conciliación/pago; solo datos del beneficiario/tenant autorizado. |

Ejemplo conceptual del nuevo cierre; campos de ruta nunca sustituyen la parada:

```json
{
  "ordenId": 125,
  "paradaId": 607,
  "codigoC": "0042",
  "operacionId": "c4e7f1c8d0f342bf9a508af10a01da22",
  "versionEsperada": "revision-opaca"
}
```

### 6.2 Respuestas, conflictos y lecturas consistentes

Conservar UTC explícito en todos los instantes y distinguir fecha comercial de
timestamp. La API ya aplica un converter de salida para resolver la pérdida de
Kind al leer datetime2.
[UTC:11](../../Login/Serialization/UtcDateTimeJsonConverter.cs#L11).

Propuesta: revisiones/ETag por agregado y versión de proyección; filas del listado,
KPIs y detalle deben exponer unidades/filtros/fecha de corte consistentes. El cliente
descarta respuestas antiguas al cambiar tenant, tienda o revisión. Los resúmenes
no se calculan sobre una página. Exportar/seleccionar IDs requiere reautorizar la
selección completa; el resultado de un lote informa éxitos/conflictos por unidad
según la política de atomicidad aún por decidir.

Misma operación y payload devuelve el resultado original confirmado; otra carga
con la misma clave es conflicto. El hash incluirá todas las paradas y reglas de
normalización relevantes, incluido su orden cuando forme parte de la intención.
No cambiar el algoritmo de operaciones antiguas ya confirmadas. La tabla actual
solo referencia una orden; otras operaciones necesitan representación de resultado
y tipo adecuada, sin fingir que ya están implementadas.
[Relectura idempotente:626](../../Login/Application/Services/OrdenService.cs#L626).

Preservar 401/403, ausencia autorizada, 409 de conflicto y 429 de intentos. Un
timeout/500 deja resultado incierto: conservar clave y consultar/reintentar. El
indicador actual `solicitudRechazada` solo permite descartar cuando el servidor
garantiza que no hubo commit ni operación previa; no usarlo para un conflicto
existente. Errores inesperados incluyen traceId, sin PIN ni datos internos.
[Manejo actual:101](../../Login/Controllers/Api/TiendaApiController.cs#L101).

### 6.3 Clientes antiguos

Propuesta: permitir la proyección antigua únicamente para órdenes de una parada
compatible. El cierre legacy B+C se adapta a esa parada, mientras el nuevo pide
solo C; nunca interpretar un éxito antiguo como “cerrar todas las paradas”. Clientes
sin capacidades nuevas no deben aceptar/asignarse rutas o multiparadas que no
pueden representar. Política de versión mínima y coexistencia debe aprobarse antes
de activar el modo nuevo. Los códigos históricos conservan validez, sin forzar
el formato corto sobre órdenes ya emitidas.

## 7. Consistencia, transacciones y automatización

El backend actual serializa mutaciones con bloqueo del tenant, revalida recursos
y usa UPDATE condicional para competir por una asignación. Bajas/activaciones
comparten ese orden. Esta base debe conservarse, no reemplazarse por validaciones
solo de pantalla.
[Bloqueo:23](../../Login/Application/Services/QuotaPolicyService.cs#L23),
[asignación:187](../../Login/Application/Services/OrdenService.cs#L187).

Pendiente operativo: la agrupación en ruta no autoriza a saltar la regla actual
de una automática activa por piloto. Antes de ofertar rutas con varias órdenes
automáticas hay que acordar unidad de oferta y capacidad; repetir la aceptación
actual N veces no convierte esas asignaciones en una activación de ruta atómica.

| Carrera o fallo | Invariante exigida / propuesta de control |
| --- | --- |
| Dos creaciones/reintentos | Una orden completa, todas sus paradas, códigos, cuota e historial; resultado original recuperable. |
| Asignación contra baja | O gana la asignación y la baja se rechaza, o gana la baja y la asignación falla; jamás ruta/orden activa sobre piloto inactivo. |
| Activar ruta contra otro despacho | Releer órdenes/piloto/tenant, pertenencia y versiones dentro de la transacción. Todas las órdenes ejecutadas tienen el mismo piloto. |
| Dos validaciones del mismo C | Un consumo y un hito; replay autorizado devuelve resultado sin otro avance ni devengo. |
| C de otra parada/orden/tenant | Rechazo aunque coincidan los cuatro dígitos. |
| Resolver incidente contra cerrar/reordenar | Aplicar versión y efecto vigente autorizado; no despausar ni cerrar por una lectura obsoleta. |
| Lote masivo contra cambios concurrentes | IDs estables, revalidación por tenant y estado, resultado explícito y operación recuperable; atomicidad por lote/orden pendiente. |
| Misma alerta repetida | Deduplicar por regla y objeto/evento apropiados; un poll no crea otra incidencia o escalación idéntica. |
| GPS retrasado/reintentado | Deduplicar muestras; un punto antiguo no sustituye la ubicación más reciente ni altera un hito C. |

Propuesta: mantener Tenant → piloto/recursos → ruta/órdenes/paradas en orden
determinista de IDs para nuevas operaciones con varios agregados. Validar el orden
con todas las mutaciones existentes antes de programarlo; no abrir una segunda
transacción dentro del bloqueo actual. Llamadas de rutas/geocodificación y cálculo
costoso deben usar entradas versionadas y revalidación, evitando retener el bloqueo
mientras se espera una API externa. Un mecanismo durable para avisos posteriores al
commit es propuesta a evaluar, sin elegir infraestructura aquí.

Mantener límites mensuales efectivos, cero ilimitado y rechazo de configuración
negativa. Actualmente el mes se calcula desde CreatedAt/UTC. Escalas, programados,
importaciones y reservas anticipadas requieren decisiones específicas sobre unidad,
mes y momento de consumo; no redefinirlas silenciosamente como paradas o rutas.
[Cuota de órdenes:364](../../Login/Application/Services/OrdenService.cs#L364).

Incidencias requieren autorización por acción: reportar, asignar responsable,
configurar reglas, resolver, reabrir o reanudar. Categoría/efecto no autorizan
al piloto a consultar otras empresas. Quedan pendientes responsables, SLAs y
política ante cambios de reglas en casos abiertos. GPS continuo aprobado tampoco
autoriza seguimiento fuera de actividad, ni GPS fresco prueba capacidad disponible.
Frescura, cadencia, retención y cierre de actividad necesitan reglas explícitas.

## 8. Finanzas congeladas e idempotentes

Confirmado: registrar y conciliar pagos externos. Programar una fecha no ejecuta
una transferencia; aprobar no significa pagar; entregar mediante C no significa
liquidar. Cada empresa administra su módulo; la app muestra exclusivamente las
ganancias del piloto autorizado. El SuperAdmin no se convierte implícitamente en
operador de caja de todos los tenants.

Propuesta de objetos económicos:

- ReglaRemuneracion versionada: unidad servicio/ruta/parada, concepto, beneficiario,
  condiciones, vigencia, moneda, fórmula y redondeo. Sus combinaciones requieren
  política para evitar pagar varias veces el mismo concepto.
- Movimiento financiero: derecho u obligación con ID estable de origen/unidad,
  concepto, regla aplicada, bases/resultado congelados, actor y fecha. Distinguir
  precio cobrado al comercio de remuneración del piloto.
- Cobro/conciliación de efectivo: importe esperado/registrado, comercio, custodio,
  fecha, diferencias y compensación autorizada. No inferir que el piloto cobró
  porque existe una entrega completada.
- Liquidación y líneas: período/corte, beneficiario, movimientos incluidos,
  conceptos, neto, moneda y revisiones/aprobaciones auditables.
- PagoExterno: importe, fecha, método, referencia y contraparte registrados;
  aplicación a liquidaciones y conciliación. Cuenta enmascarada en vistas, con
  acceso restringido a datos completos si llegaran a ser necesarios.

El ejemplo de referencia cuadra en DOP:
`12.600 + 600 − 1.260 − 240 − 1.800 = 9.900`. Sus cifras no fijan las reglas reales.
La tarifa actual base/km no define comisiones, incentivos ni efectivo a compensar.
[Tarifas actuales:566](../../Login/Application/Services/OrdenService.cs#L566).

Una liquidación aprobada conserva reglas e importes. Cambios posteriores generan
ajustes/reversiones trazables según autorización, sin borrar ni recalcular historia.
Propuesta: cada movimiento liquidable se consume una sola vez dentro de su ámbito
económico; período solapado, nueva versión de regla o reintento no crean otro derecho
por el mismo hecho. Correcciones legítimas se identifican como movimientos nuevos.

Generar, aprobar, ajustar y registrar/aplicar un pago requieren claves idempotentes,
versiones y unicidad transaccional. La clave de reintento no evita por sí sola que
dos personas registren el mismo pago externo con claves diferentes: faltan criterios
de conciliación y detección de duplicados comerciales. Parcialidades, saldos
negativos, devoluciones, compensaciones y pago de rutas incompletas siguen pendientes.

Vistas derivadas autorizadas: desglose por origen/parada, registro de ajuste,
conciliación de efectivo por comercio, revisión/aprobación, registro y detalle de
pago externo, reglas versionadas e historial de correcciones. No incorporar botón
que ejecute una transferencia ni anunciar evidencia foto/firma funcional.

## 9. Migración evolutiva y reversión

Propuesta de secuencia para revisar antes de implementar:

1. Auditar consistencia tenant/tienda/piloto/códigos del legado; reparar solo con
   datos verificables y un plan aprobado. Crear estructuras nuevas de forma aditiva.
2. Crear exactamente una ParadaEntrega por orden existente, con correspondencia
   estable/única. Copiar el snapshot que ya tiene, no la agenda mutable. Si se
   encuentra legado anterior sin congelar, conservar procedencia/calidad y solo
   datos disponibles, sin reconstruir historia inexistente.
3. Asociar el C existente a esa parada sin alterar su ID, valor, Usado/UsadoAt ni
   reserva. Mantener B de la orden, A/número interno, piloto, precio e hitos.
   Entregada conserva su cierre; no inventar sucesos En ruta o incidencias resueltas.
4. Validar backfill repetible, asociaciones uno-a-uno, FKs compuestas, conteos,
   reservas y ausencia de cruces de tenant antes de exigir restricciones nuevas.
   No suprimir el índice B/C viejo hasta que el reemplazo proteja sus casos.
5. Publicar servidor compatible y capacidades desactivadas para datos nuevos;
   revisar lectores, filtros de abiertas, QR, reportes, cuotas, DTO y panel/app.
6. Habilitar por tenant después de definir versiones mínimas, privacidad,
   transiciones y operación comercial. Retirar compatibilidad antigua solo mediante
   decisión explícita y evidencia de clientes actualizados.

La migración actual ya congeló datos disponibles sin reescribir códigos/fechas y
bloquea un Down destructivo con datos. Es antecedente, no migración multiparada.
[Backfill actual:100](../../Login/Migrations/20261111222204_ConfiabilidadOrdenesSnapshot.cs#L100),
[Down actual:166](../../Login/Migrations/20261111222204_ConfiabilidadOrdenesSnapshot.cs#L166).

**Rollback:** antes de habilitar multiparada, podría volver el código compatible
anterior manteniendo las adiciones si se demuestra que siguen siendo legibles.
Después de crear órdenes multiparada, rutas, movimientos o liquidaciones, la
versión antigua no puede operarlos con seguridad. Deshabilitar nuevas altas no
permite borrar paradas ni aplanarlas en un destino. La reversión debe mantener un
servidor capaz de finalizar esos datos o usar restauración aprobada con análisis
de operaciones posteriores; no ofrecer un Down que destruya historia.

## 10. Decisiones pendientes y criterios para el siguiente lote

| Pendiente | Qué no debe suponerse |
| --- | --- |
| Recogidas | Alcance de una recogida por orden, consolidación, horarios y operaciones excepcionales. Ruta multicomercio requiere sus B independientes. |
| Secuencia | Quién reordena, cuándo y con qué límites; reordenar no concede permiso para saltar ni entregar antes de recoger. |
| Estados | Pendiente/Por asignar, Recogido/En ruta, cancelación, retorno y resultado agregado con paradas no entregadas. |
| Privacidad/optimización | Representación del recorrido del piloto antes de cada B; el CRM completo no autoriza revelar destinos al piloto. |
| Incidencias | Categorías definitivas, quién configura/resuelve/reanuda, SLAs/calendarios y tratamiento de casos abiertos al cambiar política. |
| Programados/importaciones | Momento de reserva y cuota, zona horaria comercial, agrupación de filas, errores parciales y atomicidad. |
| Capacidad y provincias | Unidad de capacidad/uso, cobertura, fronteras/solapamientos, servicio entre provincias y demanda alta. |
| Comercial/financiero | Qué constituye un servicio remunerable, combinación de reglas, precios por comercio, redondeos, compensaciones y conciliación de pagos/cobros. |
| C y permisos | Canal de entrega al destinatario y usuarios autorizados; reglas de regeneración/recuperación no definidas. |
| Tracking | Duración de actividad, frescura/retención, cadencia y datos autorizados; nunca prometer seguimiento ilimitado con proceso terminado. |

No se ejecutan pruebas en esta fase. Para implementar después, los criterios
de aceptación deberán cubrir, al menos: C de parada ajena rechazado; primera C no
cierra una orden de varias entregas; recogida B precede a sus entregas; una ruta
multicomercio no revela destinos no recogidos; permisos y reordenamiento preservan
precedencia; carrera baja/asignación y dos consumos de C no duplican efectos;
reintentos/lotes no duplican cuota/reservas; GPS fuera de orden no retrocede el
último punto; una incidencia no pierde la entrega; ningún movimiento se liquida
dos veces; legado a una parada mantiene todos los IDs/códigos/hitos; cliente antiguo
no puede completar una multiparada ni leer otro tenant.

El documento deja preparados invariantes y alternativas revisables. Describe la
fase de análisis; no acredita contratos nuevos desplegados ni una implementación
o integración ya efectuada.
