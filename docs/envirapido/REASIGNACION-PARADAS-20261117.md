# Reasignación por paradas, custodia y renovación móvil

> Checkpoint histórico de implementación y QA local previo a publicación.
> La entrega autorizada posteriormente está en
> [DESPLEGAR-ENVIRAPIDO-2.1.md](../../DESPLEGAR-ENVIRAPIDO-2.1.md).
> Las menciones «sin commit/push» describen el momento de este recibo.

Fecha: 17/11/2026, República Dominicana. Implementación autorizada y validación
**local**, sobre SQL Server de QA y cuentas ficticias. Este lote no se ha enviado
a GitHub ni desplegado en producción. La **QA de integración local está cerrada**
para los recorridos y versiones identificados en este documento. Incluye entrega
final, cierre de ruta parcial y recepción comercial tras cancelar; no acredita
que todos los escenarios productivos o físicos estén comprobados.

Base de trabajo CRM: `3726d995eb1e1a1f9871a822ec4dd99d5c24ff43`.
Los cambios de este lote siguen sin commit, sobre esa base CRM y la base Flutter
`cd96b205`. El código quedó congelado al cerrar la QA.
La API pública, las cuentas reales, las bases productivas y el APK distribuido
anteriormente no forman parte de las mutaciones de esta QA.

## Semántica aprobada

- Una orden puede repartir sus **paradas** entre varios pilotos de la misma
  empresa. Se conserva la identidad de la orden y de cada parada. La asignación
  del padre deja de representar por sí sola los permisos sobre todos los destinos.
- Se admiten reparto antes de recoger y transferencia después de recoger.
  Asignar una entrega, poseer físicamente su paquete y completarla son hechos
  distintos y quedan registrados por parada.
- El piloto solicita el retiro con motivo; el gestor lo revisa, aprueba o rechaza.
  El gestor también puede aplicar directamente un retiro autorizado, con motivo
  obligatorio. La bandeja explica que esta acción del gestor es inmediata.
- Aprobar el retiro elimina la autorización de entrega del piloto anterior. El
  paquete sigue bajo su custodia hasta que exista recepción física registrada.
  La autorización administrativa no se interpreta como recepción.
- En un traspaso directo, confirma exclusivamente el **piloto receptor desde su
  app**. Para un retorno a comercio o centro, puede confirmar el **gestor
  autorizado**; además, el usuario autorizado del comercio receptor puede
  confirmar la recepción de su propio comercio. La supervisión administrativa
  no sustituye el acuse del piloto receptor en un traspaso entre pilotos. El
  gestor elige el destino de retorno: un comercio activo de su empresa o un centro
  con dirección registrada.
- Cancelar una entrega no elimina el paquete, su historial o la obligación de
  devolverlo. Si existe custodio, la gestión física permanece abierta hasta recibir.
- Las otras paradas, órdenes y rutas continúan. Una orden sólo alcanza un estado
  terminal cuando todas sus paradas terminan. Un cierre mixto conserva su estado
  específico y no declara falsamente que todo se entregó al destinatario.

Los motivos y notas de estas gestiones son información operativa compartida con
sus participantes. La UI advierte que no se escriban códigos C ni datos privados
del destinatario en esas notas; no son un campo de notas internas administrativas.

## Modelo, rutas y fronteras de autorización

`OrdenEntrega.GestionPorParadas` activa la gestión explícita. Cada parada conserva
`PilotoAsignadoId`, `PilotoCustodioId`, `PilotoEntregaId`, `RecogidaParadaAt`,
`RevisionAsignacion`, `EnReasignacion` y el origen actual de recogida cuando cambia.
La revisión se renueva al cambiar la asignación o registrar su recepción.

La app y el servicio validan la revisión esperada antes de recoger o entregar.
Una petición antigua no puede volver a operar por el mero hecho de que el mismo
piloto reciba nuevamente la parada después de una transferencia intermedia.
Las operaciones usan UUID y payload estables: una respuesta perdida se reconsulta
con el mismo intento; cambiar su contenido con la misma clave es conflicto.

