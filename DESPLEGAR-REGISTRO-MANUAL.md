# Actualización puntual: registro sin SMTP

Entrega para el agente que opera `envirapido.com` y `portal.envirapido.com`.
Utilizar el **SHA completo comunicado al propietario**, rama
`codex/confiabilidad-panel-app`, repositorio `2020f/Login-PilotoGot`.
La base de esta corrección es `3726d995eb1e1a1f9871a822ec4dd99d5c24ff43`.
No sustituir el SHA recibido por `main`, `master` o una rama que haya avanzado.

## Resultado esperado

El comprador registra correo de contacto, contraseña, negocio y plan sin validar
un mensaje de correo. La solicitud aparece en **SuperAdmin → Portal comercial**.
El comprador sólo consulta su cuenta y solicitud hasta que SuperAdmin apruebe,
confirme pago externo cuando corresponda y habilite las fechas de vigencia.

No se crean usuarios, credenciales o empresas CRM automáticamente. La base y
autenticación comercial siguen separadas del CRM. La recuperación sin SMTP muestra
los contactos de administración; no se añadió un restablecimiento manual de
contraseñas en el panel.

## Alcance de esta entrega

- Actualizar `Envirapido.Portal.Api`, Contracts y su configuración.
- Publicar el CRM para la vista SuperAdmin y el contrato compartido actualizado.
- Reconstruir `portal-web` React/Next y publicar su bundle standalone completo.
- **Sin migraciones nuevas**, sin cambios en tablas CRM o portal, pedidos, rutas,
  contratos móviles, dominio operativo, APK, paquete o firma Android.
- Los cambios locales pendientes de reasignación y rediseño móvil no forman parte
  de este commit. No buscar ni aplicar una migración operativa por este ajuste.

## Preparación

1. Confirmar `git rev-parse HEAD` en un checkout limpio del SHA de entrega. Si el
   servidor tiene cambios locales, preservarlos y preparar una carpeta de release
   aparte. No ejecutar reset, clean ni un pull que descarte trabajo.
2. Respaldar configuración, release y datos actuales. Conservar ambas conexiones
   SQL, claves Data Protection de cada servicio, secretos administrativos y de
   catálogo, almacenamiento privado APK y configuración HTTPS/proxies existente.
3. No crear ni recrear `EnvirapidoPortal`; reutilizar su conexión actual. No tocar
   `DefaultConnection` ni ejecutar `database update`, seeds o scripts SQL por esta
   corrección. La API mantiene sus comprobaciones de separación de base.

## Configuración y publicación

En el entorno privado del servicio API portal:

```text
ASPNETCORE_ENVIRONMENT=Production
Portal__RequireEmailConfirmation=false
Portal__Mail__Enabled=false
```

Revisar que no exista un override anterior que exija confirmación. No configurar
SMTP para resolver este registro. Conservar todas las otras variables privadas;
no reemplazar el archivo de entorno entero con una plantilla.

Publicar `Login/Login.csproj` y
`Envirapido.Portal.Api/Envirapido.Portal.Api.csproj` con .NET 10 y el runtime
indicado por los proyectos. Incluir `Envirapido.Portal.Contracts.dll` en ambos
releases. Reiniciar sólo los servicios correspondientes de forma controlada.

Reconstruir Next en el servidor con las versiones del lockfile:

```text
NEXT_PUBLIC_SITE_ORIGIN=https://envirapido.com
NEXT_PUBLIC_PORTAL_ORIGIN=https://portal.envirapido.com
PORTAL_API_ORIGIN=<origen privado real de la API portal>
```

`PORTAL_API_ORIGIN` debe tener exactamente el mismo valor al compilar y ejecutar
Next. Mantener el puerto privado que ya utiliza el servidor; no copiar puertos de
QA. Las variables `NEXT_PUBLIC_*` se fijan al compilar. Ejecutar `npm ci`,
`npm run typecheck`, `npm test` y `npm run build`; publicar todo el standalone con
`public` y `.next/static`. No copiar binarios o bundles de QA Windows a producción.

Mantener las rutas actuales: raíz comercial Next, subdominio del portal para
cuenta/API comercial y `/Identity`, `/api`, controladores y assets operativos al
CRM original. No publicar endpoints internos. Conservar HTTPS, cookies Secure,
HttpOnly, host-only y CSRF; no cambiar sus controles para facilitar el registro.

## Comprobación posterior al despliegue

1. En `https://portal.envirapido.com/portal-api/v1/auth/options`, comprobar:

   ```json
   {"emailConfirmationRequired":false,"passwordRecoveryAvailable":false}
   ```

2. Comprobar catálogo y registro con un comprador ficticio autorizado. No reutilizar
   una cuenta real sin autorización ni exponer su contraseña en logs. Debe crear
   una sola solicitud pendiente, sin aviso SMTP ni botones CRM/APK habilitados.
3. SuperAdmin debe mostrar «Correo de contacto · activación manual». Aprobar y
   fijar vigencia, confirmando un pago externo sólo si existe y corresponde al
   plan. Un reporte o una fecha nunca equivalen a dinero recibido.
4. Comprobar acceso habilitado sólo al cumplir todos los controles; suspensión,
   vencimiento o anulación del pago vuelven a bloquear recursos. Las credenciales
   CRM siguen entregándose por separado. No seguir ni alterar pedidos reales.
5. Comprobar recuperación sin SMTP: ofrece contacto, sin prometer correos. Verificar
   cierre/inicio de sesión y descarga protegida cuando haya APK publicado.
6. Revalidar HTTPS/cookies y que login/API móvil sigan en el CRM existente. Para un
   recorrido nuevo de pedidos manuales/automáticos usar únicamente cuentas y datos
   ficticios autorizados. La QA local no sustituye esta comprobación pública.

## Evidencia y rollback

Ver [REGISTRO-MANUAL-20261117.md](docs/portal/REGISTRO-MANUAL-20261117.md) para
57 pruebas SQL del portal, 8 del puente administrativo, 28 del frontend, build y
recorrido local en navegador. No se enviaron correos ni se alteró producción.

Si aparece una regresión, restaurar los releases previos conservando base,
archivos, claves y configuración privada. No revertir esquemas ni borrar usuarios.
El release anterior exige SMTP para el registro; informar ese límite del rollback.

Entregar al propietario el SHA completo desplegado, servicios reiniciados,
resultado de opciones/catálogo/registro/aprobación/vigencia, HTTPS y comprobación
operativa, sin secretos ni datos personales. Publicar en GitHub no es desplegar.
