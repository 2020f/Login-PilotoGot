# Envirapido — catálogo de vistas y contratos de interfaz

Fecha: 11 de noviembre de 2026. Estado: requisitos consolidados y propuesta de diseño; **este catálogo no acredita que los módulos estén implementados**. Se elabora después de recibir las seis referencias confirmadas. El manifiesto [REFERENCIAS.json](REFERENCIAS.json) identifica las imágenes, sus originales y hashes.

El objetivo es reproducir la identidad visual y los controles de las imágenes, completar sus acciones y derivar las vistas que faltan con el mismo lenguaje visual. La app del repartidor conserva su estilo actual y el comercio conserva su flujo existente. No se cambia código, datos, servicios ni publicación mediante este documento.

## 1. Clasificación y reglas confirmadas

- **R — Referencia:** composición, contenido o control visible en una imagen confirmada. Que una pestaña esté dibujada no define todos los detalles de su contenido interior.
- **D — Derivada propuesta:** pantalla, formulario, diálogo o contenido no mostrado, propuesto para que una acción o módulo complete su recorrido. El usuario autorizó proponer estas vistas con la misma identidad visual; eso no aprueba reglas comerciales todavía no definidas.
- **U — Aclaración confirmada:** ampliación o excepción expresamente indicada por el usuario, que prevalece sobre la literalidad de una imagen.
- **P — Pendiente:** decisión de negocio o permiso sin cerrar. Se identifica en la sección 7; no debe convertirse en un comportamiento predeterminado silencioso.

Reglas que vinculan todas las vistas:

1. Cada **empresa/tenant** tiene su propio CRM de incidencias y sus liquidaciones. El SuperAdmin de plataforma es una experiencia aparte. El selector «Operación nacional» de las referencias no autoriza consultar otras empresas.
2. Una **orden** puede ser simple o tener **escalas: varios destinatarios/paradas de entrega**. Las escalas no son pedidos independientes. Orden y parada conservan identidades distintas.
3. La recogida se confirma con **B de la orden**. Cada parada de entrega tiene su **C individual**, que se utiliza exclusivamente para cerrar esa entrega. La validación identifica también empresa, orden, parada y actor autorizado; un C no se interpreta fuera de contexto.
4. Una **ruta** puede agrupar una o varias órdenes, incluso de distintos comercios, siempre de la misma empresa. La ejecución corresponde al mismo repartidor. Una ruta planificada puede estar todavía sin repartidor.
5. Los puntos de recogida de una ruta y sus paradas de entrega son diferentes. La recogida B de cada orden precede a sus entregas C. La columna «Paradas» cuenta paradas según su unidad declarada, no órdenes.
6. El CRM autorizado puede ver el plan completo. La app **mantiene ocultos los destinos de una orden antes de confirmar su B**, incluidos marcadores, direcciones, coordenadas, búsqueda, enlaces y geometría/polilínea que permita deducirlos. Agrupar órdenes en una ruta no elimina esa restricción.
7. Reordenar paradas requiere reglas y permisos autorizados. Ordenar una tabla por ETA o nombre no reordena la ruta de ejecución.
8. El efecto de una incidencia depende del tipo y de una política configurable: **permitir continuar, pausar la parada o pausar la ruta**. No se aplica un bloqueo uniforme. Resolver una incidencia no confirma una entrega ni sustituye C.
9. **Foto, firma y comprobante de entrega quedan opacos y no funcionales**, reservados para una implementación posterior. Deben decir «Próximamente» o equivalente. No se prometerá que aparecerán al finalizar la entrega.
10. Los pagos se realizan **fuera de Envirapido**. El sistema registra, revisa, aprueba, programa, confirma y concilia; no inicia transferencias ni mueve dinero. La remuneración puede configurarse por servicio, ruta o parada, con reglas todavía por concretar.

## 2. Referencias originales y requisitos trazables

| Ref. | Archivo confirmado | Pantalla y función principal | Identidad visual nominal |
| --- | --- | --- | --- |
| I1 | [01-operaciones.png](referencias/01-operaciones.png) | Centro de operaciones, Monitoreo; cola de asignación, mapa y entregas en curso; referencia de «Mi ruta» móvil. | Sidebar oscuro; teal/turquesa en marca, selección, ruta y acciones; fondos blancos/gris claro; verde positivo, ámbar atención, rojo demora. |
| I2 | [02-detalle-pedido.png](referencias/02-detalle-pedido.png) | Detalle abierto desde «Ver», resumen, seguimiento, eventos y ficha lateral. | Misma base; teal de seguimiento/selección, verde de etapas completadas, gris de pendiente y ámbar/rojo de alerta cuando corresponda. |
| I3 | [03-pedidos.png](referencias/03-pedidos.png) | Pedidos / Listado, selección múltiple, filtros, tabla y acciones. **Es la referencia confirmada tras la corrección del usuario.** | Teal de selección/acciones; ámbar por asignar, azul recogiendo, verde en ruta/entregado, rojo demora/incidencia y gris pendiente/cancelado. |
| I4 | [04-rutas-y-zonas.png](referencias/04-rutas-y-zonas.png) | Cobertura, ficha de zona y rutas; se añade la acción obligatoria «Crear ruta» por aclaración U. | Polígonos teal, azul, morado, verde y ámbar; ruta teal; paradas completadas verdes y pendientes diferenciadas; base común. |
| I5 | [05-incidencias.png](referencias/05-incidencias.png) | CRM de incidencias, bandeja y detalle contextual con acciones. | Teal activo; rojo crítico/vencido; ámbar alta/atención; azul media; gris baja y verde de estados favorables. |
| I6 | [06-liquidaciones.png](referencias/06-liquidaciones.png) | Liquidaciones por repartidor, conciliación y detalle de partidas. Autoriza derivar vistas complementarias. | Teal de selección/acciones; ámbar revisión/diferencia; verde aprobación/pago; azul programación; neutrales para datos secundarios. |