Las rutas admiten órdenes legacy completas y selecciones explícitas de paradas
gestionadas para un mismo piloto. `CrearRutaRequest.ParadaIds` es aditivo:
seleccionar una orden gestionada no incorpora silenciosamente sus demás paradas.
Una parada ya recibida puede tener sólo una entrega pendiente; de lo contrario,
su recogida debe preceder a su entrega. Un traspaso pendiente de recepción no
puede planificarse como si el nuevo piloto ya tuviera el paquete.

Las membresías se reservan por parada para la gestión nueva y por orden para el
flujo legacy. La conversión conserva las visitas y su historia, crea membresías
por parada y retira únicamente las unidades seleccionadas. Cancelar o finalizar
una ruta parcial no reinicia las asignaciones de los otros pilotos. La evidencia
`PuntoRutaOperativa.RecogidaConfirmadaAt` conserva una recogida histórica aunque
la recepción actual de la parada cambie posteriormente.

Los pilotos reciben únicamente sus paradas y los datos necesarios para sus
recepciones. La recogida B de otro piloto no revela todos los destinos del padre.
Las rutas, geometrías, seguimiento, incidencias, carga e indicadores aplican el
ámbito por parada. La custodia física pendiente también se considera al calcular
el trabajo activo, incluso cuando su entrega ya esté cancelada.

Tenant, usuario, piloto y comercio receptor se derivan de la sesión y se revalidan
en el servidor. Las relaciones SQL conservan claves compuestas por empresa;
ningún identificador enviado por el cliente permite cambiar de tenant. El cambio
pertenece al CRM operativo: no modifica identidades, tablas ni permisos del portal
comercial independiente.

## Códigos A, B y C y compatibilidad móvil

- **A:** identifica el mismo pedido; se conservan los valores emitidos.
- **B:** valida la recogida comercial. El nuevo comando puede confirmar un lote
  explícito de paradas del piloto, cada una con su revisión. Otra recogida de la
  misma orden no declara recogidos los paquetes de todos los pilotos.
- **C:** sigue perteneciendo a su parada y sólo confirma la entrega al destinatario
  final. No se consume al aprobar un retiro, recibir un traspaso o registrar un
  retorno. Una entrega final requiere únicamente C, con los permisos y revisión
  correspondientes. Los códigos ya emitidos no se regeneran por reasignar.

El contrato de piloto pasa a **4**, mediante `X-Contrato-Ordenes: 4` y capacidades
`gestionPorParadas`, `recogidaPorLoteIdempotente` y `revisionAsignacion`.
El comando nuevo es `POST /api/piloto/ordenes/{id}/recoger-paradas`; la entrega
por parada añade la revisión esperada a su intención idempotente.

**Actualizar y comprobar los clientes compatibles antes de utilizar reparto o
transferencia gestionados.** Los clientes anteriores mantienen el flujo legacy;
los endpoints de órdenes e historial filtran el trabajo gestionado según sus
capacidades y los comandos incompatibles se rechazan con una indicación de actualización. Un
backend actualizado no basta para habilitar operativamente esas asignaciones a
pilotos que siguen utilizando el APK de contrato 3.

La actualización visible sin cerrar sesión usa consultas periódicas y renovación
al volver a la app: órdenes y detalle aproximadamente cada cinco segundos, y
operaciones aproximadamente cada treinta. Las revisiones de asignación ayudan
a retirar sólo el trabajo afectado. Este lote no añade un canal push ni garantiza
notificaciones instantáneas con la app detenida.

La candidata móvil local es **2.1.0 (10)**. Renueva acceso, piloto y comercio con
la identidad verde/turquesa y conserva la mecánica del modal deslizante
BottomSheet. El APK de esta QA es debug x86_64, paquete `com.pilotgo.app.qa` y
origen local aislado; no es el APK release para compartir con pilotos o comercios.

## Dinero y ajuste manual del trabajo previo

El efectivo ya cobrado conserva al piloto que lo recibió como responsable. Un
traspaso de paquete no transfiere ese efectivo, no confirma su recepción en la
empresa y no altera cobros conciliados o liquidaciones emitidas.

