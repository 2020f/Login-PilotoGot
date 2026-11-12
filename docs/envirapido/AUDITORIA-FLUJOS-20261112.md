# Envirapido — auditoría de procesos y flujo completo

12 de noviembre de 2026 · App comercio/piloto, CRM, API, SQL Server y administración global.

**El lote local todavía requiere correcciones antes de publicarse.** La auditoría encontró dos defectos P1 demostrados: un gestor puede cambiar la contraseña global de una identidad compartida y acceder a otra empresa; la generación de liquidaciones de comercio ignora la selección explícita de cobros. También confirmó fallos de actualización y recuperación de la app, descubrimiento de pedidos activos, coordenadas y controles del panel.

Se revisaron todos los dominios del recorrido, incluidas sus excepciones. Eso no significa que cada combinación de estados o cada condición de Android se haya ejecutado. Este informe distingue reproducción nueva, lectura de fuente, evidencia anterior conservada y pruebas pendientes. **No es una aprobación de producción ni una afirmación de funcionamiento perfecto.**

La auditoría conservó el código del producto, las pruebas existentes, los APK y los datos del servidor QA. Sólo se agregaron documentos y probes externos bajo `artifacts/auditoria-flujos-20261112`. No hubo commit, push, despliegue ni cambio de claves o configuración.

## 1. Base y reglas conservadas

| Componente | Árbol auditado |
| --- | --- |
| Servidor y panel | `C:\Users\Fernelis\source\repos\Login-PilotoGot`, rama `codex/confiabilidad-panel-app`, HEAD `36d15c7696f1b0a8018251cbcdd6be9897ae7248`, con cambios locales autorizados posteriores. |
| Flutter | `C:\Users\Fernelis\Desktop\pilotGOapp`, misma rama, HEAD `8ca95af1ea7c2481be73e38d7abf3c0156ceaae9`, árbol local Envirapido `2.0.0+5`. |
| Integridad | 331 archivos del manifiesto del servidor y 78 de Flutter conservan sus hashes. No se trataron los HEAD como identidad del árbol modificado. Huella Flutter `9631f27820cb1fa055fd14be04127e0b346918be3fc05df1f459300ef9cec89e`. |
| SQL nuevo de auditoría | Instancia LocalDB `PilotGoVisualQA_20261111`, bases GUID propias creadas y eliminadas por el arnés. Ningún probe mutó la base que usa el usuario en el puerto 5019. |
| Lecturas del host QA | Sólo GET de tres rutas de Identity para comprobar disponibilidad; sin formularios, correos ni cambios de cuentas. |

Se mantuvieron estas decisiones como criterios: aislamiento de empresa y contexto explícito de comercio; una orden simple o con varios destinatarios y un C por parada; B para recoger y únicamente C para cerrar cada entrega; cuatro dígitos nuevos sin modificar códigos emitidos; rutas de una o varias órdenes de la misma empresa; privacidad del destino antes de su B; reorden autorizado; llegada distinta de entrega; incidencias con efecto configurable; reintento/devolución autorizados y trazables; efectivo entregado siempre a la empresa; pagos externos sólo registrados; rutas cerradas fuera de Ruta y conservadas en historial.

**Foto, firma y comprobante de entrega atenuados son una excepción autorizada. No se cuentan como fallo.** Tampoco se inventaron tarifas, perfiles financieros o políticas pendientes para declarar incumplimientos.

## 2. Hallazgos prioritarios reproducidos con SQL Server

### A01 · P1 — reset de contraseña cruza el límite de identidad entre empresas

Un usuario principal de comercio A también está autorizado en un comercio B. El gestor A valida correctamente que su tienda es de A, pero cambia la contraseña del `IdentityUser` global. Puede iniciar sesión con esa credencial, seleccionar B y leer sus destinatarios. El CSRF y el filtro normal por tenant no impiden esta vía porque la sesión resultante es la identidad compartida legítima.

