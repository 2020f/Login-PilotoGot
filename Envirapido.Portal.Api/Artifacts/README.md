# Versiones Android privadas

Este módulo no instala, ejecuta ni firma aplicaciones. Recibe un APK ya firmado,
lo conserva fuera del contenido web y consulta las herramientas oficiales de
Android para comprobarlo. No incluye ni requiere el keystore de firma.

Configuración de servidor en la sección **PortalArtifacts**:

```json
{
  "PortalArtifacts": {
    "StoragePath": "/var/lib/envirapido-portal/artifacts",
    "JavaPath": "/usr/bin/java",
    "ApkSignerJarPath": "/opt/android-sdk/build-tools/36.0.0/lib/apksigner.jar",
    "Aapt2Path": "/opt/android-sdk/build-tools/36.0.0/aapt2",
    "MaxUploadBytes": 157286400,
    "MaxExpandedBytes": 1073741824,
    "MinimumVersionCode": 9,
    "ToolTimeoutSeconds": 30
  }
}
```

Las rutas deben ser absolutas, con las herramientas instaladas desde Android SDK
y permisos limitados al usuario del servicio. `StoragePath` no puede estar dentro
del directorio de la aplicación ni de `wwwroot`. No usar una carpeta servida por
el proxy, CDN o servidor estático. En Windows se configura `java.exe`, el JAR de
apksigner y `aapt2.exe`; nunca se invoca `cmd`, un BAT, ni una herramienta subida
por el usuario. Se limita el proceso por tiempo, heap Java y volumen de salida.

La firma permitida predeterminada es la definitiva de Envirapido. Una firma
distinta, paquete QA/debug, código no creciente o archivo dañado no se publican.
Fuera de Development/Testing el mínimo nunca baja de 9, aunque la configuración
indique un valor inferior. Ese piso es inclusivo: el APK 9 firmado ya distribuido
puede ser la primera versión privada. Después, cada publicación exige un código
mayor al máximo publicado, incluso tras retirar la versión vigente.

Administración: `/portal-api/internal/admin/artifacts` requiere exclusivamente
la política interna `PortalAdmin`. Los POST requieren `Idempotency-Key`. La
carga es multipart con `file`, `notes`, `sourceCommit` de 40 caracteres hexadecimales y
`publicBaseUrl=https://envirapido.com`; `expectedSha256` es opcional. Primero
queda en `Quarantined`. `/id/validate` devuelve un registro `Validated` o
`Rejected`. `/id/publish` requiere `operationId`, `expectedVersion`,
`qaApproved=true` y `reason`; no basta una firma correcta para dar por comprobado
el comportamiento o la URL compilada. El operador debe haber realizado esa QA.

Publicación y retirada son idempotentes y mantienen un máximo histórico de
versionCode. Una retirada no permite reutilizarlo ni revierte instalaciones.
Las respuestas administrativas contienen metadatos, nunca rutas físicas.

Compradores: `/portal-api/v1/downloads/current` y `/downloads/id/file` aceptan
solo la cookie del portal y comprueban su acceso efectivo en cada solicitud,
incluidos rangos y condiciones HTTP. El archivo se transmite con descarga
adjunta, sin caché pública. Los eventos registran solicitudes autorizadas, no
afirman que el cliente terminó de recibir todos los bytes.

No hay redirección al APK histórico de GitHub: ese enlace permanece público y
no es un almacenamiento privado. Si no hay versión nueva publicada, el portal
devuelve un estado vacío con 404 controlado. Herramientas/almacenamiento sin configurar
producen 503 seguro; no se inventa una validación ni se publica automáticamente.

Referencias: [apksigner](https://developer.android.com/tools/apksigner) y
[AAPT2](https://developer.android.com/tools/aapt2).
