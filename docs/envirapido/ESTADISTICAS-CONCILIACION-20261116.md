# Estadísticas propias de conciliación

Fecha: 16 de noviembre de 2026. Base: `85024bfcfed3fecb5c826b55030cdd7e2f970395`,
rama `codex/confiabilidad-panel-app`.

## Problema y resultado

La pestaña Conciliación reutilizaba las tarjetas de liquidaciones: neto, revisión,
aprobado, programado y pagado. Un cobro conciliado y recibido de RD$200 podía
aparecer correctamente en su tabla mientras esas tarjetas seguían en cero,
porque recibir efectivo no genera una liquidación.

Ahora esa pestaña muestra cinco indicadores de los propios cobros:

| Indicador | Cálculo |
| --- | --- |
| Cobros registrados | Suma del importe declarado y cantidad de cobros. |
| Importe verificado | Importes con conciliación e importe verificado; muestra también cuántos faltan por conciliar. |
| Recibido en empresa | Importe efectivamente registrado como recibido por la empresa. |
| Pendiente de recibir | Importes ya verificados cuya recepción todavía no se registró. No incluye cobros sin conciliar. |
| Diferencias detectadas | Suma de diferencias absolutas entre declarado y verificado, con cantidad de cobros afectados. Faltantes y sobrantes no se cancelan. |

Para un único cobro de RD$200 declarado, verificado y recibido, el resultado es
**200 / 200 / 200 / 0 / 0**. El dinero recibido no se presenta como ganancia ni
convierte una liquidación en pagada. Las demás pestañas conservan sus indicadores.

## Alcance y fechas

El resumen se calcula con una consulta completa por empresa y, cuando el actor
es piloto, exclusivamente sobre sus cobros. No suma las filas de la tabla, que
pueden estar limitadas o paginadas. Sólo proyecta importes y estado de verificación/
recepción, sin datos personales. Se mantiene precisión decimal en SQL Server y
SQLite mediante el mismo patrón de suma utilizado por los indicadores financieros.

El período usa la **fecha de creación del cobro**, igual que la tabla existente,
con inicio de día RD y hasta exclusivo del día siguiente. No filtra por fecha de
recepción física; la vista explica ese criterio. No se cambia el significado
del filtro ni se modifican registros para hacer coincidir los totales.

`IndicadoresOperativos` incorpora `CobrosPeriodo` al final como dato opcional.
El refresco de fragmentos existente ya observa los indicadores y actualiza las
tarjetas; no requiere otro mecanismo de polling, migración SQL ni APK nuevo.

## Validación

Las **7 pruebas nuevas pasaron con SQL Server y .NET/ASP.NET Core 10.0.15**.
Cubren el ciclo registrado→conciliado→recibido de RD$200 sin crear liquidaciones,
refresco de fragmentos, casos vacíos, importes sin verificar, diferencias
absolutas, precisión decimal, separación de empresas/pilotos, límites del día
RD y 2,005 cobros aunque la tabla muestre sólo 2,000.

La suite completa pasó **289/289**, sin fallos ni omitidas, incluyendo las 7
pruebas nuevas y las regresiones anteriores de pedidos, rutas y multitenant. Se registra en
`artifacts/conciliation-stats-20261116/conciliation-full-sql.trx`.
La compilación Release del backend y herramienta QA pasó sin errores; conserva
advertencias anteriores. La revisión visual en un host efímero con datos
ficticios de RD$500 mostró las tres primeras tarjetas en 500 y las restantes
en cero. Se detuvo el host y se eliminó únicamente su base GUID con guardas.

Evidencia: `artifacts/conciliation-stats-20261116/`, incluidos logs, TRX,
`visual-environment.json`, `visual-cleanup.json` y `conciliation-cash-cards.png`.
No se registraron ni editaron cobros, recepciones o liquidaciones productivas.
Las pruebas SQL y el host visual utilizaron bases nuevas con cuentas ficticias.

## Publicación

Publicación en GitHub autorizada el 16/11/2026 en la rama
`codex/confiabilidad-panel-app`. El otro agente debe desplegar el commit exacto
de este lote; el push no acredita una actualización del servidor público.

Desplegar el backend completo conservando configuración, conexión SQL y claves
actuales. No ejecutar seeds ni generar una migración por esta mejora. Después
comprobar la pestaña Conciliación y sus filtros con los cobros ya existentes,
sin volver a registrar recepciones para probar las estadísticas. Para un único
cobro declarado, verificado y recibido de RD$200, las tarjetas deben mostrar
200 / 200 / 200 / 0 / 0 en ese período. Verificar que las otras pestañas conservan
sus totales de liquidaciones y que no se modifica el cobro, su recepción o su
historial. El APK actual sigue siendo compatible.
