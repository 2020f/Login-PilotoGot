# PilotGo 1.1.0: actualización y distribución

Esta versión conserva la sesión móvil, permite recordar la contraseña y
actualiza las asignaciones manuales y automáticas sin cerrar sesión. Las
órdenes **nuevas** muestran A, B y C con cuatro dígitos, incluidos ceros iniciales.
Las órdenes existentes conservan sus códigos.

## Primero actualizar el servidor

Publicar código y generar el APK no actualiza el servidor. No se ejecutó esta
migración contra la base de datos pública.

1. Respaldar la base y configuración; detener la aplicación durante la
   migración para evitar creaciones con la versión anterior. Conservar la
   conexión SQL, archivos y claves de protección de datos del servidor.
2. Obtener esta versión de `2020f/Login-PilotoGot`, rama `master`.
3. Configurar `ASPNETCORE_ENVIRONMENT=Production` y `Jwt__Key` con un secreto
   privado aleatorio de al menos 32 bytes. La clave publicada de desarrollo
   no se admite en producción. Conservar `Jwt__Issuer` y `Jwt__Audience` si ya
   se configuraban. Cambiar la clave requiere iniciar sesión una vez. Nunca
   subirla a Git. `Jwt__RefreshDays` es opcional (30 días por defecto).
4. Aplicar `20261110033858_CodigosCuatroDigitos` a la base existente. El script
   [codigos-cuatro-digitos.sql](qa/codigos-cuatro-digitos.sql) es idempotente y
   parte de `20260908024715_AddPilotoEnLinea`. Ejecutarlo con SQL Server/SSMS o
   `sqlcmd -b`, que reconocen `GO` y permiten abortar ante errores. Si faltan
   migraciones previas, usar EF Core 8.0.23 con la configuración privada:

   ```powershell
   dotnet ef database update --project Login/Login.csproj
   dotnet publish Login/Login.csproj -c Release -o artifacts/server
   ```

5. Copiar la publicación al servicio existente y reiniciarlo con su
   configuración privada. Si se usa `dotnet run`, agregar `--no-launch-profile`
   para no activar el perfil de desarrollo.
6. Verificar: crear → asignar/aceptar → B → C+B → recibir la siguiente orden
   con la misma sesión. Repetir asignación manual y separación entre tenants.
7. Distribuir el APK. Las apps anteriores pueden terminar órdenes antiguas;
   los códigos nuevos requieren actualizar porque la validación exige `ordenId`.

Producción ya no crea cuentas de demostración automáticamente. Esto no elimina
las existentes: retirar cuentas de prueba antes de entregar accesos reales.

## Reglas de los códigos

`OrdenId` y `NumeroOrdenA` permanecen únicos e históricos. El nuevo `CodigoA`
es una cadena; NULL conserva el A antiguo. A se reserva por tenant y B/C por
tenant y tipo mientras la orden esté abierta. Los números pueden repetirse
en otro tenant o después de completar la entrega.

La creación es transaccional y serializa la secuencia por tenant. Los índices
filtrados impiden duplicados activos. El generador recorre como máximo 10.000
candidatos y devuelve un error si se agotan. La validación comprueba orden,
piloto, tenant y estado. Las confirmaciones se limitan a diez solicitudes por
usuario/minuto/proceso, sin limitar las consultas de órdenes.

La migración completa tenant/reserva sin cambiar códigos antiguos. Revertir
después de emitir códigos nuevos se bloquea; restaurar el respaldo previo es
la vía de rollback que conserva esos datos.

## APK y firma

- Fuente móvil: `https://github.com/2020f/pilotgo-app` (privado).
- Versión **1.1.0 (2)**, paquete definitivo **com.pilotgo.app**.
- APK: `C:\Users\Fernelis\Desktop\pilotGOapp\build\app\outputs\flutter-apk\app-release.apk`.
- API: `https://pilotogoapp.zapflow77.dpdns.org`.
- La app de pruebas era `com.example.pilotgo`. Esta primera distribución es
  independiente y requiere iniciar sesión una vez. Conservar paquete y firma
  para las actualizaciones siguientes.
- Firma privada: `C:\Users\Fernelis\.pilotgo\signing\pilotgo-release.jks`.
  Su `key.properties` está en la misma carpeta. Respaldar ambos privadamente.
  Compartir solo el APK; nunca la carpeta de firma.
- Google Maps: autorizar `com.pilotgo.app` y SHA-1
  `72:4B:73:6A:47:72:9F:13:C4:6C:65:0B:3D:8A:9F:EC:27:6A:94:2D`.
  No se verificó la configuración del proveedor. La clave Android se toma de
  `PILOTGO_MAPS_API_KEY` o `maps.apiKey` en `android/local.properties`, ignorado.

## Validación y límites

Resultados y hash del APK: [release-1.1.0.json](qa/release-1.1.0.json).
Las pruebas usan datos ficticios y bases desechables. El escenario de flota
recorre cuarenta órdenes de cuarenta comercios y veinte pilotos en dos tenants,
dos rondas y cinco flujos concurrentes. Prueba aislamiento, asignación y
entregas con sesiones reutilizadas; no certifica capacidad del servidor público.

La app consulta cada cinco segundos en primer plano y al reanudarse. No incluye
push con el proceso cerrado. Almacenamiento nativo, Maps, cámara y GPS requieren
validación física. No se instaló el APK en el teléfono ni se tocaron órdenes reales.

Referencias: [Android release en Flutter](https://docs.flutter.dev/deployment/android),
[índices en EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/indexes),
[migraciones](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing).