Se reprodujeron por HTTP de TestServer la asignación de membresía B, el reset desde A, el nuevo login y la lectura de `Destino privado B`, con SQL Server y formularios CSRF válidos. No se usaron cuentas reales. Es una condición de identidad compartida; **no** se afirma que cualquier gestor pueda acceder directamente a cualquier empresa.

Fuente: `Login/Controllers/SupervisorController.cs:249–273` y `Login/Controllers/GestorController.cs:199–220`. Evidencia: [probe y TRX](../../artifacts/auditoria-flujos-20261112/panel/probes/TestResults/shared-identity-reset-sql.trx), caso `SharedIdentityResetProbe`.

Corrección requerida: separar la administración de una pertenencia comercial de la propiedad de la credencial global. El gestor de una empresa no debe poder tomar una identidad que conserva accesos externos. Deben existir recuperación o invitación verificadas por el titular, revocación de pertenencia y pruebas cruzadas de acceso/credenciales.

### A02 · P1 — una liquidación incorpora cobros que el operador no seleccionó

Con dos cobros recibidos por la empresa, de 100 y 900, se envió una generación para incluir sólo el ID del cobro de 100. La respuesta HTTP fue 200 y generó neto 1.000, vinculando también el de 900. `CobroIds` forma parte del contrato y del formulario, pero no limita la consulta financiera. El sistema no transfiere dinero; el defecto afecta el registro, el neto y qué cobros quedan consumidos por esa liquidación.

Fuente: `Login/Views/Envirapido/_ModuleDialogs.cshtml:24`, `Login/Application/Operations/OperationsContracts.cs:26–27` y `EnvirapidoOperationsService.Finanzas.cs:110–117`. Evidencia: [TRX SQL del caso](../../artifacts/auditoria-flujos-20261112/panel/probes/TestResults/cash-selection-sql.trx), `CashSelectionProbe`.

Corrección requerida: aplicar la selección explícita y revalidar tenant, comercio, período, recepción y elegibilidad bajo la misma transacción; definir claramente el alcance cuando no se seleccione nada. El neto y el desglose deben explicar exactamente los cobros incluidos. Cubrir selección parcial, ID ajeno/no elegible, reintento y concurrencia.

## 3. Registro consolidado del resto de defectos

P2 significa una corrección funcional necesaria; P3 identifica una inconsistencia de presentación/contexto de menor impacto. «Fuente» confirma la condición del código, pero no equivale a ejecución del recorrido completo. Los informes por componente incluyen líneas, escenarios y límites detallados.

