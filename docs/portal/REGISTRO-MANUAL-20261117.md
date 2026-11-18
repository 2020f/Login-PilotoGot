# Registro del portal sin SMTP y activación manual

## Decisión del propietario

El comprador registra su correo como dato de contacto, contraseña, negocio y plan.
No tiene que validar un mensaje de correo. SuperAdmin revisa la solicitud, confirma
el pago externo cuando corresponda y habilita las fechas de acceso del portal.
Registrar la cuenta permite consultar su solicitud; no concede acceso operativo.

Este cambio está implementado y validado localmente. El propietario autorizó su
publicación en GitHub para que otro agente lo despliegue. Este informe documenta
la validación previa a esa entrega; no acredita despliegue público. Usar el SHA
completo comunicado con la entrega y seguir
[DESPLEGAR-REGISTRO-MANUAL.md](../../DESPLEGAR-REGISTRO-MANUAL.md).

## Comportamiento implementado

- `Portal:RequireEmailConfirmation=false` es el modo predeterminado.
- El registro funciona con `Portal:Mail:Enabled=false`: no genera tokens de
  confirmación ni mensajes en la cola SMTP y conserva `EmailConfirmed=false`.
  No se representa una dirección sin verificar como verificada.
- La solicitud queda `Submitted`; el comprador ve que administración la revisará.
  Su enlace CRM y las descargas permanecen bloqueados.
- Aprobar la solicitud no confirma pagos ni asigna automáticamente vigencia.
  Se exige cuenta activa, solicitud propia aprobada, permiso habilitado,
  pago confirmado suficiente cuando corresponda y fechas RD inclusivas.
- Suspensión, revocación, anulación del pago y vencimiento siguen bloqueando los
  recursos en el servidor. GET, HEAD y Range de descargas vuelven a comprobarlo.
- SuperAdmin muestra «Correo de contacto · activación manual». La cuenta web,
  contraseña y base de datos siguen separadas de Identity, tenants y cobertura CRM.
- Sin SMTP, la recuperación de contraseña muestra los contactos de administración;
  no promete un mensaje automático. No se añadió un restablecimiento manual de
  contraseñas por SuperAdmin como parte de esta corrección.
- El modo anterior sigue disponible explícitamente con
  `Portal:RequireEmailConfirmation=true`; ese modo requiere correo transaccional.
- La API informa la política en `GET /portal-api/v1/auth/options` y en los DTO de
  usuario/comprador. El frontend no interpreta la ausencia del flag como aprobación.
- Un `EMAIL_UNAVAILABLE` tipado es un fallo concluyente: el formulario conserva
  sus campos sin bloquearlos por un resultado incierto. Los demás fallos de red o
  503 conservan el mismo UUID y payload para reintentar sin duplicar registros.

## Validación ejecutada

Evidencia local del árbol de trabajo, basado en backend
`3726d995eb1e1a1f9871a822ec4dd99d5c24ff43`, rama
`codex/confiabilidad-panel-app`. Ese SHA es la base, no un commit de este cambio.
Se conservaron las modificaciones previas de reasignación y de la app móvil.

| Comprobación | Resultado | Evidencia local |
| --- | --- | --- |
| Suite API portal sobre SQL Server LocalDB | 57/57, 0 fallos y 0 omitidas | `artifacts/portal-registro-20261117/portal-manual-sql-final.trx` |
| Puente CRM/SuperAdmin del portal con SQL Server | 8/8, 0 fallos y 0 omitidas | `artifacts/portal-registro-20261117/crm-portal-bridge-final.trx` |
| Pruebas Node del portal | 28/28 | `artifacts/portal-registro-20261117/node-final.log` |
| Next.js producción y empaquetado standalone | Aprobado | `artifacts/portal-registro-20261117/next-build.log` |
| API, Contracts, CRM/Razor y host QA .NET 10 | Compilación aprobada | `artifacts/portal-registro-20261117/build.log` y `crm-portal-bridge-final.log` |

Antes de publicar se repitieron las 57 pruebas SQL del portal, las 8 del puente
SuperAdmin y las 28 Node en un candidato aislado: archivo de la base Git más
únicamente los archivos de esta entrega. Todo pasó sin fallos ni omitidas. Así
se comprobó que el lote no depende de los cambios locales pendientes de
reasignación ni del rediseño Flutter. Evidencia:
`artifacts/portal-registro-20261117/publicacion/portal-isolated.trx`,
`bridge-isolated.trx`, `node-isolated.log` y `source-manifest.json`.
El build Next y el recorrido visual corresponden a los mismos fuentes de portal
validados previamente; no se presentan como un nuevo despliegue ni una nueva
ejecución de la app móvil.