Los colores se ajustarán visualmente contra estos PNG, incluidos fondos, bordes, transparencias y contraste. **No se asignan HEX «exactos» inventados**. Los nombres de zona, personas, referencias, cifras, horas, importes y porcentajes dibujados son ejemplos: deben sustituirse por resultados coherentes del mismo conjunto de datos y filtros.

### I1 — Centro de operaciones

- **I1.1:** shell compartido: marca Envirapido, selector operativo, breadcrumb, búsqueda por pedido/repartidor/comercio, indicador de sincronización, notificaciones y perfil. Menú: Resumen, Operaciones, Pedidos, Repartidores, Comercios, Rutas y zonas, Incidencias, Liquidaciones, Reportes, Usuarios y permisos, Configuración.
- **I1.2:** título/ayuda, fecha, zona, Exportar y Crear envío; pestañas Monitoreo, Despacho, Rutas, Automatización e Historial.
- **I1.3:** entregas hoy, en ruta, repartidores activos, entregas a tiempo e incidencias abiertas; unidades, comparaciones y series reales.
- **I1.4:** mapa con Todos, Disponibles, En entrega, Con incidencia; clústeres de flota, recogida, entrega, repartidor, ruta seleccionada, ficha contextual, capas, zoom, escala y leyenda.
- **I1.5:** cola con cantidad, Automática/Manual, cercanía y disponibilidad, identificación/origen-destino permitido, espera, estado, Asignar y Revisar alertas.
- **I1.6:** entregas en curso con búsqueda/filtro, chips, selección, pedido, comercio, repartidor, zona, estado, ETA, ordenación, Ver y menú contextual. «Ver» conduce a I2 para orden simple o con escalas.
- **I1.7:** app «Mi ruta»: estado en línea, mapa, entregas/distancia, próxima parada, datos permitidos, avance Recogido/En camino/Entregado, acción de llegada y navegación Ruta/Entregas/Ganancias/Perfil. Llegar no equivale a confirmar B o C.

### I2 — Detalle de pedido

- **I2.1:** volver, identificador de orden, estado, prioridad, comercio/zona/creación; Contactar repartidor, Reportar incidencia y menú contextual; pestañas Resumen, Seguimiento, Historial, Evidencias y Costos.
- **I2.2:** timeline Creado, Asignado, Recogido, En ruta y Entregado con horarios, diferenciando pasos pendientes de instantes reales.
- **I2.3:** GPS y antigüedad, llegada estimada, tiempo/distancia restantes; mapa con recogida/repartidor/destino/ruta, capas, zoom, escala y leyenda.
- **I2.4:** recogida y destinatario/dirección/instrucciones; historial resumido y acceso completo; repartidor/perfil, referencia RP, conexión, vehículo, unidad, última ubicación y estado; referencia del envío, paquetes, peso y servicio; notas operativas.
- **I2.5:** reserva opaca del comprobante/foto/firma según excepción U. La frase de ejemplo «Disponible al finalizar» se sustituye para no prometer una función inexistente.
- **I2.6:** mismos contenedores para orden simple y escalas; el contexto de parada determina destinatario, instrucciones, etapa, C, incidencia y ETA correspondientes. El lugar definitivo del selector/lista de paradas forma parte del diseño derivado, sin inventar otra clase de pedido.

### I3 — Pedidos / Listado

- **I3.1:** Importar, Exportar, Crear envío; pestañas Listado, Programados, Importaciones y Devoluciones.
- **I3.2:** Pedidos hoy, Por asignar, En curso —recogiendo y en ruta—, Entregados y Con incidencias.
- **I3.3:** buscar pedido/referencia/destinatario, fecha, comercio, zona, repartidor, Más filtros, Demorados; chips Todos, Por asignar, Pendientes, Recogiendo, En ruta, Entregados, Cancelados.
- **I3.4:** Columnas, multiselección y barra con cantidad, Asignar repartidor, Imprimir etiquetas, Exportar selección y Deseleccionar.
- **I3.5:** pedido, comercio, destinatario/dirección, repartidor, zona, estado, ETA, Ver y menú; columnas ordenables, filas seleccionadas, contador, tamaño de página —10 en el ejemplo—, paginación y antigüedad de actualización —4 s en el ejemplo—.
- **I3.6:** una fila/selección corresponde a una orden; una orden con varios destinatarios no se cuenta varias veces. La demora es una condición superpuesta a la etapa operativa, no una sustitución de «En ruta» o «Recogiendo».

### I4 — Rutas y zonas

- **I4.1:** Vista general, Zonas de cobertura, Planificador, Rutas activas e Historial; fecha, Crear zona y Planificar ruta; **Crear ruta obligatorio** por U.
- **I4.2:** zonas activas, rutas en curso, paradas pendientes y zonas con alta carga.
- **I4.3:** cobertura/recorridos, buscar dirección/zona, región, capas, toggles Zonas/Rutas/Repartidores, polígonos, ruta seleccionada, piloto y paradas numeradas/completadas, zoom, centrar, Dibujar zona y leyenda.
- **I4.4:** zona seleccionada, estado, código, región/área, superficie, horario, capacidad simultánea y porcentaje, repartidores disponibles y pedidos sin asignar; Editar límites, Reglas de zona y Revisar capacidad/alerta de alta demanda.
- **I4.5:** rutas de hoy, filtros Todas/En curso/Planificadas y zona, Ver todas; ruta/zona/repartidor/paradas completadas-total/distancia/tiempo restante/estado, ordenación, Ver ruta, menú y actualización. Se conserva el caso planificado sin repartidor.

### I5 — Incidencias

