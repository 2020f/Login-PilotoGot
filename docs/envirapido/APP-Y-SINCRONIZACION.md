# Envirapido: app y sincronización

Fecha: 11 de noviembre de 2026. Documento de análisis y preparación de alcance, no de funcionalidad ya implementada. Recoge las seis referencias y las aclaraciones posteriores del usuario; no contiene prototipos ni cambia código, contratos, datos o permisos de dispositivos.

Base comprobada: Flutter `C:\Users\Fernelis\Desktop\pilotGOapp`, commit `8ca95af1ea7c2481be73e38d7abf3c0156ceaae9`; backend `36d15c7696f1b0a8018251cbcdd6be9897ae7248`. Ambos estaban limpios al comenzar esta documentación. Las rutas, versiones, claves de operación y campos sugeridos más abajo son propuestas de diseño, salvo lo identificado expresamente como contrato actual. No se asigna una versión futura ni se inventan endpoints nuevos.

Las referencias visuales están inventariadas en [REFERENCIAS.json](REFERENCIAS.json). El estilo de la app seguirá siendo el de Flutter actual: mapa, superficies claras, tarjetas redondeadas, colores de estado y acciones actuales. El teléfono del mockup no sustituye ese diseño.

## 1. Decisiones confirmadas

- Nombre visible **Envirapido**. Conservar paquete Android `com.pilotgo.app`, firma original y API pública `https://pilotogoapp.zapflow77.dpdns.org`. Mantener las claves de almacenamiento de sesión/credenciales/intenciones (`pilotgo.session.v2`, `pilotgo.credentials.v1` y `pilotgo.pending-order.v1`, con sus ámbitos actuales); cambiar la marca no justifica perder instalaciones o acceso persistido. La variante QA permanece aislada de producción.
- Piloto: navegación **Ruta / Entregas / Ganancias / Perfil**, conservando el flujo automático y manual. El comercio mantiene sus funciones de órdenes, creación y destinatarios.
- Una orden simple tiene un destinatario. Una orden con escalas tiene varios destinatarios y un mismo piloto; cada parada de entrega conserva su C.
- Una ruta puede reunir una o varias órdenes de la misma empresa/tenant, procedentes de varios comercios de esa empresa, con un mismo piloto. No hay traspasos entre pilotos en la definición de escalas.
- B confirma recogida. C es el único requisito de código para finalizar la entrega correspondiente. Foto, firma y comprobante quedan opacos, desactivados y sin captura/subida en esta fase.
- Reordenar no cambia identidad, pertenencia ni C de una parada. El avance del padre refleja sus paradas; una entrega parcial no completa por sí sola la orden o ruta.
- El piloto tendrá sección para reportar incidencias. Su efecto será configurable por tipo/política: continuar, pausar una parada o pausar la ruta. Reportar no elimina avance ni confirma entregas. Resolver casos o añadir notas no otorga permisos nuevos al piloto por defecto.
- Los pagos ocurren fuera del sistema. Ganancias muestra partidas, liquidaciones y estados del backend, con reglas históricas configurables por servicio, ruta o parada. C no acredita cobro ni pago. No hay integración bancaria ni iniciación de pagos desde la app.
- El GPS debe continuar durante entregas/rutas activas con pantalla bloqueada y mientras se usa Waze/Maps. Esta decisión funcional no equivale a haber concedido permisos reales al Samsung ni a una prueba física ya aprobada.
- Cada empresa opera su CRM aislado. SuperAdmin conserva su ámbito separado; la app no adquiere acceso transversal a empresas por incorporar estas funciones.

## 2. Situación comprobada frente al objetivo

