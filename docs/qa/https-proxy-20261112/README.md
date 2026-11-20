# QA de HTTPS y cookies — 12/11/2026

Base: `a4cdb3021f6c600940c9749bdd573dd9ecbbf4ee`, rama
`codex/confiabilidad-panel-app`. Este recibo distingue la corrección local del
servidor público; el push no ejecuta un despliegue.

## Cambio

Procesamiento de `X-Forwarded-Proto` antes de autenticación, desde IPs exactas
autorizadas y con un solo salto. Cookies Identity (incluidas External y 2FA),
antiforgery y TempData requieren `Secure` y `HttpOnly` fuera de Development.
No se modificaron modelo, migraciones, claves JWT/Data Protection, contratos
móviles ni reglas de órdenes, rutas o empresas. El puente QA HTTP conserva
una excepción explícita exclusivamente en `tools/Envirapido.Qa`.

## Ejecución local nueva

| Validación | Resultado |
| --- | --- |
| Suite completa, SQL Server LocalDB y .NET/ASP.NET Core 10.0.15 | **254/254**, cero fallos y cero omitidas. Configuración Debug. |
| Pruebas nuevas de seguridad incluidas en esa suite | **23**, con proxy confiable/desconocido, encabezados falsificados, límite de salto, cookies reales, login/CSRF y contratos 401/403. |
| Backend, tests y herramienta QA, compilación Release | **PASS**, cero errores; conserva 26 advertencias de nulabilidad/análisis en archivos anteriores. No equivale a ejecutar otra suite completa Release. |
| Revisión independiente del cambio | Sin defecto material identificado. |
| Flutter | Sin cambios; no se repitió su suite para este cambio de servidor. |

La suite usa bases nuevas `PilotGoTest_<GUID>`, con limpieza del arnés. No utiliza
la conexión productiva ni la base QA histórica preservada. Incluye las regresiones
existentes de recepción automática/manual simultánea, paradas, autorización,
multitenant y finanzas.

Recibos locales ignorados por Git: `artifacts/https-proxy-20261112/https-full-sql.trx`,
`full-sql.log`, `release-build.log`. El subconjunto previo está en
`artifacts/https-security-20261112/` y queda superado por la suite completa final.

```powershell
. .\tools\Envirapido.Qa\Use-Net10Qa.ps1
$env:PILOTGO_TEST_SQLSERVER = '1'
$env:PILOTGO_TEST_SQLSERVER_INSTANCE = 'PilotGoVisualQA_20261111'
& $env:DOTNET_HOST_PATH test Login.Tests\Login.Tests.csproj --no-restore
& $env:DOTNET_HOST_PATH build tools\Envirapido.Qa\Envirapido.Qa.csproj -c Release --no-restore
```

## Estado público al preparar este commit

A las **00:34 UTC del 13/11** (20:34 del 12/11 en Santo Domingo), la raíz todavía
redirigía a HTTP y las cookies nuevas carecían de `Secure`. La API anónima devolvía
401 JSON. Las cuentas públicas autorizadas iniciaron sesión por HTTPS y los roles
ajenos fueron rechazados con 403. Se registró una referencia cifrada de los pedidos
existentes para compararlos tras el despliegue; no se crearon órdenes en este lote.

Ese resultado pertenece al servidor previo al despliegue del arreglo y **no valida
la corrección pública**. El APK existente `2.0.0+8`, paquete `com.pilotgo.app`,
mantiene la URL pública y el hash verificado
`7b359b2557f08c14a85b7bed9528308756c0bfa249e04998b46a907d84c4cdd0`.

El otro agente debe desplegar el commit del arreglo, conservando la configuración
privada y siguiendo [la guía HTTPS/proxy](../../envirapido/HTTPS-PROXY-DESPLIEGUE.md).
Después se exige comprobar redirecciones, cookies de sesión autenticada y CSRF,
continuidad de sesiones y flujo del mismo APK con manuales y automáticas mientras
hay órdenes activas. No se acredita aquí la versión exacta del host público, acceso
SQL directo productivo ni ese recorrido posterior al despliegue.