- **I5.1:** Bandeja, Escalaciones, Historial, Reglas de atención; Exportar y Registrar incidencia. El reporte desde la app del repartidor es obligatorio por U.
- **I5.2:** Abiertas, Críticas, Sin responsable, Fuera de plazo, Resueltas hoy; búsqueda por incidencia/pedido/ruta, prioridad/categoría/zona/responsable, chips Todas abiertas/Nuevas/En atención/Escaladas/Resueltas y «Más urgentes primero».
- **I5.3:** alerta de atención inmediata y Ver urgentes; tabla con identificación/título, pedido o ruta/zona, prioridad, responsable, estado, plazo, Ver/menú, ordenación, paginación y actualización.
- **I5.4:** lateral del caso seleccionado: prioridad/estado/retraso, pedido/ruta/comercio/repartidor/zona, responsable/Cambiar, plazo, última nota, actividad y origen automático/manual; Contactar repartidor, Registrar resolución, Añadir nota y Ver pedido.
- **I5.5:** prioridad, estado de atención, vencimiento y efecto operativo son dimensiones diferentes. Efecto por tipo configurable; una resolución administrativa no entrega una parada.

### I6 — Liquidaciones

- **I6.1:** período, Exportar, Generar liquidación; Repartidores, Comercios, Conciliación, Historial y Reglas de liquidación.
- **I6.2:** neto del período, en revisión, aprobado por pagar, programado y pagado, con moneda/importes y cantidades; buscar liquidación/beneficiario, zona, conciliación, método, Más filtros, chips de estado y Con diferencias.
- **I6.3:** selección, Columnas, identificador, repartidor/referencia RP, entregas, neto, conciliación, estado, Ver/menú, ordenación, páginas y actualización.
- **I6.4:** lateral: período/beneficiario/entregas, conciliación, servicios + incentivos − comisiones ± ajustes − efectivo aplicable, neto; método externo, cuenta enmascarada, programación, generación; Aprobar, Ver desglose, Registrar ajuste y Descargar resumen.
- **I6.5:** dinero pagado externamente. Revisar/aprobar/programar/confirmar/conciliar son operaciones de registro, no transferencias. Fórmulas, descuentos, compensaciones y devengo siguen sujetos a reglas configurables aprobadas.

## 3. Perfiles de acceso para especificar las vistas

Los siguientes son **perfiles lógicos propuestos**, no roles nuevos ya creados ni permisos automáticamente concedidos:

| Sigla | Perfil y ámbito |
| --- | --- |
| AT | Administrador de la empresa/tenant, a partir del gestor actual; solo los permisos autorizados dentro de su empresa. |
| DO | Despachador/operador de la empresa; planificación y asignación según permisos que se definan. |
| SC | Agente de atención/CRM de la empresa, con alcance de casos y acciones autorizado. |
| FT | Responsable financiero de la empresa; separar lectura, ajustes, aprobación, programación y confirmación cuando se configure. |
| RP | Repartidor autenticado; únicamente su ejecución, datos permitidos, reportes y ganancias publicadas. |
| CO | Usuario de comercio; comercios autorizados y contexto explícito de tienda, sin ampliar su alcance al resto del tenant. |
| SA | SuperAdmin de plataforma, en su panel separado. El catálogo operativo no le crea una vista global de casos/liquidaciones mezclando empresas. |

En las tablas, «AT/DO autorizado» indica candidatos de diseño, no que ambos reciban todas las acciones. Ver, editar, exportar, asignar, reordenar, resolver y aprobar son capacidades distintas. La matriz definitiva es P06.

## 4. Catálogo compacto — 45 vistas o grupos funcionales

Se agrupan variantes de un mismo recorrido para no multiplicar pantallas artificialmente. En cada grupo D se propone una composición compatible con las referencias; formularios y acciones que dependan de P no se dan por cerrados. Los IDs son de catálogo, no URLs ni nombres definitivos de tablas.

### Shell y centro de operaciones

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| SH-01 · I1–I6 · R/U | Shell Envirapido: navegación completa, empresa/ámbito, breadcrumb, sincronización, búsqueda global, notificaciones y perfil. | Cambiar módulo/ámbito permitido; buscar y abrir un resultado; abrir aviso/perfil; cerrar sesión. | Cada perfil ve solo módulos/campos autorizados; SA aparte. | Todos los destinos completan un recorrido; resultados y badges no cruzan tenants; «En tiempo real» refleja conexión/frescura reales. |
| OP-01 · I1.2–I1.6 · R | Monitoreo: cinco KPIs, mapa, cola y entregas en curso con el mismo contexto de fecha/zona. | Filtrar, exportar, crear envío, seleccionar ruta/pedido/repartidor, asignar, revisar alertas y abrir detalle. | AT/DO autorizado. | Composición y colores de I1, datos reales y filtros compartidos; sin cifras/series decorativas ni estados verdes ante error de sincronización. |
| OP-02 · I1.5 · R/D | Despacho y selección de candidato: carga, disponibilidad, conexión/GPS, cercanía y espera cuando puedan calcularse. | Alternar automática/manual, seleccionar/asignar y confirmar resultado; abrir revisión de alertas. | AT/DO con permiso de asignación. | Lista real; revalidación al confirmar; conflictos visibles; «automático» no promete optimización todavía no definida en P05. |
| OP-03 · I1.2, I4 · R/D | Pestaña Rutas del centro: resumen de rutas operativas y selección contextual. | Filtrar, abrir ruta y entrar al planificador/crear ruta según permiso. | AT/DO autorizado. | Reutiliza identidad y resultados de RZ-03/RZ-04; seleccionar desde el centro no duplica ni cambia la ruta. |
| OP-04 · I1.2 · R/D/P | Automatización: reglas y estado de ejecución de políticas operativas aprobadas. | Consultar, proponer/editar, activar o desactivar reglas y revisar efectos según permiso. | AT; DO solo alcance concedido. | Muestra qué regla se aplica y por qué; no define umbrales, radios, lotes o prioridad comercial sin P03/P05/P06. |
| OP-05 · I1.2 · R/D | Historial operativo: eventos por fecha, orden, ruta y actor autorizado. | Buscar, filtrar, abrir detalle y exportar alcance permitido. | AT/DO/SC con lectura histórica. | Paginado y trazable; mantiene orden y parada como contextos distintos; no expone C en exportaciones generales ni URLs. |

