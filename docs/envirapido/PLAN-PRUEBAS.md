# Envirapido: plan de pruebas y criterios de aceptación

Fecha: 11 de noviembre de 2026. Estado: **plan, no ejecución**.

Este archivo especifica pruebas de comportamiento para los lotes del
[análisis](README.md). No crea tests de implementación ni convierte resultados
históricos en una certificación de los módulos nuevos.

## 1. Entornos y datos

- SQL Server aislado: esquema, migraciones, índices, carreras y conciliación.
- SQLite: lógica compatible y pruebas rápidas, identificando explícitamente las
  capacidades específicas de SQL Server. No cambiar producción para simularlas.
- Flutter: contratos, persistencia, recuperación, permisos y estados de interfaz.
- Navegador: sesiones reales de QA, cookies/CSRF, roles y seis referencias.
- Android emulador y Samsung físico: paquetes QA aislados; producción y datos
  personales quedan fuera. Conceder permisos reales según el flujo autorizado.
- Tenant A y B con IDs/códigos intencionalmente parecidos; dos comercios por
  tenant como mínimo, un usuario multicomercio y pilotos autorizados/inactivos.
- Flujo ampliado: veinte pilotos y cuarenta comercios repartidos entre tenants,
  dos rondas con las mismas sesiones y órdenes con varias paradas. Es una prueba
  funcional de flota; medir capacidad del servidor exige un ensayo separado.

## 2. Matriz de comportamiento

