# A12 — seguimiento Android con autorización temporal

Implementación y validación local del servicio `PilotTrackingService`, 12/11/2026. **A12 aprobado para el escenario nativo ejecutado en el emulador, con SQL Server y ubicación simulada.** Se observó corte de conexión, pausa sin solicitudes GPS, recuperación en segundo plano dentro del mismo FGS y detención del seguimiento al terminar el trabajo. No acredita navegación física ni comportamiento de un Samsung/OEM bajo todas las condiciones.

## Ciclo del servicio

1. La app visible obtiene ubicación precisa y una autorización de trabajo vigente del servidor. El bridge recibe solamente un identificador de contexto y la duración restante de esa autorización: nunca JWT ni contraseñas.
2. MainActivity permite iniciar el servicio únicamente estando `resumed`. El Future Dart espera tanto el ACK del canal de eventos como el ACK real de `Service.onStartCommand`, posterior a crear el FGS tipo `location` y registrar GPS en LocationManager.
3. Durante una autorización vigente, LocationManager solicita GPS cada10s/0m. Sólo se emiten posiciones capturadas dentro de la ventana de autorización del mismo contexto; no existe cola nativa. La cola cifrada, idempotencia y envío siguen en Dart.
4. Al vencer la autorización, un Handler nativo con reloj `SystemClock.elapsedRealtime` quita las solicitudes de GPS aunque los timers Flutter no se ejecuten. Los callbacks tardíos también comprueban el plazo y se descartan. El servicio permanece foreground con notificación **GPS en pausa**, sin capturar ni emitir posiciones.
5. Esta pausa conserva como máximo cinco minutos de recuperación, con wakelock temporal acotado. Renovar dentro de esa ventana actualiza la autorización y vuelve a registrar GPS **en el mismo servicio ya activo**, sin intentar otro `startForeground` desde segundo plano.
6. Al agotar la ventana, detener/revocar/cerrar sesión o terminar el trabajo, cesan las actualizaciones y el servicio termina. `START_NOT_STICKY` impide reinicios implícitos sin una nueva validación visible. Recuperarse después exige volver a la app. La notificación de recuperación es de mejor esfuerzo: Android no garantiza callbacks al matar un proceso, y el permiso de notificaciones puede estar denegado.

El servicio no necesita `ACCESS_BACKGROUND_LOCATION`: se inicia desde una Activity visible y conserva su FGS existente durante la recuperación. El manifiesto declara ubicación precisa/aproximada, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_LOCATION`, notificaciones y wakelock. No se modificó código del plugin Geolocator.

## Aislamiento y límites

- El owner es inmutable y debe incluir API/cuenta/versión de sesión más un nonce por tracker. Es información de contexto en memoria; no una credencial ni autorización autónoma.
- `pause`, `renew` y `stop` sólo afectan al owner esperado. Una orden o callback tardío de un contexto anterior no debe detener uno nuevo.
- El lease admitido nunca supera15 minutos y se descuenta tiempo de tránsito del comando. El reloj del plazo nativo es monotónico.
- Pausas repetidas no extienden los cinco minutos de recuperación. Una renovación de fondo después de ese plazo se rechaza; iniciar otra vez requiere Activity visible.
- La revocación del permiso preciso detiene el servicio. Una entrega/tenant/sesión inválidos se detienen desde las guardas Dart y del servidor; el lease nativo limita el tiempo si el cliente deja de ejecutar.
- Los EventSink se desprenden al destruir Activity y están vinculados al owner. Si el messenger desaparece, se descartan eventos; no se conservan posiciones en Android.

## Contrato Dart

`NativePilotTracking` expone `active`, `alive`, `paused` y ChangeNotifier. `start(owner, leaseMillis)` devuelve el stream de Position sólo después de confirmar el arranque nativo. `renew`, `pause`, `stop` y `status` usan argumentos nombrados. El estado pausado mantiene el stream abierto, `alive=true`, `active=false`; no es un error que destruya el FGS. Un cierre inesperado o ventana vencida comunica que se debe abrir la app.

Los canales son `envirapido/pilot_tracking` y `envirapido/pilot_tracking_events`. El bridge serializa comandos y acota esperas para no bloquear otros contextos si la Activity desaparece. Cancelar una suscripción Dart sólo desprende eventos; la autorización nativa, `pause` y `stop` controlan realmente el GPS.

## Recibo nativo observado — emulador y SQL Server

El recorrido se ejecutó en `emulator-5554` con el APK QA profile x64 **2.0.0+6**, paquete `com.pilotgo.app.qa`. El API embebido era `http://127.0.0.1:5019`; el túnel del emulador lo conectaba al host QA en5021. Se usó la base ficticia `PilotGoTest_549aabba9bdc4061bf017dcf5e73b530`, piloto2, sin producción ni otro dispositivo. Las horas siguientes son UTC del12/11/2026.

