# Envirapido: preparación de despliegue con .NET 10

Fecha de revisión: 12/11/2026. La actualización autorizada a .NET 10 está implementada y validada en QA local con los resultados que se detallan aquí. El [recorrido visual completado](NET10-ACTUALIZACION-20261112.md) acredita sesión móvil conservada, asignación manual adicional, aceptación automática con seis órdenes activas y cierre B/C. No se ha desplegado el servidor público ni hecho push a GitHub. El pase real de producción permanece pendiente.

## Versión y paquete que se deben desplegar

Microsoft identifica .NET 10 como LTS, con parche actual **10.0.15** publicado el 10/11/2026 y soporte hasta el 14/11/2028. La versión debe mantenerse en el nivel de mantenimiento vigente al desplegar; si aparece un parche posterior, registrarlo y validar ese artefacto. [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [actualización 10.0.15](https://support.microsoft.com/en-gb/servicing/dotnet/net-10/2026/net-10-0-update-november-10-2026).

El producto `Login`, `Login.Tests` y `tools/Envirapido.Qa` usan ahora **`net10.0`**, con **`RuntimeFrameworkVersion` 10.0.15**. El lote se compiló con **SDK 10.0.201**; `global.json` selecciona esa base, permite `latestFeature` y rechaza versiones preliminares. Las referencias ASP.NET Core, Identity y EF Core están en **10.0.15**. Se eliminó el paquete de scaffolding sin uso y se agregó `Microsoft.EntityFrameworkCore.Design` con `PrivateAssets="all"`. No se cambió el modelo ni se creó una migración por el cambio de runtime.

El runtime ASP.NET Core/.NET 10.0.15 de QA se mantiene en `artifacts/net10-20261112/runtime`, separado de la instalación pública. El helper `tools/Envirapido.Qa/Use-Net10Qa.ps1` verifica su presencia y configura `DOTNET_ROOT`, `DOTNET_ROOT_X64`, `DOTNET_HOST_PATH`, `VSTEST_DOTNET_PATH` y el PATH del proceso que lo carga; no cambia variables persistentes del sistema. Cargarlo mediante dot-sourcing en la sesión de pruebas que lo necesite. La disponibilidad de ese runtime privado no instala ni acredita el runtime del servidor de producción. Conservar el SDK, runtimes, paquetes, salida de pruebas y hashes exactos de la publicación validada.

Para una publicación dependiente del framework, el alojamiento necesita `Microsoft.NETCore.App` y **`Microsoft.AspNetCore.App` 10.0.15** de la arquitectura correcta; instalar únicamente el runtime de consola no basta para ASP.NET Core. En IIS se requiere el Hosting Bundle y su módulo ASP.NET Core; en Linux se requiere el runtime ASP.NET Core y el servicio de proceso/proxy configurados. Un paquete autocontenido debe declararse y probarse expresamente para el RID de destino y necesita su propia actualización de runtime. No mezclar binarios de publicaciones distintas. [Alojamiento ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/?view=aspnetcore-10.0), [IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0), [Linux con Nginx](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-10.0).

El artefacto público es la publicación de `Login/Login.csproj`. **No desplegar `tools/Envirapido.Qa`**, sus cuentas, atajos `/qa/login`, conexión LocalDB ni endpoint de metadatos. Publicar el código revisado en una carpeta de salida nueva, fuera de la instalación activa. Registrar antes del corte:

- Sistema operativo y arquitectura admitidos, proveedor de alojamiento, proceso/servicio y puerto interno real.
- SDK de compilación y runtimes del servidor, obtenidos mediante `dotnet --info` y `dotnet --list-runtimes`.
- Carpeta de publicación, identificador Git/tree del lote y hash del artefacto.
- Base/instancia de destino confirmadas, última migración EF aplicada y copia restaurable.
- Identidad del servicio y permisos necesarios, sin publicar secretos en recibos.

## Conservar identidad, sesiones y configuración

La actualización de runtime no debe sustituir la base ni recrear empresas o usuarios. Conservar IDs, roles y pertenencias; `AspNetUserTokens` del proveedor `PilotGoMobile`; sellos de seguridad; órdenes, códigos, rutas y registros financieros. No ejecutar seeds de desarrollo sobre la base real.

Conservar **`Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`**, las duraciones configuradas y el origen público. Cambiar la clave o el emisor invalida tokens existentes; no hacerlo como efecto secundario del despliegue. Usar los valores privados reales del alojamiento, nunca el fallback de desarrollo. No registrar JWT, refresh tokens ni cadenas de conexión.

Se probó una cookie Identity emitida por un proceso real **.NET/ASP.NET Core 8.0.25** y consumida por otro proceso real **10.0.15**: ticket protegido y serializador compatibles, identidad/rol y expiración conservados; rechazos correctos ante alteración, nombre de aplicación distinto o propósito distinto. Ambos procesos compartieron el mismo almacén persistente de claves, protegido con DPAPI en Windows. Resultado: **PASS**, en `artifacts/net10-20261112/cookie-net8-net10-result.json`.

Esa comprobación es aislada: **no acredita continuidad de las cookies del host QA**, cuyo proveedor de Data Protection es efímero, ni de una instalación de producción aún no inspeccionada. Antes del pase, identificar y respaldar su almacén, identidad de acceso y discriminador de aplicación. El producto no declara persistencia explícita en `Program.cs`; no asumir que una nueva carpeta, usuario de servicio o contenedor mantiene claves por defecto. Conservar la identidad efectiva, proteger las claves antiguas necesarias y comprobar cookies reales del destino. Varias réplicas necesitan almacén y nombre compatibles; las claves deben quedar fuera de la publicación reemplazable. [Configuración oficial de Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Configuración que debe conservarse o verificarse sin exponer sus valores:

| Configuración | Control |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Instancia/base productivas originales; SQL Server real, no LocalDB QA. |
| `ASPNETCORE_ENVIRONMENT` | `Production` en destino; sin página de excepción de desarrollo ni seed. |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Valores privados existentes y validación de tokens. |
| `Email__*` | Proveedor SMTP, remitente y `Email__PublicBaseUrl` canónico; entrega real aún requiere prueba. |
| `GoogleMaps__BrowserApiKey`, `GoogleMaps__ApiKey` y restricciones del proveedor | Mantener funciones separadas, restricciones de origen/API y secretos fuera del paquete público. |
| Data Protection | Ubicación persistente, protección, ACL e identidad efectiva verificadas antes del cambio. |

La API pública de las instalaciones Android existentes sigue siendo **`https://pilotogoapp.zapflow77.dpdns.org`**. Está confirmada en `pilotGOapp/lib/core/config/api_config.dart` y los comandos de distribución del README móvil. Este lote no cambia ese origen, los caminos `/api/auth`, `/api/piloto`, `/api/tienda` y `/api/operaciones`, ni el paquete/firma de Android. Una respuesta 401 al consultar sin credenciales no demuestra que el nuevo servidor esté desplegado.

Mantener HTTPS, DNS y el proxy existentes. Si el alojamiento termina TLS delante de Kestrel, comprobar las cabeceras reenviadas y restringir la confianza a los proxies reales; el `Program.cs` inspeccionado no declara `UseForwardedHeaders`. No dar por validado el esquema HTTPS ni ampliar la confianza globalmente para resolver una redirección. Confirmar generación de enlaces canónicos y rate limiting con la topología real. [Guía oficial del proxy Nginx](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-10.0).

## Migración SQL Server y corte seguro

La comparación de modelo EF Core 10 confirmó **sin diferencias respecto a la última migración** (`artifacts/net10-20261112/model-diff.txt`): no se creó una migración adicional para .NET 10. La suite SQL Server nueva pasó **231/231**, sin fallos ni omisiones, y cubre también migraciones/protección del rollback. El archivo de comparación conserva advertencias ya existentes sobre la precisión por defecto de `Plan.PrecioMensual` y `DatosEnvioOperativos.PesoKg`; no se afirma ausencia de advertencias ni se alteró su esquema en este lote.

Antes de cambiar el host se respaldó la base QA preservada con **CHECKSUM** y se ejecutó **RESTORE VERIFYONLY**. Archivo: `artifacts/net10-20261112/qa-before-net10.bak`; SHA-256 `CF3C3E55143867EBCF39886FD4F9047BC284380F8EC2CC6D7B942DD6D75D0735`. Se verificó el archivo de respaldo; **no se ejecutó una restauración real**. El ensayo de restauración del entorno de destino permanece pendiente.

El host QA actualizado escucha en **5019** con runtime y ASP.NET Core **10.0.15**, modo `attached-preserved`, y conserva `PilotGoTest_4612d89bd20d464394bd3724a9fea91a`. Los recibos `qa-before-runtime.json` y `qa-after-runtime.json` son iguales: 17 órdenes, 35 códigos, 10 usuarios, 17 sesiones y los mismos hashes de órdenes, códigos, paradas, rutas, puntos, identidades, roles, facturas y pagos. Esto documenta preservación durante la sustitución del runtime; la prueba posterior de refresh sí rota expresamente las dos sesiones ficticias elegidas.

La actualización del runtime no autoriza por sí sola cambios de datos o de esquema. Examinar el SQL generado y probarlo antes de aplicarlo; usar una identidad de despliegue distinta de la identidad de ejecución con los permisos mínimos de cada función. Microsoft documenta scripts revisables y bundles como estrategias de migración; el arranque normal no debe convertirse en un migrador con permisos administrativos. [Aplicar migraciones EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

Recorrido propuesto, pendiente de ejecutar en el destino confirmado:

1. Identificar el estado de `__EFMigrationsHistory` real y comparar el esquema; no introducir entradas de historial para ocultar diferencias.
2. Generar desde el código del lote el script correspondiente a ese estado, conservarlo y revisarlo. La migración SaaS añadida es `20261112201846_EnvirapidoSaasBilling`; también verificar las migraciones previas de paradas y retornos.
3. Ensayar sobre una **base SQL Server nueva** y sobre una restauración aislada de una copia representativa. Comprobar compatibilidad con EF Core 10, índices filtrados, precisión monetaria, restricciones y ausencia de cambios operativos inesperados.
4. Registrar respaldo con checksum, verificarlo y probar su restauración. Conservar código/configuración anterior y definir la ventana de mantenimiento y responsable del corte.
5. Aplicar sólo el script revisado al destino confirmado, con sesiones escritoras controladas. Comparar conteos/hashes de órdenes, códigos, tenant/piloto/comercio, asignaciones y pagos antes/después.
6. Iniciar la publicación exacta, ejecutar el smoke con roles y empresas separados, y luego abrir tráfico según el procedimiento de despliegue autorizado.

La base QA preservada `PilotGoTest_4612d89bd20d464394bd3724a9fea91a` se creó históricamente con `EnsureCreated`. Su script `qa-add-saas-schema.sql` y la ausencia de historial EF son excepciones documentadas de QA, **no una receta de producción**. No usar esa base como sustituto de una restauración del destino.

El rollback de la migración SaaS bloquea la retirada de tablas cuando existen registros financieros/auditoría; no eludir esa protección ni borrar registros para degradar. Si una reversión de aplicación requiere el esquema anterior, usar el respaldo y la estrategia de restauración ensayada, teniendo en cuenta las escrituras posteriores al corte. La actualización de runtime y el rollback de base son operaciones distintas.

## Compatibilidad .NET 10: identidad, Razor y contratos HTTP

.NET 10 cambia el comportamiento predeterminado de cookies para endpoints identificados como API: devuelve 401/403 en lugar de redirigir a login/denegación. Los eventos personalizados siguen siendo relevantes. En este proyecto, `ConfigureApplicationCookie` maneja explícitamente `/api` con JSON y mantiene redirección web; JWT también emite respuestas JSON explícitas. Los contratos de autorización están cubiertos en la suite SQL .NET 10. **No se activó un switch global que restaure redirects de API** para hacer pasar las pruebas. [Cambio oficial de cookies en .NET 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints?view=aspnetcore-10.0).

También se verificaron **JWT y refresh tokens realmente emitidos por el host .NET 8.0.25**, antes del cambio, contra el host QA .NET 10.0.15 y la misma base: piloto B y comercio B conservaron identidad/rol, consultaron su operación, fueron rechazados con 403 en el rol ajeno y no obtuvieron datos del tenant ajeno. El refresh antiguo se aceptó una sola vez, el JWT nuevo funcionó y la repetición del refresh anterior devolvió **401**. Los JWT originales siguieron aceptándose durante su vigencia. Resultado **PASS** en `compat-net8-net10-result.json`; el script `Verify-Net10AuthCompatibility.ps1` no registra los tokens en ese recibo público y no modifica órdenes ni al piloto activo del emulador.

El perfil es deliberadamente denegado hasta que exista un módulo de autogestión revisado. La página local `Areas/Identity/Pages/Account/Manage/Index.cshtml` declara `/Identity/Account/Manage/{*path}`, mientras `AccessDenied` devuelve 403 y navegación según el rol. Revalidar que el catch-all no genere ambigüedad, no sea reemplazado por UI predeterminada y no filtre una vista de perfil al cambiar runtime. Probar páginas compiladas de la publicación; la lectura de archivos fuente o CSS nuevo no acredita que el Razor publicado cambió.

Matriz mínima de comprobación sobre el artefacto actualizado:

| Superficie | Comprobación exigida |
| --- | --- |
| Login/logout y sesión web existente | Entrada/salida reales, cookie y CSRF, redirección local, continuidad o explicación controlada de una sesión expirada. |
| `/Identity/Account/Manage`, `/Manage/Index`, `/Manage/Email`, `/Manage/ChangePassword` | Usuario autenticado obtiene mensaje «No tienes autorización», status 403 y regreso apropiado; sin 500 ni bucles. Los caminos completos son bajo `/Identity/Account`. |
| API anónima, rol incorrecto y tenant ajeno | 401/403 JSON, sin HTML de login ni 302, sin datos ajenos. Los 404 de recursos ajenos mantienen su contrato. |
| SuperAdmin | Resumen, empresas, detalle, crear/editar empresa/gestor/plan, facturación, factura/pago, accesos, auditoría y sistema. |
| Gestor | Centro de operaciones y `/Suscripcion`; solicitud/cambio de plan, facturas propias y aislamiento de otra empresa. |
| Fechas y cobros | Vencida sin pago; parcial conserva saldo; pago final concilia; referencia duplicada y fecha futura rechazadas; anulación trazable; suspensión manual no se levanta por pagar. |
| App y rutas | Restaurar sesión, varias asignaciones automáticas/manuales simultáneas, ruta existente intacta, B en recogida y sólo C para entrega final/parada; terminar trabajo autorizado tras vencer cobertura. |
| Presentación | Desktop y móvil, menú, tablas, formularios, fechas y estilo de Mi suscripción; sin desbordamiento horizontal del documento. |
| Migración/estado | Esquema esperado y runtime 10 real; señal de migraciones pendientes interpretada según historial existente, sin publicar secretos. |

## Espacio local y registro de evidencia

Esta revisión midió **1,39 GiB libres en C:**. Durante el lote se habían observado alrededor de 1,44 GiB. Es un margen bajo para SDK, paquetes NuGet, publicación, restauraciones SQL y snapshots del emulador: comprobar de nuevo antes de cada operación grande. No borrar proyectos, bases, firmas ni recibos históricos para forzar el pase. La compresión de salidas regenerables conserva los bytes, pero no demuestra por sí sola espacio suficiente.

Resultados confirmados del lote nuevo, en `artifacts/net10-20261112/`:

| Evidencia | Resultado y límite |
| --- | --- |
| TFM/SDK/runtime | Producto, Tests y QA `net10.0`; SDK 10.0.201; mínimo runtime y ASP.NET Core 10.0.15. `global.json`, los tres proyectos y `host-environment-final.json`. |
| Suite completa SQL Server en bases nuevas | **231/231**, cero fallos y cero omitidas. `net10-full-sql.trx`, `full-sql.log`; incluye autorización, finanzas, multitenant, rutas y protección de migraciones. |
| Flutter | **93/93**, ejecución nueva aprobada; `flutter-tests.jsonl`. Sin actualización del protocolo móvil por el cambio de runtime. |
| Modelo y migraciones | Sin diferencias de modelo ni migración nueva por .NET 10; `model-diff.txt`, `net10-model-compatibility-sql.trx`. Las migraciones funcionales todavía pendientes en un servidor real deben aplicarse mediante el procedimiento revisado. |
| Dependencias del producto | **0 vulnerabilidades conocidas reportadas**, directas y transitivas consultadas con NuGet; `package-audit.json`. No equivale a una auditoría de todo el código ni garantiza ausencia de fallos desconocidos. |
| Cookies Identity nativas 8.0.25 → 10.0.15 | **PASS** con las mismas claves persistentes y nombre/propósito; alteraciones rechazadas. `cookie-net8-net10-result.json`. No afirma continuidad de cookies efímeras del host QA ni cookies productivas. |
| JWT/refresh nativos 8 → host 10 | **PASS** para piloto y comercio ficticios; rol/tenant conservados, replay del refresh 401. `compat-net8-net10-result.json`. |
| Preservación de base y host | 5019, base QA histórica preservada, runtime real 10.0.15, hashes operativos y financieros iguales antes/después. `host-environment-final.json`, `qa-before-runtime.json`, `qa-after-runtime.json`. |
| Respaldo QA | CHECKSUM y VERIFYONLY completados; `backup-receipt.json`, `qa-before-net10.bak`. **No hubo restauración real**. |
| Recorrido visual del emulador | **PASS** con el host .NET 10 y la base preservada: sesión anterior, manual 0017, automática 0018 con seis activas, recogida B, entrega sólo C e historial. `emulator-six-active.png`, `emulator-history-delivered.png`, `e2e-final-sql.json`. |
| Recorrido Razor publicado | **PASS del recorrido principal** comercio/CRM/Sistema sobre QA Release .NET 10; comprobante, asignación, modalidad automática y diagnóstico real. Capturas y límites en `NET10-ACTUALIZACION-20261112.md`; la matriz completa de contratos queda cubierta por la suite SQL. |
| Preservación tras el recorrido | 10/10 fingerprints iguales para datos originales; nueva manual activa y automática entregada, 0 cruces de tenant de piloto/ruta/código. `original-preservation-result.json`. |
| Publicación del producto | Candidato local en `artifacts/envirapido-net10-release`; `Login.dll` idéntica por hash a la del host QA Release del recorrido. La suite completa fue Debug; no se afirma igualdad binaria Debug/Release. |
| Host real, SMTP, mapas, TLS/proxy y restauración | **Pendientes** de validar en el destino autorizado. |
| Push y producción | **No ejecutados**. La publicación local de QA no actualiza el servidor público. |

## Condiciones del pase real

El trabajo local aprobado todavía necesita un servidor de destino con **ASP.NET Core/.NET 10.0.15 o el parche posterior revisado**, arquitectura compatible y configuración privada preservada. Verificar allí Data Protection, JWT, base/estado de migraciones, respaldo restaurable, TLS/proxy, correo y mapas. Conservar el origen público original y ejecutar el smoke público autenticado de administrador, gestor, comercio y piloto con aislamiento de empresa, recepción automática/manual y cierre de entrega. No sustituirlo por una consulta anónima que sólo devuelve 401.

El recorrido visual local está cerrado y la publicación local registrada. El pase requiere después la autorización final para publicar/desplegar y las comprobaciones del destino. Ni la suite local, ni la prueba aislada de cookies, ni VERIFYONLY autorizan por sí solos el cambio de producción.