| ID / prioridad | Proceso y resultado observado | Evidencia / alcance |
| --- | --- | --- |
| **A03 · P2** | Un pedido activo antiguo desaparece del comercio al existir 200 pedidos más recientes, aunque estén terminados. El resumen API aún indica una asignada. La web aplica el mismo límite y calcula sus contadores con ese subconjunto. El pedido sigue en la BD y en CRM/piloto. | Backend B1, panel PANEL-04. HTTP API reproducido en SQL y SQLite; consulta web revisada. `TiendaAppService.cs:160–164`, `TiendaController.cs:449–453`. |
| **A04 · P2** | La geocodificación exitosa sirve para cotizar 2 km/precio 20, pero sus coordenadas se descartan antes del snapshot. Recogida/destino/parada quedan sin latitud/longitud y el trazado/ETA no puede usar esos puntos. B/C siguen siendo posibles y la dirección textual se conserva. | Backend B2, SQL y SQLite, proveedor simulado sin Google externo. `OrdenService.cs:558–587`, `:141–171`; `RecorridosService.cs:37–41`. |
| **A05 · P2** | Eliminar un destinatario usado únicamente en escalas devuelve HTTP500. La guarda sólo busca cabeceras, mientras la parada mantiene una FK restringida. La FK conserva el contacto y la orden. | Backend B3, panel PANEL-03. SQL y SQLite por API; guardas web equivalentes por fuente. `TiendaAppService.cs:143–151`, `ApplicationDbContext.cs:96–98`. |
| **A06 · P2** | La app comercio recibe órdenes nuevas, pero si falla sólo el resumen conserva la lista anterior. Un recurso accesorio impide publicar una lectura válida. | APP-02, controlador real con HTTP simulado. `tienda_orders_controller.dart:54–72`. |
| **A07 · P2** | La notificación propia de una asignación manual espera a que termine la consulta de ofertas. Otra reconstrucción podría mostrarla antes; se confirmó el acoplamiento y el retraso de notificación, no una pérdida permanente ni necesidad de logout. | APP-03 con oferta en espera. `pilot_orders_controller.dart:59–86`; timeout general 20 s en `api_client.dart:27`. |
| **A08 · P2** | Salir del formulario mientras llega el QR puede borrar la recuperación confirmada sin mostrar el comprobante. Al volver aparece una orden nueva vacía. La orden y su QR siguen consultables desde el listado. | APP-04, widget y HTTP simulados. `crear_orden_screen.dart:139–145`, `tienda_repository.dart:56–61`. No se demostró duplicación ni pérdida de orden. |
| **A09 · P2** | La app habilita Registrar llegada en varios puntos futuros; el servidor sólo acepta la primera visita pendiente y rechaza los demás con409. La guarda del servidor funciona; la acción ofrecida no coincide con ella. | APP-01 más contrato servidor leído. `operations_models.dart:52–53,136–142`; `operations_views.dart:162–170`; backend Rutas.cs:154–155. |
| **A10 · P2** | Una empresa sin reglas activas no permite reportar incidencias desde la app: categorías vacías y envío deshabilitado. El backend sí admite un reporte sin regla, con Continuar y sin SLA automático. | APP-05, widget real simulado; `operations_views.dart:590–596,646–698`, backend Incidencias.cs:30–37. Falta coherencia entre catálogo y reglas. |
| **A11 · P2** | Una incidencia ya confirmada y presente en el snapshot conserva su clave global si sólo falla GET devoluciones. Abrir un reporte en otra orden restaura el anterior y bloquea temporalmente el nuevo contexto. Es recuperable; no pierde ni duplica el caso. | APP-06, un POST con ACK y devoluciones503. `operations_views.dart:507–568`; `pilot_operations_controller.dart:102–129`. |
| **A12 · P2** | Si el GPS vence su autorización temporal durante un corte de red en segundo plano, se detiene correctamente por privacidad. Tras recuperar red no vuelve el seguimiento continuo hasta regresar a la app. | ROOT-02, probe Flutter de ciclo de vida/controladores con lease y red simulados. No prueba física Android ni recomendación de eludir el lease. |
| **A13 · P2** | «Cambiar Tienda» web lleva a una vista fija de «Acceso no disponible» aunque ambas tiendas estén activas. El GET obtiene opciones pero la vista no las usa; el POST de selección sí funciona. | PANEL-05, fuente y GET observado durante probe SQL. `SeleccionarTienda.cshtml:20–30`, `TiendaController.cs:67–86`. |
| **A14 · P2** | La búsqueda del panel promete referencia comercial, pero el predicado no consulta `DatosEnvio.Referencia`. | PANEL-06, fuente. `_Filtros.cshtml:5`, `EnvirapidoController.cs:376`. |
| **A15 · P2** | Monitoreo conserva el JSON inicial de recorridos del mapa mientras actualiza tabla y flota. Crear/reordenar/recoger/finalizar puede dejar el mapa de I1 con el recorrido anterior. | PANEL-07, fuente; efecto visual inferido, sin nueva sesión de navegador. `Index.cshtml:26`, `envirapido.js:98,104–118`. Diferente del fix anterior del mapa I4. |
| **A16 · P2** | Reintentar un lote parcialmente asignado vuelve a enviar los IDs que ya tuvieron éxito. Puede acabar con ambos asignados, selección vacía y modal aún en error. | PANEL-08, callback real ejecutado en Node con HTTP simulado y contrato servidor que rechaza orden ya asignada. `envirapido.js:79–83`. |
| **A17 · P2** | Historial de incidencias carga sólo Resueltas, pero el filtro inicial de JavaScript oculta Resueltas. La tabla parece vacía hasta pulsar ese chip. | PANEL-09, predicado real reproducido en Node y fuente Razor. `_Incidencias.cshtml:4`, `envirapido-modulos.js:87–88`. |
| **A18 · P2** | Reportar incidencia desde una parada seleccionada del detalle conserva ordenId pero pierde paradaId. Sólo se recupera si el operador escribe el ID manualmente. El alcance puede diferir del punto consultado. | PANEL-10, fuente. `Pedido.cshtml:27`, `EnvirapidoController.cs:152,179`, `envirapido-modulos.js:124`. No se presume una política de pausa concreta. |
| **A19 · P2/P3** | Ordenar montos/fechas compara texto presentado: 1.200 precede a900 y02/dic a30/nov en orden ascendente. No cambia valores almacenados. | PANEL-11, comparador real reproducido en Node. `envirapido-modulos.js:89`. |
| **A20 · P2** | SuperAdmin cuenta Cancelada, Devuelta y cierres con devoluciones como órdenes abiertas al excluir sólo Entregada. | PANEL-12, fuente. `SuperAdminController.cs:48`, Dashboard.cshtml:10. |
| **A21 · P2** | El lateral de una liquidación de comercio no muestra el importe del efectivo recibido que explica su neto. El desglose/CSV sí lo contiene. Es explicación incompleta, aparte del cálculo de A02. | PANEL-13, fuente y aritmética. Finanzas.cs:114–121, `_Liquidaciones.cshtml:18`. |
| **A22 · P3** | Un acceso desde Historial a liquidación de comercio puede abrir la pestaña Repartidores con el lateral de comercio, mezclando contextos de consulta dentro del mismo tenant. | PANEL-14, fuente. `_Liquidaciones.cshtml:4,14`. No es fuga entre empresas. |
| **A23 · P2** | La recuperación de contraseña ofrecida por web no tiene páginas funcionales; ForgotPassword, ResetPassword y ConfirmEmail devuelven404. El emisor de correo sólo registra mensajes en logs. | ROOT-01, tres GET locales y fuente; sin correos/POST. No equivale a probar registro público completo. |