| Área | Flutter actual en la base indicada | Objetivo y dependencia |
| --- | --- | --- |
| Acceso | Sesión renovable, tokens y credenciales recordadas opcionales en almacenamiento seguro; selector de perfil si la cuenta tiene ambos roles. | Conservarlo al cambiar marca y navegación. La autorización efectiva sigue siendo del servidor. |
| Navegación piloto | Home con mapa y panel deslizable; menú con perfil informativo, ayuda, conexión, cambio de perfil autorizado y logout. | Shell con las cuatro pestañas acordadas y un estado operativo compartido. |
| Recepción | Consulta de órdenes cada cinco segundos en primer plano; manuales visibles aunque recepción automática esté apagada; varias órdenes activas. | Mantener comportamiento y recuperación al reanudar. Varias órdenes asignadas no equivalen a una orden multiparada. |
| Creación comercio | Un destinatario y nota. Intención persistida por API/cuenta/tienda, `OperacionId` estable y recuperación independiente del comprobante. | Simple o escalas; creación coherente de padre/paradas sin duplicados. Formulario y contrato futuros por definir. |
| Compatibilidad | Antes de cada POST incierto de creación consulta capacidades sin caché; requiere contrato actual 2, idempotencia y contexto de tienda. | Negociar capacidades nuevas antes de operar multiparada, rutas o cierre solo C. No basta la versión actual 2. |
| Códigos | Recogida con B; cierre actual solicita B+C. Nuevas órdenes usan cuatro dígitos; se admiten códigos históricos. | Cambiar coordinadamente UI, ayuda, repositorio y backend a C por parada. Conservar los códigos ya emitidos. |
| Ruta/mapa | Una coordenada de destino por detalle; una línea recta, no recorrido vial ni ETA. | Recogidas y entregas autorizadas, secuencia, avance y estimaciones respaldadas por datos reales. |
| GPS | `getCurrentPosition` periódico cada diez segundos mientras el home está en primer plano y hay recepción automática o entregas activas. | Seguimiento desacoplado de widgets con servicio Android de ubicación y ciclo de vida comprobado. |
| Incidencias | No hay reporte móvil. El enum/contador actual no constituye un módulo de casos. | Reporte, resultado persistido y efecto operativo recibido del backend. |
| Ganancias | No existen modelos/endpoints móviles de partidas o liquidaciones. | Lectura por período, desglose y estados financieros propios. |
| Evidencia de entrega | No hay flujo móvil de captura/subida de foto/firma. | Mantener la sección opaca y no funcional; no convertirla en requisito de C. |

Fuentes locales de esta comprobación, relativas a la raíz Flutter de la base indicada: `lib/main.dart`, `lib/features/auth/session_controller.dart`, `lib/core/storage/session_store.dart`; dentro de `lib/features/piloto/`: `presentation/piloto_home_screen.dart`, `pilot_orders_controller.dart`, `presentation/piloto_detalle_screen.dart`, `data/piloto_repository.dart`, `models/piloto_models.dart` y `presentation/widgets/live_map.dart`; y `lib/features/tienda/data/tienda_repository.dart` / `pending_order_store.dart`.

## 3. Flujos por rol y navegación

### Piloto

| Pestaña | Responsabilidad prevista | Límites |
| --- | --- | --- |
| Ruta | Actividad asignada, próximos puntos autorizados, recogidas B, entregas C y progreso. | No publicar destinos de órdenes sin recoger. No suponer que la primera orden de una lista es una ruta planificada. |
| Entregas | Órdenes y paradas propias; acceso a detalle, estado e historial autorizado. | Una orden conserva su identidad aunque pertenezca a una ruta. Cancelaciones/reasignaciones deben invalidar acciones antiguas. |
| Ganancias | Resumen, partidas/servicios, liquidaciones, historial de estados y efectivo conciliado que corresponda mostrar. | Backend calcula. Sin seleccionar cuentas bancarias, aprobar liquidaciones ni iniciar pagos. Cuenta enmascarada si se muestra. |
| Perfil | Cuenta, perfil activo, ayuda, estado de conexión/GPS, preferencias permitidas y logout. | La edición de datos o permisos nuevos necesita definición expresa. |

La sección de reportar incidencia debe ser accesible conservando las cuatro pestañas. Su ubicación concreta se resolverá en el diseño posterior; este documento no añade una quinta pestaña ni inventa formularios. El caso debe quedar vinculado al alcance correcto: ruta, orden o parada.

El switch de recepción automática sigue controlando ofertas, no las asignaciones manuales ni por sí solo el seguimiento de una actividad en curso. Navegar entre pestañas no debe crear varios controladores, temporizadores, suscripciones GPS o colas de mutación.

### Comercio y CRM de empresa

El comercio crea simple/escalas dentro de su tienda autorizada, mantiene destinatarios y consulta órdenes/avance. Una operación incierta de creación debe recuperarse antes de permitir editar o crear otra con la misma clave. La ruta puede reunir órdenes de varias tiendas del mismo tenant; eso no permite consultar otro tenant ni enviar un `X-Tienda-Id` arbitrario.

El CRM autorizado planifica, asigna individualmente o en bloque, reordena según permisos, gestiona casos y registra/concilia pagos externos. Asignar varias órdenes al mismo piloto no las fusiona. La app del piloto recibe una proyección autorizada, no el snapshot completo de planificación del CRM. SuperAdmin queda separado del CRM operativo de cada empresa.