El gestor puede elegir **Ajustar trabajo previo** en una gestión autorizada con
piloto de origen. Registra un importe explícito mayor que cero y un motivo;
el sistema no inventa una tarifa por recogida, traslado o entrega incompleta.
Se crea una liquidación **En revisión**, con cero entregas y un movimiento de
ajuste manual. No se registra efectivo compensado, aprobación ni pago automático.
La UI enlaza con Liquidaciones para continuar su proceso de revisión, aprobación
y registro de pago externo.

Se admite un ajuste por gestión mediante una clave de origen única. El mismo
intento puede reconsultarse; una compensación adicional exige corregir la
liquidación por el procedimiento existente, con motivo y trazabilidad. Las
entregas efectivas se atribuyen a su ejecutor registrado por parada, no al piloto
actual del padre.

## Migración SQL y reversión

Migración del **CRM**:
`20261117230916_EnvirapidoReasignacionPorParadas`.
Se validó la actualización desde `20261112201846_EnvirapidoSaasBilling`.
No se aplica sobre `PortalConnection` ni sobre la base comercial del portal.

La migración añade el estado por parada, retiros, recepción histórica y relaciones
de rutas por membresía. Antes de cambiar la FK de visitas, repara `RutaOrdenId`
con su relación legacy real de empresa/ruta/orden. Una visita sin membresía válida
detiene la migración para revisión, en lugar de eliminarla o asociarla arbitrariamente.

Se conserva la unicidad legacy de orden vigente mediante un índice filtrado para
membresías sin parada; otro índice impide dos membresías vigentes de una misma
parada. Las visitas enlazan con el identificador de su membresía además de empresa,
ruta y orden. Las órdenes existentes conservan `GestionPorParadas=false`: no se
convierten, reasignan, regeneran códigos o reabren terminales automáticamente al
migrar. La conversión se realiza únicamente mediante las operaciones autorizadas.

`Down` rechaza la reversión cuando existen órdenes gestionadas o historial de
retiros. No se deben borrar esas filas para forzar un downgrade. En ese estado,
se requiere recuperar un release compatible y conservar los datos; no volver a
publicar un binario legacy incapaz de interpretar las nuevas asignaciones.
Preparar una futura actualización productiva requiere respaldo, revisión del
entorno y migración explícita antes de habilitar los clientes y el flujo nuevos.
Esta documentación no acredita que esos pasos productivos se hayan ejecutado.

El script idempotente exportado desde la migración SaaS anterior se encuentra en
`docs/envirapido/20261117-reasignacion-paradas.sql`, SHA-256
`1ba71fc8efa15ab8755b2ce16bdfe6bb5f7f5a27cdcd8bb13bdc203f388b3852`.
La comprobación EF de cambios pendientes del modelo pasó. La generación del script
y la actualización probada en bases QA no equivalen a aplicarlo en producción.

## QA local y evidencia

| Validación | Resultado confirmado | Evidencia local |
| --- | --- | --- |
| Suite completa CRM con SQL Server QA, cierre final | **340/340**, cero fallos y cero no ejecutadas | `artifacts/reasignacion-20261117/sqlserver-cierre-final.trx` |
| Suite completa CRM SQL, etapa anterior | **335/335**; conservada como etapa previa | `artifacts/reasignacion-20261117/sqlserver-final.trx` |
| Guardas de ciclo de vida | **3 casos incluidos en el cierre 340** | `Login.Tests/ManagedPilotLifecycleTests.cs` |
| Fragmento de Reasignaciones | **2 casos incluidos en el cierre 340** | `Login.Tests/ReassignmentFragmentTests.cs` |
| Flutter, suite completa secuencial tras corregir CTA y endpoints de retiro | **113/113**; anterior al último ajuste de navegación | App: `build/managed-return-fix-final-tests.log` |
| Flutter analyze de esa etapa | Sin incidencias | App: `build/managed-return-fix-analyze.log` |
| Flutter, casos afectados tras el ajuste final de navegación | **16/16**; no es una nueva suite completa | App: `build/transfer-navigation-fix-tests.log` |
| Flutter analyze tras el ajuste final de navegación | Sin incidencias | App: `build/transfer-navigation-fix-analyze.log` |
| Flutter, primeras etapas | **108/108** secuencial y **13/13** focal GPS/nuevos casos | App: `build/modernizacion-reasignacion-final-tests.log` y `build/modernizacion-focused-gps-reassignment.log` |
| Recorrido integrado CRM + API + app nativa en emulador | **Cerrado localmente** para la secuencia y las etapas identificadas más abajo | Capturas y `artifacts/reasignacion-20261117/qa-auditoria-final.json` |

