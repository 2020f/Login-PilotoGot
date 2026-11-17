# Envirapido · CRM de última milla y portal comercial

## Agente de despliegue: empezar aquí

**Actualización más reciente: [portada interactiva](DESPLEGAR-PORTADA.md).**
Sobre el portal base ya instalado, publicar **sólo el bundle React/Next**.
Esta actualización no cambia las demás vistas, CRM, API, SQL, app ni multitenant.
La guía distingue este paso de la instalación inicial descrita a continuación.

El portal comercial se entrega en la rama **`codex/confiabilidad-panel-app`**.
Desplegar el **SHA completo comunicado por el propietario**, desde un checkout
limpio. No sustituirlo por `master`, ni actualizar a ciegas el directorio en servicio.

**Instrucciones del cambio: [DESPLEGAR-PORTAL.md](DESPLEGAR-PORTAL.md).**
Incluyen qué actualizar, base SQL separada, SMTP, servicios, HTTPS, pruebas y rollback.

| Componente | Destino |
| --- | --- |
| `Login/` | Actualizar el CRM existente y su nueva sección SuperAdmin; conservar su base y autenticación |
| `Envirapido.Portal.Api/` | Servicio comercial nuevo, con base `EnvirapidoPortal`, identidad y claves propias |
| `portal-web/` | React/Next.js: portada en `envirapido.com` y cuenta en `portal.envirapido.com` |
| App Flutter / APK | Sin cambios en esta entrega; su API sigue en `envirapido.com/api/*` |

No aplicar las migraciones del portal sobre la base CRM. No crear otro CRM ni
reemplazar sus claves JWT, sesiones, Data Protection, configuración SQL o datos.

- [Arquitectura y configuración](docs/portal/DESPLIEGUE.md)
- [QA local y sus límites](docs/portal/QA-20261117.md)
- [Contrato de integración](docs/portal/API-CONTRACT.md)
- [Frontend y arranque standalone](portal-web/README.md)

`tools/Envirapido.Portal.Qa` y sus cuentas ficticias son sólo para QA local. No
publicar ese ejecutable, sus endpoints `/qa/*`, su proxy responsive o sus recibos.
