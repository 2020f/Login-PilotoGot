# QA de HTTPS y desglose de Pedidos

Lote web sobre la base `88bd6fac79c1cd35ea0a5ba26f468efa48622e08`.
El [recibo](verification.json) contiene los resultados y hashes de las fuentes
validadas. Los TRX, logs y respuestas anónimas completas quedan en
`artifacts/web-https-metrics-20261117/`, fuera de Git.

Se comprueba el pipeline real en Production con un host de prueba, claves
efímeras, automatizaciones deshabilitadas y la base aislada de `PilotGoFactory`.
HTTP debe redirigir con 307 al mismo host en HTTPS/443, antes de Identity y
archivos estáticos. Se verifican método, consulta, proxies confiables y ajenos,
rechazo de Host, cookies Secure/HttpOnly y denegación JSON de la API por HTTPS.

La regresión del desglose conserva la tarjeta inicial y exige ambos enlaces
de actualización; comprueba los fragmentos durante asignación, recogida con B
y entrega con C de dos órdenes. Incluye filtros por comercio y piloto y datos
de una segunda empresa. El test integra HTTP, render y contrato del refresco;
no ejecuta el JavaScript en un navegador.

Las pruebas SQL utilizan únicamente bases ficticias `PilotGoTest_<GUID>` de la
instancia LocalDB QA indicada en el recibo. No son una validación del SQL del
servidor publicado. La app móvil no cambia en este lote.

El sondeo público previo al despliegue encontró HTTPS/cookies/API correctos en
ambos dominios, pero HTTP todavía devuelve 302 hacia login HTTP y 500 en login.
Ese resultado no corresponde al código nuevo instalado: este lote se publica
para que el agente de despliegue lo instale y verifique según la
[guía](../../envirapido/HTTPS-METRICAS-20261117.md).

La aceptación final del servidor y la observación visual del refresco quedan
pendientes de ese despliegue; no modificar pedidos reales para provocarlas.