| Instante / fase | Muestras guardadas en SQL | Estado nativo observado |
| --- | --- | --- |
| **14:26:52**, antes de cortar el túnel | 12; última captura14:26:37.149. | GPS `HIGH_ACCURACY` cada10s y FGS activo. Se corta la conexión a14:26:52.688. |
| **14:29:40**, pausa sin red, más de167s después | Continúan12, con la misma última captura/recepción. | GPS `ProviderRequest[OFF]`, sin listener activo del seguimiento. El FGS permanece activo para la recuperación. |
| **14:32:53**, restauración del túnel | Todavía12. | GPS sigueOFF en la lectura previa a renovar; FGS retenido. Se restaura la conexión a14:32:53.552, sin abrir la app. |
| **14:33:13**, recuperado en segundo plano | 16; última captura14:32:59.367, recibida14:32:59.936. | Vuelve GPS `HIGH_ACCURACY` cada10s dentro del mismo FGS. No se abrió la app para recuperarlo. |
| **14:39:01**, después de finalizar el recorrido | 27; última captura14:38:27.565. | `PilotTrackingService` ya no figura en servicios activos; `serviceForeground=false`. |
| **14:39:32**, sin trabajo y sin nueva muestra | Se mantienen27 y la misma última captura/recepción durante otros30s. | Sigue ausente el servicio de seguimiento y el proveedorGPS apareceOFF. |

El registro del servicio fue **`ServiceRecord{25cf3bf … PilotTrackingService}`** antes del corte, durante la pausa y al recuperarse. Los tres dumps conservan **`startForegroundCount=1`**: la recuperación observada reutilizó el FGS existente. Este resultado prueba el camino nativo; no deriva sólo de mocks o de un contador Dart.

El cierre general del recorrido completó R1 con dos recogidas B y tres entregas C. Los recibos posteriores prueban que el seguimiento terminó y dejó de añadir muestras SQL. El primer dump al finalizar todavía muestra una solicitud Fused de otra función en pantalla; no se interpreta como `PilotTrackingService` activo ni se afirma que todo uso de ubicación del teléfono cesara instantáneamente. La lectura posterior sin trabajo muestra el proveedorOFF.

Evidencia conservada:

- [Antes del corte](../../artifacts/correcciones-flujos-20261112/gps/antes-corte.json), [GPS](../../artifacts/correcciones-flujos-20261112/gps/antes-corte-location.txt) y [servicio](../../artifacts/correcciones-flujos-20261112/gps/antes-corte-servicio.txt).
- [Pausa sin red](../../artifacts/correcciones-flujos-20261112/gps/pausa-sin-red.json), [GPS OFF](../../artifacts/correcciones-flujos-20261112/gps/pausa-sin-red-location.txt) y [mismo servicio](../../artifacts/correcciones-flujos-20261112/gps/pausa-sin-red-servicio.txt).
- [Restauración](../../artifacts/correcciones-flujos-20261112/gps/restauracion.json), [recuperación en segundo plano](../../artifacts/correcciones-flujos-20261112/gps/recuperado-background.json), [GPS restablecido](../../artifacts/correcciones-flujos-20261112/gps/recuperado-background-location.txt) y [FGS conservado](../../artifacts/correcciones-flujos-20261112/gps/recuperado-background-servicio.txt).
- [Ruta finalizada](../../artifacts/correcciones-flujos-20261112/gps/ruta-finalizada.json), [servicio detenido](../../artifacts/correcciones-flujos-20261112/gps/ruta-finalizada-servicio.txt), [SQL estable sin trabajo](../../artifacts/correcciones-flujos-20261112/gps/sin-trabajo-sin-captura.json) y [GPS posterior OFF](../../artifacts/correcciones-flujos-20261112/gps/sin-trabajo-sin-captura-location.txt).

El [recibo del APK](../../artifacts/correcciones-flujos-20261112/app/Envirapido-2.0.0-6-qa-verificacion.json) identifica SHA-256 **`154e477275577ffc0d4567449a8722de8a04591a935be392c32219a3c458de66`**, ELF x86_64, firma QA, API local e instalación sólo en el emulador. La [suite final Flutter](../../artifacts/correcciones-flujos-20261112/app/flutter-suite-final.log) aprobó **86/86** y el [análisis final](../../artifacts/correcciones-flujos-20261112/app/analyze-final.log) terminó sin incidencias. Estas comprobaciones automatizadas complementan el recibo nativo anterior.

## Límites de esta aprobación

Se aprobó la recuperación del servicio existente dentro de su ventana y su cierre al terminar el trabajo, en el escenario documentado del emulador. Siguen pendientes Samsung físico, navegación real con Waze/Maps, ahorro de batería/OEM, muerte forzada del proceso durante trabajo, reinicio del teléfono y recuperación después de agotar los cinco minutos. No se extiende este PASS a esas condiciones ni a producción. El APK es de QA local y no una distribución para los pilotos.
