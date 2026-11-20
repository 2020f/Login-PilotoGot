# HTTPS, proxy confiable y cookies de Envirapido

Fecha: 12 de noviembre de 2026. Guía para el agente que despliega el backend.
La publicación de este cambio en GitHub no acredita su instalación en el servidor.

> Actualización del 17 de noviembre: para la redirección HTTP a HTTPS y su
> comprobación, seguir [HTTPS-METRICAS-20261117.md](HTTPS-METRICAS-20261117.md).
> Ese lote agrega `UseHttpsRedirection`; el resto de esta guía conserva el
> contexto y las reglas de confianza del cambio del 12 de noviembre.

## Qué corrige

La validación pública detectó una redirección de la raíz HTTPS hacia login HTTP y
cookies nuevas de Identity y antiforgery sin `Secure`. El cambio procesa
`X-Forwarded-Proto` antes de autenticación y exige cookies seguras fuera de
Development. Mantiene la URL pública
`https://pilotogoapp.zapflow77.dpdns.org`, el contrato móvil, JWT, reglas de
órdenes, permisos y multitenant. No requiere una migración SQL ni un APK nuevo.

La aplicación acepta únicamente el protocolo reenviado por el proxy inmediato
confiable, con `ForwardLimit=1`. Los valores anteriores de una cadena no conceden
confianza adicional. No interpreta `X-Forwarded-Host`, `X-Forwarded-For` ni
`X-Forwarded-Prefix` en esta configuración. El límite por IP conserva su
comportamiento anterior; este lote no introduce una política nueva de IP de cliente.

## Preparación antes del reinicio

1. Identificar el proceso y la IP del **proxy inmediato que ve Kestrel como
   `RemoteIpAddress`**, incluidos contenedores, gateway o túnel. La topología del
   servidor público no se ha inspeccionado desde este repositorio. Las direcciones
   de clientes o rangos públicos de Cloudflare no sustituyen esa comprobación.
2. Si ese proxy entra realmente por `127.0.0.1` o `::1`, ya está autorizado por
   defecto. Para cualquier otra dirección, agregar cada IP exacta en la
   configuración privada del servicio **antes de iniciar la nueva versión**.
   No se aceptan CIDR, comodines, nombres DNS ni listas separadas por comas.
3. Comprobar que el proxy entrega `X-Forwarded-Proto: https` para una solicitud
   pública HTTPS y conserva el host público. El componente de confianza debe
   sobrescribir/sanear el valor; no copiar sin validar una cabecera que envió
   directamente el visitante.
4. Conservar secretos y configuración de conexión, clave JWT y claves persistentes
   de Data Protection. No regenerar claves para resolver este problema.

Ejemplo conceptual para variables de entorno del servicio; sustituir el marcador
por la IP comprobada. No copiar literalmente el marcador:

```text
ReverseProxy__KnownProxies__0=<IP_EXACTA_DEL_PROXY_INMEDIATO>
```

Si existe más de un proxy inmediato legítimo, usar los índices `__1`, `__2`, etc.
Una dirección IPv4 también contempla su representación IPv4 mapeada a IPv6.
La configuración inválida impide arrancar para evitar una ampliación accidental
de confianza. La lista siempre conserva los dos loopbacks exactos.

No habilitar `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, no vaciar las listas de
confianza como solución y no autorizar una subred completa para evitar averiguar
la IP. Microsoft explica el procesamiento y los límites de confianza en
[proxy y balanceador de ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

## Terminación TLS y varios saltos

Cuando Nginx termina directamente TLS, su protocolo observado puede representar
el protocolo público. Cuando Cloudflare/cloudflared termina TLS antes de Nginx,
el enlace posterior puede ser HTTP: establecer sin revisar
`X-Forwarded-Proto $scheme` en ese salto perdería nuevamente el HTTPS original.
Configurar la cadena según la topología real para que el último proxy autorizado
entregue un único valor del protocolo público ya validado. No confiar en toda la
cadena ni ampliar `ForwardLimit` para ocultar esa pérdida.

Restringir Kestrel al enlace interno previsto y mantener la política de HTTP a
HTTPS en el borde público. Este cambio no añade `UseHttpsRedirection`; evita
introducir redirecciones de puerto o bucles basados en una topología supuesta.

`SecurePolicy.Always` de antiforgery requiere que el esquema efectivo sea HTTPS.
Si login devuelve 500 tras instalar el cambio, comprobar primero la IP confiable
y el protocolo reenviado. No desactivar `Secure` ni CSRF para hacer desaparecer
el error. El arnés local de QA que utiliza HTTP tiene una excepción explícita y
aislada; no utilizar su configuración para producción. Véase
[protección antiforgery de ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Comprobaciones de cierre en el servidor público

El comprobador anónimo no sigue redirecciones ni registra valores de cookies:

```powershell
.\tools\Verify-PublicHttps.ps1 -RequireSuccess
```

Comprueba raíz, login, atributos de cookies nuevas y API anónima. Devuelve un
error si todavía falla alguno de esos controles. Su aprobación no sustituye el
login autenticado ni el recorrido de pedidos del APK indicado a continuación.

- La raíz y el acceso conservan HTTPS durante todo el recorrido, sin bucles ni
  saltos a HTTP. Inspeccionar `Location` con seguimiento automático desactivado.
- Iniciar una sesión nueva autorizada: cookies Identity y antiforgery con
  `Secure` y `HttpOnly`; login, navegación, CSRF y logout funcionan. Una cookie
  anterior no cambia retroactivamente sus atributos: revisar las recién emitidas.
- API anónima devuelve 401 JSON; rol incorrecto, 403 JSON. No debe devolver
  HTML de login ni una redirección. Comprobar separación de roles y empresa.
- Con el mismo APK release y la URL pública, verificar restauración de sesión,
  asignación manual y dos automáticas consecutivas teniendo otra orden activa,
  sin cerrar sesión. Preservar órdenes ajenas y rutas existentes; B en recogida
  y únicamente C al terminar la parada. Usar destinatarios ficticios autorizados.
- Registrar commit/artefacto desplegado, hora UTC, resultados y límites de la
  validación. No publicar tokens, cookies, contraseñas ni datos reales en recibos.

## Reversión

Conservar binario y configuración previos al corte. Si hace falta revertir,
restaurarlos de forma coordinada manteniendo base de datos y claves Data
Protection/JWT; este cambio no modifica el esquema ni requiere revertir SQL.
No borrar sesiones, órdenes o historial. La reversión al binario anterior puede
restaurar el defecto de transporte: no comunicarla como solución de seguridad y
mantener el cierre pendiente hasta verificar nuevamente HTTPS y cookies.