El destinatario aporta el C de su parada. No se presupone una nueva app de destinatario ni un canal de notificación: su distribución debe concretarse sin filtrar todos los C al piloto.

## 4. Identidad, estados y privacidad de recorrido

### Identificadores y unidades diferentes

Usar `ClienteAppId` como tenant actual del backend y distinguir `OrdenId`, `ParadaId` y `RutaId` estables. La nomenclatura de los nuevos identificadores y su representación final siguen pendientes de contrato. Todo vínculo se valida en el servidor: tenant, tienda autorizada, piloto, pertenencia y estado.

Los códigos A/B/C son valores de negocio, no claves primarias, índices de lista ni localizadores globales. Conservarlos como cadenas, incluidos ceros iniciales y valores históricos. C no se busca sin contexto de orden/parada: cuatro dígitos pueden repetirse en otros contextos. Reordenar cambia una posición, nunca `ParadaId` ni C.

Un **punto de recogida de ruta** es distinto de una **parada de entrega**. Una ruta de varias órdenes puede requerir varias recogidas B y varias entregas C. No introducir un B único de ruta que sustituya los B de las órdenes sin una decisión expresa. El alcance de recogidas adicionales dentro de una misma orden permanece por precisar.

Las métricas deben indicar su unidad: entregas completadas/total de paradas de entrega, recogidas confirmadas/total de recogidas y, si se necesita, puntos visitados/total de puntos de ruta. No sumar una recogida B como entrega C ni confundir número de órdenes con destinatarios, puntos o unidades remunerables.

### Estados explícitos

| Concepto | Regla de presentación/transición |
| --- | --- |
| Creado, pendiente o por asignar | Concretar equivalencias con el backend; no inventar actividad física por una etiqueta del CRM. |
| Asignado/recogiendo | Piloto autorizado y recogida pendiente; C no sustituye B. |
| Recogido/en ruta | Recogida confirmada; la separación de ambos hitos/horas necesita contrato, pues hoy no existe un evento independiente de salida. |
| Parada entregada | Resultado confirmado por el servidor para la parada y su C. |
| Padre con avance parcial | Reflejar las paradas confirmadas sin cerrar automáticamente el padre. El efecto de canceladas/devoluciones en el denominador debe definirse. |
| Cancelación/devolución | Transiciones específicas y auditadas; no deducirlas de valores numéricos o de una incidencia. |
| Demorada | Indicador sobre la actividad operativa; no convierte “en ruta” en una entrega o una cancelación. |
| Incidencia | Estado de atención y efecto operativo configurado separados del avance de entrega. No reutilizar ciegamente el enum actual si hace desaparecer la actividad del piloto. |
| Liquidación/pago | Ciclo financiero independiente. Entregada, liquidada y pagada no son sinónimos. |

Comparar conjuntos/estados explícitos; **no usar `estado >= ...`** para autorización, avance, destino visible o acciones. Permisos de acción y transiciones deben provenir del servidor y revalidarse al escribir. Hitos confirmados llevan hora UTC del servidor; la UI convierte una vez a zona local. “Sin registrar” no se completa con `DateTime.now()` para simular un evento.

### Privacidad antes de B: conflicto y propuesta

La planificación completa de CRM entra en conflicto con el ocultamiento actual del destino hasta recoger cada orden. La solución propuesta conserva esa privacidad: CRM autorizado planifica completo, pero el servidor entrega al piloto una **proyección por orden desbloqueada**, no simplemente datos completos con widgets ocultos.

Antes de B de una orden no deben aparecer sus destinatarios/direcciones/coordenadas restringidas en JSON, caché móvil, marcadores, polilíneas, enlaces Waze/Maps, búsquedas, respuestas de incidencias, mensajes de error ni exportaciones accesibles al piloto. Tampoco una polilínea completa debe revelar indirectamente destinos aún ocultos. Revisar derivados, previsualizaciones y trazas.

Después de B se actualiza la proyección autorizada de esa orden, sin desbloquear automáticamente las demás de la ruta. Con órdenes parcialmente recogidas, el mapa muestra sólo puntos permitidos. El CRM puede conservar su mapa completo con otro permiso y otra representación. Una futura necesidad de revelar antes toda la ruta requiere una decisión expresa; no se deduce del mockup ni de la optimización.

## 5. Sincronización propuesta

El estado actual usa polling en primer plano, sin solapamiento y con recuperación al reanudar. Mantener esa fiabilidad como base; no presentar WebSocket/push, ETags ni sincronización en segundo plano como implementados.