## 4. Brechas frente a las seis referencias y sus vistas derivadas

Estas brechas son distintas de los bugs anteriores y de las políticas aún por definir. **El panel no está terminado «tal cual» todas las referencias.** La revisión es de controles y recorrido en fuente, contrastada con el catálogo y las capturas anteriores; no es una nueva comparación visual píxel a píxel.

| Área | Lo presente | Parte que aún falta o no completa el recorrido |
| --- | --- | --- |
| I1 / Operaciones | Indicadores reales, cola manual/automática, flota agrupada por celdas/zoom, mapa, tabla y ETA. | Comparaciones y series de KPIs; cercanía/candidatos comparados en cola; controles de capas de I1/I2. No se reporta ausente el clúster, porque sí existe. Refresco de recorridos: A15. |
| I2 / Detalle | Timeline, cinco pestañas, destino por parada, seguimiento, eventos, costos e incidencia. | Vehículo/unidad no existen en entidad/ficha. Ver perfil lleva al listado general, sin mantener piloto. Contexto de incidencia A18. Foto/firma opaca se conserva como decisión autorizada. |
| I3 / Pedidos | Listado/programados/importaciones/devoluciones, selección, etiquetas/exportación, filtros básicos y paginación CRM. | Referencia A14, Más filtros y parte de ordenación de columnas. La paginación CRM no corrige el límite del comercio de A03. |
| I4 / Rutas y zonas | Crear y planificar rutas reales, polígonos, capas, fecha, capacidades, puntos B/C/retorno y progreso. | Búsqueda dirección/zona/región; filtros Todas/En curso/Planificadas y zona de la tabla. Zona seleccionada toma la primera; pulsar polígono abre editar y no cambia esa ficha. Reglas de zona reutiliza el editor, sin un recorrido diferenciado. |
| I5 / Incidencias | Registrar, atender/escalar/resolver/reabrir, notas, responsable, reglas de categoría/SLA/efecto y filtros. | A10/A11/A17/A18. Equipo/escalación automatizada y matriz fina de perfiles necesitan cerrar política; no se consideran permisos aprobados implícitamente. |
| I6 / Liquidaciones | Pilotos/comercios, partidas y reglas Servicio/Ruta/Parada; ajustes, aprobación, programación y registro externo; caja separada de ganancias. | A02/A19/A21/A22; zona/Más filtros/chip de diferencias; previsualización de elegibles/neto antes de generar. Conciliada nace true sin un ciclo público completo de diferencias: el filtro no sustituye ese proceso. |
| Reportes | Agregados y enlace a Estadísticas con período, comercio/piloto, series, tiempos y rankings. | Exportación contextual de esos resultados y recorrido propio de productividad/puntualidad/incidencias; exportar pedidos o metadatos no equivale a exportar el reporte. |
| Usuarios y permisos | Listar pertenencias y asignar por email un Cliente existente. | Invitar/crear, editar/revocar una pertenencia individual, paginación más allá de300 y capacidades. Separar revocar acceso a tienda de cambiar la credencial global (A01). |
| Resumen / Configuración | Resumen reutiliza Centro y configuración enlaza tarifas, zonas y reglas existentes. | Editor de identidad/contactos/políticas horarias/avisos y experiencia completa de configuración. Los enlaces no acreditan un módulo acabado. |