### Pedidos y detalle

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| PD-01 · I2.1/I2.2/I2.4/I2.6 · R/U | Detalle/Resumen simple o con escalas: header, prioridad, comercio, zona, etapas/horas, recogida y destinatarios, piloto, paquetes/servicio, notas. | Volver, contactar repartidor, reportar incidencia, elegir contexto de parada y navegar pestañas/menú. | AT/DO/SC autorizado; cualquier vista CO/RP tiene recorte propio. | «Ver» de I1/I3 llega al mismo pedido; cambiar parada actualiza sus bloques sin convertirla en otra orden; no se inventan horas ni destinos. |
| PD-02 · I2.3 · R/D | Seguimiento: GPS/antigüedad, llegada estimada, tiempo y distancia restantes, mapa y legibilidad de recogida/piloto/destino/recorrido. | Seleccionar tramo/parada, usar capas, zoom y centrar. | CRM autorizado; RP solo datos liberados por B de cada orden. | Diferencia estimación y evento real; GPS vencido/ETA desconocida son visibles; ninguna polilínea del RP revela destinos bloqueados. |
| PD-03 · I2.1/I2.4 · R/D | Historial completo de una orden y sus paradas, con tipo, actor, origen y tiempo. | Filtrar/ver eventos y volver a la parada/orden correspondiente. | AT/DO/SC con permiso histórico. | El resumen y el historial completo coinciden; incluye acciones y decisiones permitidas, sin alterar retrospectivamente eventos ni datos congelados. |
| PD-04 · I2.5 · R/U | Reserva visual Evidencias/comprobante de entrega, foto y firma. | Solo visualizar el estado reservado. | Mismo acceso autorizado al detalle. | Opaco/deshabilitado, texto «Próximamente»; sin cámara, carga, firma, descarga ni promesa de disponibilidad al finalizar. |
| PD-05 · I2.1, I6 · R/D/P | Costos: desglose autorizado de tarifa/cotización y referencias económicas vinculadas. | Consultar desglose y navegar a registro financiero permitido. | AT/FT; CO solo sus importes; otros perfiles según P06. | Precio de envío, cobro comercial y neto del repartidor no se presentan como equivalentes; reglas y versión visibles cuando se definan. |
| PD-06 · I1.2/I3.1 · R/D/U/P | Crear envío simple o una orden con varios destinatarios: comercio/recogida, datos congelados, paradas, instrucciones, servicio y referencia. | Añadir/editar destinatarios, revisar y confirmar; recuperar una creación incierta o comprobante ya confirmado. | CO en comercio autorizado; AT/DO solo permiso y contexto concedidos. | Preserva flujo comercial e idempotencia; una confirmación no crea varios pedidos por escala; B por orden y C por parada; condiciones de P02/P04/P07 explícitas. |
| PD-07 · I3.1–I3.6 · R | Listado: cinco KPIs, filtros, chips, columnas, tabla, ETA, selección y paginación. | Buscar, ordenar, elegir columnas/tamaño, filtrar demorados y abrir Ver/menú. | AT/DO autorizado; CO únicamente variante de su ámbito. | Coincide con I3 confirmada; demora superpuesta; cantidades por órdenes; valores/selección coherentes durante actualizaciones. |
| PD-08 · I3.1 · R/D/P | Programados: compromisos futuros y elegibilidad de despacho, diferenciados de órdenes ya liberadas. | Crear/consultar/editar programación y gestionar su liberación según reglas. | AT/DO/CO con alcance concedido. | El programa usa fecha/zona horaria explícitas; no consume ni libera capacidad por una regla inventada; vincula la misma orden cuando corresponda. |
| PD-09 · I3.1 · R/D/P | Importaciones: archivo, mapeo, revisión, resultados y errores por registro. | Importar, previsualizar/validar, confirmar, descargar errores y revisar intentos. | AT/DO/CO solo ámbito autorizado. | Identifica duplicados/reintentos y errores; no asigna por archivo otro tenant/comercio; formatos, límites y política de parciales definidos antes de confirmar. |
| PD-10 · I3.1/I3.3 · R/D/P | Devoluciones y acciones de cancelación: elegibilidad, motivo, etapa y destino de retorno cuando proceda. | Consultar, solicitar/revisar y ejecutar únicamente transiciones autorizadas. | AT/DO/SC y solicitantes según política. | No convierte «incidencia resuelta» en entrega/cancelación; no devuelve físicamente ni cierra dinero sin reglas P04 aprobadas. |
| PD-11 · I3.4/I3.5 · R/D/P | Grupo de acciones de listado: asignación masiva, columnas, etiquetas y exportación del conjunto elegido. | Seleccionar/deseleccionar, revisar alcance, asignar, imprimir etiquetas y exportar selección. | Permisos independientes de asignar/imprimir/exportar. | Muestra cuántas órdenes y de qué alcance; revalida filas cambiadas; no incluye C de cierre en etiquetas operativas por defecto; P08 resuelve selección entre páginas. |

