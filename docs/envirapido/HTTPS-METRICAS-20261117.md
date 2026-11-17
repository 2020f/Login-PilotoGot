# HTTPS y actualización de métricas de Pedidos

Fecha: 17 de noviembre de 2026 (República Dominicana). Guía del lote para el
agente de despliegue. Su publicación no acredita instalación ni validación del
servidor público; registrar el commit exacto desplegado y sus comprobaciones.

## Alcance y vigencia

Este lote agrega la redirección HTTP a HTTPS antes de Identity y corrige los dos
valores del subtítulo de la tarjeta **Pedidos → En curso** durante el refresco
automático. No cambia asignaciones, estados de pedidos, rutas, cobros, permisos,
suscripciones, códigos B/C ni aislamiento por empresa. No requiere migración SQL,
seeds ni otro APK: la app publicada ya utiliza HTTPS.

Esta guía actualiza la política de redirección descrita en
[HTTPS-PROXY-DESPLIEGUE.md](HTTPS-PROXY-DESPLIEGUE.md), del 12 de noviembre.
La indicación histórica de aquel lote sobre no agregar `UseHttpsRedirection`
corresponde a ese cambio anterior. Desde este lote sí se utiliza el middleware
con el puerto público explícito. Se mantienen las mismas fronteras de confianza
del proxy, las cookies Secure/HttpOnly y la protección CSRF.

Conservar el dominio nuevo y el anterior atendiendo directamente el mismo
backend, base de datos y claves, según
[DOMINIO-ENVIRAPIDO-20261116.md](DOMINIO-ENVIRAPIDO-20261116.md):

- `https://envirapido.com`: origen canónico del APK 2.0.1 (9) y del panel.
- `https://pilotogoapp.zapflow77.dpdns.org`: compatibilidad con APK anteriores.

No sustituir la API anterior por una redirección entre dominios.

## Redirección y despliegue

El middleware de redirección se ejecuta **después de `UseForwardedHeaders`** y
antes de archivos estáticos, autenticación y páginas de Identity/Razor. En los
entornos publicados, incluidos Production y Staging, HTTP recibe **307 hacia
HTTPS en el puerto público 443**, conservando host, ruta y consulta. 307 conserva
el método y el cuerpo cuando el cliente sigue la redirección. Development y
Testing quedan exentos de esta redirección; sus excepciones de QA no deben
trasladarse al servidor publicado.

Una petición HTTPS efectiva, incluida la que llega por HTTP interno desde el
proxy autorizado con `X-Forwarded-Proto: https`, continúa sin otro salto HTTPS.
Una cabecera de protocolo enviada desde un proxy desconocido no concede esa
confianza. El puerto 443 explícito evita descubrir un puerto interno de Kestrel
o una variable de desarrollo como destino público.

Antes del reinicio:

1. Conservar conexión SQL, claves JWT y Data Protection, sesiones, configuración
   privada, mapas, SMTP y los nombres atendidos. No regenerar claves ni iniciar
   otra base para aplicar esta corrección.
2. Comprobar la IP exacta del proxy inmediato que ve Kestrel. Mantener las
   direcciones autorizadas actuales y `ForwardLimit=1`. No agregar redes,
   comodines, nombres DNS ni direcciones de visitantes a la confianza del proxy.
3. El proxy debe sobrescribir/sanear `X-Forwarded-Proto` con el protocolo público
   validado, conservar el host y mantener Kestrel en su enlace interno. Si hay
   varios saltos, no reemplazar el protocolo HTTPS original por el HTTP del
   último enlace interno. No habilitar confianza global ni interpretar
   `X-Forwarded-Host` como solución a un bucle.
4. Mantener `AllowedHosts` restringido a los nombres legítimos de la instalación,
   incluyendo el dominio anterior y cualquier health check interno confirmado.
   Véase [envirapido.com.env.example](envirapido.com.env.example). La redirección
   conserva el host recibido; el filtrado de hosts debe rechazar los ajenos.
5. Instalar el backend completo del commit indicado, preservando su entorno
   Production o Staging. Mantener TLS válido y el acceso por ambos dominios.
   No cambiar el entorno a Development/Testing para evitar una redirección.

El borde público también puede efectuar la subida HTTP a HTTPS. Para métodos
con cuerpo, utilizar 307/308 si el borde redirige, evitando convertir un POST en
GET. Las credenciales siempre deben enviarse directamente a HTTPS; una
redirección no protege un cuerpo que ya se transmitió por HTTP.

## Qué corrige la tarjeta En curso

La tarjeta conserva su total y ahora identifica los dos valores del subtítulo
con `data-ev-value="recogiendo"` y `data-ev-value="enRuta"`. El fragmento ya
entregaba esas cifras; el JavaScript existente actualiza los elementos enlazados
sin recargar la página. La vista mantiene el cálculo **En curso = recogiendo +
en ruta**, con los mismos filtros de comercio, repartidor y empresa.

Con dos órdenes ficticias y sin otras órdenes en ese contexto, el escenario
esperado es:

| Momento | Recogiendo | En ruta | En curso | Entregados |
| --- | ---: | ---: | ---: | ---: |
| Ambas asignadas | 2 | 0 | 2 | 0 |
| Primera recogida con B | 1 | 1 | 2 | 0 |
| Primera entregada con C | 1 | 0 | 1 | 1 |
| Segunda recogida con B | 0 | 1 | 1 | 1 |
| Segunda entregada con C | 0 | 0 | 0 | 2 |

El refresco existente se programa cada cinco segundos. Se pausa mientras la
pestaña está oculta, un diálogo está abierto o se edita un campo; comprobarlo con
la página visible y sin una edición activa. Este lote enlaza los valores del
subtítulo; no introduce otro mecanismo de consulta ni modifica las métricas de
pilotos o las estadísticas financieras de Conciliación.

## Validación local con SQL Server

Usar el checkout del commit a desplegar y dependencias restauradas. El arnés y
las pruebas trabajan con bases `PilotGoTest_<GUID>` y cuentas ficticias, nunca
con `DefaultConnection`. Iniciar únicamente la instancia LocalDB QA prevista.
En esta PC, el runtime privado se selecciona para el proceso actual:

```powershell
. .\tools\Envirapido.Qa\Use-Net10Qa.ps1
$env:PILOTGO_TEST_SQLSERVER='1'
$env:PILOTGO_TEST_SQLSERVER_INSTANCE='PilotGoVisualQA_20261111'
& $env:DOTNET_HOST_PATH test Login.sln --no-restore --filter 'FullyQualifiedName~HttpsEnforcementTests|FullyQualifiedName~LiveOrderMetricsTests|FullyQualifiedName~TransportSecurity' --logger trx --results-directory artifacts/https-metricas-20261117
```

Comprobar HTTP GET/POST, conservación de ruta/consulta y puerto público, HTTPS
directo y reenviado por proxy confiable, proxy desconocido, rechazo de host
ajeno, cookies y ausencia de bucles. Las pruebas de producción deben conservar
la base aislada y deshabilitar las automatizaciones de fondo; no deben consultar
el SQL configurado para un servidor real.

`LiveOrderMetricsTests` recorre asignación, recogida B y entrega C de dos órdenes;
verifica los dos enlaces del subtítulo y el resumen recibido por el refresco,
con filtros por comercio y piloto y exclusión de otra empresa. Completar las
regresiones de pedidos, rutas, sesiones y multitenant apropiadas al lote y
registrar los resultados TRX reales. Una guía o un resultado de un commit previo
no acredita la ejecución sobre el árbol final.

Para la comprobación visual con SQL local, usar el
[arnés QA de loopback](../../tools/Envirapido.Qa/README.md) con su base correcta.
Mantener Pedidos abierto mientras cambian las dos órdenes ficticias y observar
total y subtítulo después del siguiente refresco exitoso. No sembrar ni borrar
una base QA histórica para conseguir las cifras del ejemplo.

## Comprobación después del despliegue

Ejecutar el comprobador anónimo contra ambos dominios:

```powershell
.\tools\Verify-PublicHttps.ps1 -BaseUrl https://envirapido.com -RequireSuccess
.\tools\Verify-PublicHttps.ps1 -BaseUrl https://pilotogoapp.zapflow77.dpdns.org -RequireSuccess
```

Además de HTTPS/cookies/API, comprueba GET HTTP de raíz y login con una consulta
ficticia fija: exige subir a HTTPS, al mismo host y al puerto 443, conservando
ruta y consulta. Admite 301/302/307/308 en esos GET por si el borde resuelve la
redirección antes del backend. No sigue `Location`, no inicia sesión, no envía
credenciales ni imprime valores de cookies o autenticación. El recibo informa
estado y destino desglosado; la consulta del sondeo no contiene datos reales.

El resultado anónimo no acredita la cookie autenticada, el commit instalado,
SQL ni los pedidos. Con las cuentas de prueba autorizadas, iniciar sesión web
directamente por HTTPS, abrir Pedidos y verificar el refresco del subtítulo junto
con su total y filtros. Si la página estaba abierta antes del despliegue,
recargarla una vez para recibir el HTML nuevo; después observar los cambios sin
recargas manuales. Si hace falta recorrer cambios de estado, usar sólo
órdenes ficticias autorizadas y mantener el flujo B en recogida/C en entrega.
No editar operaciones reales para provocar cifras ni volver a registrar cobros
para comprobar una tarjeta. Conservar los demás pedidos, rutas y empresas.

Registrar commit/artefacto, hora UTC, dominios, resultados y límites; excluir
contraseñas, tokens, cookies y datos personales de los recibos publicados.
No declarar el servidor corregido hasta completar estas verificaciones después
de instalar el commit.

## Reversión

Conservar el binario y configuración anteriores junto con la misma base y claves.
Este lote no requiere revertir SQL ni reinstalar la app. Si el proxy provoca un
bucle, corregir su protocolo/IP de confianza o revertir el binario de manera
coordinada; no desactivar Secure, CSRF ni ampliar la confianza a cualquier proxy.
Una reversión puede restaurar el acceso HTTP defectuoso y el subtítulo detenido:
mantener el cierre pendiente hasta verificar otra vez el despliegue efectivo.

## Referencias

- [ASP.NET Core 10: HTTPS y puerto de redirección detrás de un proxy](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0).
- [ASP.NET Core 10: proxies, cabeceras reenviadas y confianza](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).
