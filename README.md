# Envirapido · CRM de última milla y portal comercial

## Agente de despliegue: empezar aquí

**Entrega puntual del 20/11/2026: [HTTP a HTTPS de la portada](DESPLEGAR-HTTPS-PORTADA.md).**
Aplicar únicamente la regla del borde público mediante el instalador y su verificador.
Este lote no requiere compilar/reiniciar CRM, portal o app, ni ejecutar migraciones.
La base operativa sigue en `59cd152`, [Envirapido 2.1.1 y APK 2.1.1 (11)](DESPLEGAR-ENVIRAPIDO-2.1.1.md).

El portal comercial se entrega en la rama **`codex/confiabilidad-panel-app`**.
Desplegar el **SHA completo comunicado por el propietario**, desde un checkout
limpio. No sustituirlo por `master`, ni actualizar a ciegas el directorio en servicio.

| Componente | Acción en este lote HTTPS |
| --- | --- |
| Borde Cloudflare | Aplicar sólo la regla HTTP→HTTPS; comprobarla con las herramientas nuevas |
| `Login/` | Conservar backend, vistas, autenticación, pedidos, rutas y multitenant ya desplegados |
| `Envirapido.Portal.Api/` | Conservar servicio, SQL, claves, almacenamiento y vigencias independientes |
| `portal-web/` | Conservar React/Next y sus vistas; no recompilar ni cambiar puertos |
| App Flutter / APK | Conservar 2.1.1 (11), firma definitiva y API `https://envirapido.com` |

No aplicar las migraciones del portal sobre la base CRM. No crear otro CRM ni
reemplazar sus claves JWT, sesiones, Data Protection, configuración SQL o datos.

## Guías de entregas anteriores

No repetir los pasos de estas entregas por el cambio HTTPS actual:

- [Envirapido 2.1.1: retiros, incidencias y APK 11](DESPLEGAR-ENVIRAPIDO-2.1.1.md).
- [GPS y primera preparación de la regla HTTPS](DESPLEGAR-GPS-HTTPS.md).
- [Base 2.1: reasignación y logos](DESPLEGAR-ENVIRAPIDO-2.1.md). Su instalación inicial necesita la migración operativa indicada; no volver a ejecutarla para este lote ni aplicarla al SQL del portal.
- [Instalación inicial del portal](DESPLEGAR-PORTAL.md), [portada interactiva](DESPLEGAR-PORTADA.md) y [registro manual sin SMTP](DESPLEGAR-REGISTRO-MANUAL.md). El registro vigente usa aprobación manual.

Las fuentes en GitHub no equivalen a aplicar una regla de Cloudflare ni a publicar
un APK. El agente del servidor debe conservar el recibo del despliegue y su
comprobación pública.

- [Arquitectura y configuración](docs/portal/DESPLIEGUE.md)
- [QA local y sus límites](docs/portal/QA-20261117.md)
- [Contrato de integración](docs/portal/API-CONTRACT.md)
- [Frontend y arranque standalone](portal-web/README.md)

`tools/Envirapido.Portal.Qa` y sus cuentas ficticias son sólo para QA local. No
publicar ese ejecutable, sus endpoints `/qa/*`, su proxy responsive o sus recibos.