### Rutas y zonas

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| RZ-01 · I4.1–I4.5 · R/U | Vista general: KPIs, mapa por capas, zona seleccionada y tabla de rutas. | Fecha/región/búsqueda, toggles, seleccionar, centrar, zoom/capas, crear zona, planificar y crear ruta. | AT/DO autorizado. | Reproduce I4; números pertenecen al mismo contexto; rutas planificadas sin piloto se conservan y no se declaran en ejecución. |
| RZ-02 · I4.1/I4.3 · R/D/P | Zonas de cobertura y creación/edición: geometría, nombre/código, región, superficie y estado. | Dibujar, revisar, crear/editar y activar/desactivar según permiso. | AT/DO con gestión de cobertura. | Polígono y ficha coinciden; solapamientos, límites y puntos sin geocodificar siguen P05; editar una zona no altera silenciosamente una ejecución activa. |
| RZ-03 · I4.1, U · R/D/U/P | Planificador y **Crear ruta**: una o más órdenes del mismo tenant, recogidas B, paradas C, secuencia, asignación opcional y estimaciones. | Añadir/revisar órdenes y puntos, planificar, crear y asignar cuando corresponda; reordenar solo si está permitido. | AT/DO con permisos de crear/asignar/reordenar separados. | Crear ruta completa un recorrido real; admite órdenes de distintos comercios del mismo tenant y ruta planificada sin repartidor; no viola precedencia B→C ni duplica órdenes. |
| RZ-04 · I4.5, I2 · R/D/U/P | Rutas activas / Ver ruta: recogidas, entregas, progreso, piloto, estado, distancia/tiempo y contexto de órdenes/paradas. | Buscar/filtrar/ordenar, abrir ruta/orden/parada y acciones autorizadas del menú. | AT/DO; variante RP limitada a su ejecución. | Paradas completadas/total son paradas, no pedidos; estado de ruta/orden/parada no se confunde; tramo y destinatario seleccionado coinciden. |
| RZ-05 · I4.1 · R/D | Historial de rutas: plan, ejecución, cambios y resultados asociados. | Buscar, filtrar, abrir detalle histórico y exportar según permiso. | AT/DO con lectura histórica. | Puede reconstruir qué ruta se ejecutó y qué cambió, sin recalcular retrospectivamente el recorrido con direcciones nuevas. |
| RZ-06 · I4.4 · R/D/P | Ficha/reglas de zona: horario, capacidad, porcentaje, carga, pilotos disponibles, sin asignar y alta demanda. | Editar límites/reglas, revisar capacidad y alertas. | AT; DO según permiso. | Unidad de 168/240 y fórmula del porcentaje declaradas; cuota SaaS y capacidad operativa separadas; umbrales y excepciones P05 visibles. |

### CRM de incidencias

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| IN-01 · I5.1–I5.4 · R/U | Bandeja y lateral del caso: KPIs, filtros/urgencia, tabla, selección, contexto, responsable, plazo, última nota y actividad. | Buscar/filtrar/ordenar/paginar, ver urgente/caso/pedido, contactar y exportar. | AT/SC; DO según alcance. | Respeta I5; conserva incidencia seleccionada; prioridad/estado/plazo/efecto no se fusionan; contadores no se suman como categorías disjuntas si se solapan. |
| IN-02 · I5.1/I5.4, U · R/D/U/P | Grupo de registro/atención: formulario, nota, cambio de responsable y resolución; contexto orden/parada/ruta y origen. | Registrar manualmente, asignar/cambiar responsable, añadir nota y resolver con motivo. | SC/AT; DO según permiso; RP usa AP-02 para reportar. | Cada acción tiene actor y ámbito; resolver no entrega; aplicar pausa/continuación depende del tipo y permiso configurados, sin bloqueo uniforme. |
| IN-03 · I5.1/I5.2 · R/D/P | Escalaciones: casos escalados, destino de atención, motivo y plazo. | Revisar, escalar o derivar y abrir caso según reglas. | SC/AT con escalación; receptores autorizados. | Escalación y criticidad son distintas; transiciones y responsables siguen P03/P06, con trazabilidad de origen automático/manual. |
| IN-04 · I5.1 · R/D | Historial de incidencias: actividad/resoluciones y relación con ejecuciones. | Consultar, filtrar, abrir antecedentes y exportar alcance permitido. | AT/SC; otros con lectura concedida. | Resueltas hoy usa fecha de resolución y día comercial; historial no altera el estado real de una entrega ni revela casos ajenos. |
| IN-05 · I5.1/I5.5, U · R/D/U/P | Reglas de atención por tipo: prioridad, responsables, tiempos, escalación y efecto operativo. | Configurar/revisar/activar políticas con permiso explícito. | AT y perfiles habilitados en P06. | Permitir continuar/pausar parada/pausar ruta se configura por tipo; no se inventan tiempos, reglas activas ni permisos de desbloqueo. |