El [informe del panel](../../artifacts/auditoria-flujos-20261112/panel/INFORME-PANEL.md) identifica cada brecha contra I1–I6 y los grupos del catálogo, con sus archivos y líneas.

## 5. Matriz del flujo completo

**N:** reproducción nueva; **F:** lectura actual de fuente/contrato; **R:** pruebas anteriores conservadas, con fuente verificada; **P:** ejecución adicional pendiente. La columna de controles describe lo encontrado, sin convertir una lectura en PASS.

| Proceso | Controles/revisión efectuada | Evidencia y siguiente cobertura |
| --- | --- | --- |
| Sesión persistente, contraseña recordada, refresh y logout | Secure storage; contexto cuenta/API; refresh single-flight; rotación por dispositivo, sello/rol actuales y descarte de respuestas de otra sesión. | F/R de sesiones. A01/A23 requieren corrección y nuevos casos cruzados. No nuevo login físico en este lote. |
| Registro, comercios y pertenencias | Registro no concede rol operativo/tenant automáticamente; contexto tienda explícito y validado; usuarios existentes pueden compartir pertenencias. | F, N de A01/A13. Falta flujo completo de invitación/revocación/recuperación. |
| Agenda y snapshots | Destinatario de tienda/tenant correcto; snapshots congelados para no modificar entregas históricas al editar agenda. | N SQL/SQLite A04/A05. Carrera de edición concurrente aún no reproducida. |
| Crear simple / provincia a provincia | Cotización y cobertura textual/ubicación, cuota por orden y operación idempotente con payload original. No se añadió traspaso entre pilotos. | F/R, N de coordenadas/QR. P: cobertura física real interprovincial y proveedor de mapas real. |
| Crear escalas | Una orden, un B, IDs de parada y C individual; reserva de cuatro dígitos por ámbito/tipo y conservación legacy. | F/R SQL de multi/códigos/cuotas; N de efectos secundarios agenda/geocodificación. |
| Creación incierta / recuperar QR | Clave y payload persistentes; creación y obtención de comprobante separadas. | N APP-04; no se volvió a crear una orden real en el host QA. |
| Programadas vs Por asignar | Futuras fuera de oferta/asignación/inicio; tarea habilita al llegar el horario; notas no deben borrar programación. | F/R. P: reinicio/cambio de hora integrado y carga prolongada, no simulado por una lista de casos del plan. |
| Asignar manual / automático sucesivo | Lock tenant/piloto/orden, validación de activo, ganador único y compatibilidad de cliente antes de ofrecer trabajo. | F/R SQL y recorrido emulador anterior. N APP-03 y Node de reintento masivo. P: red intermitente integrada con varios pilotos. |
| Listas de trabajo y refresco | Ciclos sin superposición; frescura y últimos datos ante error; versiones contra respuesta anterior a mutación. | N A03/A06/A07; el historial móvil finalizado sigue filtrado y preservado según fix `2.0.0+5`. |
| Recogida B y privacidad | Destino, coordenadas, notas restringidas y geometría ocultos antes del B correspondiente; validación de orden/piloto/empresa. | F/R de API/SQL y emulador. No nueva prueba física de QR/cámara en esta auditoría. |
| Entrega C parcial / final | C identifica parada; una C no cierra otras; último resultado terminal decide padre; replay conserva ACK y no duplica devengo. | F/R. P: nuevas carreras C vs pausa/cancelación/devolución tras incorporar las correcciones propuestas. |
| Ruta planificada, reserva y múltiples comercios | Conserva órdenes, B por recogida, C por destinatario; planificada sin piloto no se presenta como trabajo iniciado; reorden con permiso/revisión. | F/R y N APP-01. P: nuevos conflictos integrados de secuencia con varios operadores. |
| Llegada, mapa, ETA y cierre | Llegada no entrega; ETA según coordenadas/recorrido/frescura; terminales no abren actividad vigente. Historial sólo consulta. | N A04/A09; F A15. R de última validación visual de Ruta/Historial. |
| Zonas/capacidad/cobertura | Geometría, límites, horario y capacidad; validación tenant y versión al editar. | F/R. Brechas I4; no se midió comportamiento operacional real con solapamientos y alta carga. |
| Incidencias y atención | Contextos propios; efecto Continuar/PausaParada/PausaRuta; nota/responsable/SLA; resolver no consume C ni entrega. | F/R y N APP-05/06 + Node Historial. F de pérdida de parada y responsable/equipo pendiente. |
| Fallida, reintento y devolución | Solicitud trazable, autorización gestor, custodia, retorno al origen y recepción CRM. Devuelta/mixto distinto de Entregada. | F/R SQL y emulador; N del recurso devoluciones503 afectando otro formulario, sin una devolución real nueva. |
| Efectivo | Declarado, verificado y recibido separados; siempre empresa; no se resta automáticamente al salario ni se interpreta C como cobro. | F/R y N SQL CobroIds. P: sesiones cruzadas/cierre de caja y diferencias con selección reparada. |
| Liquidación y remuneración | Regla aplicada e importes congelados; unidad servicio/ruta/parada; cobertura contra doble remuneración y períodos superpuestos. | F/R SQL; N A02/A19. F de desglose/contexto y previsualización pendiente. |
| Aprobar / programar / registrar pago | Acciones autorizadas del gestor, cuenta enmascarada, revisión/operación y referencia; programar no paga ni ejecuta transferencias. | F/R. La separación de capacidades financieras futuras requiere política explícita, no se declara ya disponible. |
| Importaciones / etiquetas / exportación | Preview y validación tenant/comercio/contacto; dedupe de operación; resultados por fila. Exportación restringida y protección de fórmulas; etiquetas por parada sin C. | F/R y límites explícitos inspeccionados. P: lotes grandes, archivos hostiles y reanudación integrada después de arreglar lista/snapshots. |
| Multiempresa / suspensión / cuotas | Tenant derivado en servidor; actor/estado revalidados al mutar, relaciones y versiones; rutas y finanzas no se autorizan con IDs del cliente. | F/R. N A01 demuestra una excepción grave por credencial compartida; las otras guardas no la compensan. |
| SuperAdmin, planes y gestor | Administración global separada; alta/edición/límites; prevención de desactivar recursos con trabajo abierto. | F/R, A20. No se cambió un plan ni empresa del host del usuario. |
| GPS, cola y ciclo de vida | Fuente con servicio de ubicación, lease, muestras con captura, cola cifrada/acotada y rechazo de seguimiento sin actividad válida. | N ROOT-02. R de emulador con pantalla bloqueada. P: Samsung, Waze real, ahorro de batería, proceso/reinicio, corte de red físico y múltiples dispositivos. |
| Contrato, migraciones y publicación | Cliente viejo excluido de trabajo incompatible; migración/backfill conserva IDs/códigos; downgrade destructivo protegido. | F/R SQL de migración. P: copia representativa respaldada, restore, staging real, configuración remota y runtime soportado. |

