# Evidencia local conservada para la publicación

Estos cinco recibos corresponden a la validación local final del 12/11/2026,
anterior al commit/push autorizado posteriormente. Se copiaron sin modificar
desde los artefactos QA. Los HEAD de `verification.json` son las bases previas
del árbol validado; los valores `githubPushed=false` y `productionDeployed=false`
describen ese momento, no el estado futuro de GitHub o del servidor.

- SQL Server: 231/231, sin fallos ni omitidas, configuración Debug.
- Flutter: 93/93, ejecución nueva.
- App en emulador contra QA Release: sesión anterior, manual y automática con
  seis activas, B para recogida, sólo C al entregar, cierre e historial.
- Fingerprints originales: 10/10 conservados; no se modificaron las 17 órdenes
  originales ni sus 35 códigos en los campos medidos.
- Interoperabilidad real de JWT/refresh .NET 8 → host .NET 10 y de cookies con
  las mismas claves persistentes: PASS, con límites detallados en los recibos.

`upgrade-source-hashes.json` identifica las fuentes de la actualización de
runtime. No es un inventario completo de todos los archivos de la solución.
Git normaliza finales de línea según los atributos de cada repositorio; estos
hashes corresponden a los bytes locales registrados durante QA.

La DLL Release del producto publicada localmente y la del host QA usado por el
emulador coinciden en SHA-256:
`7AF4A33072C32B99339D828D7D822767EC2C9AC6D25B2904A36F3B17F9730F9A`.
La suite SQL fue Debug; no se atribuye igualdad binaria Debug/Release.

Los logs completos, capturas, binarios, tokens de interoperabilidad, claves,
respaldos y runtime privado permanecen fuera de Git. No hay credenciales de
producción en estos recibos. Publicar el código no despliega ni migra el servidor.
Consultar [el informe completo](../../envirapido/NET10-ACTUALIZACION-20261112.md)
y [los requisitos del destino](../../envirapido/NET10-DESPLIEGUE.md).
