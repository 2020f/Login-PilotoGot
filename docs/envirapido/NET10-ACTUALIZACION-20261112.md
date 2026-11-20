# Envirapido: actualización a .NET 10 y validación local

Fecha: 12/11/2026, America/Santo_Domingo. Actualización autorizada y completada
en desarrollo/QA local; **sin commit, push ni despliegue de producción**.

## Resultado

Producto, pruebas y arnés QA ejecutan .NET/ASP.NET Core **10.0.15**. La suite
completa con SQL Server aprobó **231/231**, Flutter **93/93**, y se completó
el recorrido real comercio → CRM → piloto en el emulador contra el host nuevo.
La app conservó su sesión y sus cuatro órdenes previas, recibió una manual
adicional y aceptó otra automática mientras atendía las anteriores. La nueva
automática se recogió con B y se entregó sólo con C; pasó al historial y las
otras cinco permanecieron activas.

Los datos operativos anteriores, rutas, identidades, roles, facturas y pagos
conservaron los diez fingerprints registrados. No fue necesaria una migración
por actualizar .NET. Esto acredita QA local; el servidor público todavía no
se ha actualizado ni validado con este lote.

## Base y cambios concretos

| Elemento | Estado |
| --- | --- |
| Servidor/panel | `C:\Users\Fernelis\source\repos\Login-PilotoGot` |
| App Flutter | `C:\Users\Fernelis\Desktop\pilotGOapp` |
| Rama de ambos | `codex/confiabilidad-panel-app` |
| HEAD servidor previo, sin commit nuevo | `36d15c7696f1b0a8018251cbcdd6be9897ae7248` |
| HEAD app previo, sin commit nuevo | `8ca95af1ea7c2481be73e38d7abf3c0156ceaae9` |
| Estado | Se preservan los cambios locales de lotes anteriores. Los HEAD no representan por sí solos este árbol sin publicar. |

- Los tres proyectos `Login`, `Login.Tests` y `tools/Envirapido.Qa` usan
  `net10.0` y mínimo `RuntimeFrameworkVersion` 10.0.15. Referencias Microsoft
  ASP.NET Core, Identity, EF Core y Mvc.Testing alineadas en 10.0.15.
- `global.json` selecciona SDK 10.0.201, permite `latestFeature` y rechaza
  versiones preliminares. QA usa una copia privada oficial de ambos runtimes
  10.0.15, verificada por SHA-512 contra el manifiesto de Microsoft. El helper
  `Use-Net10Qa.ps1` modifica sólo el proceso PowerShell actual. No se cambió la
  instalación global ni el PATH persistente del equipo.
- Identity conserva esquema V1 y longitud de claves anterior mediante
  `Stores.SchemaVersion` y `MaxLengthForKeys=0`: sin passkeys añadidas,
  acortamiento de claves, cambio de hashes de contraseña o sellos de seguridad.
- SQL Server mantiene la traducción parametrizada de colecciones mediante
  `UseParameterizedCollectionMode(ParameterTranslationMode.Parameter)`;
  la prueba de 3.002 IDs evita una regresión por el límite SQL de 2.100 parámetros.
- Se eliminó scaffolding de Visual Studio que no se utilizaba y arrastraba
  dependencias vulnerables. `Microsoft.EntityFrameworkCore.Design` queda
  explícito con `PrivateAssets=all`. Auditoría NuGet del producto: cero
  vulnerabilidades conocidas reportadas, incluidas las transitivas.
- `/qa/environment` muestra las versiones reales del runtime y ASP.NET Core,
  sin secretos. Ese endpoint y los accesos ficticios son exclusivos del arnés
  local, fuera del producto publicado.
- El cambio de backend no modifica el diseño Flutter, sus códigos ni su API
  pública. No se generó un APK nuevo en este lote: se probó Envirapido QA
  2.0.0+8 instalado en `emulator-5554`, paquete `com.pilotgo.app.qa`.

## SQL Server y conservación

Host QA: `http://127.0.0.1:5019`, modo `attached-preserved`, instancia
`(localdb)\PilotGoVisualQA_20261111`, base
`PilotGoTest_4612d89bd20d464394bd3724a9fea91a`. Panel y emulador usan el mismo
servidor/base mediante el helper de conexión QA. El arranque adjunto no sembró,
recreó ni migró la base.

