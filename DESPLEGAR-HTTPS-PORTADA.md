# Desplegar la redirección HTTPS de la portada

Entrega puntual del **20/11/2026**, posterior al backend `59cd1525b7ef6666a801ad48ceeff1b57aa7dff2` y al APK 2.1.1 (11). El propietario autorizó publicar esta corrección y sus instrucciones en GitHub; el agente del servidor aplica la configuración pública.

## Qué se corrige

La QA pública comprobó que `http://envirapido.com/` y `/planes` sirven contenido por HTTP, aunque el login y las API del CRM sí redirigen correctamente a HTTPS. La portada React/Next y el CRM son procesos separados. **Volver a publicar Login.dll o Next no aplica una regla de Cloudflare.**

La regla [https-redirect.cloudflare.rule.json](docs/portal/https-redirect.cloudflare.rule.json) ya define el comportamiento correcto: 308 al mismo host, misma ruta y consulta, sólo cuando la conexión pública no usa TLS. Esta entrega incorpora su instalación comprobable y repetible mediante la API oficial, conservando las reglas ajenas.

**Este despliegue actualiza únicamente la regla pública de redirección.** No compilar, reiniciar ni publicar CRM, API comercial, Next o APK por este lote. No ejecutar migraciones/seeds, modificar conexiones SQL, JWT, Identity, cookies, DNS, TLS, planes, datos de tenants, pedidos, rutas, tarifas ni pagos. El APK 2.1.1 (11), su firma y su URL `https://envirapido.com` se conservan.

## 1. Obtener el commit exacto

Repositorio: `2020f/Login-PilotoGot`. Rama: `codex/confiabilidad-panel-app`.

```bash
git fetch origin codex/confiabilidad-panel-app
git show --stat <SHA_COMPLETO_ENTREGADO>
git worktree add --detach /RUTA_NUEVA_RELEASE_HTTPS <SHA_COMPLETO_ENTREGADO>
cd /RUTA_NUEVA_RELEASE_HTTPS
git rev-parse HEAD
```

Usar el SHA completo comunicado en la entrega. No sustituirlo por `master`, un HEAD posterior ni el tag de la aplicación2.1.1. Preservar el checkout en servicio y cualquier cambio local; usar un directorio nuevo. No ejecutar reset/clean/rebase ni copiar configuraciones de QA.

## 2. Comprobar el borde y preparar acceso limitado

1. Confirmar que el borde público de `envirapido.com` y `portal.envirapido.com` corresponde a la zona activa `envirapido.com` de Cloudflare, con sus registros web proxificados. El instalador lo verifica y aborta si no coincide; no cambia DNS.
2. Usar **Node 22.18 o posterior**, ya requerido por el portal. Las herramientas son Node estándar: no necesitan npm install, build de Next, Python ni paquetes adicionales.
3. Inyectar mediante el almacén privado del operador las variables `CLOUDFLARE_API_TOKEN` y `CLOUDFLARE_ZONE_ID`. La segunda es el ID de 32 caracteres hexadecimales de esa zona, no el account ID.
4. Limitar el token a esa zona y a la lectura de zona/DNS y administración de Single Redirects (`Dynamic URL Redirects Write` en la API). No usar Global API Key ni permisos para todas las zonas. No poner tokens en argumentos, archivos del repositorio, recibos públicos, capturas, historial o GitHub.
5. Elegir un directorio de respaldo privado **fuera de cualquier checkout Git**. En Linux el instalador exige permisos privados y crea archivos 0600 en directorio 0700. En Windows usar una carpeta con ACL restringida al operador; el modo POSIX no sustituye las ACL.

Si el dominio no pasa por Cloudflare, detener este procedimiento y documentar la topología real antes de adaptarlo. No añadir una redirección incondicional al origen HTTP de cloudflared: las visitas HTTPS también llegan por HTTP al origen y se produciría un bucle. No confiar en `X-Forwarded-Proto` enviado por el visitante ni cambiar SSL a Flexible.

## 3. Revisar el plan y aplicar sólo la regla

Con las variables privadas ya inyectadas, ejecutar primero:

```bash
node tools/Apply-PortalHttps.mjs
```

Sin `--apply` sólo consulta Cloudflare y muestra un plan sanitizado. Revisar que la zona y el alcance sean los previstos. No imprime el token ni las expresiones de las demás reglas.

Aplicación:

```bash
node tools/Apply-PortalHttps.mjs \
  --apply \
  --backup-dir /RUTA_PRIVADA_FUERA_DE_GIT/envirapido-https-20261120
```

El instalador:

- Identifica el entry point `http_request_dynamic_redirect` y la regla por su `ref` estable **`envirapido_http_https_20261119`**. No crea otra referencia con la fecha del despliegue.
- Guarda la configuración previa antes de escribir y relee su versión para detectar cambios simultáneos.
- Añade únicamente esta regla con POST de regla individual, o la actualiza con PATCH de esa regla. La sitúa primera para evitar que otra redirección HTTP se ejecute antes. Conserva el orden relativo y la configuración de las demás.
- Si no existe ese entry point, crea uno nuevo con esta única regla; nunca reemplaza uno existente mediante PUT.
- Si la regla ya es idéntica y primera, finaliza sin escribir ni crear otra versión.
- Comprueba después la regla efectiva y la conservación de sus vecinas. No reintenta escrituras automáticamente ante timeout o resultado incierto: inspeccionar el estado con el modo de consulta antes de decidir el siguiente paso.