Los logs de app se encuentran en el checkout Flutter separado
`C:\Users\Fernelis\Desktop\pilotGOapp`; su directorio build apunta al almacenamiento
QA en D:. La suite completa se ejecutó con
`flutter test --no-pub --concurrency=1`; el análisis usó `flutter analyze --no-pub`.
Los recibos del APK QA y los hashes de fuentes/logs están en
`build/envirapido-qa/2.1.0-10/`, con subdirectorios por etapa. Los recibos de una
etapa anterior, incluido `QA-APP.md` de la primera candidata, no acreditan por sí
solos el contenido de una candidata posterior. Estos artefactos de ejecución no
se incorporan automáticamente a Git.

La etapa de 113 pruebas completas corresponde a la fuente con huella
`706eefaa030a9b354877b2f0c871c15362e0c567894a27ef85fd354ba608e10f` y a los
recibos `managed-return-fix/`. Allí se corrigieron las rutas POST de retiro y
recepción, manteniendo GET en `/api/piloto/operaciones/reasignaciones`; el journal
conserva el UUID y el payload del intento que había recibido 404 para recuperarlo
en la ruta correcta. También se retiró la acción legacy inaplicable a una parada
gestionada. El APK de esa etapa tiene SHA-256
`cfe1b71bc029799d3f8e0aaf5e22b2041b322f019fa3031fd027a97dc96f0be4`:
con él se observaron la recuperación RT2, su retiro y la recepción por el piloto
destino. Esos pasos no se atribuyen como una ejecución nueva bajo navfix.

Después se ajustó sólo la navegación del retorno: un destino piloto muestra la
coordinación y contacto sin ofrecer navegación hacia texto que no es una dirección;
Comercio/Centro conservan Navegar cuando hay dirección y la gestión está abierta.
Esta modificación tiene 16 pruebas afectadas y análisis aprobados, **sin nueva
ejecución completa de las 113**. La fuente de esta etapa tiene huella
`77d523248d99ade6fe21e594a7d3258d25e588ed4b9f48d80889091ab44a6d88`.
El APK generado es
`build/envirapido-qa/2.1.0-10/transfer-navigation-fix/Envirapido-2.1.0-10-qa-x64-debug-navfix.apk`,
SHA-256 `8db5b2f7490351830dba9862cf7c1b9ebf7d3b56f01186c49b6938172d9510c7`.
Su recibo `qa-x64-verificacion.json` confirma el paquete QA, versión 2.1.0 (10),
certificado de QA y API local `http://127.0.0.1:5019`. Este APK se instaló como
actualización, sin desinstalar, en el emulador `emulator-5556`; la sesión del
piloto A2 persistió. En navfix se comprobaron llegada, entrega C, finalización de
ruta, recepción comercial y las vistas finales del comercio. El único cambio de
producto respecto a la etapa CTAfix afecta a la navegación contextual indicada
arriba; las reglas de negocio no cambiaron entre esos dos artefactos. Se conserva
la atribución separada: 113 pruebas completas en CTAfix, 16 afectadas y análisis
en navfix, y los pasos nativos correspondientes a cada APK.

La suite SQL incluye reparto de paradas, privacidad entre pilotos/tenants,
recogida por lote, revisiones e idempotencia, rutas mixtas, conversión y retiro
parcial con historia conservada, cancelación con custodia, recepción autorizada,
ajuste manual sin pago y actualización/reversión de migraciones. Conserva también
las regresiones del flujo legacy de pedidos automáticos y manuales.