El [plan T01–T50 y G01–G08](PLAN-PRUEBAS.md) es un catálogo de aceptación, no un recibo de ejecución. Por ejemplo, T40 menciona una compensación de efectivo anterior; prevalece la decisión posterior de entregarlo siempre a la empresa. Los detalles actuales están en las matrices de [backend](../../artifacts/auditoria-flujos-20261112/backend/HALLAZGOS-BACKEND.md), [app](../../artifacts/auditoria-flujos-20261112/app/AUDITORIA-CLIENTE.md), [panel](../../artifacts/auditoria-flujos-20261112/panel/INFORME-PANEL.md) e [identidad/GPS](../../artifacts/auditoria-flujos-20261112/root/INFORME-IDENTIDAD-GPS.md).

## 6. Riesgos y condiciones todavía no demostrados

- **Escalabilidad:** SnapshotAsync carga todas las órdenes/paradas autorizadas antes de preparar la respuesta (`EnvirapidoOperationsService.cs:117–119`), incluso con período elegido para otras colecciones. La respuesta y varias consultas crecen con el histórico; CRM refresca cada5s y el controlador móvil de operaciones por defecto cada30s. Es un riesgo confirmado de diseño, **no una latencia medida** ni una prueba de 20 pilotos/40 comercios. Acotar proyecciones y paginar histórico debe preservar todas las activas.
- **Edición concurrente del destinatario:** dos lecturas separadas para cotización/cabecera y paradas podrían observar versiones distintas mientras un editor ya autorizado guarda sin el mismo lock. No se ejecutó el interleaving; queda como inferencia de revisión.
- **Proveedor dentro de lock:** una recotización puede esperar a Maps dentro de la transacción del tenant. No se midió bloqueo ni se inyectó demora; revisar timeout, resolución previa y revalidación de versión.
- **Cambiar de perfil y GPS:** una cuenta Piloto+Cliente recrea el árbol de pantallas y dispone el tracker al elegir Cliente, aun con trabajos pendientes. Fuente confirmada, no prueba física. Definir/implementar continuidad o restricción explícita del cambio, en el ciclo de trabajo y cuenta.
- **Runtime:** net8.0 terminó soporte el10/11/2026 según la [política oficial de Microsoft consultada](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Preparar migración a una versión soportada y validarla antes de esta distribución; no se actualizó en el audit.
- **Mapas/configuración:** las claves locales de servidor y navegador tienen el mismo valor, aunque ya se retiró el fallback. Sólo se guardó la comparación booleana. Separar valores y revisar restricciones/billing según la [guía oficial de Google Maps](https://developers.google.com/maps/api-security-best-practices); no se verificó Google Cloud ni se rotaron claves. Las llamadas de Routes/Geocoding de las pruebas usan proveedores controlados.
- **Registro/recuperación:** opciones marcadas como desarrollo (sin confirmación y mínimo seis caracteres) se aplican en todos los entornos. Cerrar la política de alta y recuperación; no se demostró elevación de una cuenta pública sin rol/membresía.
- **Operación remota:** TLS/proxy, Data Protection persistente, secretos, backups/restauración, alertas y soporte operativo deben contrastarse con la configuración real. No se inspeccionó ni modificó el servidor publicado. No se afirma que esos controles estén ausentes remotamente.

## 7. Pruebas ejecutadas y valor de la evidencia

| Evidencia nueva | Resultado e interpretación |
| --- | --- |
| SQL Server, backend | 3/3 caracterizaciones: listado200, snapshot sin coordenadas y eliminación500. Cada caso reproduce un defecto. |
| SQLite, mismos backend | 3/3 reproducciones independientes del proveedor; no son tres defectos adicionales. |
| SQL Server, panel | 1/1 identidad compartida +1/1 selección de cobros, con HTTP real de TestServer/cookies/CSRF y DB GUID propia. |
| Flutter app | 6/6 caracterizaciones de widget/controlador con HTTP y almacenamiento simulados. No llaman QA/producción ni prueban Android físico. |
| Flutter GPS | 1/1 caracterización de ciclo de vida/controladores; red, ubicación y duración de autorización simuladas. |
| JavaScript | 3 comprobaciones Node sobre callbacks/predicados/comparador reales. HTTP simulado en el lote; no equivalen a DOM o navegador. |
| Identity local | Tres GET devuelven404. Sin formularios, correos ni modificación de cuentas. |
| Integridad | 331/331 archivos servidor y78/78 Flutter sin cambios frente a manifiestos anteriores. Hashes de recibos nuevos en [VERIFICACION-AUDITORIA.json](../../artifacts/auditoria-flujos-20261112/VERIFICACION-AUDITORIA.json). |

Las suites previas **SQL Server122/122**, **SQLite117 aprobadas +5 específicas de SQL omitidas** y **Flutter68/68 con análisis limpio** se conservan como antecedentes atribuidos a sus fuentes. No se repitieron en esta auditoría. Los fallos nuevos exponen escenarios que esas suites no cubrían; que un probe de caracterización pase significa que el defecto se reprodujo, no que se aprobó el producto.

Las pruebas visuales anteriores del panel SQL y del emulador, incluido el fix Ruta/Historial `2.0.0+5`, conservan su alcance original. **En esta auditoría no se hizo una nueva ronda visual ni una instalación en teléfono.** No se entregó otro APK y los APK QA existentes siguen usando sólo API local. Ninguno constituye una distribución para los pilotos o actualización remota.

## 8. Orden recomendado para corregir y cerrar

1. **Identidad y contabilidad:** resolver A01/A02, agregar regresiones de aislamiento y de selección exacta de cobros; comprobar revocación/propiedad de credenciales y concurrencia financiera.
2. **Recepción y recuperación móvil:** desacoplar listas, resumen/ofertas/devoluciones, reconciliar cada ACK con su recurso y conservar recuperación QR al salir. Probar timeouts antes/después del commit, cambio de pantalla/tienda/rol y pérdida de respuesta sin logout.
3. **Datos de ejecución:** conservar coordenadas resueltas en el snapshot nuevo; guardas de agenda multi; disponibilidad de activas con historial paginado; llegada del siguiente punto y contexto/categorías de incidencias.
4. **Panel:** refrescar recorridos reales, corregir filtros/historial/reintentos/ordenación/KPIs y hacer que los desgloses y pestañas correspondan a su selección.
5. **Completar referencias:** cerrar vehículo/unidad/perfil, zonas y filtros, previsualización/diferencias de liquidación, reportes contextuales, pertenencias/permisos y configuración. Mantener las decisiones comerciales pendientes explícitas.
6. **Preparación operativa:** runtime soportado, claves separadas/restringidas, snapshot acotado y prueba de carga con histórico/concurrencia representativos de pilotos y comercios.
7. **Validación de salida:** convertir cada reproducción en una regresión que exija la conducta correcta; ejecutar suites apropiadas sobre el árbol corregido, migración/restore de copia representativa, recorrido visual SQL, y Android físico autorizado con Waze/bloqueo/red/permisos/batería/reinicio. Vincular capturas, recibos y APK a los mismos hashes antes de decidir publicación.

Este orden es una propuesta de cierre. La auditoría no aplicó esos cambios ni autorizó por sí sola commit, push, actualización del servidor o distribución.
