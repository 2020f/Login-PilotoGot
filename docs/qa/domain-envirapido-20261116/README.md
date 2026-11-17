# Evidencia del cambio de dominio

Candidato Android **2.0.1 (9)** para `https://envirapido.com`.
El [recibo sanitizado](verification.json) identifica fuente, APK, firma y alcance
real de las pruebas. Los archivos compilados, logs completos y configuración
privada permanecen fuera del repositorio.

La fuente móvil publicada coincide con los 83 hashes guardados durante la QA y
construcción del APK. La publicación conserva la validación previa: **99/99
pruebas Flutter** y análisis sin incidencias; no se repitió la suite para subir
los mismos archivos a GitHub. Firma original, paquete, versión, tres ABI y
alineación de 16 KiB verificados sobre el APK distribuible.

Las pruebas usan HTTP y almacenamiento nativo simulados. No acreditan un login,
pedidos ni GPS físico en el dominio nuevo. La observación anónima previa al
apuntado encontró raíz 522 y API 200 HTML en el dominio nuevo; el anterior pasó
HTTPS/cookies/401 JSON. Este candidato permanece preliminar hasta configurar el
host y completar la validación autenticada.

Instrucciones para el agente: [guía de dominio](../../envirapido/DOMINIO-ENVIRAPIDO-20261116.md).
