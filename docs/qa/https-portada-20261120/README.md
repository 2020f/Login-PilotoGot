# QA local y punto previo al despliegue HTTPS

Entrega del 20/11/2026 sobre `59cd1525b7ef6666a801ad48ceeff1b57aa7dff2`.

## Resultado local: CI_LOCAL

- **33/33** pruebas Node aprobadas: 18 del instalador y 15 del verificador.
- **8/8** pruebas PowerShell existentes de la política de aceptación HTTPS aprobadas.
- `node --check` y `--help` de las dos herramientas aprobados.
- Git diff sin errores de espacios.

El proveedor Cloudflare se simuló en las pruebas locales. Se ejercitaron alta y PATCH de una regla, conservación de reglas ajenas y orden relativo, repetición sin duplicados, versión concurrente, resultado incierto sin reintento, backup fuera de Git, zona/DNS/alcance inválidos, origen API fijo y rechazo de redirecciones del proveedor. El verificador se comprobó con errores de red, bucles, destinos ajenos, HTTP200, pérdida de consulta, métodos no preservados y cookies sin sus atributos. Los fixtures no contienen credenciales reales.

No se ejecutó una escritura real contra Cloudflare ni se presenta este resultado como un PASS remoto de GitHub Actions. La guía de despliegue y las herramientas deben ejecutarse en el borde real con el acceso privado del operador.

## Verificación pública anterior a aplicar la regla

Se ejecutaron una vez 25 sondas anónimas: **14 aprobadas y 11 pendientes**, salida 1. El [resumen conservado](public-before-summary.json) identifica los casos observados; es un resumen del resultado real, no el recibo completo de las 25 sondas. No se reconstruyó un recibo completo ni se repitió el tráfico para aparentarlo.

- Portada y portal comprador continúan sirviendo páginas por HTTP; la ruta codificada devuelve 404. Sigue faltando la regla del borde.
- Tres sondas HTTP del apex —login, API y POST vacío a ruta ficticia— devuelven 307. Conservan el método y ya redirigen a HTTPS, pero la nueva condición de despliegue exige 308 de Cloudflare, por lo que aparecen como pendientes de ese código exacto.
- HTTPS de portada/acceso, login/cookies anónimas Secure/HttpOnly, API 401 JSON y comportamiento del dominio histórico se conservaron.
- Sin credenciales, seguimiento de redirecciones ni creación de cuentas/pedidos/cobros/pagos. El único POST fue vacío a una ruta inexistente de prueba.

El agente de despliegue debe guardar un **recibo completo nuevo** del verificador después de aplicar la regla, con salida 0 y 25/25 aprobadas. Este snapshot previo no acredita que la redirección ya esté desplegada.

## Alcance preservado

No se modificaron fuentes de `Login`, API comercial, contratos, Next o Flutter; tampoco migraciones, autenticación, tenants, datos operativos, firma o bytes del APK. Las únicas altas/cambios del repositorio son herramientas, sus pruebas y documentación. La validación pública autenticada anterior del APK 2.1.1 (11) pertenece al lote previo, no se vuelve a declarar como una ejecución de esta entrega.

Guía para el operador: [DESPLEGAR-HTTPS-PORTADA.md](../../../DESPLEGAR-HTTPS-PORTADA.md).
