# Envirapido: lote para el agente de despliegue

El propietario autorizó publicar el código validado en GitHub el 12/11/2026.
El despliegue y las comprobaciones del servidor real corresponden al agente
encargado de ese entorno. Este lote no ejecuta despliegues ni cambios en la
base productiva.

## Repositorios y versiones

| Componente | Repositorio y referencia |
| --- | --- |
| Servidor/CRM | `2020f/Login-PilotoGot`, rama `codex/confiabilidad-panel-app`. Usar el commit que contiene este documento, con sus migraciones y snapshot completos. |
| App Flutter | `2020f/pilotgo-app`, misma rama, commit **`73e48c171ca2398d9a7efb8c35de9a2d11824a17`**, árbol `7a95161f6352f0a5117bb49a54a188abec9d04f7`. |
| Runtime del servidor | .NET y ASP.NET Core **10.0.15**, TFM `net10.0`, SDK base **10.0.201**. |
| Cliente móvil | **2.0.0+8**, contrato de órdenes **3**, paquete habitual `com.pilotgo.app`. |

El servidor usa la rama indicada; `master` y `main` son referencias distintas.
La publicación de este lote no implica fusionar esas ramas ni generar una
release/APK de distribución. Los commits y referencias remotas deben verificarse
al preparar el checkout de despliegue, sin sustituirlos por una versión anterior.

## Validación conservada

SQL Server **231/231**, Flutter **93/93**, nuevas pruebas de compatibilidad
.NET 10 y recorrido visual en emulador contra el host QA Release. La sesión
continuó, se recibieron manuales y automáticas con otras activas, se observaron
seis simultáneas y la automática terminó con B en recogida y sólo C al entregar.
Los diez fingerprints anteriores permanecieron iguales; no hubo cruces de
empresa entre pilotos, rutas ni códigos en SQL.

Los [recibos sanitizados versionados](../qa/envirapido-net10-20261112/README.md)
conservan los resultados y sus límites. Sus banderas de publicación describen
el momento previo al commit/push. Los logs completos, firmas, JWT/refresh,
keyring, respaldos, bases, APK QA y runtime privado permanecen fuera de Git.

## Requisitos del destino

- Publicar **`Login/Login.csproj`**; `tools/Envirapido.Qa` y sus accesos ficticios
  son herramientas locales, fuera del producto.
- Revisar/aplicar según el historial real las migraciones funcionales incluidas:
  `20261112021932_EnvirapidoParadasOperaciones`,
  `20261112025055_EnvirapidoRetornosYCajaEmpresa` y
  `20261112201846_EnvirapidoSaasBilling`, además de las anteriores que correspondan.
  Actualizar .NET no agregó una migración. La base QA histórica `EnsureCreated`
  no sustituye el historial del servidor real.
- Conservar configuración JWT, claves Data Protection e identidad del servicio,
  base original, firma Android y origen público
  `https://pilotogoapp.zapflow77.dpdns.org`. Backend y esquema compatibles antes
  de distribuir el cliente con contrato 3.
- Configurar secretos del destino fuera de Git, entorno Production, correo,
  mapas y proxy/TLS. La revisión detectó claves Google preexistentes en
  `Login/appsettings.json`, ya presentes en el historial público: deben rotarse
  en el proveedor y suministrarse por configuración privada antes del pase.
  No se cambiaron ni rotaron credenciales en este lote de publicación.
- Completar respaldo restaurado en ensayo y smoke autenticado del destino.
  El respaldo QA tuvo CHECKSUM y VERIFYONLY, no restauración real. El host
  público, SMTP y GPS físico no están acreditados por las pruebas locales.

Detalles: [informe .NET 10](NET10-ACTUALIZACION-20261112.md),
[guía de despliegue](NET10-DESPLIEGUE.md) y
[superadministración SaaS](SUPERADMIN-SAAS-20261112.md).