### Liquidaciones

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| LI-01 · I6.1–I6.4 · R/U | Repartidores: período, KPIs monetarios/cantidades, filtros, tabla y lateral con partidas, conciliación, estado y método externo. | Seleccionar/ver, ordenar/paginar, columnas, exportar, abrir desglose/ajuste/aprobación y descargar resumen. | FT/AT con capacidades específicas. | Replica I6 con datos reales; fila, lateral, período y partidas coinciden; cuenta enmascarada; conciliación no se interpreta como pago. |
| LI-02 · I6.1 · R/D/U/P | Comercios: liquidaciones/estados de cuenta de comercios, cargos y registros externos autorizados. | Consultar/filtrar, ver desglose, exportar y registrar operaciones permitidas. | FT/AT; CO solo sus comercios. | No expone remuneración privada de otros actores; separa cobro del comercio, efectivo y pago al repartidor; definición comercial pendiente P07. |
| LI-03 · I6.1/I6.2 · R/D/U/P | Conciliación: diferencias y correspondencia entre operaciones, importes y pagos externos registrados. | Revisar diferencias, registrar correspondencias, notas y resultado de conciliación autorizado. | FT con conciliación; AT según matriz. | No mueve dinero; diferencias quedan justificadas y no desaparecen por cambiar un badge; efectivo a compensar sigue regla aprobada. |
| LI-04 · I6.1 · R/D | Historial financiero: generación, ajustes, aprobación, programación, confirmación y conciliación. | Buscar/filtrar, consultar detalle y descargar registros autorizados. | FT/AT; RP/CO solo variantes propias publicadas. | Conserva actor, fecha, referencia y resultado; una actualización de reglas no reescribe liquidaciones anteriores. |
| LI-05 · I6.1/I6.5, U · R/D/U/P | Reglas de liquidación: remuneración configurable por servicio, ruta o parada, moneda/período y conceptos. | Revisar/configurar reglas y vigencia según permiso. | FT/AT específicamente autorizado. | Declara unidad y versión; no inventa importes, tasas, comisiones, impuestos, compensaciones ni condiciones de devengo. |
| LI-06 · I6.1/I6.4, U · R/D/U/P | Generación/previsualización y desglose de operaciones elegibles, beneficiario, período, conceptos y neto. | Seleccionar criterios, revisar detalle, generar y descargar resumen. | FT/AT con generación. | Se puede explicar cada partida; identifica qué orden/ruta/parada se remunera; no duplica liquidación por reintento ni trata C como prueba de cobro. |
| LI-07 · I6.4, U · R/D/U/P | Grupo complementario: ajuste con motivo, revisión/aprobación, programación y confirmación de un pago efectuado fuera. | Registrar ajuste, aprobar, programar, confirmar pago externo y consultar referencia/método. | Capacidades financieras separadas; RP/CO no aprueban por ser beneficiarios. | Aprobar no transfiere ni marca pagado; confirmar exige datos/alcance acordados; rechazo/conflicto no muestra éxito; cualquier corrección conserva trazabilidad. |

### Menús sin una pantalla completa de referencia

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| OT-01 · menú I1–I6 · D/U | Resumen del tenant: operación, compromisos y accesos de su empresa; distinto del dashboard global SA. | Filtrar contexto, abrir operación/alertas/reportes autorizados. | AT/DO según lectura. | Indicadores del mismo tenant y unidad definida; navegación mantiene contexto; no mezcla datos del SuperAdmin. |
| OT-02 · menú I1–I6, ficha I2 · D | Repartidores: listado, perfil/ficha, vehículo/unidad y disponibilidad/carga/GPS. | Consultar, buscar y gestionar alta/edición/baja/permisos concedidos. | AT; DO consulta/operación según matriz; RP perfil propio. | Cuenta activa, conexión y carga se distinguen; conserva bajas seguras y no desactiva con trabajo abierto; el enlace Ver perfil de I2 funciona. |
| OT-03 · menú I1–I6 · D/U | Comercios: listado, ficha, contactos, direcciones, reglas autorizadas y accesos. | Buscar/consultar, crear/editar y desactivar/reactivar con controles. | AT y CO en su ámbito; DO según permiso. | Conserva cuotas/bajas seguras y pertenencias; editar agenda no cambia snapshots de pedidos existentes. |
| OT-04 · menú I1–I6 · D/P | Reportes: productividad, tiempos, puntualidad e incidencias con filtros y exportación. | Consultar períodos, desglosar, filtrar y exportar resultados autorizados. | AT/DO/FT según datos y permiso. | Cada métrica declara unidad/cohorte/período; no presenta puntualidad sin SLA ni hace pasar prueba de flota por capacidad productiva. |
| OT-05 · menú I1–I6 · D/P | Usuarios y permisos: miembros, comercios, perfiles, capacidades y acceso propio. | Invitar/crear, revisar/editar/revocar pertenencia o permisos y consultar estado de acceso. | AT con gestión de equipo; SA en su administración separada. | Las autorizaciones se comprueban en servidor; no permite asignarse privilegios ni revocar una tienda bloqueando globalmente una identidad multitienda. |
| OT-06 · menú I1–I6 · D/P | Configuración del tenant: identidad visible, contactos y políticas operativas/horarias/avisos habilitadas. | Consultar/editar parámetros permitidos y navegar a reglas específicas. | AT; demás perfiles solo capacidad concedida. | Distingue apariencia de reglas comerciales; secretos de servidor/proveedor no se muestran; no concede poderes SA al tenant. |

### App del repartidor y conservación del comercio

| ID · referencia/clase | Propósito y datos | Acciones | Acceso propuesto | Criterio de aceptación |
| --- | --- | --- | --- | --- |
| AP-01 · I1.7, I2/I4, U · R/D/U/P | Ruta: estilo actual de la app, recorrido propio, próxima recogida/entrega permitida, carga/progreso y disponibilidad. | Navegar/centrar, abrir parada, registrar llegada cuando proceda, confirmar B y C en sus pasos y reordenar solo si está autorizado. | RP sobre su ejecución/tenant. | No convierte llegada en entrega; oculta destinos y geometría antes del B correspondiente; rutas de distintos comercios respetan precedencia y permisos. |
| AP-02 · I1.7/I5, U · R/D/U/P | Entregas: órdenes/paradas propias, detalle y **Reportar incidencia obligatorio** con contexto. | Consultar/filtrar, recoger con B, cerrar cada entrega con su C, reportar incidencia y consultar su atención permitida. | RP asignado; reportes y lectura limitados por política. | Escalas conservan una orden; una C no cierra otras paradas; una incidencia aplica su efecto configurado y no filtra datos de otro reparto. |
| AP-03 · I1.7/I6, U · R/D/U/P | Ganancias: períodos, partidas y estados propios publicados a partir de liquidaciones/reglas configuradas. | Consultar/desglosar y descargar resumen propio si se habilita. | RP beneficiario autenticado. | Importes coinciden con la liquidación autorizada y su unidad; no puede aprobar, ajustar o ejecutar pagos; datos bancarios permanecen enmascarados. |
| AP-04 · I1.7, U · R/D/U/P | Perfil: identidad propia, disponibilidad, vehículo/unidad, preferencias y sesión autorizadas. | Consultar/editar lo permitido, seleccionar perfil autorizado y cerrar sesión. | Usuario autenticado en su perfil habilitado. | Preserva sesión/credenciales y estilo actual; no cambia identidad técnica/dominio por rebranding; no permite asignarse roles. |

