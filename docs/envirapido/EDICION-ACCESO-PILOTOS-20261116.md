# Edición del acceso de pilotos

Fecha: 16 de noviembre de 2026. Base local y remota comprobada:
`98859679bfe8bac39942a962bd31d7093bf17f1f`, rama `codex/confiabilidad-panel-app`.

## Uso

En el panel de empresa: **Repartidores → Editar**. El gestor puede modificar
el correo de acceso y, opcionalmente, escribir una contraseña nueva y su
confirmación. Ambas contraseñas vacías conservan la contraseña actual. Nunca
se muestra la contraseña anterior ni se devuelve la nueva en los formularios
con errores. Se conservan las clases y colores existentes del panel.

El cambio de correo actualiza también el nombre de acceso y los normalizados
de Identity: el correo anterior deja de servir como alias. El cambio de correo
o contraseña exige volver a iniciar sesión en la app con las credenciales nuevas.
Editar únicamente nombre o teléfono conserva la sesión, incluso en cuentas
antiguas cuyo nombre de acceso difiere del correo.

## Seguridad y preservación

- Sólo un gestor puede operar sobre un piloto de su empresa; se conserva CSRF.
- La credencial sólo se modifica si la identidad es exclusivamente de piloto:
  sin otros perfiles globales, pertenencias comerciales/administrativas ni
  otras filas de piloto compartiendo la identidad, incluidas pertenencias inactivas.
  El servidor vuelve a comprobarlo; no confía en campos de permiso del formulario.
- Una cuenta compartida mantiene la edición de nombre/teléfono, pero muestra
  el acceso como no editable y orienta al titular a recuperar su contraseña.
- Perfil, correo y contraseña se guardan dentro de una transacción Serializable,
  con bloqueos de empresa e identidad. Duplicados, errores de validación o fallo
  de la política de contraseña revierten todo, incluso si Identity ya guardó
  una etapa anterior dentro de la transacción.
- Se usan `UserManager.SetUserNameAsync`, `SetEmailAsync` y `ResetPasswordAsync`;
  no se escribe un password en texto claro ni se reemplaza el hash manualmente.
  Los JWT y refresh de la app actual anteriores al cambio quedan rechazados
  por SecurityStamp. Las cookies web conservan el intervalo normal de
  revalidación de Identity; no se promete revocación web instantánea.
- No se elimina un bloqueo de Identity ni se reactiva automáticamente al piloto
  por cambiar su contraseña. Las cuotas y guardas de desactivación se conservan.
- Permanecen los mismos IdentityUserId, piloto, empresa, órdenes, códigos y rutas.
  No hay migración SQL ni cambio de contrato móvil o APK.

Referencias del mecanismo: [SetEmailAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.setemailasync?view=aspnetcore-10.0)
y [ResetPasswordAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.resetpasswordasync?view=aspnetcore-10.0).
La comprobación efectiva de revocación móvil y rollback se realiza con las pruebas del proyecto.

## Validación local

El subconjunto inicial pasó **27/27** con SQL Server y runtime .NET/ASP.NET Core
10.0.15. Se añadió después una regresión de alias legado: el lote incorpora
**28 casos nuevos**. La suite completa final pasó **282/282**, cero fallos y cero
omitidas, incluyendo ese caso adicional y las regresiones anteriores. Se registra en
`artifacts/pilot-credentials-20261116/pilot-credentials-full-sql.trx`.

Se cubren correo solo, contraseña sola, ambos, acceso antiguo rechazado,
JWT/refresh revocados sólo para el piloto objetivo, perfil sin invalidar sesión,
conservación de trabajo/ruta, duplicados, confirmación inválida, rollback después
de actualizar correo, cuentas compartidas, roles privilegiados, empresa ajena y
CSRF. Se ajustaron dos fixtures anteriores para que sigan probando cuota y baja
con el formulario completo.

Backend/Razor Debug y Release compilados sin errores. Se mantienen advertencias
anteriores en otros archivos. Revisión visual de la página QA local aprobada;
se observaron campos habilitados y contraseñas vacías sin enviar un cambio de
credenciales mediante la interfaz. Los cambios efectivos se prueban por HTTP
en bases SQL aisladas con identidades ficticias `example.test`.

Evidencia local: `artifacts/pilot-credentials-20261116/`, incluida captura
`pilot-access-form.png`, logs, TRX y recibos del host. El host visual usó una base
GUID nueva, se detuvo y se eliminó exclusivamente esa base temporal mediante
guardas de nombre, antigüedad e identidades ficticias. No se tocó la base QA
histórica, el emulador, el Samsung ni cuentas productivas.

## Publicación

Publicación en GitHub autorizada el 16/11/2026, en la rama
`codex/confiabilidad-panel-app`. El agente de despliegue debe utilizar el commit
exacto de este lote; publicar el código no acredita que el servidor público
esté actualizado.

Desplegar únicamente el backend, manteniendo conexión SQL, configuración HTTPS,
clave JWT y claves persistentes de Data Protection. No ejecutar seeds ni crear
una migración por este cambio. El mismo APK sigue siendo válido.

Después del despliegue, comprobar con cuentas ficticias de una empresa propia:
Repartidores → Editar, correo editable, contraseña nueva opcional y confirmación;
nuevo acceso correcto y anterior rechazado, sin modificar los pedidos o rutas
asignados. Un cambio de credenciales exige que ese piloto vuelva a iniciar sesión;
editar sólo nombre o teléfono conserva su sesión. Revalidar rechazo de recursos
ajenos y controles CSRF. No se cambian credenciales reales como parte del push.
