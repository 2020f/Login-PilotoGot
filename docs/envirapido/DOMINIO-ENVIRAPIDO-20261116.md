# Preparación del dominio envirapido.com

Fecha: 16 de noviembre de 2026 (República Dominicana). El propietario autorizó
publicar la preparación en GitHub para que el agente de despliegue conecte la web
y API al dominio nuevo. Publicar no acredita despliegue ni validación autenticada.

## Destino y compatibilidad

Origen canónico de la app 2.0.1 (9): **https://envirapido.com**. La API conserva
`/api/auth`, `/api/piloto`, `/api/tienda` y `/api/operaciones`, contrato 3.
No se modifican pedidos, asignación, rutas, cobros, límites ni reglas multitenant.
No se necesita migración SQL por cambiar el dominio.

## Referencias para el agente

- Servidor: repositorio `2020f/Login-PilotoGot`, rama
  `codex/confiabilidad-panel-app`; usar el commit que contiene esta guía y su
  archivo de configuración de ejemplo. Incluye la corrección de estadísticas
  de Conciliación `7e7819335cf3aebd6b2b4763e209a38d2d654777`.
- App móvil: repositorio privado `2020f/pilotgo-app`, misma rama, commit
  [`cd96b205da886627452cf5dc2cbec4b940bf6b93`](https://github.com/2020f/pilotgo-app/commit/cd96b205da886627452cf5dc2cbec4b940bf6b93),
  árbol `23847846ed7d4cd1fdd30e2c3e3903b642cddb15`.
- APK firmado: [Envirapido 2.0.1 (9), versión preliminar](https://github.com/2020f/Login-PilotoGot/releases/tag/envirapido-android-v2.0.1).
  SHA-256 `c977a823214dd958920b347de420a394363ab7dc3a5ae79a4134e0b5ab8cf268`.
- [Evidencia sanitizada y límites de QA](../qa/domain-envirapido-20261116/README.md).

La web y API ya usan rutas relativas: el destino nuevo se activa en DNS,
túnel/proxy y configuración del servicio existente. El APK nuevo incorpora el
origen canónico y no necesita volver a compilarse al terminar ese apuntado.
No fusionar ramas por inferencia ni sustituir los commits indicados por `main`,
`master` o versiones antiguas durante el despliegue.

Ambos nombres deben atender **el mismo backend y la misma base de datos**:

- `envirapido.com`: panel y API de la nueva versión.
- `pilotogoapp.zapflow77.dpdns.org`: compatibilidad con los APK ya distribuidos.

Mantener la API anterior directamente disponible. No reemplazarla por una
redirección al otro dominio: una redirección de POST o de autorización entre
hosts puede romper las aplicaciones instaladas. No crear otra instancia con
datos, sesiones o claves distintas para el dominio nuevo. No usar un fallback
automático en el cliente que pueda repetir operaciones en otro servidor.

## Configuración que corresponde al despliegue

Aplicar al servicio existente los dos valores de
[`envirapido.com.env.example`](envirapido.com.env.example), conservando el resto de
su configuración privada. Es un archivo de variables para el servicio Linux,
Docker o el gestor de procesos del destino; no contiene secretos y no debe
reemplazar el archivo privado completo. Si hay comprobaciones internas con otro
Host legítimo, añadir ese nombre exacto a `AllowedHosts` antes de reiniciar.
El ejemplo usa formato `env-file`; no ejecutarlo ni cargarlo con `source` como
script de shell. Mantener el valor de `AllowedHosts` como una sola variable.

1. Apuntar DNS/túnel/proxy al alojamiento real de Envirapido. El repositorio no
   determina la IP o el identificador del túnel de producción. Si se publica
   también `www`, configurarlo con TLS y redirección web al origen canónico;
   la app usa únicamente el dominio raíz.
2. Instalar/verificar TLS válido para el dominio nuevo. Mantener la terminación
   HTTPS y las cabeceras saneadas del proxy inmediato real, conforme a
   [HTTPS-PROXY-DESPLIEGUE.md](HTTPS-PROXY-DESPLIEGUE.md). No ampliar la lista de
   proxies confiables por cambiar DNS ni desactivar las cookies Secure o CSRF.
3. Conservar conexión SQL, `Jwt__Key`, emisor/audiencia JWT, claves Data Protection,
   sesiones y configuración de la aplicación existente. Restringir en la
   configuración privada de producción `AllowedHosts` a todos los hosts reales
   atendidos, incluyendo el anterior durante la transición y cualquier nombre
   interno legítimo que requiera la topología. No cambiar globalmente QA/local.
4. Si el correo transaccional está habilitado, establecer
   `Email__PublicBaseUrl=https://envirapido.com`. Los enlaces enviados previamente
   necesitan que el host anterior continúe disponible. No construir enlaces
   desde un Host proporcionado libremente por el visitante.
5. Confirmar que la clave de mapas del panel permite el nuevo origen en sus
   restricciones de referrer. Android conserva paquete y certificado: mantener
   las restricciones nativas por paquete/firma. No publicar claves privadas.

No es necesario ampliar CORS para la app nativa ni para el panel del mismo
origen. Las cookies del navegador pertenecen al host: el gestor normalmente
iniciará una sesión nueva al entrar por el dominio nuevo.

## Actualización Android y persistencia

Mantener `com.pilotgo.app` y el certificado definitivo original, con versionCode
9 superior al APK 2.0.0 (8). Actualizar instalando sobre la app existente, sin
desinstalar ni borrar sus datos. La variante QA sigue usando su paquete separado.

`ApiConfig.storageScope` conserva las claves cifradas históricas para los dos
orígenes HTTPS exactos que representan esta misma instalación. Esto mantiene
sesiones, contraseña recordada opcional, creaciones pendientes con su operación
idempotente, acciones del piloto y cola/secuencia GPS. Cuenta, tienda y acción
conservan su aislamiento; QA y cualquier otro servidor no comparten sus claves.
La equivalencia afecta únicamente al almacenamiento local. Las solicitudes HTTP
usan el dominio nuevo y conservan la validación TLS habitual.

Mantener los validadores del servidor, las operaciones idempotentes y las
credenciales existentes. Un cambio de dominio no autoriza recuperar accesos
revocados ni modificar empresas, roles, suscripciones o pagos.

## Comprobación pública previa a distribuir

Primero ejecutar los controles anónimos contra ambos orígenes:

```powershell
.\tools\Verify-PublicHttps.ps1 -BaseUrl https://envirapido.com -RequireSuccess
.\tools\Verify-PublicHttps.ps1 -BaseUrl https://pilotogoapp.zapflow77.dpdns.org -RequireSuccess
```

Deben conservar host y HTTPS al redirigir a login, emitir cookies nuevas Secure
y HttpOnly y devolver 401 JSON sin redirección en las API protegidas anónimas.
Estos controles no acreditan acceso autenticado, SQL ni pedidos.

Después usar cuentas dedicadas autorizadas de la misma empresa para actualizar
el APK conservando datos, restaurar/renovar sesión y recibir una asignación manual
y dos automáticas consecutivas con otra activa, sin relogin. Confirmar B al
recoger y únicamente C al finalizar cada parada; verificar rutas y separación
entre empresas, preservando operaciones reales. No generar cobros ni pagos
reales para probar conectividad. Cerrar únicamente sesiones temporales propias.

La comprobación anónima inicial del 16/11 detectó DNS existente para el dominio
nuevo, raíz HTTP 522 y API HTTP 200 con HTML, sin los atributos de cookies
esperados. El dominio anterior aprobó los controles anónimos. Esta observación
es previa al apuntado final: **no compartir el APK nuevo hasta completar el
apuntado y la validación autenticada**. Evidencia local fuera de Git:
`artifacts/domain-envirapido-20261116/*-https-before.json`.

La preparación local aprobó 99/99 pruebas Flutter y `flutter analyze` sin
incidencias. Incluye seis regresiones nuevas de restauración/renovación de sesión,
acceso recordado, aislamiento de QA/cuentas/tiendas, recuperación idempotente de
creaciones y confirmaciones C, y continuidad de dispositivo/secuencia/lotes GPS.
Son pruebas con HTTP y almacenamiento nativo simulados; no equivalen a un
recorrido autenticado del dominio ni a una actualización física del APK.
La ejecución completa se hizo secuencial: el test GPS preexistente con permiso
de un segundo depende del reloj real y falló inicialmente bajo carga paralela;
aprobó aislado y en la ejecución completa secuencial, sin cambiar su política.
Los resultados y diagnósticos permanecen en el directorio local ignorado
`pilotGOapp/build/domain-envirapido-20261116/`.

## Reversión

Conservar el APK previo y el hostname anterior, junto con el mismo backend,
base de datos y claves. Si el dominio nuevo falla, reparar DNS/proxy antes de
distribuirlo. Si ya se distribuyó, una nueva actualización requiere versionCode
superior; no depender de instalar una versión inferior ni de borrar datos.
