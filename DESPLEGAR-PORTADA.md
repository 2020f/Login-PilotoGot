# Actualización de portada interactiva · Envirapido

Entrega aprobada por el propietario el 17/11/2026. Esta actualización cambia
**únicamente la página de inicio comercial y sus componentes exclusivos**.
Los demás cambios de este commit son documentación y dos pruebas del recorrido
ilustrativo. No contiene cambios de CRM, API comercial, SQL, autenticación,
multitenant, Flutter, APK, firma, dominios ni contratos de pedidos o rutas.

## Versión y condición previa

- Repositorio: `https://github.com/2020f/Login-PilotoGot`.
- Rama: **`codex/confiabilidad-panel-app`**, no `master`.
- Base de esta actualización: **`4e1b2d423ffaea26b91ec330da40b9945148c60d`**.
- Desplegar el **SHA completo comunicado por el propietario**, que incluye este
  documento; verificarlo antes de compilar. No sustituirlo por el último commit
  disponible sin comprobar su contenido.
- Inventariar primero el release y proceso Node actuales, sus variables privadas,
  el origen interno de la API comercial y las rutas del proxy. Preservar cualquier
  checkout con trabajo local; usar una carpeta de release limpia y separada.

**Si el portal de la versión base ya está instalado y operativo, actualizar sólo
su servicio web Next.** No repetir el despliegue completo de los servicios.
Si todavía falta la instalación inicial del portal, completar primero los
prerrequisitos de [DESPLEGAR-PORTAL.md](DESPLEGAR-PORTAL.md), incluidos SQL separado
y SMTP, mediante el procedimiento ya autorizado para esa entrega. La portada no
sustituye esos servicios ni acredita que estén desplegados.

## Alcance exacto

| Componente | Acción en esta actualización |
| --- | --- |
| `portal-web/app/page.tsx` | Nueva composición de la portada; conserva `force-dynamic`, `loadCatalog` y `Plans` |
| `home-hero`, `home-demo`, `home-role-explorer`, `home-reveal` y sus CSS Modules | Componentes exclusivos de Inicio; no escriben datos ni llaman APIs de pedidos |
| `portal-web/app/home.module.css` | Estilos y movimiento limitados a la portada |
| Otras vistas y estilos compartidos | Conservar sin cambios de fuente ni configuración |
| CRM y `Envirapido.Portal.Api` | Mantener binarios, procesos, variables, claves y almacenamiento actuales |
| SQL CRM y SQL portal | **Ninguna migración, seed, limpieza ni actualización de datos** |
| App móvil y APK | No recompilar, reinstalar ni volver a distribuir por este cambio |
| Proxy, DNS, TLS y cookies | Conservar el enrutamiento y las protecciones existentes |

Las escenas de pedidos, rutas y equipos están identificadas como **ilustrativas**.
No muestran telemetría productiva ni crean órdenes. El catálogo sigue viniendo de
la API comercial configurada; no se añadieron precios ni planes de sustitución.
Las imágenes y logotipos aprobados se reutilizan sin cambios. No hay dependencias
nuevas ni cambios de `package.json` o lockfile.

## Compilación en el servidor

Conservar la versión Node compatible ya empleada por el portal. El requisito del
proyecto es Node `>=22.18.0`; la QA local utilizó Node `26.12.1`. Compilar en el
sistema de destino; no copiar `node_modules` ni `.next` desde Windows a Linux.

Ejemplo para preparar un checkout separado, desde el clon del repositorio.
El operador debe definir `RELEASE_SHA` con el SHA entregado y `RELEASE_SRC` con una
ruta absoluta nueva, fuera del directorio en servicio:

```bash
set -euo pipefail
test "${#RELEASE_SHA}" -eq 40
test ! -e "$RELEASE_SRC"
git fetch origin codex/confiabilidad-panel-app
git worktree add --detach "$RELEASE_SRC" "$RELEASE_SHA"
test "$(git -C "$RELEASE_SRC" rev-parse HEAD)" = "$RELEASE_SHA"
cd "$RELEASE_SRC/portal-web"
```

Fijar estas variables **antes del build** y conservar el mismo origen interno de
API al ejecutar el servicio:

```text
NEXT_PUBLIC_SITE_ORIGIN=https://envirapido.com
NEXT_PUBLIC_PORTAL_ORIGIN=https://portal.envirapido.com
PORTAL_API_ORIGIN=http://127.0.0.1:5031
```

