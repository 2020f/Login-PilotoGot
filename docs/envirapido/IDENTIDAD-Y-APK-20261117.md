# Identidad del CRM y APK con la caja

> Registro de la validación local previa a publicación. La entrega del 18/11/2026
> y el SHA móvil definitivo constan en
> [DESPLEGAR-ENVIRAPIDO-2.1.md](../../DESPLEGAR-ENVIRAPIDO-2.1.md).
> El `sourceCommit:null` del recibo técnico original se conserva como checkpoint;
> el recibo de entrega registra la correspondencia final sin inventar un rebuild.

## Diferencia observada

El archivo distribuido `Envirapido-2.0.1-9-release.apk` se compiló antes del
rediseño móvil. Contiene el icono y colores anteriores. Publicar ese mismo archivo
en el portal no incorpora cambios del árbol Flutter ni modifica el login CRM.
Los cambios operativos y móviles posteriores permanecían locales.

Base backend actual: `0f7cd32c4d27617b697217c80916188857a19247`.
Base Flutter: `cd96b205da886627452cf5dc2cbec4b940bf6b93`.
Ambos árboles conservan cambios pendientes; no se hizo commit, push, publicación
de APK ni despliegue productivo durante esta revisión.

## Login CRM implementado

- `Login/Areas/Identity/Pages/Account/Login.cshtml` carga el logo original, caja e
  ilustración de última milla; composición mint/turquesa con formulario blanco.
- `Login/wwwroot/css/auth-login.css` contiene estilos exclusivos de esta página.
  `auth.css`, `_LayoutAuth.cshtml` y el handler C# no se modificaron.
- Los tres PNG de `wwwroot/branding` se copiaron byte por byte del portal.
- Se conservaron `Input.*`, rutas, CSRF, recordar sesión, ReturnUrl y proveedores
  externos. Se separó el formulario externo del principal para eliminar HTML
  anidado, manteniendo su acción y parámetros.
- Navegador real local: recursos cargados, escritorio 1440 px y móvil 360/320 px
  sin desbordamiento horizontal, mostrar/ocultar contraseña, credenciales inválidas,
  login de gestor ficticio y logout correctos. No se guardaron claves en navegador.

Validación fresca: build .NET aprobado y **35/35 pruebas SQL Server**, sin fallos
ni omitidas, seleccionando `AuditIdentityRegressionTests`, `OrderFlowTests` y
`PortalIntegrationTests`. Evidencia:
`artifacts/branding-release-20261117/branding-auth-sql.trx` y logs del mismo directorio.
Capturas: `crm-login-desktop.png` y `crm-login-mobile.png`.

El host del emulador se reabrió en loopback5039 adjunto a la base QA ficticia
conservada `PilotGoTest_322032134601455fb87d4d991dd3d91b`, sin seed, migraciones
ni reiniciar pedidos. No se modificó la base del portal ni producción.

## APK release candidato correcto

Archivo local del repositorio móvil:
`build/branding-release-20261117/Envirapido-2.1.0-10-release-candidate.apk`.

| Dato | Verificación |
| --- | --- |
| Versión y paquete | 2.1.0 (10), `com.pilotgo.app` |
| SHA256 | `d1fc8278288adc75390af9baca9998158c7ee07559f979ed6aeaeed8e7dd4be3` |
| Tamaño | 75,427,589 bytes |
| Certificado definitivo | `773d72ba127d16a95f032d520d37162708ea8676bf2f7e3e48168f751724e487` |
| Firma / alineación | V2 válida y zipalign16K aprobado |
| ABI | armeabi-v7a, arm64-v8a y x86_64 |
| URL compilada | `https://envirapido.com` en las tres ABI; sin origen QA5019 |
| Sesión | Conserva el ámbito cifrado legacy en los binarios |
| Caja | Asset idéntico al original; raster y adaptativo comprobados |

`flutter analyze --no-pub`: sin incidencias. Suite secuencial completa:
**120/120 pruebas aprobadas**. Build release aprobado con `PILOTGO_QA` ausente,
sin fallback de firma debug. No se abrieron ni registraron secretos de firma.
Logs, manifiesto de fuentes y `apk-inspection.json` están en el directorio del APK.

Se instaló únicamente en emulator5556, separado del paquete `.qa` ya existente.
El SHA256 del APK instalado coincide con el candidato. Se observaron el launcher
con la caja y el login móvil mint con la caja. No existía antes el paquete release
en ese emulador: esto no es una nueva prueba de actualización conservando sesión
del release anterior. No se modificó el Samsung ni se inició sesión en producción.
Capturas reales: `android-launcher-release.png` y `android-release-login.png`,
en `artifacts/branding-release-20261117` del backend.

En la variante QA separada se conservó y observó el listado comercial renovado
con datos de SQL local; después se inició sesión con el piloto ficticio y se
observó su cabecera con caja, acciones turquesa y panel deslizante. No se recogió,
entregó, reasignó ni canceló ningún pedido durante esta revisión visual. Capturas:
`android-qa-comercio.png` y `android-qa-piloto.png`. Estas vistas autenticadas son
de QA local; el release público sólo se abrió hasta su login sin credenciales.
Se arrastró el panel del piloto hacia arriba y se observó su expansión conservando
el contenido y la navegación: `android-qa-panel-expandido.png`.

## Entrega y límites

El candidato proviene de fuentes locales pendientes, por lo que su recibo tiene
`sourceCommit:null`; no usar el SHA viejo como commit de esta compilación. Antes de
publicar hay que fijar el SHA exacto de las fuentes, comprobar su correspondencia
con el candidato y efectuar la entrega GitHub autorizada.

La identidad visual del APK no actualiza el backend. Para utilizar reasignaciones
y recogida por lote, publicar el backend compatible y aplicar sólo al SQL CRM la
migración `20261117230916_EnvirapidoReasignacionPorParadas`, siguiendo
[REASIGNACION-PARADAS-20261117.md](REASIGNACION-PARADAS-20261117.md). La revisión
estática encontró fallback legacy para capacidades3; no sustituye una prueba pública.

Falta validar el APK definitivo con el backend público que vaya a distribuirse,
incluyendo órdenes manuales/automáticas y multitenant. La QA anterior de340 SQL
del lote operativo y sus recorridos nativos se conservan como evidencia histórica;
las comprobaciones frescas de esta revisión son las indicadas arriba.

El login CRM necesita un nuevo release web con sus assets; sustituir un APK no
actualiza HTML/CSS. Para publicar en el portal usar una entrada nueva con versionCode
mayor al máximo del canal, firma validada y QA funcional real. No reutilizar el APK9
para anunciar el icono o diseño nuevos.