| ID | Escenario | Resultado verificable | Nivel principal |
| --- | --- | --- | --- |
| T01 | Migrar órdenes antiguas creadas, asignadas, recogidas y entregadas. | Una parada equivalente sin cambiar IDs, A/B/C, importes, fechas, snapshots ni historial. | SQL Server |
| T02 | Repetir despliegue del script y comprobar datos. | Ninguna parada duplicada ni regeneración de códigos; se conserva la evidencia previa. | SQL Server |
| T03 | Crear simple, asignación automática, aceptar, B y C. | Completa la entrega con la misma sesión; C final no exige B. | API + Flutter + físico |
| T04 | Crear simple manual con recepción automática apagada. | Llega sin logout; B y C funcionan; GPS activo si corresponde al trabajo. | API + app |
| T05 | Entregar y recibir otra automática, luego otra manual. | Sin otro login, ofertas ni trabajos duplicados; disponibilidad coherente. | API + app |
| T06 | Crear una orden con tres destinatarios. | Una orden y tres paradas estables, cada una con su C; consumo de cuotas según regla aprobada. | SQL + API |
| T07 | Confirmar C de la primera parada. | Solo esa parada termina; dos siguen pendientes; el padre no queda entregado. | API + UI |
| T08 | Repetir la última confirmación tras timeout, incluida una C intermedia con remuneración por ruta. | Mismo resultado; un único evento de confirmación y ningún devengo duplicado: cero o las partidas que correspondan según la regla aplicada. | SQL + API + app |
| T09 | C de otro destinatario, otra orden o tenant. | Rechazo sin revelar datos ni alterar avance; intentos limitados. | Seguridad API |
| T10 | Reutilizar un código de una entrega antigua. | ID/parada/asignación/estado impiden completar otra entrega indebidamente. | SQL + API |
| T11 | Creación concurrente y espacio de cuatro dígitos agotado. | Reservas activas correctas y error controlado, sin duplicados ni bucle de generación. | SQL Server |
| T12 | Cliente viejo frente a orden multiparada o ruta. | No recibe un trabajo que no puede ejecutar; error/capacidad explícitos. | Contrato |
| T13 | Cliente nuevo con servidor anterior. | No crea órdenes incompatibles ni pierde una operación pendiente. | Flutter + contrato |
| T14 | Reintentar creación/importación con misma clave y mismo contenido. | Una orden/lote/operación; recupera resultado original. | SQL + API |
| T15 | Reutilizar clave con contenido diferente. | Conflicto explícito; no sobrescribe ni descarta operación incierta. | API + app |
| T16 | Dos pilotos aceptan la misma oferta; otro usuario asigna manual. | Un responsable coherente y un historial de asignación válido. | SQL Server |
| T17 | Desactivar piloto/comercio durante una asignación. | Ninguna orden o ruta queda varada; se conservan las protecciones de baja. | SQL Server |
| T18 | Suspensión, vencimiento o membresía revocada. | Mismas políticas vigentes en API, CRM, exportación y nuevas funciones. | Seguridad |
| T19 | Alternar comercios en dos dispositivos con la misma cuenta. | Cada dispositivo conserva contexto explícito; respuestas antiguas no contaminan otra tienda. | API + Flutter |
| T20 | Usuario A intenta IDs de rutas, paradas, incidencias, zonas o pagos B. | Lectura/escritura/listado/exportación/archivos inaccesibles fuera del tenant. | Seguridad |
| T21 | Relacionar una parada A con orden/ruta/piloto/comercio B. | Rechazo de aplicación y restricciones de persistencia correspondientes. | SQL + API |
| T22 | Crear ruta con varias órdenes del mismo tenant. | Conserva órdenes y C; todas las asignaciones del recorrido son coherentes. | SQL + API + UI |
| T23 | Crear ruta planificada sin piloto. | No se presenta como trabajo aceptado ni se asigna por accidente. | API + UI |
| T24 | Ruta con recogidas en distintos comercios. | B de una orden desbloquea solo sus destinos; entrega no precede a recogida. | API + app |
| T25 | Consultar ruta antes de B. | Sin destino restringido en JSON, mapa, polilínea, enlace, caché o exportación del piloto. | Seguridad + visual |
| T26 | Reordenar con permiso, sin permiso y con revisión antigua. | Solo la acción autorizada procede; IDs/C no cambian; conflicto explícito al competir. | API + app |
| T27 | Confirmar mientras CRM cancela, pausa o modifica la ruta. | Comando valida la versión y política aplicable; no confirma una acción invalidada. | SQL + API |
| T28 | Asignación masiva con permisos/estados cambiados desde la selección. | Revalidación al ejecutar y resultado comprensible; no acceso por selección manipulada. | SQL + web |
| T29 | Refresco de lista mientras hay selección y filtros. | No selecciona registros nuevos ni pierde silenciosamente el alcance de la operación. | Navegador |
| T30 | Límite de zona, zona solapada, sin coordenadas y horario cerrado. | Resultado conforme a política explícita; no decide por orden accidental de consulta. | Dominio + mapa |
| T31 | Falta GPS, posición vieja, precisión insuficiente o proveedor de rutas falla. | Frescura y estimaciones indisponibles visibles; no ETA inventado. | API + app + web |
| T32 | Incidencia desde piloto, CRM y detección repetida. | Caso trazable del tenant/contexto correcto, sin duplicación por reintentos. | SQL + API + UI |
| T33 | Regla de incidencia permite continuar, pausa parada o pausa ruta. | Cada efecto se aplica y se revoca solo mediante transición autorizada. | Dominio + app |
| T34 | Resolver incidencia con entregas pendientes. | No marca entregado ni consume C; conserva custodia y avance. | API + UI |
| T35 | SLA/escalación y cambio de responsable concurrentes. | Plazos calculados con reglas/versiones, un resultado auditable y sin alertas repetidas. | SQL + tareas |
| T36 | Recogido frente a En ruta, llegada y demorada. | Hitos y horas reales; demora conserva etapa; no inferencias por orden numérico del enum. | Dominio + UI |
| T37 | Cancelación/devolución después de una entrega parcial. | Entregas confirmadas permanecen; pendientes, custodia, códigos y cuotas siguen la política aprobada. | SQL + API + app |
| T38 | Generar liquidaciones con períodos superpuestos y reintentos. | No se incluye dos veces la misma unidad/componente liquidable. | SQL Server |
| T39 | Reglas por servicio, ruta o parada; modificación posterior. | Partidas explicables con regla aplicada; no recálculo silencioso del histórico. | Dominio financiero |
| T40 | Ajustes positivos/negativos y efectivo compensado. | Neto reproduce sus partidas y conciliación; motivos/autor conservados. | SQL + UI |
| T41 | Aprobar, programar y alcanzar fecha. | No confirma pago por sí solo. Registro externo autorizado y referencia determinan el estado. | API + UI |
| T42 | Pago externo repetido o importado con la misma referencia. | Tratamiento idempotente según alcance definido; no doble movimiento. | SQL Server |
| T43 | Intento de ajuste/aprobación/pago por piloto o rol insuficiente. | Rechazo servidor; banco enmascarado y datos ajenos inaccesibles. | Seguridad |
| T44 | Redondeos, saldos cero/negativos y cierre de período local. | Importes decimales y política acordada; sin variación por zona o presentación. | Dominio financiero |
| T45 | Ganancias del piloto y resumen comercial. | Solo datos propios autorizados; entrega, liquidación y pago diferenciados. | App + API |
| T46 | Importación con filas válidas/erróneas, destinatarios repetidos y reanudación. | Agrupación y resultado por fila explícitos; no duplicar ni crear otra orden por cada parada. | SQL + UI |
| T47 | Etiquetas/exportaciones de selección o consulta filtrada. | Alcance explícito, tenant verificado, C protegido y datos ajustados al permiso. | API + archivos |
| T48 | Datos con fórmulas CSV, HTML o archivos excesivos. | Validación y salida segura; errores legibles; ninguna ejecución o exposición adicional. | Seguridad |
| T49 | Programado en cambio de fecha, reintento y reinicio de tarea. | Se habilita una vez en su horario; cuota/códigos según política definida. | SQL + tareas |
| T50 | Sesión expira durante petición, app se reinicia, cambia perfil o cierra sesión. | Renovación/persistencia correctas; no peticiones antiguas aplicadas a otra identidad. | Flutter + API |