`5031` es un ejemplo: usar el puerto privado real ya configurado para la API
comercial. HTTP sólo se admite en loopback; entre máquinas usar HTTPS validado.
No copiar los orígenes `http://127.0.0.1:3000` de la QA local al build productivo.
No colocar claves internas, conexiones SQL ni credenciales en `NEXT_PUBLIC_*`.

```bash
npm ci --no-audit --no-fund
npm run typecheck
npm test
npm run build
test -f .next/standalone/server.js
test -d .next/standalone/public
test -d .next/standalone/.next/static
```

El build ya copia imágenes y chunks al bundle. Publicar **todo**
`.next/standalone` en una carpeta nueva del release, manteniendo juntos
`server.js`, `public` y `.next/static`. No mezclar chunks de dos compilaciones.
Conservar el release anterior y sus assets para la reversión y las pestañas que
todavía lo estén utilizando.

Cambiar de forma controlada el release del **servicio Node del portal** con el
gestor de procesos existente. Conservar usuario, variables, puerto privado y
escucha loopback; ejecutar `node server.js` desde el bundle con
`NODE_ENV=production`. Desde un checkout completo también existe `npm start`.
No usar `next start` para esta salida standalone.

No reiniciar CRM, API comercial ni SQL por esta actualización. No cambiar reglas
de `/api/*`, `/Identity/*`, SignalR, controladores operativos, descargas ni del
dominio anterior. No eliminar cookies, sesiones o claves de Data Protection.

## Comprobación después del despliegue

1. Confirmar el SHA publicado y comprobar `https://envirapido.com/`: título
   **«Tu última milla. En movimiento.»**, imagen y controles sin errores de chunks,
   hidratación o consola. No deben aparecer planes QA en producción.
2. Probar Simple y Varios destinos, las cinco etapas y reproducir/pausar/repetir.
   La reproducción se detiene al finalizar y se pausa fuera de vista o con la
   pestaña oculta. Con movimiento reducido se exploran las etapas manualmente.
3. Probar Comercio / Operaciones / Piloto con clic y teclado (flechas, Home y
   End). Sólo un panel es visible; Tab y los atajos del navegador se conservan.
4. Revisar desplazamiento, tarjetas, preguntas desplegables y enlaces. Comprobar
   320, 390, 900 y 1440 px, sin desbordamiento horizontal ni contenido inaccesible.
5. Abrir `/funciones`, `/planes`, `/contacto`, acceso, registro y cuenta en sus
   hosts correctos. Su diseño y comportamiento deben conservarse. Los planes y
   solicitudes siguen dependiendo del catálogo y permisos reales de la API.
6. Comprobar HTTPS y cookies Secure existentes, login CRM y una sesión móvil
   autorizada. Las rutas operativas/API deben seguir respondiendo desde el CRM,
   sin HTML comercial en su lugar. No hace falta crear pedidos para comprobar la
   demo; cualquier QA productiva que cree datos requiere el alcance autorizado.

No anunciar el despliegue como validado hasta comprobarlo en el servidor público.
El push a GitHub y la QA local no son evidencia de publicación productiva.

## QA de esta entrega

- `npm test`: **23/23**, incluidos dos casos del recorrido ilustrativo y la suite
  existente de transporte, permisos, fechas y empaquetado.
- Typecheck y build standalone: aprobados.
- Navegador local: controles de demo, reproducción hasta Entregado, modos,
  pestañas por clic/teclado, preguntas desplegables y tamaños 320/390/900/1440.
- Sin errores de consola observados. Estilos compartidos, otras vistas, APIs y
  app Flutter contrastados sin cambios contra la base.
- Movimiento reducido y limpieza de efectos revisados en código; no se cambiaron
  preferencias del sistema operativo. Las comprobaciones SQL y del emulador de
  la entrega base no se repitieron para este cambio de portada.
- La vista previa local usa el catálogo ficticio del arnés SQL QA. No publicar el
  arnés, sus datos, endpoints `/qa/*`, archivos de evidencia o entorno local.

## Reversión

Restaurar únicamente el bundle Next anterior, con sus variables y puerto, mediante
el mismo gestor de procesos. No revertir bases, claves, sesiones, binarios CRM,
API comercial ni APK. Si la instalación inicial del portal seguía pendiente,
tratar su despliegue y reversión por separado con la guía original.