Guardar el JSON sanitizado del resultado y la ruta del backup en el registro privado del despliegue. **El éxito de la API no acredita todavía el resultado HTTP público**; ejecutar la validación del paso4.

La condición se limita a `envirapido.com` y `portal.envirapido.com`; usa `not ssl`, la señal TLS observada por Cloudflare. No afecta solicitudes HTTPS ni agrega `www` u otros hosts. El dominio histórico mantiene su configuración y su API vigentes. La respuesta 308 conserva también el método HTTP.

## 4. Validar desde fuera del servidor

Sin credenciales ni cookies operativas:

```bash
node tools/Verify-PortalHttps.mjs \
  --receipt /RUTA_PRIVADA_FUERA_DE_GIT/https-publico-20261120.json
```

El proceso debe salir con código0. Cualquier fallo conserva la entrega como pendiente. El recibo no se sobrescribe: usar un nombre nuevo para una nueva comprobación.

El verificador cubre los tres hosts conocidos: portada y portal comprador con 308; dominio histórico con su 307/308; conservación de ruta/consulta codificada; POST vacío a una ruta ficticia de prueba; intentos HTTP con cabeceras reenviadas falsificadas; disponibilidad HTTPS de portada/acceso; cookies anónimas Secure/HttpOnly y respuestas API 401 JSON del CRM. No sigue redirecciones, no transmite credenciales ni registra pedidos/cobros/pagos. No exige HSTS ni altera su configuración.

Comprobación breve independiente en Linux:

```bash
curl --silent --show-error --max-time 20 --output /dev/null \
  --write-out '%{http_code} %{redirect_url}\n' \
  'http://envirapido.com/planes?qa_https_probe=env%2Frapido%20test'
curl --silent --show-error --max-time 20 --output /dev/null \
  --header 'X-Forwarded-Proto: https' \
  --write-out '%{http_code} %{redirect_url}\n' \
  'http://envirapido.com/?qa_https_probe=1'
curl --silent --show-error --max-time 20 --output /dev/null \
  --write-out '%{http_code} %{redirect_url}\n' \
  'https://envirapido.com/'
```

Las dos primeras deben devolver 308 al mismo host HTTPS, conservando ruta/consulta. La tercera debe devolver 200, sin redirección a sí misma. No usar `-L` para ocultar saltos intermedios ni desactivar la validación TLS.

También se conserva el verificador PowerShell existente, útil desde Windows:

```powershell
./tools/Verify-PublicHttps.ps1 -BaseUrl https://envirapido.com -RootMode Marketing -RequireSuccess
./tools/Verify-PublicHttps.ps1 -BaseUrl https://pilotogoapp.zapflow77.dpdns.org -RootMode CrmRedirect -RequireSuccess
```

## 5. Reversión acotada y cierre

La reversión se limita a la regla propia del backup: si existía, restaurar sus campos y posición con PATCH individual; si fue añadida, deshabilitar sólo esa regla para recuperar el comportamiento previo. Conservar el ruleset y todas las reglas ajenas. No restaurar de golpe el backup completo, SQL, APK ni otra configuración. Antes de cualquier reversión, comprobar que no haya una modificación posterior de otro operador y preservar la versión actual.

Si la API respondió con timeout después de una escritura, no repetir a ciegas ni borrar reglas: ejecutar modo consulta, revisar el `ref` y la versión efectiva, y contrastar con el backup. Una segunda aplicación del mismo estado correcto debe ser de sólo lectura.

Registrar: SHA del checkout, zona/ruleset/regla/versión, resultado sanitizado del instalador, ubicación privada del backup y recibo público con salida0. Sólo entonces indicar que **la redirección pública quedó aplicada**. La publicación de estos archivos en GitHub por sí sola no la activa.

## QA de las herramientas y referencias

Pruebas locales, sin token ni acceso a Cloudflare:

```bash
node --test tools/tests/portal-https-*.test.mjs
```

Cubren planificación sin escrituras, alta/actualización individual, idempotencia, concurrencia, preservación de otras reglas, rechazos de zona/DNS/alcance incorrectos, backup previo y controles HTTP del verificador. No sustituyen las pruebas públicas tras aplicar la regla real. El alcance de este lote permite mantener la QA previa del APK 2.1.1 (11): no cambian sus bytes ni los componentes operativos.

Fuentes oficiales consultadas el 20/11/2026: [alta individual de regla y posición](https://developers.cloudflare.com/ruleset-engine/rulesets-api/add-rule/), [actualización individual](https://developers.cloudflare.com/ruleset-engine/rulesets-api/update-rule/), [entry points y consulta de versión](https://developers.cloudflare.com/ruleset-engine/rulesets-api/view/), [308 y conservación de consulta/método](https://developers.cloudflare.com/rules/url-forwarding/single-redirects/settings/).
