# Recuperación de cuentas y límite entre identidad y empresa

Correcciones A01/A23, 12 de noviembre de 2026. Este documento describe la implementación; los resultados de SQL Server y del recorrido visual deben vincularse en el recibo de validación del lote. No acredita entrega SMTP real.

## Credenciales compartidas

El reset de un gestor sólo puede modificar una identidad comercial exclusiva de su empresa:

- Debe ser el principal activo de una tienda de esa empresa.
- Su único rol global debe ser `Cliente`.
- No puede tener ninguna pertenencia comercial de otra empresa, incluso inactiva, ni registros de piloto o gestor.
- Comprobación, cambio de contraseña y desbloqueo existente ocurren en una transacción `Serializable`. El orden de locks es tenant → identidad → roles/pertenencias. Se toma un lock de escritura no-op en Identity antes de su lectura para evitar conversión compartido/exclusivo al rotar el sello.

La pertenencia multitienda de la misma empresa se conserva. Ante una identidad compartida, el gestor recibe un rechazo sin conocer nombres/IDs de otras empresas y el titular debe recuperar su cuenta por correo. Cambiar contraseña no añade permisos ni cambia de comercio; Identity rota su sello de seguridad para invalidar sesiones móviles anteriores.

## Recuperación del titular

Las páginas Identity `ForgotPassword`, `ResetPassword`, sus confirmaciones y `ConfirmEmail` usan el estilo de acceso de Envirapido. El enlace cifrado de Identity vence en una hora; el reset exitoso invalida su reutilización y no inicia sesión automáticamente. Un token de otra identidad, mal formado o vencido se rechaza sin cambiar credenciales. La recuperación no activa tenants, comercios, perfiles o pertenencias suspendidos y no elimina bloqueos administrativos.

Las solicitudes para un correo registrado o desconocido tienen el mismo resultado público. Si el transporte falla, tampoco se expone la existencia de la cuenta ni se afirma que el correo llegó. Las páginas con tokens usan `no-store` y `Referrer-Policy: no-referrer`. El endpoint de cuenta tiene límite de diez peticiones por minuto y origen de red; los códigos B/C mantienen su política independiente por usuario.

## Configuración de correo

No se almacena ninguna credencial en este documento ni se agregó configuración de entrega ficticia. Usar secretos del entorno o del servidor:

| Variable | Significado |
| --- | --- |
| `Email__Enabled` | `true` sólo después de configurar el proveedor real. Por defecto deshabilitado. |
| `Email__Host` / `Email__Port` | Servidor SMTP y puerto de STARTTLS; puerto predeterminado587. TLS siempre habilitado, con validación de certificados del sistema. |
| `Email__UserName` / `Email__Password` | Credenciales del proveedor, ambas o ninguna si el relay seguro autorizado no usa autenticación. |
| `Email__FromAddress` / `Email__FromName` | Remitente autorizado por el proveedor y nombre visible. |
| `Email__PublicBaseUrl` | Origen canónico HTTPS de la instalación. No se construyen enlaces desde Host/Forwarded-Host del solicitante. HTTP se admite únicamente en loopback para QA local explícita. |

La entrega tiene timeout de15 segundos. Los logs registran únicamente el tipo de fallo/estado SMTP, nunca destinatarios, credenciales, HTML, enlaces o tokens. No existe fallback a «enviar» escribiendo en consola. Sin configuración, ForgotPassword muestra que el servicio está indisponible y su POST devuelve503 para cualquier correo. El registro público existente conserva su política actual de acceso; si no exige confirmación puede completarse sin simular correo, sin conceder roles o empresas.

## Pruebas

`Login.Tests/AuditIdentityRegressionTests.cs` contiene casos de identidad compartida activa/inactiva, roles privilegiados, tienda ajena, identidad exclusiva con varias tiendas propias, revocación móvil, enlace canónico/single-use, expiración, ausencia/fallo de entrega no enumerante, confirmación y registro sin proveedor. El `CapturingEmail` sólo existe en pruebas y no se usa como proveedor en la app ni en producción.

No se envió ningún correo real durante esta corrección. Antes de habilitar la recuperación en una instalación real se necesita configurar el proveedor y comprobar su entrega al buzón del titular.