Antes del corte: **17 órdenes, 35 códigos, 10 usuarios y 17 sesiones**. Los
recibos inmediatamente anteriores/posteriores al cambio de runtime son iguales.
Tras las pruebas visuales, se compararon de nuevo las 17 órdenes originales y
35 códigos originales, excluyendo únicamente las dos órdenes QA recién creadas.
Los diez hashes siguen iguales: campos registrados de órdenes/códigos/paradas,
rutas/asociaciones/puntos, identidad/roles y facturas/pagos. No se afirman hashes
de columnas que no estén en `capture-qa-state.sql`.

La prueba de refresh rota expresamente las sesiones ficticias de piloto B y
comercio B; el GPS de piloto A se actualiza por la app. Son escrituras esperadas
y no implican reiniciar o regenerar identidades. La tienda A1 se activó para
probar automático y se dejó en su configuración previa, automático desactivado.

Respaldo previo: `artifacts/net10-20261112/qa-before-net10.bak`, con CHECKSUM y
`RESTORE VERIFYONLY` satisfactorios. SHA-256:
`CF3C3E55143867EBCF39886FD4F9047BC284380F8EC2CC6D7B942DD6D75D0735`.
**No se ejecutó una restauración real**. EF 10 informa que no hay diferencias
entre modelo y snapshot; no se agregó migración ni se suprimió el control.

La vista Sistema mantiene «Revisar migraciones» en esta base histórica, creada
con `EnsureCreated` y sin `__EFMigrationsHistory`. Esa señal conserva su sentido:
no se inventaron filas de historial para ocultarla. El modelo es compatible,
pero el esquema e historial de un destino productivo requieren revisión propia.

## Pruebas y evidencia

Todos los archivos citados están en `artifacts/net10-20261112/`, salvo donde se
indique otra carpeta. Los secretos capturados para interoperabilidad y las
claves Data Protection se mantienen ignorados por Git y no se publican.

`verification.json` consolida estos resultados; `upgrade-source-hashes.json`
identifica las fuentes de la actualización y `product-publication-manifest.json`
registra los hashes de los 529 archivos de la publicación local.

| Validación ejecutada | Resultado | Recibo |
| --- | --- | --- |
| Suite completa SQL Server, bases QA nuevas | 231/231, sin fallos ni omitidas; ejecutada en Debug/net10.0 | `net10-full-sql.trx`, `full-sql.log` |
| Compatibilidad de modelo, Identity y colecciones SQL | 5/5; incluidos en la suite completa | `net10-model-compatibility-sql.trx`, `model-diff.txt` |
| Compatibilidad adicional de autenticación | 5/5; incluidos en la suite completa | `net10-auth-compatibility-sql.trx` |
| Flutter, ejecución nueva | 93/93 | `flutter-tests.jsonl` |
| Dependencias NuGet directas/transitivas del producto | 0 vulnerabilidades conocidas reportadas | `package-audit.json` |
| Cookie Identity emitida por proceso real 8.0.25 y leída por 10.0.15 | PASS, identidad/rol/caducidad y serializer; alteración/aplicación/propósito incorrectos rechazados | `cookie-net8-net10-result.json` |
| JWT y refresh realmente emitidos por host .NET 8 y usados en host .NET 10 | PASS piloto B y comercio B; roles y empresa aislados, refresh único, replay 401 | `compat-net8-net10-result.json` |
| Sustitución del runtime, misma base | Fingerprints iguales | `qa-before-runtime.json`, `qa-after-runtime.json` |
| Datos anteriores tras recorrido visual | 10/10 fingerprints iguales; 17 órdenes y 35 códigos originales | `original-preservation-result.json` |
| Estado final y relaciones por empresa | Manual activa, automática entregada; 0 cruces tenant de piloto/ruta/código | `e2e-final-sql.json` |
| Publicación Release del arnés QA | Correcta; host real 10.0.15 | `qa-publish.log`, `host-environment-final.json` |
| Publicación Release del producto | Correcta; fuera del arnés y sin exponer accesos QA | `product-publish.log`, `../envirapido-net10-release/` |

