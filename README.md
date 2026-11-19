# Envirapido · CRM de última milla y portal comercial

## Agente de despliegue: empezar aquí

**Correccion posterior a la QA del APK 10: [GPS y HTTP/HTTPS](DESPLEGAR-GPS-HTTPS.md).**
Aplicar sobre 2.1: corrige GPS por paradas y prepara la regla HTTPS de la portada;
no requiere otra migracion ni recompilar el APK. El despliegue y la QA publica posterior estan pendientes.

**Entrega base: [Envirapido 2.1, reasignación y logos](DESPLEGAR-ENVIRAPIDO-2.1.md).**
La entrega base 2.1 actualizó el CRM, sus vistas y el cliente móvil de su repositorio
separado. **Su instalación inicial requiere la migración operativa del SQL CRM**, conservando pedidos,
rutas, códigos y tenants. No aplicar esa migración al SQL del portal.

El APK firmado 2.1.0 (10) se entrega al propietario para subirlo al portal con
descargas protegidas. Las fuentes en GitHub no equivalen a un despliegue ni a
la publicación del APK. La guía identifica el archivo, su hash y el orden de QA.

La [portada interactiva](DESPLEGAR-PORTADA.md) y el
[registro manual sin SMTP](DESPLEGAR-REGISTRO-MANUAL.md) son entregas anteriores.
Sus instrucciones de actualizar sólo Next o no migrar se limitan a esos cambios;
no sustituyen el handoff de la entrega actual.

El portal comercial se entrega en la rama **`codex/confiabilidad-panel-app`**.
Desplegar el **SHA completo comunicado por el propietario**, desde un checkout
limpio. No sustituirlo por `master`, ni actualizar a ciegas el directorio en servicio.

La [instalación inicial del portal](DESPLEGAR-PORTAL.md) queda como referencia.
El registro vigente usa aprobación manual, sin exigir SMTP para registrar.

| Componente | Destino |
| --- | --- |
| `Login/` | Publicar backend y vistas del CRM con la migración operativa revisada, conservando autenticación y datos |
| `Envirapido.Portal.Api/` | Conservar el servicio y SQL independiente ya instalados; no hay migración comercial nueva en este lote |
| `portal-web/` | Conservar React/Next ya desplegado; este lote no cambia sus fuentes |
| App Flutter / APK | Cliente 2.1.0 (10), firma definitiva y API `https://envirapido.com`; usar el SHA móvil indicado en el handoff |

No aplicar las migraciones del portal sobre la base CRM. No crear otro CRM ni
reemplazar sus claves JWT, sesiones, Data Protection, configuración SQL o datos.

- [Arquitectura y configuración](docs/portal/DESPLIEGUE.md)
- [QA local y sus límites](docs/portal/QA-20261117.md)
- [Contrato de integración](docs/portal/API-CONTRACT.md)
- [Frontend y arranque standalone](portal-web/README.md)

`tools/Envirapido.Portal.Qa` y sus cuentas ficticias son sólo para QA local. No
publicar ese ejecutable, sus endpoints `/qa/*`, su proxy responsive o sus recibos.