**Comercio:** PD-06 y OT-03 amplían únicamente lo aprobado sin sustituir el flujo actual de tienda activa, destinatarios, creación recuperable, comprobante/QR y seguimiento. La reserva de foto/firma no deshabilita el QR de recogida existente ni cambia su propósito. Las vistas CO deben mantener el contexto de comercio explícito y no recuperar datos de otro comercio por una selección global cambiante.

## 5. Contratos transversales de la interfaz

### Identidad visual y adaptación

- Web: respetar proporciones, agrupaciones, estados seleccionados, tipografía jerárquica, iconografía, tarjetas, tablas, bordes y paleta nominal de I1–I6. Sustituir la marca de ejemplo por **Envirapido**; ajustar tokens visuales contra las imágenes, no por estimaciones declaradas como colores exactos.
- Conservar significado de colores entre módulos. Color solo no identifica demora, prioridad, selección, completitud ni GPS: acompañar texto/icono/leyenda.
- Las vistas D emplean el mismo shell, espaciado y componentes. No se afirma que una captura haya mostrado sus formularios interiores.
- Escritorio conserva tablas/mapa/panel lateral. En móvil: navegación accesible, contenido legible, mapa utilizable, panel contextual adaptable, formularios y cuotas con wrap, tablas desplazables dentro de su contenedor; sin cortar el documento completo horizontalmente. No se reduce texto a una letra por columna.
- Teclado, foco, Escape, lectura de etiquetas, estados de carga y contraste deben funcionar. Cerrar/volver conserva contexto cuando corresponda.

### Datos, filtros, actualización y selección

- Período, fecha, zona, región, comercio, repartidor, búsqueda, chips, orden y tamaño de página determinan un conjunto de datos explícito. KPIs indican si aplican esos filtros o un alcance distinto; se evita mostrar totales de otro ámbito sin declararlo.
- Persistir contexto al volver de detalle. Paginar/ordenar no cambia IDs ni secuencia de ejecución. Los valores de ejemplo y números de página no se codifican como resultados reales.
- La selección se vincula a IDs permanentes autorizados. Un refresco no añade registros nuevos a una selección ni sustituye silenciosamente sus objetivos. Mostrar cantidad y alcance —página/conjunto—; persistencia entre páginas/filtros y selección total quedan P08.
- Actualización sin solicitudes solapadas, con recuperación de red/visibilidad y protección de formularios/modales. «Actualizado hace…» se calcula con éxito de sincronización real; I3/I4/I5/I6 muestran el ejemplo de cuatro segundos, que debe conciliarse con la política final de actualización y carga.
- Cada acción contextual/masiva revalida tenant, permisos, estado y elegibilidad actuales. Una orden/ruta que cambió mientras se veía la tabla puede producir conflicto; devolver resultado comprensible por elemento y no afirmar éxito global ficticio. Los reintentos no deben duplicar creación, asignación, ajuste o registro de pago.
- Exportar vista, selección, detalle y resumen son alcances distintos y requieren permiso. Exportación/paginación/búsqueda respetan las mismas autorizaciones del servidor; no descargan C, cuentas completas o datos de otros actores por omisión.
- Guardar instantes con una referencia inequívoca y transportar UTC explícito; mostrar el día comercial dominicano donde corresponde. Separar fechas de agenda/período de instantes. Si una configuración futura cambia zona horaria, esa decisión debe ser explícita, sin doble conversión.

### Mapas, geometría y ETA

- Separar posición recibida, hora de recepción, frescura y estimación. Sin GPS válido no se dibuja una ubicación ficticia ni se anuncia presencia en vivo. Ubicación vencida, falta de proveedor y fallo de red tienen estados visibles.
- Zona, ruta, orden, punto de recogida y parada de entrega conservan contextos diferentes. Toggles/capas no pierden la selección; tabla, mapa y ficha comparten el mismo elemento autorizado.
- Etiquetas, números, completitud, clústeres y leyendas representan datos reales. Polígono/cobertura no autoriza una entrega fuera de política ni cambia automáticamente una ejecución activa.
- Distancia calculada no acredita ETA, tráfico o tiempo restante. Indicar origen/fecha de la estimación y estado desconocido cuando no hay datos. Llegada estimada y llegada registrada son campos distintos.
- **Privacidad por orden dentro de la ruta:** que el CRM planifique un recorrido completo no habilita al RP a deducir destinos anteriores a su B por polilínea, bbox, marcador, instrucciones, exportación, búsqueda o respuesta API. El recorte se hace también en servidor.

### Permisos y consistencia

- Ocultar un botón no es autorización. Comprobar en servidor empresa, pertenencia del recurso al padre, actor/perfil, capacidad y estado; incluir endpoints SSR, parciales, APIs, búsqueda, mapas, archivos/exportaciones y acciones masivas.
- Una parada solicitada debe pertenecer a la orden indicada y una orden a la ruta/tenant cuando corresponda. Un identificador introducido manualmente no amplía acceso.
- Las mutaciones web conservan protección CSRF; los códigos no van en URLs. Lecturas sensibles no se almacenan en caché compartida ni se reutilizan tras cambiar cuenta/tenant/comercio.
- La identificación visual del usuario/empresa, roles y menús debe venir del contexto autenticado. La selección de perfil solo ofrece roles permitidos.
- Preservar integridad ante carreras: asignación/bajas/capacidad y cierre no se deciden con la fotografía antigua de una tabla. Conservar auditoría y datos congelados pertinentes; no modificar el historial para aparentar un estado nuevo.

