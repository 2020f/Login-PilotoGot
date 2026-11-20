# Evidencia visual de panel y app — 11 de noviembre de 2026

Panel web con SQL Server LocalDB aislado y datos ficticios. Las vistas estrechas
se capturaron en un viewport de navegador; no son pruebas en teléfono físico.

| Archivo | Alcance |
| --- | --- |
| [19-estadisticas-sqlserver.jpg](19-estadisticas-sqlserver.jpg) | Estadísticas por defecto: cuatro órdenes del tenant A, dos entregadas y dos pendientes, gráfico y rankings. |
| [22-detalle-corregido.jpg](22-detalle-corregido.jpg) | Detalle de orden entregada, datos conservados y cuatro eventos del historial. |
| [23-admin-movil-corregido.jpg](23-admin-movil-corregido.jpg) | Dashboard SuperAdmin en viewport móvil web de 390 píxeles. |
| [25-empresas-movil.jpg](25-empresas-movil.jpg) | Listado de empresas en viewport móvil, dentro de un área de tabla con desplazamiento horizontal. |
| [27-qr-manual-sqlserver.jpg](27-qr-manual-sqlserver.jpg) | Comprobante de orden manual A2, con códigos ficticios y QR generado por ID. |
| [30-piloto-destino-bloqueado.png](30-piloto-destino-bloqueado.png) | Emulador: automática ORD-0004 aceptada; solo se muestra recogida, antes de confirmar B. |
| [31-destino-desbloqueado.png](31-destino-desbloqueado.png) | Emulador: ORD-0004 en ruta, destino disponible después de confirmar recogida. |
| [32-segunda-auto-misma-sesion.png](32-segunda-auto-misma-sesion.png) | Segunda automática ORD-0005 apareció tras entregar ORD-0004, sin otro login. |
| [33-asignacion-manual-auto-off.jpg](33-asignacion-manual-auto-off.jpg) | Gestor asignó ORD-0006 de A2 al Piloto A durante la misma sesión de app. |
| [34-manual-misma-sesion-auto-off.png](34-manual-misma-sesion-auto-off.png) | Emulador: nueva manual visible junto a ORD-0003, dos activas y recepción automática apagada, sin logout. |
| [35-gps-manual-auto-off.jpg](35-gps-manual-auto-off.jpg) | Panel: Piloto A con dos entregas, recepción automática pausada y GPS actualizado. El mapa web no dispone de clave Maps en este QA. |
| [37-manual-entregada.png](37-manual-entregada.png) | Emulador tras finalizar ORD-0006 con B/C: confirmación y solo ORD-0003 pendiente. |
| [38-comercio-manual-entregada.jpg](38-comercio-manual-entregada.jpg) | Comercio A2: ORD-0006 entregada y contador de una entregada hoy. |
| [39-sesion-restaurada-reinicio.png](39-sesion-restaurada-reinicio.png) | Arranque frío después de force-stop, sin borrar datos: sesión restaurada, ORD-0003 y auto apagado. |
| [40-acceso-recordado.png](40-acceso-recordado.png) | Después de logout: email QA, contraseña enmascarada y Recordar activo; se pudo iniciar sin volver a escribirlos. |
| [41-camera-vista-previa-virtual.png](41-camera-vista-previa-virtual.png) | Segundo intento de escáner: imagen visible de la habitación virtual del emulador. No acredita cámara física ni lectura de QR. |
| [42-samsung-comercio-fechas-corregidas.png](42-samsung-comercio-fechas-corregidas.png) | Samsung físico con app QA, después de UTC: A2 muestra ORD-0006 entregada, ORD-0003 pendiente, contadores 1/0/1 y horas locales correctas. |

El filtro A2 de estadísticas y el menú lateral móvil también se revisaron durante
el recorrido. Las capturas conservan pasos concretos; no equivalen a cobertura
visual completa de todas las pantallas y estados.

[manifest.json](manifest.json) registra tamaño y SHA-256 de cada imagen, comprobados
contra los originales. Las capturas de incidencias previas se excluyeron de esta
selección de resultados aprobados. Las capturas de emulador corresponden a una app QA
profile en emulador con GPS Android 5.2.1, no al APK público conectado a producción.
El recorrido automático alcanzó entrega con B/C y el comercio A1 reflejó tres
entregadas hoy. La segunda oferta fue rechazada para revisar la recepción manual.
El recorrido manual también llegó a entrega; A2 mostró una entregada hoy. Se
comprobó restauración de sesión después de force-stop y arranque frío sin borrar
datos; después de logout, las credenciales recordadas permitieron ingresar sin
reescribirlas. El escáner abrió, pidió permiso y conservó el código ya escrito
al cancelarlo. Tras un primer intento negro con ayuda de escena virtual, un
segundo intento mostró la habitación virtual con TV y libros. Esto acredita
vista previa solo en emulador. No se logró orientar la cámara hacia el QR
importado a la pared virtual; no se acredita lectura QR ni cámara física.
Continúan pendientes decodificación con cámara, ruta real y ciclo completo en
teléfono físico. En Samsung, después de la corrección UTC, se inició sesión A2
con email/contraseña recordados sin reescribirlos. Se observaron ORD-0006 entregada,
ORD-0003 pendiente y contadores 1 asignada / 0 en ruta / 1 entregada hoy. Las horas
locales fueron creación 20:21, asignación 20:22, recogida 20:23 y entrega 20:30,
con reloj del teléfono 20:38. La captura 42 solo contiene nuestra app con datos
ficticios. No se guardaron imágenes personales ni se acredita ciclo físico completo,
GPS o cámara física.

El servidor QA, la instancia SQL y la proyección se conservan activos por petición
del usuario. No se programa su limpieza ni apagado.