Proponer snapshots operativos coherentes, con versión y permisos, para ruta/órdenes/paradas/casos. Su definición exacta pertenece al contrato futuro. Si varias lecturas componen una pantalla, evitar mezclar revisión antigua del detalle con secuencia nueva o permisos revocados. Los totales agregados los calcula el backend, no una página truncada de la lista.

Separar estas claves de contexto: API, cuenta autenticada, tenant, rol activo, piloto y tienda seleccionada cuando corresponda. El servidor resuelve y valida identidad/membresía; no confía en IDs enviados por el cliente. La versión de sesión local invalida respuestas tardías, igual que las revisiones de selección actuales. Cambiar cuenta/rol/tenant/tienda no puede reutilizar snapshots, ETags o intenciones de otro contexto.

**ETag es una propuesta:** `If-None-Match` permite validar lecturas sin descargar un snapshot idéntico; `If-Match` con un validador fuerte puede proteger mutaciones contra cambios concurrentes. Una precondición fallida requiere reconciliar, no sobrescribir silenciosamente. Idempotencia y control de versión resuelven problemas distintos. Su semántica HTTP se basa en [RFC 9110, solicitudes condicionales](https://www.rfc-editor.org/rfc/rfc9110.html#name-conditional-requests); no existe todavía ese contrato en esta app.

Validar autorización antes de responder incluso `304`. El validador debe corresponder a la representación y visibilidad correctas: confirmar B, reasignar, cancelar o revocar permisos puede cambiar lo que el piloto puede ver aunque el padre conserve otros datos. Una nueva proyección invalida detalle, marcadores y acciones anteriores de forma coherente.

Al reanudar, recuperar conectividad o completar una mutación, reconciliar sin solicitudes duplicadas. Mientras se actualiza, conservar última lista/hora y distinguir datos desactualizados de lista vacía. Revalidar acciones sensibles abiertas en modales antiguos. El servicio GPS y la consulta de actividad necesitan coordinarse sin depender de qué pestaña esté montada.

Un eventual canal de eventos puede invalidar snapshots y acelerar la consulta; no reemplaza autenticación, lectura autoritativa ni recuperación de eventos perdidos. El servicio GPS tampoco convierte automáticamente el polling actual en recepción de órdenes/push con la app cerrada. Esa capacidad no se promete en esta fase documental.

## 6. Trabajo sin conexión e idempotencia

### Base actual que se conserva

La creación ya persiste una clave segura y un payload estable por cuenta/API/tienda. Comprueba capacidades frescas antes de cada POST incierto. Si el POST confirmó la orden y falló obtener su comprobante, recupera sólo el comprobante; no crea otra orden.

El contrato actual sólo libera la intención ante `400` con `solicitudRechazada: true` explícito. Un timeout, error de red, `5xx`, `400` genérico, `403` o conflicto `409` no demuestra que no se haya creado. Conservar el resultado incierto y el payload; no regenerar una clave para evitar el conflicto. Un cambio de permisos puede requerir recuperar acceso o intervención autorizada, manteniendo aislada la intención.

### Extensión propuesta al núcleo operativo

Aplicar el mismo principio a recogida B, confirmación C por parada y reporte de incidencia, una vez que el servidor implemente el contrato correspondiente. No basta reintentar automáticamente los endpoints actuales de B/C: hoy no tienen la idempotencia de creación documentada para esas acciones.

Persistir de forma protegida contexto, identificador estable de operación, acción, IDs estables, payload original y revisión esperada. C y ubicación son datos sensibles: no escribirlos en logs, notificaciones o telemetría. La persistencia futura de la cola debe tener límites y recuperación verificable; este documento no selecciona una nueva base/librería.

| Resultado local | Conducta propuesta |
| --- | --- |
| Preparada o pendiente sin red | Mostrar que todavía no está confirmada por el servidor. No adelantar contadores, ingresos ni estado final. |
| Enviada sin respuesta fiable | Mantener misma clave/contenido; consultar o reintentar según contrato para recuperar el resultado original. |
| Confirmada | Aplicar resultado y snapshot/revisión correspondiente; un replay devuelve el mismo resultado autorizado. |
| Rechazo definitivo explícito | Mostrar causa, conservar trazabilidad y permitir corregir mediante una nueva intención sólo cuando se demuestra que la anterior no se aplicó. |
| Conflicto de revisión/payload, cancelación o reasignación | Detener ese reintento automático y reconciliar. No trasladar C a otra parada ni editar la operación bajo la misma clave. |
| Sesión/permisos no válidos | Suspender envíos y aislar los pendientes. No enviar con la siguiente cuenta ni borrar incertidumbre para desbloquear la pantalla. |

Respetar dependencias por agregado: no confirmar C en servidor antes de su recogida B, ni aplicar una secuencia local que contradiga la revisión vigente. Reordenar no cambia IDs/C. Resolver una incidencia no confirma una parada. Reintentos del mismo resultado no crean eventos, casos, cobros o partidas duplicados.

En el contrato futuro, recuperar un resultado idempotente ya aplicado debe preceder a exigir nuevamente la transición o revisión mutable que aquella operación ya consumió. Validar identidad, contexto, clave/payload y permisos para exponerlo; no convertir un éxito incierto en un nuevo comando ni en falso rechazo porque el estado ahora sea Entregada. Si no existe resultado y la revisión cambió, reconciliar antes de decidir otra intención. El orden exacto de esas comprobaciones necesita pruebas de servidor.

Offline no equivale a autorización indefinida. Una instantánea local puede orientar, pero no asegurar que la ruta siga asignada ni que el C se aceptará al reconectar. La política para confirmar físicamente sin red, la antigüedad máxima de autorización y la resolución de conflictos deben fijarse antes de liberar la función. El panel debe mostrar pendiente de sincronización cuando aún no existe confirmación del servidor.

## 7. GPS Android continuo durante actividad

### Alcance actual y selección técnica pendiente

La base fija Geolocator `14.1.2` y `geolocator_android 5.2.1`; el GPS Dart actual se detiene al dejar primer plano. La corrección previa del bloqueo NMEA no implementa por sí sola seguimiento continuo. El servicio del plugin aparece en el manifest fusionado de los artefactos locales revisados, pero no aparecen allí los permisos `FOREGROUND_SERVICE`/`FOREGROUND_SERVICE_LOCATION`; no se debe activar un modo FGS suponiendo que el paquete los aporta.

La documentación del mantenedor expone `AndroidSettings.foregroundNotificationConfig` para un servicio con notificación persistente. Es una capacidad a evaluar, no una selección cerrada ni prueba de subida HTTP con motor Dart dormido. Verificar manifest fusionado, propiedad del ciclo de vida, ejecución real de callbacks/envíos y compatibilidad exacta de la dependencia antes de decidir mantenerla, extender código nativo o evaluar otra solución. [API oficial del plugin](https://pub.dev/documentation/geolocator/latest/geolocator/AndroidSettings-class.html).

### Servicio y permisos: distinciones obligatorias

Android considera acceso de ubicación en primer plano tanto una actividad visible como un servicio en primer plano; con ese servicio conserva acceso cuando la app pasa a segundo plano o se apaga la pantalla. Por eso la solución objetivo es un **FGS de tipo `location` iniciado correctamente durante una acción/actividad visible**, independiente del home. La autorización de negocio debe corresponder a actividad activa del piloto. [Android: permisos de ubicación](https://developer.android.com/develop/sensors-and-location/location/permissions#foreground).

Verificar en el artefacto final el servicio con `foregroundServiceType="location"`, permiso general `FOREGROUND_SERVICE` al apuntar a Android 9/API 28 o superior y permiso específico `FOREGROUND_SERVICE_LOCATION` al apuntar a Android 14/API 34 o superior. Obtener previamente ubicación coarse/fine y comprobar que el servicio de ubicación esté habilitado. Android 14 valida prerrequisitos del tipo y puede lanzar `SecurityException`; declarar una línea sin permiso en tiempo de ejecución no basta. [Declaración de FGS](https://developer.android.com/develop/background-work/services/fgs/declare), [prerrequisitos de location](https://developer.android.com/about/versions/14/changes/fgs-types-required#location).

Iniciar el servicio y promoverlo a primer plano con notificación son pasos distintos que debe manejar correctamente la implementación. La notificación debe identificar el seguimiento de actividad; no puede fingirse servicio invisible. [Android: lanzamiento de FGS](https://developer.android.com/develop/background-work/services/fgs/launch). Android 13 añade permiso de notificaciones: denegarlo no prohíbe por sí solo iniciar un FGS, pero el servicio sigue obligado a aportar su notificación; el aviso puede quedar en Task Manager en lugar de la bandeja. No confundir ese permiso con el de ubicación. [Android: notificaciones](https://developer.android.com/develop/ui/compose/notifications/notification-permission).

**Continuar no es iniciar desde background.** En targets Android 12 o superiores el inicio desde segundo plano está restringido salvo excepciones. Android 14 añade la comprobación de permisos while-in-use al crear el servicio; que `checkSelfPermission` devuelva concedido no garantiza que se pueda arrancar desde background. Hay que satisfacer ambas restricciones aplicables; `ACCESS_BACKGROUND_LOCATION` no es una exención universal. No resolverlo con bucles de reinicio, alarmas, overlays o un supuesto arranque infalible tras reboot. [Restricciones oficiales de inicio](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start).

No pedir acceso “siempre” automáticamente sólo por bloquear pantalla o usar Waze si basta la continuación de un FGS autorizado. Si un caso posterior exige verdadero acceso/inicio desde background, justificarlo, definir la excepción aplicable y solicitar el permiso específico mediante el flujo permitido. En Android 11 o superior “Permitir siempre” se concede desde ajustes; la precisión aproximada también limita background. Poder rechazar permisos no debe eliminar acceso a las demás funciones. [Android: solicitud de ubicación en background](https://developer.android.com/develop/sensors-and-location/location/permissions/background).

### Propiedad del ciclo de vida y detención

Propuesta de comportamiento, pendiente de implementación:

1. Confirmar sesión/contexto, permisos y existencia de actividad autorizada antes de iniciar seguimiento. Una asignación conocida con la app totalmente detenida no autoriza por sí sola un arranque nativo que Android prohíba.
2. Mantener un único propietario del seguimiento independiente de pestañas, mapas y navegación externa. Permitir volver a la app sin reiniciar contadores, colas o servicio duplicado.
3. Reconciliar actividad y autorización también mientras corre el seguimiento, no sólo desde widgets visibles. Una sesión vencida renovable se distingue de revocación confirmada; una pérdida de red se distingue de rechazo de permisos.
4. Al terminar la última actividad activa, cancelar o retirar toda actividad autorizada, cerrar sesión, cambiar de cuenta o detectar revocación, detener captación, callbacks/envíos y liberar recursos/notificación. No mantener rastreo continuo sólo por tener sesión abierta. El alcance de ubicación puntual al esperar ofertas se debe concretar conservando el modo automático y sin asumir seguimiento permanente.
5. Una pausa de incidencia conserva avance; su política determina el efecto operativo. No confundir pausa con entrega terminada ni inventar una excepción para rastrear fuera de la actividad autorizada.
6. Si no hay red, la revocación remota no puede conocerse instantáneamente. Proponer autorización temporal acotada/renovación y un límite offline antes de mantener seguimiento; su duración y contrato quedan por definir. Al confirmarse revocación, detener localmente y rechazar envíos en servidor.

Android permite al usuario detener desde Task Manager una app con FGS, eliminando el proceso y sin enviar callback. No prometer seguimiento inmortal ni reinicio inmediato tras esa decisión o force-stop. En el siguiente inicio visible, reconciliar actividad y pendientes; indicar el intervalo sin señal y respetar restricciones de arranque. Diferenciar retirar de recientes, stop de Task Manager, force-stop y reinicio del teléfono en QA. [Android: detención por el usuario](https://developer.android.com/develop/background-work/services/fgs/handle-user-stopping).

### Energía, red y calidad

Definir una política adaptativa medible según movimiento, actividad, precisión, conectividad y ahorro de energía. Los diez segundos actuales son una implementación de primer plano, no una garantía del sistema ni SLA futuro. Mayor precisión/frecuencia y menor latencia consumen más energía; ajustar intervalos y distancia tras pruebas, sin fijar aquí números de producción. [Android: ubicación y batería](https://developer.android.com/develop/sensors-and-location/location/battery).

Acotar cola de posiciones y reintentos; conservar hora de captura y recepción para no sustituir la última posición reciente por un lote viejo. La deduplicación/secuencia y la validación de reloj/precisión son propuestas para el contrato geo, no campos ya publicados. No tratar una ubicación antigua como nueva ni deducir distancia recorrida de una línea directa.

Verificar ahorro de batería y comportamiento del Samsung real antes de pedir cambios de configuración. Un wakelock/exención de energía no se añade por defecto ni garantiza permanencia; cualquier recurso retenido necesita justificación, medición y liberación al detener actividad. GPS puede seguir captando sin red, pero eso no equivale a tracking actualizado en CRM.

La app y el CRM deben distinguir última posición, antigüedad, precisión disponible, última recepción y estado de transmisión. Mostrar “sin ubicación reciente” o equivalente cuando corresponda. Los umbrales son configuración pendiente; no simular movimiento, ETA ni continuidad durante huecos. Distancia restante/ETA requieren motor/ruta autorizada y estimaciones explícitas, preservando el ocultamiento por B.

## 8. Incidencias, ganancias y datos del detalle web

El reporte móvil debe conservar categoría, descripción y alcance al reintentar. Las categorías de las referencias incluyen bloqueo de entrega, vehículo inmovilizado, sin contacto, demora, dirección incompleta, comercio cerrado, error de cobro, daño, etiqueta ilegible y acceso restringido. No convertir el reporte en un cambio ciego del estado de orden que la quite de la consulta de activas. La política recibida determina continuar/pausar y qué acciones permanecen autorizadas.

La resolución de un caso no reemplaza C ni regenera códigos. Responsable, prioridades, SLA, notas permitidas, permisos para resolver/reabrir y reglas de reanudación necesitan definición conjunta. No inferir adjuntos obligatorios de incidencia; la evidencia de entrega sigue inactiva.

Ganancias ofrece consulta de período, partidas, liquidaciones, estados y efectivo que el backend permita ver. La unidad remunerable puede ser servicio/ruta/parada según una regla histórica; no sumar dinero por cada C ni recalcular históricos al cambiar tarifas. Los cobros declarados, conciliados y compensados son hechos distintos; tampoco se deducen de “entregado”. No añadir permisos financieros al piloto por mostrar el módulo.

Peso, paquetes, servicio, vehículo/unidad y notas operativas requieren origen de datos definido en creación/maestros/acciones autorizadas. No completar el panel con valores ficticios provenientes del teléfono. Creación/asignación/recogida/entrega tienen hechos distintos de GPS, incidencias y pagos; el hito EnRuta separado sigue pendiente de definición.

## 9. Plan de QA futuro y evidencia disponible

Esta documentación no ejecuta pruebas. La evidencia previa se conserva en [QA visual del 11 de noviembre](../qa/visual-20261111/README.md) y su manifest de hashes. No acredita las funciones nuevas de Envirapido ni GPS continuo:

- Emulador QA profile: automáticas sucesivas, manual con recepción automática apagada, entrega legacy B+C, restauración tras force-stop y credenciales recordadas. Vista previa de cámara virtual visible; lectura de QR no acreditada.
- Samsung QA: acceso del comercio con credenciales recordadas, listado/resumen y horas locales correctas tras UTC. No se acreditó GPS físico, cámara/QR, ruta real ni persistencia tras cerrar proceso físico.
- Analyzer y pruebas Flutter previos corresponden al lote existente, no a la futura navegación, multiparada, FGS, incidencias o ganancias. No se vuelven a ejecutar en esta fase documental.

| Bloque futuro | Casos y evidencia necesaria |
| --- | --- |
| Roles/aislamiento | Dos tenants, varias tiendas, dos pilotos y cuentas con ambos roles; denegar acceso cruzado por URL/IDs/headers; cambiar cuenta/rol/tienda con respuestas tardías sin mezclar datos. |
| Simple/multiparada | Una parada y varias, C correcto/incorrecto/repetido en distintos contextos, recogidas B, padre parcial, última parada y métricas de unidad correcta. Conservar códigos históricos y ceros iniciales. |
| Ruta de varias órdenes/tiendas | Misma empresa/piloto, varias recogidas y entregas, permisos de reordenar, reasignación/cancelación concurrentes y resolución de pendientes sin cambiar C. |
| Privacidad antes de B | Revisar payloads/caché/enlaces/marcadores/polilíneas del piloto antes de cada recogida. CRM autorizado sí puede planificar completo. Confirmar una orden no desbloquea las demás. |
| Sincronización | Snapshot antiguo contra nuevo, revisiones/ETags por contexto, `304` tras revocación, detalle/modal desactualizado, resumen agregado y reconexión sin perder lista ni duplicar controladores. |
| Núcleo offline | Cortar red antes/después del commit; timeout y cierre de proceso; recuperar creación/comprobante/B/C/caso con misma intención. Rechazo definitivo frente a incierto; conflicto, revocación o reasignación con cola pendiente. |
| GPS emulador | Permisos/servicio ubicaciones, aproximada/denegada/revocada, UI→Waze/Maps, pantalla apagada, reanudación y ausencia de duplicados; usar coordenadas ficticias. Esto no sustituye GPS físico. |
| GPS Samsung | QA aislada y datos ficticios; pruebas físicas con consentimiento de ubicación/recorrido confirmado antes de realizarlas. Registrar versión Android, target, APK/firma, política de energía, duración y precisión/cadencia observadas. |
| Límites Android | Activity visible frente a background al iniciar FGS, permisos de tipo, notificaciones denegadas, ahorro de energía, proceso detenido por SO/usuario, recientes, Task Manager, force-stop y reboot. Registrar errores y huecos reales. |
| Fin de actividad | Última entrega, cancelación total, logout, cambio de cuenta y revocación: cese de captación/envío y recursos. Revocación offline limitada por política explícita, sin prometer detección instantánea. |
| Incidencias | Cada efecto configurable, reporte duplicado/pendiente, conservación de avance, permisos de notas/resolución y pausa/reanudación sin confirmar entrega. |
| Ganancias | Partidas/estados desde backend, reglas históricas por unidad, parciales y efectivo conciliado. Una confirmación C no genera pagos ni permite ejecutarlos. |
| Compatibilidad | Cliente viejo/simple, cliente nuevo/simple y multiparada, servidor sin capacidades o degradado, operación incierta ya enviada y actualización de APK preservando sesión/credenciales/datos. |

El ciclo físico de piloto necesita permiso específico para ubicación/cámara y recorrido real cuando se vaya a ejecutar; no hereda automáticamente los permisos concedidos al emulador. Usar `com.pilotgo.app.qa`, API QA y almacenamiento independiente para no sustituir instalaciones reales. La autorización funcional de GPS continuo tampoco autoriza publicar, desplegar o registrar datos físicos sin esa preparación.

## 10. Rollout y coexistencia propuestos

1. Cerrar contratos/estados, permisos, proyecciones de privacidad, intenciones y capacidades antes de implementar. La versión actual 2 sólo asegura creación idempotente/contexto tienda; no anunciar con ella multiparada, cierre solo C o GPS continuo.
2. Proponer evolución aditiva de datos: una orden histórica representa su entrega existente sin cambiar ID, A/B/C, estado, horas o importes. La materialización de parada histórica y vínculos de ruta necesita migración diseñada/probada por backend. No regenerar códigos para encajar una nueva pantalla.
3. Coexistir con clientes antiguos sólo en operaciones compatibles. Un APK legacy no puede operar una orden multiparada ni cerrar su padre desde un endpoint legado por error. El servidor debe impedirlo; un adaptador no puede traducir silenciosamente “entregar orden” a todas sus paradas. La política de versiones mínimas/capacidades y cierre legacy de simples debe concretarse.
4. Antes de nuevos comandos, comprobar soporte real y contexto autorizado. Si falta soporte, impedir el envío y explicar actualización necesaria; conservar intenciones inciertas existentes. No degradar una multiparada a una orden simple ni eliminar pendientes tras downgrade.
5. Validar por capas con fixtures, emulador y Samsung QA; después un despliegue acotado por tenant/piloto con revisión de sincronización, privacidad, batería, posiciones recientes y conciliación. Métricas y umbrales se fijan antes del piloto físico; no se declara aquí una duración o batería aprobada.
6. Distribuir preservando paquete/firma/API y almacenamiento, con versión/hashes y evidencia ligados al commit/artefacto exactos. Activar funciones sólo con backend/migración compatibles. Un rollback que quite capacidad debe dejar de ofrecer la acción nueva sin perder datos ya creados ni interpretar mal intenciones pendientes.

Este documento no publica cambios ni autoriza automáticamente esa distribución. No se han creado prototipos visuales en esta fase.

## 11. Definiciones que todavía requieren cierre

- Alcance exacto de recogidas B, secuencias obligatorias/libres y reordenamientos permitidos; comportamiento ante parciales, devolución y paradas canceladas, incluido el cierre y denominadores del padre.
- Modalidades simple/escalas/provincia a provincia: campos, alcance geográfico y validación; envío de C a cada destinatario sin revelar todos los códigos al piloto.
- Matriz de roles operativos, permisos de reordenar/reasignar y resolver/notar incidencias, políticas de bloqueo y reanudación.
- Contratos reales de snapshots/capacidades/versiones, alcance de ETags, acciones idempotentes y recuperación de resultados; política offline, caducidad de autorización y conservación de datos.
- Geografía, precisión y consentimiento; mecanismo nativo/plugin comprobado, target Android final, frecuencia adaptativa, revocación mientras el servicio está activo, límites de batería/red y umbral de ubicación desactualizada. No hay selección nueva de librería en este documento.
- Reglas históricas de remuneración por servicio/ruta/parada, tratamiento de parciales y quién registra/valida efectivo o pagos externos. El piloto conserva sólo los permisos definidos.
