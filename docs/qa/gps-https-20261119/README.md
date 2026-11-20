# QA local de GPS y preparación de HTTPS — 19/11/2026

Base: `b637fae5ff05b52161cee032016342556675c4c6`. Este recibo documenta el corte local de QA previo al commit y al despliegue. La publicación en GitHub se autorizó posteriormente el mismo día; no equivale a desplegar.

La regresión se reprodujo con SQL Server: cuatro de los seis casos fallaban por autorización GPS antes del cambio; los dos controles pasaban. Tras unificar la consulta de autorización pasan los seis casos. La suite completa termina con **346 aprobadas, cero fallos y cero omitidas**. Se cubren paradas repartidas con cabecera sin piloto, asignación previa y posterior a recogida, custodia hasta recepción, cancelación con devolución pendiente, exclusión del historial terminado, tenant ajeno, actor falsificado, idempotencia y ciclo legacy.

`verification.json` conserva contadores, fechas y hashes de los recibos TRX locales y de los tres archivos C# probados. Los TRX completos están en la carpeta local ignorada `artifacts/gps-https-fix-20261118/`; no se presentan como ejecución de CI remoto.

Los ocho casos de `tools/Test-PublicHttpsChecks.ps1` pasan sin red. Verifican la decisión del comprobador; no acreditan que la regla exista en Cloudflare. El JSON público adjunto procede de peticiones anónimas: HTTPS, acceso al login, atributos Secure/HttpOnly de cookies nuevas y respuestas API anónimas cumplen. La portada y planes todavía responden por HTTP sin redirección; el resultado global es **false**, esperado antes del despliegue. No se enviaron credenciales, cookies ni operaciones sobre datos de producción.

No se ejecutó nuevamente el recorrido del emulador con el servidor corregido: exige desplegar primero y repetir las pruebas públicas con fixtures autorizadas. El APK 2.1.0 (10) se conserva; no se reconstruyó ni publicó. No hay nueva migración ni cambios móviles o en el SQL separado del portal.

Instrucciones: [DESPLEGAR-GPS-HTTPS.md](../../../DESPLEGAR-GPS-HTTPS.md).