Los tres casos de ciclo de vida impiden dar de baja un piloto con asignaciones
propias aunque el padre ya no tenga un piloto único; mantienen protegido al
custodio y al receptor pendiente de una entrega cancelada; e impiden desactivar
el comercio de retorno antes de recibir el paquete. Una cancelación no permite
eludir las obligaciones de custodia.

La bandeja nueva consulta un fragmento de sólo lectura cada cinco segundos en
primer plano. No solapa consultas, limita su tiempo y se pausa al seleccionar,
editar, abrir un diálogo o enviar una mutación; una respuesta tardía no reemplaza
una edición que ya empezó. Los formularios conservan sus UUID e intenciones y no
se recarga la página completa para recibir novedades. El indicador común de
sincronización se actualiza al aplicar un fragmento válido del servidor mediante
`envirapido:scoped-snapshot-applied`, sin declarar éxito por el mero paso del tiempo.
Los dos casos HTTP verifican que una nueva solicitud aparezca con la misma sesión
del gestor, que el GET no escriba operaciones/historial/códigos y no incluya
formularios, así como `NoStore`, permisos de gestor y aislamiento de tenant.

Durante la integración se corrigieron la resolución de identidad de las
membresías al leer rutas gestionadas, la conservación de la etapa recogida del
padre y fixtures que asumían revisión adicional de un retiro ya aplicado por el
gestor. Dos fixtures de migración utilizaban el modelo nuevo contra esquemas
antiguos: se adaptaron a inserciones de columnas legacy o a proyecciones escalares
del esquema que continuaba disponible. Las guardas y las aserciones de
preservación no se eliminaron para obtener el resultado final.

Una ejecución posterior a la etapa 335 se interrumpió después de detectar una
expectativa incorrecta del fixture de autorización del fragmento: la navegación
MVC devolvía 302 a AccessDenied en vez del 403 esperado. Se corrigió únicamente
la aserción para admitir 403 o un 302 con el mismo esquema y host hacia exactamente
`/Identity/Account/AccessDenied`; no admite 200 ni una redirección externa.
La política del producto no se relajó. Después de esa corrección, la ejecución
completa `sqlserver-cierre-final.trx` aprobó los 340 casos. La ejecución interrumpida
no se presenta como una suite completa aprobada.

El primer ensayo Flutter en paralelo registró 106 aprobadas y un fallo de tiempo
en `gps_recovery_test.dart`, caso
`background recovery renews the same live service with LocationPermission.always`:
el fixture usa una autorización real de un segundo y la aserción de arranque
observó cero inicios en vez de uno bajo carga. Se conservaron ese log y el fallo.
El caso aislado y la suite final secuencial pasaron; no se modificó el GPS ni se
amplió la duración del fixture para resolverlo. La corrida paralela inicial y la
suite secuencial se identifican por separado. Las etapas posteriores de 113 y 16
casos mantienen su atribución de fuentes y alcance descrita arriba.

## Recorrido nativo integrado cerrado localmente

Los siguientes identificadores pertenecen exclusivamente a la base ficticia QA.
Son observaciones del recorrido integrado completado en el emulador, no una
declaración de perfección de toda la app ni de producción:

| Caso | Resultado observado | Artefacto y alcance |
| --- | --- | --- |
| RT2, recuperación de solicitud | El journal recuperó el intento que había recibido 404 usando el mismo UUID. Se aprobó el retiro; la parada desapareció del piloto A1 sin cerrar sesión y permanecieron sus otros trabajos de las órdenes 6 y 8. | Observado con CTAfix. |
| RT2, transferencia | Se asignó al piloto A2, que confirmó la recepción en la app. El CRM mostró el cambio automáticamente. | Observado con CTAfix; el acuse no consumió C ni declaró entrega final. |
| Actualización móvil | Se instaló navfix como actualización y persistió la sesión A2. El modal deslizante expandido mostró R2 con su única entrega pendiente, recogidas 0/0 y entregas 0/1. | Navfix; `app-modal-ruta-final.png`. |
| Entrega de parada 3 | Registrar llegada funcionó. La entrega se completó con C, sin volver a introducir B. La parada 4 permaneció Pendiente; antes de su B no se reveló su destino privado y se conservó su pausa por incidencia. | Navfix; `app-entrega-C-final.png`. El valor de C no se incorpora a esta documentación. |
| Cancelación parcial | El CRM canceló únicamente la parada 4, que no tenía custodio. La parada 3 siguió Entregada y la 4 quedó Cancelada; no se reabrió la entrega cumplida. | Auditoría final de orden 3. |
| Ruta 2 | Creada desde el CRM para la parada 3 ya recibida, con cero recogidas y una entrega, e iniciada. Tras entregar, pasó automáticamente a Finalizada, entregas 1/1 y recogidas 0/0, y desapareció de rutas activas. R1 y la orden 4 continuaron activas. | Navfix y CRM; `panel-ruta-finalizada.png`. |
| RT3, orden 6 | Se programó retorno al comercio A1 y se canceló la entrega. La custodia A1 permaneció abierta hasta que el usuario del comercio A1, desde Recepciones de mi tienda, añadió una nota y confirmó recepción. RT3 pasó a Recibida, cerrada y sin custodio; la entrega siguió Cancelada. | Navfix; `app-recepcion-comercio-confirmada.png` y `panel-retorno-cerrado.png`. Cancelar por sí solo no registró recepción. |
| RT1, ajuste de trabajo previo | La UI creó LQ3 por RD$50, En revisión, conciliación pendiente y cero entregas. El pago siguió Sin confirmar. | No se ejecutó una operación de pago. `ajuste-sin-pago.png` y `panel-ajuste-en-revision.png`. |
| Bandeja CRM | El host QA actualizado aplicó el fragmento y mantuvo el indicador Sincronizado después del anterior umbral de treinta segundos. | Polling de la vista, sujeto a las pausas de edición y red. |
| Comercio y Nueva orden | Las tarjetas finales del comercio y la vista Nueva orden fueron aprobadas visualmente. | Navfix; `app-comercio-final.png` y `app-nueva-orden-final.png`. En esta última inspección no se creó otra orden. |
| Aislamiento multitenant | El gestor A recibió 404 al consultar la orden 7 del tenant B. El gestor B pudo consultar su propia orden 7, con una parada y sin gestiones ajenas. | `qa-auditoria-final.json`; no se interpreta un identificador global como autorización. |

Las evidencias nombradas se encuentran en `artifacts/reasignacion-20261117/`,
junto a `app-parada-retirada.png` y `app-recepcion-piloto-confirmada.png` de la
etapa CTAfix. La auditoría final registra la parada 3 Entregada, asignada al piloto
2 y sin custodio; la parada 4 Cancelada, sin asignado ni custodio; y RT3 Recibida
con `abierta=false`, manteniendo cancelada la entrega de la orden 6. El recibo
identifica el APK navfix por su hash completo. Una captura aislada no sustituye
la secuencia ni extiende el alcance de estas observaciones.

El host QA quedó en el puerto 5039, adjunto a la base ficticia
`PilotGoTest_322032134601455fb87d4d991dd3d91b`, preservando el estado del recorrido.
La app mantuvo su origen local 5019, redirigido al host mediante adb reverse.
Esta disposición pertenece exclusivamente a la validación local; no cambia la
URL pública ni la configuración de una instalación productiva.

## Cierre y límites

Se cerró la integración local descrita con SQL Server, CRM y la app nativa en el
emulador. Las suites de Flutter usan dobles de red; se complementan con el recorrido
real de estos servicios locales y Android, sin sustituirlo ni ampliar sus casos.

Este lote no acredita despliegue público, recorrido físico de paquetes, escaneo de
QR real, prueba de flota ni resistencia prolongada del GPS con pantalla bloqueada.
No se modificó el teléfono físico, no se publicó el APK QA y no se hizo push.
El pase productivo y un APK release firmado, si se autorizan después, requieren
su propia preparación y evidencia sobre el mismo código que vaya a distribuirse.
