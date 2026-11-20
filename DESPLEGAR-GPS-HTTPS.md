# Correcciones GPS y HTTP/HTTPS — 19/11/2026

> Para aplicar únicamente la redirección de la portada después de Envirapido 2.1.1,
> usar [DESPLEGAR-HTTPS-PORTADA.md](DESPLEGAR-HTTPS-PORTADA.md), con el instalador
> acotado y su verificación pública. No repetir el despliegue GPS de esta guía.

Este lote parte de `b637fae5ff05b52161cee032016342556675c4c6` y corrige los dos
pendientes de la QA publica del APK 2.1.0 (10). El propietario autorizo su
publicacion en GitHub el 19/11/2026. El despliegue y la QA publica posterior siguen
pendientes: el commit no acredita que el servidor ya este corregido.

## Obtener la entrega en GitHub

Repositorio: `2020f/Login-PilotoGot`.
Rama: `codex/confiabilidad-panel-app`.
Usar el SHA completo de esta entrega que facilite el propietario; no sustituirlo
por el HEAD futuro de la rama ni por `master`.

```sh
git fetch origin codex/confiabilidad-panel-app
git show --stat <SHA_COMPLETO_DE_ESTA_ENTREGA>
```

Desplegar desde un checkout limpio de ese SHA, conservando los cambios locales y
las configuraciones del servidor. Si el checkout de despliegue esta ocupado o
modificado, usar otro directorio/worktree; no hacer reset, clean ni rebase.
Compilar/publicar el backend con el procedimiento .NET 10 existente y conservar
las conexiones SQL, secretos, almacenamiento privado y procesos del portal.

La publicacion del codigo tiene dos pasos operativos independientes: actualizar
el backend GPS y aplicar la regla HTTPS en el borde real. Registrar el SHA del
backend y el ID/version de la regla por separado. Aplicar solo los pasos de este
lote: no repetir migraciones o seeds de entregas anteriores.
## Backend GPS

Publicar `Login/Login.csproj` completo desde el SHA exacto que se entregue.
`TieneActividadGpsAsync` se comparte entre actividad, snapshot y envio GPS:

- Siempre exige piloto autenticado y activo de su empresa.
- En legacy conserva la asignacion por cabecera y el ciclo anterior.
- En gestion por paradas admite trabajo asignado no terminal o custodia fisica
  pendiente, incluso si la orden se cancelo. Deja de admitir custodia cuando el
  paquete se entrega/devuelve/recibe por el siguiente responsable.
- No depende de activar ofertas automaticas ni cambia propietarios, codigos,
  rutas, tarifas, cobros o estados para conceder el permiso.

No hay migracion nueva. Requiere tener ya aplicada la migracion operativa del
lote 2.1. No ejecutar seeds, recrear bases, cambiar conexiones ni claves. El portal
comercial conserva su SQL, Identity y vigencias independientes.

La firma, el archivo y el origen del APK 2.1.0 (10) no cambian:
`https://envirapido.com`, SHA256
`d1fc8278288adc75390af9baca9998158c7ee07559f979ed6aeaeed8e7dd4be3`.
No es necesario reconstruirlo para esta correccion de consultas del servidor.
Mantenerlo en cuarentena hasta repetir el caso publico corregido.

## Redireccion de la portada: aplicar en el borde TLS real

La portada Next responde por un proceso distinto del CRM: publicar solamente
Login.dll no corrige su HTTP. No se modifican React/Next ni sus vistas.

Si estos hosts estan proxificados por Cloudflare, incorporar la regla revisada
[https-redirect.cloudflare.rule.json](docs/portal/https-redirect.cloudflare.rule.json)
en el ruleset de zona de la fase `http_request_dynamic_redirect`.

1. Confirmar en el panel la zona `envirapido.com` y que los hosts realmente usan
   su proxy. Respaldar las reglas vigentes y registrar la version actual.
2. Anadir o actualizar SOLAMENTE la regla con ref
   `envirapido_http_https_20261119`. El archivo es UNA regla, no un ruleset completo.
   No reemplazar ni vaciar reglas existentes. Revisar prioridades: una regla de
   redireccion anterior que coincida puede ejecutarse primero.
3. La condicion limita los hosts a `envirapido.com` y `portal.envirapido.com`.
   Usa el TLS observado por Cloudflare (`not ssl`), no una cabecera aportada por
   el visitante. Devuelve 308 al mismo host, conserva consulta y usa ruta raw.
4. Validar con la API/simulador de Cloudflare antes de habilitarla y guardar su ID
   y version, sin publicar tokens ni configuracion privada. Las funciones raw
   pueden conservar normalizaciones basicas del servidor de Cloudflare; comprobar
   expresamente las rutas codificadas con el verificador al desplegar.