## 3. GPS físico y uso real de Android

Estas pruebas requieren permisos y ubicación de prueba autorizados. No basta
con sustituir sensores por mocks ni observar un marcador en el emulador.

| ID | Recorrido físico | Evidencia |
| --- | --- | --- |
| G01 | Iniciar trabajo con app visible y pasar a Waze/Maps. | GPS sigue llegando dentro de la cadencia acordada; una sola fuente por dispositivo. |
| G02 | Bloquear pantalla durante ruta y desbloquear. | Servicio y timestamps verificados; estado actual sin nuevo login. |
| G03 | Ahorro de batería, intervalo en reposo y retorno a movimiento. | Cadencia/precisión medidas y límites documentados; sin promesa de continuidad absoluta. |
| G04 | Permiso aproximado/preciso, denegado y revocado en ejecución. | Mensaje accionable, sin ANR/bucle ni indicador falso de GPS activo. |
| G05 | Cortar red y recuperarla. | Posiciones con tiempo de captura, antigüedad correcta, límites de cola y sin mezclar sesiones. |
| G06 | Terminar última actividad, logout o revocación. | Detiene seguimiento cuando corresponde; conserva únicamente operaciones permitidas. |
| G07 | Reiniciar proceso/teléfono o forzar detención. | Recuperación conforme a Android y acción del usuario; no prometer reinicio oculto prohibido. |
| G08 | Ruta multiparada, dos comercios, B por orden y C por destinatario. | Recorrido completo con privacidad, incidencias y avance coincidente entre app/CRM. |

Medir consumo y tráfico antes de fijar cadencias finales. Seguimiento durante
trabajo activo no autoriza seguimiento personal fuera de actividad.

## 4. Aceptación visual y de interacción

- Comparar las seis capturas conservadas con datos QA propios, no con valores
  dibujados del ejemplo. Verificar marca Envirapido.
- Escritorio y anchos estrechos: tarjetas, panel lateral, tabla con desplazamiento
  contenido, filtros, menús, modales, estados vacíos y mensajes largos.
- Cada control activo completa su acción: crear, asignar, importar, exportar,
  imprimir, planificar, editar zona, reportar/resolver, ajustar, aprobar o pagar.
- «Ver» conserva orden/parada/ruta/beneficiario correctos y navegación de retorno.
- Mapa, tabla, panel lateral y contadores responden a la misma selección y alcance.
- Selección/foco no saltan al refrescar; teclado, etiquetas, iconos y texto apoyan
  el color. Error, carga, sin conexión y datos obsoletos son estados reales.
- La app conserva su estilo y navegación aprobados. Comercio y piloto mantienen
  experiencias propias según rol.
- Foto/firma/comprobante son la excepción autorizada: atenuados, identificados
  como futuros, sin acción que prometa una carga o evidencia inexistente.
- Fechas de instantes en UTC explícito y presentación en zona de negocio/dispositivo;
  no reaparece el desfase de cuatro horas detectado en la base anterior.

## 5. Puertas de salida por lote y publicación

1. Trazabilidad requisito → vista → contrato → prueba, con pendientes del lote cerrados.
2. Checks de compilación/análisis y pruebas pertinentes al cambio. No repetir
   baterías completas sin nuevas modificaciones o dudas justificadas.
3. Evidencia de SQL Server para restricciones/carreras; no atribuir un PASS de
   SQLite a funciones exclusivas del motor.
4. Validación visual de recorridos y estados de error afectados.
5. Ensayo de migración/compatibilidad/restauración con copia de datos y códigos
   históricos. Probar convivencia de clientes y activación por tenant.
6. Recorrido físico para GPS, sesión, recepción sucesiva y C; lectura real de QR
   cuando corresponda al método implementado. Evidencias solo de cuentas QA.
7. Registro de SHA, árbol, configuración, período y ambiente de cada resultado.
   Una prueba de veinte pilotos/cuarenta comercios no es un benchmark de producción.
8. Backend/base compatibles antes de distribuir APK firmado; verificar URL pública,
   paquete, certificado y capacidades del servidor. Publicación y despliegue se
   documentan como hechos separados del análisis y de la compilación.

No se ejecutó ninguna de estas pruebas en esta entrega documental. Los resultados
se añadirán en recibos por lote; no se anticipan como aprobados.