La cookie entre versiones se probó con las mismas claves persistentes, nombre
de aplicación y propósito; keyring protegido por DPAPI. **No acredita continuidad
de cookies del host QA**, que usa claves efímeras: para las vistas web se inició
sesión nuevamente mediante el acceso ficticio. La sesión móvil de piloto A sí
continuó sin cerrar sesión ni borrar datos. Producción debe conservar su almacén
Data Protection, configuración JWT e identidad de servicio.

## Recorrido visual en emulador y panel

1. Al iniciar la validación con el servidor nuevo, la app conservó las órdenes
   `0013`, `0012`, `0009` y `0010` en Activas sin volver al login.
2. El comercio creó `0017` (ID SQL 18). A1 estaba en modalidad manual; el gestor
   asignó Piloto A desde el CRM, que mostraba sus cuatro órdenes activas. La app
   recibió la quinta y mostró `0017` sin cerrar sesión.
3. Se activó automático en la tienda QA A1 y el comercio creó `0018` (ID SQL 19).
   Apareció la oferta en el modal deslizante con «Continuar mis entregas». Se
   aceptó y Ruta mostró **seis entregas activas**. No se sustituyó la asignación
   anterior ni se alteró la ruta existente de otro piloto.
4. Se abrió únicamente `0018`, se introdujo B y se confirmó la recogida. La vista
   pasó a «En ruta», mostró el destinatario y permitió validar con C solamente.
5. Se introdujo C y se finalizó. La app confirmó 1/1 entregadas, volvió a `0017`,
   dejó cinco en Activas y mostró `0018` en Historial. Comercio registró horas
   de asignación, recogida y entrega. SQL confirma B/C consumidos y liberados.
6. Se restauró automático desactivado en A1. El SuperAdmin abrió Estado del
   sistema: runtime **10.0.15**, «En soporte», base «Disponible», entorno Testing.
   Las señales de SMTP y migraciones se conservaron, sin falsos indicadores verdes.

Capturas de este recorrido, tomadas del entorno .NET 10:

- `emulator-session-restored.png`: sesión y cuatro asignaciones previas.
- `emulator-manual-five-active.png`: nueva manual sin cerrar sesión.
- `emulator-auto-offer.png`: oferta y modal original.
- `emulator-six-active.png`: seis entregas simultáneas.
- `commerce-auto-real-created.png`: comprobante de la nueva automática.
- `emulator-picked-final-only-c.png`, `emulator-final-c-form.png`: recogida y C.
- `emulator-delivered.png`, `emulator-history-delivered.png`: cierre e historial.
- `emulator-final-five-active.png`: restantes activas.
- `superadmin-net10-system.png`: diagnóstico real de runtime/SQL.

La nota de `0017` conserva el nombre inicial «QA NET10 AUTO», pero su modalidad
real comprobada es manual (`EsAuto=false`); `0018` es la automática. Se conservan
ambas órdenes de QA para inspección. No son pedidos productivos ni entregas
físicas. No se probó de nuevo el Samsung ni se modificó ese teléfono.

## Publicación local y límites del pase

La carpeta `artifacts/envirapido-net10-release` contiene la publicación del
producto. Su `Login.dll` es idéntica por SHA-256 a la publicada en el host QA
Release usado para el recorrido. Su runtimeconfig exige .NET/ASP.NET Core
10.0.15. La suite SQL se ejecutó en Debug y la revisión visual en Release; no
se atribuye identidad binaria entre Debug y Release.

Esta carpeta es un candidato local, no una instalación productiva configurada.
No distribuir el arnés QA, sus credenciales, respaldos, tokens, claves ni el
runtime privado con enlaces al SDK local. La instalación del servidor requiere
ASP.NET Core 10.0.15 o el parche posterior revisado, secretos reales y claves
persistentes, SMTP, TLS/proxy, verificación del historial EF, respaldo restaurado
en ensayo y smoke autenticado en el origen público original. Consultar
[NET10-DESPLIEGUE.md](NET10-DESPLIEGUE.md).

Quedan como límites explícitos: no se desplegó el servidor público; no se validó
SMTP real, restauración completa del backup, GPS físico con pantalla bloqueada
ni proveedor de rutas en producción. Persisten advertencias heredadas de
precisión decimal/nullable; no se ocultaron ni se introdujo una migración de
datos ajena al alcance. El proyecto y QA local superan el bloqueo del runtime
antiguo; las comprobaciones del destino se completan durante un pase autorizado.