Las pruebas nuevas cubren alta sin SMTP con el guard de correo no local,
idempotencia, ausencia de tokens/cola/pagos/permisos automáticos, nuevo login,
aprobación, pago aplicable, inicio futuro, activación, suspensión, anulación de
pago, vencimiento y descargas protegidas con un artefacto sintético de prueba.
El modo opcional de confirmación conserva el fallo seguro si SMTP no está
disponible y permite reintentar la misma operación tras recuperar el transporte.

La primera ejecución de la suite desde un directorio de salida personalizado
aprobó 54 pruebas y falló 3 pruebas de localización del script de migración.
La ejecución final desde la salida normal del proyecto encontró el script
versionado y aprobó las 57, sin modificar las aserciones ni el producto por ello.

Recorrido real en navegador sobre servicios loopback con dos bases SQL nuevas y
separadas, perfil y comprador ficticios, catálogo CRM de QA y SMTP deshabilitado:

1. Registro del comprador, plan gratuito ficticio y datos del negocio.
2. Cuenta pendiente, sin descarga ni enlace al CRM habilitados.
3. Solicitud visible en SuperAdmin con correo de contacto y activación manual.
4. Aprobación y vigencia desde formularios del panel; contador vigente pasa a 1.
5. Comprador con acceso habilitado y URL CRM canónica, sin seguirla a producción.
6. Perfil sin aviso de confirmar correo; ayuda de recuperación sin promesa SMTP.
7. Cierre de sesión y nuevo acceso con la contraseña registrada.

Capturas locales: `registro-pendiente.png`, `registro-activado.png`,
`recuperacion-sin-smtp.png` y `login-manual-activado.png`, en el mismo directorio
de evidencia. El plan con pago obligatorio se validó en las pruebas SQL; no se
registró dinero real. No se creó ningún comprador en producción.

## Entrega al agente del servidor

Consultar también [DESPLIEGUE.md](DESPLIEGUE.md),
[API-CONTRACT.md](API-CONTRACT.md) y [DESPLEGAR-PORTAL.md](../../DESPLEGAR-PORTAL.md).
Este ajuste del registro no añade migraciones ni columnas. No aplicar por ello
migraciones operativas de otros cambios pendientes del árbol de trabajo.

1. Respaldar y conservar conexiones separadas, datos, claves Data Protection,
   credenciales de integración, almacenamiento privado y configuración del CRM.
2. Publicar API portal y Contracts, CRM para la vista/contrato de SuperAdmin y
   reconstruir `portal-web` desde el SHA completo entregado al agente del servidor.
3. Fijar `Portal__RequireEmailConfirmation=false` y mantener
   `Portal__Mail__Enabled=false` si no hay servicio de correo. No usar Testing o
   Development en producción ni desactivar HTTPS, Secure o CSRF.
4. Conservar `NEXT_PUBLIC_SITE_ORIGIN=https://envirapido.com` y
   `NEXT_PUBLIC_PORTAL_ORIGIN=https://portal.envirapido.com`. El valor privado de
   `PORTAL_API_ORIGIN` debe coincidir al compilar y ejecutar Next. No copiar el
   bundle QA, sus puertos, cuentas o bases ficticias a producción.
5. Comprobar `/portal-api/v1/auth/options` desde el subdominio del portal: ambos
   flags false en esta configuración. Validar con un comprador ficticio autorizado
   que registrar no habilita recursos y que SuperAdmin controla pago y vigencia.
6. Revalidar HTTPS/cookies y las rutas operativas existentes tras el despliegue.
   No cambiar dominio, paquete, firma, APK ni contratos de pedidos/rutas por este
   ajuste de registro.

## Límites de la evidencia

Esta validación es local, con SQL Server real y navegador. No constituye una
ejecución nueva de todos los flujos de pedidos o del emulador, ni un despliegue
del servidor público. La app móvil y la lógica operativa no se editaron en este
ajuste. No se han enviado correos ni pagos, ni se han alterado datos de producción.
