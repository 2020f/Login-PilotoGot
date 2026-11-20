# Entrega del portal a GitHub · 17/11/2026

El propietario autorizó publicar la implementación y dejar las instrucciones en
el propio repositorio para que el agente del servidor realice el despliegue.
El punto de entrada es [DESPLEGAR-PORTAL.md](../../DESPLEGAR-PORTAL.md), enlazado
desde el README y desde CLAUDE.md. Usar el SHA completo de esta entrega, no inferir
que `master` contiene el portal.

## Revisión previa a publicar

- Se preservó el árbol operativo del CRM: sólo se integraron el cliente privado,
  el catálogo de planes y la sección SuperAdmin. No hay nuevas migraciones CRM.
- App Flutter sin cambios; se conserva su API, firma y APK existente.
- El SQL nuevo pertenece a `EnvirapidoPortal`; SMTP, usuarios, cookies, sesiones,
  claves y almacenamiento de APK son independientes de la base operativa.
- El stage excluye claves privadas, APK/AAB, bases, cachés, node_modules, .next,
  recibos y logs QA. Los cuatro PNG de marca/ilustraciones se incluyen.
- Se corrigió una exclusión Git de Windows: `artifacts/` también ocultaba la
  carpeta fuente `Envirapido.Portal.Api/Artifacts`. Sus seis archivos ahora están
  incluidos explícitamente, manteniendo ignorados los datos de ejecución.
- La plantilla CRM usa el nombre real `PortalIntegration__CatalogCurrency`.

## Dos correcciones para que el despliegue funcione

1. El catálogo SSR de Next usa `X-Forwarded-Proto: https` sólo en HTTP loopback,
   al igual que los clientes privados .NET. No sigue redirecciones ni envía cookies
   o claves del navegador. SSR y rewrites rechazan HTTP remoto y orígenes con
   credenciales/rutas/consulta/fragmento. `PORTAL_API_ORIGIN` debe coincidir en build
   y runtime; `NEXT_PUBLIC_*` se fija al compilar para los dominios públicos.
2. El script SQL repite sus guardas antes de cada lote mutante. Un ejecutor que
   continúe después de errores `GO` tampoco puede crear tablas del portal en una
   base con nombre equivocado o con tablas CRM. Las instrucciones exigen igualmente
   conexión explícita `-d EnvirapidoPortal`, `sqlcmd -b` y detenerse si falla.

## Evidencia de validación

Las siguientes pruebas se ejecutaron durante la implementación y se conservaron;
no se presentan como una nueva ejecución ni como CI remoto:

| Grupo retenido | Resultado |
| --- | --- |
| Regresión CRM, pedidos, rutas y multitenant SQL Server | 308/308 |
| Portal SQL Server, cuentas/pagos/vigencias/APK | 46/46 |
| Transporte privado .NET con middleware Production | 7/7 |
| SuperAdmin en Release con SQL Server QA | 8/8 |

Comprobaciones nuevas de esta preparación de entrega:

- **21/21 pruebas frontend**, TypeScript y build standalone aprobados. Incluyen
  servidor HTTP local que exige el header del proxy, rechazo de HTTP remoto y
  pruebas sobre la configuración real de los rewrites.
- **3/3 pruebas de migración SQL Server**, cero fallos u omitidas, sobre bases
  nuevas de QA que se eliminaron al terminar. Incluyen ejecutar el script dos
  veces y continuar deliberadamente tras errores de cada lote, preservando la
  tabla/fila ficticia original y sin crear tablas comerciales en el destino erróneo.
- El arnés local de previsualización compila sin errores. No es un servicio
  productivo ni se publica con el portal.
- El diff no contiene cambios a pedidos, rutas, límites de empresa, tokens móviles
  o código Flutter. Las imágenes conservan su identidad por SHA-256.

Recibos nuevos locales: `artifacts/portal-publicacion-20261117/`.
Recibos anteriores: `artifacts/portal-implementation-20261117/`.
Están ignorados por Git por incluir datos del entorno; el informe versionado
[QA-20261117.md](QA-20261117.md) explica alcance y límites de la evidencia anterior.

## Qué todavía debe comprobar el agente del servidor

Publicar el commit no configura la infraestructura. El agente debe crear la base
comercial y sus permisos exclusivos, completar secretos/SMTP reales, publicar
ambos servicios y el CRM actualizado, configurar TLS del subdominio y preservar
las rutas operativas. Después debe comprobar entrega real de correo, HTTPS,
cookies, altas/activación, descargas y regresión manual/automática multitenant con
fixtures autorizadas. Reportar el SHA efectivamente desplegado y los pendientes.

La vista previa local solicitada por el propietario utiliza únicamente datos QA;
no demuestra que `envirapido.com` esté actualizado ni que SMTP real funcione.