## 6. Unidades y coherencia de indicadores

| Indicador o contador | Contrato requerido |
| --- | --- |
| Pedidos / órdenes | Cuenta órdenes; una orden con varios destinatarios sigue siendo una. Declarar fecha de creación, ejecución o compromiso utilizada. |
| Paradas / entregas | Declarar si cuenta destinatarios, visitas totales o servicios; no mezclar recogidas B con entregas C ni órdenes con paradas. |
| En curso | Mostrar la composición acordada de recogiendo/en ruta; una demora no elimina la etapa subyacente. |
| Demorados | Condición calculada sobre un compromiso/umbral aprobado. No crear un estado operativo excluyente solo para copiar el badge rojo. |
| Puntualidad y comparaciones | Definir SLA, denominador, exclusiones y período comparado; porcentajes/pp/series no se generan de constantes de la imagen. |
| Repartidores activos/disponibles | Diferenciar cuenta activa, recepción de ofertas, jornada/conexión, carga y frescura GPS; «online» no implica automáticamente capacidad. |
| Incidencias | Abiertas puede contener críticas, sin responsable y vencidas simultáneamente. Resueltas hoy usa resolución, no creación. |
| Capacidad de zona | Definir unidad simultánea y horario; no reutilizar la cuota SaaS. Porcentaje y conteos se derivan de esa unidad. |
| Paradas de ruta | Contar paradas reales según tipo declarado; progreso de órdenes y progreso de paradas no son la misma fracción. |
| Neto y estados financieros | Declarar moneda, período, unidad de remuneración y cantidad de liquidaciones. Conciliación y estado financiero son ejes separados. Evitar doble conteo entre aprobado por pagar/programado/pagado según su definición final. |
| Efectivo y entrega | C acredita el cierre de una entrega; no acredita recepción de efectivo, transferencia ni conciliación financiera. |

Los ejemplos no resuelven todas las definiciones: la demora debe conservar etapa aunque se pinte de rojo; «Por asignar» y «Pendientes» no se equiparan por comodidad; las sumas se validan con la semántica aprobada y no con etiquetas ambiguas.

## 7. Decisiones de negocio todavía pendientes

| ID | Decisión pendiente | Vistas afectadas |
| --- | --- | --- |
| P01 | Diferencia entre Por asignar y Pendientes; correspondencia entre etapas de orden, parada y ruta; evento propio de En ruta frente a recogida. | OP-01/02, PD-01/02/07/08, RZ-04, AP-01/02. |
| P02 | Secuencia permitida, reordenación, restricciones por recogida, capacidad y cambios durante ejecución; resultado agregado de órdenes con entregas parciales/fallidas. | PD-01/06, RZ-03/04, AP-01/02. |
| P03 | Compromisos/SLA y umbrales: demora, puntualidad, primera atención/resolución, pausas del reloj, prioridad y escalación. | OP-01/04, PD-02/07, IN-01/03/05, OT-04. |
| P04 | Cancelación, reintentos, devolución/retorno, custodia y elegibilidad por etapa; efectos y costos comerciales correspondientes. | PD-08/09/10/11, RZ-04, IN-02/05, LI-02/05/06. |
| P05 | Capacidad, disponibilidad, horarios, zonas solapadas/límites, región, radios/cercanía, elegibilidad de lote y reglas de automatización; fuentes de ETA. | OP-02/03/04, RZ-01/02/03/04/06, AP-01. |
| P06 | Matriz de roles/capacidades/campos, responsables CRM, quién pausa/reanuda/reordena, datos visibles y permisos financieros/exportación. | Todos los grupos, especialmente OT-05, IN-05, LI-03/05/07. |
| P07 | Reglas comerciales: servicio/ruta/parada remunerables, devengo, períodos, importes, incentivos, comisiones, ajustes, redondeos, efectivo, diferencias y correcciones. No incluye integración que transfiera dinero. | PD-05/06, LI-01–07, AP-03, OT-06. |
| P08 | Selección entre páginas/cambio de filtros, selección total, resultados parciales de acciones/importaciones, formatos/límites de archivos y políticas de exportación. | PD-09/11, LI-01/06/07, IN-01 y listados/exportaciones. |
| P09 | Campos/acciones finales de los menús sin captura completa y contenidos derivados de pestañas/diálogos; datos editables del perfil, vehículo y unidad. | OT-01–06, AP-04 y variantes D. |

Estas decisiones no impiden consolidar estilo y navegación, pero sí impiden declarar terminado el comportamiento que dependa de ellas. La ampliación futura de base de datos está autorizada en alcance; este catálogo no fija su arquitectura ni ejecuta migraciones.

## 8. Revisión de aceptación del catálogo

- Hay **45 grupos** con ID, origen, propósito/datos, acciones, permisos propuestos y criterio verificable. Son grupos de recorrido, no 45 capturas que el usuario haya entregado.
- I1–I6 conservan sus controles y las aclaraciones U prevalecen: Envirapido; CRM/liquidaciones por tenant; escalas dentro de una orden; rutas agrupadas; C por parada; Crear ruta; reporte del repartidor; remuneración configurable y pagos externos.
- No se entregarán enlaces vacíos ni botones que simulen éxito. Un dato realmente desconocido se presenta como tal; eso no sustituye implementar la función requerida.
- La única reserva funcional expresamente definida aquí es foto/firma/comprobante de entrega. No se reutiliza esa excepción para dar por terminados otros módulos pendientes.
- Los criterios comunes de filtros, frescura, selección, contexto de parada, permisos, estado vacío/error y responsive aplican a cada grupo, aunque no se repitan íntegros en todas las filas.
- El siguiente trabajo de diseño/implementación debe relacionar cada grupo con sus requisitos I y cerrar las P necesarias antes de aprobar su lógica. Este documento por sí solo no publica, transfiere dinero, instala una app ni cambia producción.
