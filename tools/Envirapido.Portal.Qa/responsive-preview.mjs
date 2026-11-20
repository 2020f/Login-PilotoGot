// QA local y sólo lectura. Alternativa cuando el viewport del navegador no cambia.
// No se incorpora al producto ni sustituye pruebas de headers de producción.
import http from 'node:http';
const pages = new Set(['/', '/acceso', '/planes', '/cuenta/descargas']);
const server = http.createServer(async (request, response) => {
  if (request.method !== 'GET' || request.headers.host !== '127.0.0.1:5089') {
    response.writeHead(403); response.end(); return;
  }
  const url = new URL(request.url, 'http://127.0.0.1:5089');
  if (url.pathname === '/__qa_responsive') {
    const page = url.searchParams.get('page') || '/';
    if (!pages.has(page)) { response.writeHead(400); response.end(); return; }
    response.writeHead(200, { 'Content-Type':'text/html; charset=utf-8', 'Cache-Control':'no-store' });
    response.end(`<!doctype html><meta charset="utf-8"><title>QA responsive 390 px</title><style>body{margin:0;background:#e9efed;font:14px Arial;color:#173d42}header{padding:12px}iframe{display:block;width:390px;height:844px;border:1px solid #aac4bf;margin:0 12px 12px;background:white}</style><header>QA local · ancho real 390 px · sólo lectura</header><iframe title="Portal a 390 píxeles" src="${page}"></iframe>`);
    return;
  }
  const permitted = pages.has(url.pathname) || /^\/(?:_next|branding|illustrations)\//.test(url.pathname)
    || /^\/portal-api\/v1\/(?:account|catalog|subscription|requests|csrf|auth\/me|downloads\/current)$/.test(url.pathname);
  if (!permitted) { response.writeHead(404); response.end(); return; }
  try {
    const result = await fetch('http://127.0.0.1:3000' + url.pathname + url.search, {
      headers: { cookie:request.headers.cookie || '', accept:request.headers.accept || '*/*' }, redirect:'manual'
    });
    const headers = {};
    for (const [name, value] of result.headers) {
      // Sólo este fixture permite embeber la UI para comprobar media queries.
      // Next original mantiene DENY y se comprueba fuera de este proxy.
      if (!['x-frame-options','content-security-policy','content-encoding','content-length','transfer-encoding','connection','set-cookie'].includes(name)) headers[name] = value;
    }
    headers['cache-control'] = 'no-store';
    response.writeHead(result.status, headers);
    if (result.body) for await (const chunk of result.body) response.write(chunk);
    response.end();
  } catch { response.writeHead(502); response.end('QA local no disponible'); }
});
server.listen(5089, '127.0.0.1', () => process.stdout.write('QA responsive http://127.0.0.1:5089/__qa_responsive\n'));
process.on('SIGINT', () => server.close(() => process.exit(0)));
