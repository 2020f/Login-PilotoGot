# Envirapido: arnés local de QA

Herramienta de desarrollo separada del servidor publicado. Usa `PilotGoFactory`, SQL Server LocalDB y una base nueva `PilotGoTest_<guid>` por ejecución. No lee `DefaultConnection`, no modifica la base del host QA anterior y elimina sólo su base GUID al cerrar normalmente. No se distribuye con la aplicación. El proceso no se debe publicar ni exponer mediante túneles.

Requiere SDK .NET 10 (base 10.0.201 en `global.json`), los runtimes .NET y ASP.NET Core **10.0.15 o posteriores compatibles**, y una instancia LocalDB exclusiva iniciada. Por defecto usa `PilotGoVisualQA_20261111`; se admite `PILOTGO_TEST_SQLSERVER_INSTANCE` con el mismo validador de las pruebas. En un entorno restringido, LocalDB requiere ejecución nativa autorizada para sus named pipes.

En esta PC se preparó un runtime privado verificado de Microsoft en
`artifacts/net10-20261112/runtime`. Los siguientes ejemplos lo seleccionan sólo
para el proceso PowerShell actual. El helper no instala runtimes ni cambia el
PATH persistente; aborta si faltan sus archivos. En otra PC, instalar previamente
el SDK y ASP.NET Core compatibles o preparar ese runtime; no copiar los enlaces
del SDK local como si fueran una distribución portátil.

```powershell
. .\tools\Envirapido.Qa\Use-Net10Qa.ps1
$env:PILOTGO_TEST_SQLSERVER_INSTANCE='PilotGoVisualQA_20261111'
& $env:DOTNET_HOST_PATH run --project tools/Envirapido.Qa -- --verify-seed
# La verificación crea los datos, imprime únicamente fixtures ficticios y cierra su base.

& $env:DOTNET_HOST_PATH run --project tools/Envirapido.Qa
# http://127.0.0.1:5019/qa/login/manager-a
```

Para reutilizar una base QA existente **sin volver a sembrarla ni eliminarla al cerrar**:

```powershell
. .\tools\Envirapido.Qa\Use-Net10Qa.ps1
& $env:DOTNET_HOST_PATH run --project tools/Envirapido.Qa -- --port 5019 --database PilotGoTest_GUID_EXISTENTE
```

Sustituir `GUID_EXISTENTE` por los 32 caracteres hexadecimales del nombre QA real.
El modo adjunto exige SQL Server LocalDB y las identidades ficticias y pertenencias
QA esperadas. Sólo lee metadatos al arrancar: no ejecuta `Seed`, `EnsureCreated`
ni migraciones; tampoco elimina esa base al disponer el host. Un nombre inválido,
una base ausente o metadatos ajenos abortan el arranque. `--verify-seed` junto
con `--database` imprime únicamente los metadatos conservados y termina sin
crear ni borrar datos.

Antes de sustituir un host antiguo creado como entorno efímero, respaldar y
verificar su base: su código anterior puede eliminarla al cerrarse normalmente.
No iniciar otro proceso en un puerto ocupado. Panel y emulador deben utilizar
el mismo host y la misma base; conservar un proxy ADB hacia otra base oculta los
pedidos creados en el panel.

`GET /qa/environment` permite verificar en loopback el puerto, el nombre de
base, el modo (`attached-preserved` o `ephemeral-seeded`) y las versiones reales
de runtime/ASP.NET Core. No devuelve claves,
contraseñas, sesiones ni datos de las órdenes. Para el entorno de usuario de
5019, usar el panel en `http://127.0.0.1:5019` y dirigir el puerto del APK QA al
mismo host: `adb -s SERIAL_QA reverse tcp:5019 tcp:5019`. Comprobar primero que
`/qa/environment` corresponde a la base preservada; no apuntar ese emulador al
host de otra base de auditoría.

Para el emulador, usar preferentemente el helper que verifica base, modo y paquete
antes de conectar ADB (sustituir el GUID por la base preservada del panel):

```powershell
.\tools\Envirapido.Qa\Connect-QaEmulator.ps1 -Port 5019 -ExpectedDatabase PilotGoTest_GUID_EXISTENTE -Device emulator-5554
```

Sólo admite identificadores `emulator-<número>`, exige el modo
`attached-preserved` y devuelve un recibo JSON sin credenciales. Si el host no
coincide, aborta antes de modificar el túnel.

No ejecutar mientras otro servidor usa 5019. El proxy escucha únicamente loopback y además rechaza IP remota no loopback. Los cuerpos de formularios/JSON se bufferizan con límite de 1 MiB y cancelación; no acepta subidas de archivo. Los atajos `/qa/login/<id>` son exclusivos de las cuentas ficticias permitidas. Para Android usar `adb reverse tcp:5019 tcp:5019` y un APK de QA cuyo servidor sea `http://127.0.0.1:5019`; nunca alterar el APK público para esta prueba.

Cuentas: `manager-a@example.test`, `manager-b@example.test`, `qa-admin@example.test`, `shop-a@example.test`, `qa-shop-a2@example.test`, `shop-b@example.test`, `multi@example.test`, `pilot-a@example.test`, `pilot-a2@example.test`, `pilot-b@example.test`. Contraseña de todos: `Test-only-Password42!`. Gestores abren `/Operaciones`; SuperAdmin conserva su panel separado; comercios abren sus órdenes. La cuenta `multi` pertenece a A1 y A2; B es otra empresa. Los atajos del navegador no sustituyen el login real de la app.

Semillas creadas mediante servicios reales, con contactos y coordenadas ficticias en Santo Domingo:

- Dos entregas completadas por piloto A2; códigos C consumidos únicamente como entrega.
- Cobro de DOP 500 declarado, conciliado y recibido por la empresa, separado de ganancias.
- Regla de pago por servicio y por parada; una liquidación de piloto pagada fuera del sistema, con referencia QA, y una de comercio en revisión.
- Zona poligonal; ruta activa de piloto A2 que agrupa una orden A1 con dos destinatarios/C y una orden A2 simple. Contiene dos recogidas B y tres entregas C.
- Incidencia de parada según regla `Sin contacto`, con pausa y plazo de 30 minutos. La recogida B puede continuar; su entrega requiere resolver la pausa y respetar secuencia.
- Una orden programada para el día siguiente, una orden lista por asignar y otra exclusiva de empresa B.
- Piloto A queda sin órdenes asignadas para probar creación, recepción automática, asignación manual y continuidad de sesión mediante UI.

`QA_SEED` en la salida contiene IDs y códigos sólo de estos fixtures, para repetir el recorrido. Cambian cada ejecución. No contiene JWT ni claves reales.

Los mapas de servidor y ETA se mantienen deshabilitados por defecto. `--mapas` activa únicamente Maps JavaScript con `GoogleMaps:BrowserApiKey` ya configurada: no imprime la clave, no usa la clave de servidor como fallback y no geocodifica ni consulta Routes API. Las coordenadas permanecen ficticias; no es evidencia de rastreo o recorrido físico real. Si falta clave de navegador, el mapa permanece no disponible. No guardar capturas que incluyan datos personales ajenos a estos fixtures.