5. No cambiar SSL a Flexible, no desactivar TLS, Secure, CSRF ni autenticacion.
   No ampliar la confianza en X-Forwarded-Proto, X-Forwarded-Host o rangos proxy.
   Conservar el dominio historico y su API: esta regla no cambia otros hosts.
6. Si no es Cloudflare, aplicar la redireccion en el componente que termina TLS
   publico. Solo si Nginx recibe directamente HTTP publico en 80: integrar un
   `return 308 https://$host$request_uri;` en su vhost HTTP de nombres EXACTOS,
   rechazando hosts desconocidos, revisar duplicados con `nginx -T`, probar
   `nginx -t` y recargar. No instalarlo a ciegas en un origen HTTP de cloudflared:
   las visitas HTTPS tambien llegan por HTTP a ese origen y producirian un bucle.

Referencias primarias revisadas:
[creacion de Single Redirects](https://developers.cloudflare.com/rules/url-forwarding/single-redirects/create-api/),
[parametros 308 y consulta](https://developers.cloudflare.com/rules/url-forwarding/single-redirects/settings/),
[TLS observado](https://developers.cloudflare.com/ruleset-engine/rules-language/fields/reference/ssl/),
[ruta raw y sus limites](https://developers.cloudflare.com/ruleset-engine/rules-language/fields/reference/raw.http.request.uri.path/).

## Verificacion tras desplegar

En Windows/PowerShell del operador:

```powershell
./tools/Verify-PublicHttps.ps1 -BaseUrl https://envirapido.com -RootMode Marketing -RequireSuccess
./tools/Verify-PublicHttps.ps1 -BaseUrl https://pilotogoapp.zapflow77.dpdns.org -RootMode CrmRedirect -RequireSuccess
```

El verificador comprueba portada, ruta comercial, ruta codificada, login/API,
consulta, preservacion de metodo, cookies y un intento HTTP con
`X-Forwarded-Proto: https`. No sigue redirects ni envia credenciales/cookies.
Esa cabecera falsificada no debe permitir servir la portada por HTTP.

El host comprador no tiene los endpoints CRM del verificador completo. Comprobar
su redireccion y ausencia de bucle por separado, sin cookies ni credenciales:

```sh
curl -sS -o /dev/null -w '%{http_code} %{redirect_url}\n' 'http://portal.envirapido.com/acceso?qa_https_probe=env%2Frapido'
curl -sS -o /dev/null -w '%{http_code} %{redirect_url}\n' 'https://portal.envirapido.com/acceso'
```

La primera debe ser 308 al mismo host HTTPS conservando ruta/consulta; la segunda
no debe redirigirse a si misma. Validar el registro manual y las descargas
protegidas sin cambiar pagos ni vigencias. La portada HTTPS debe seguir dando 200.

En APK 10 con fixtures autorizadas: repartir una orden entre dos pilotos sin otra
orden legacy que oculte el defecto. Con ambos offline de ofertas, actividad y
snapshot deben autorizar GPS; enviar una muestra simulada debe dar 200. Verificar
custodio anterior antes/despues de la recepcion y cancelada con retorno pendiente;
no reactivar pedidos entregados ni modificar ordenes/rutas reales.

## Evidencia local y limites

- Nuevas regresiones SQL: cuatro casos fallaban por permiso GPS y dos controles
  pasaban antes de la correccion. Despues pasan 6/6, incluyendo API HTTP, tenant
  ajeno, actor falsificado, replay GPS, custodia y ciclo legacy.
- Suite SQL completa: **346/346**, cero fallos y cero omitidas; recibo local
  `artifacts/gps-https-fix-20261118/full-sql.trx`. El lote conserva 6/6 regresiones
  GPS y 8/8 comprobaciones locales del criterio de redireccion HTTP.
- La regla de borde esta preparada y revisada; no se aplico a Cloudflare desde
  esta tarea. El repositorio no contiene sus credenciales ni una atestacion de
  configuracion del servidor. Validacion publica y APK despues del despliegue,
  no sustituibles por las pruebas SQL locales.
- No hay cambios de fuente movil, base comercial ni nuevos paquetes.

Reversion: retirar solo esta regla de Cloudflare usando su ID/version guardados,
o restaurar solo el vhost revisado. No restaurar SQL ni cambiar claves. El backend
previo vuelve a tener el defecto GPS: no publicar el APK con ese caso sin resolver.

Resumen versionado y recibo público anónimo: [docs/qa/gps-https-20261119](docs/qa/gps-https-20261119/README.md).
