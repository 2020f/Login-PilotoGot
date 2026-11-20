#!/usr/bin/env node
// Node >=22.18, no packages. Default: plan/read only. Never writes CRM/DNS/SQL/APK.
// Windows operators must provision backup-dir with a private ACL; POSIX modes
// are enforced on Unix. The backup contains configuration, never the API token.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { randomUUID } from 'node:crypto';

const ZONE = 'envirapido.com';
const HOSTS = [ZONE, 'portal.envirapido.com'];
const PHASE = 'http_request_dynamic_redirect';
const REF = 'envirapido_http_https_20261119';
const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const RULE_FILE = path.join(REPO, 'docs/portal/https-redirect.cloudflare.rule.json');
const API_ORIGIN = 'https://api.cloudflare.com/client/v4';
const FIELDS = ['ref', 'description', 'expression', 'action', 'action_parameters', 'enabled'];

export class CFError extends Error {}

function identifier(value) {
  if (typeof value !== 'string' || !/^[0-9a-f]{32}$/.test(value)) {
    throw new CFError('Identificador Cloudflare invalido: requiere 32 caracteres hexadecimales.');
  }
  return value;
}
function canonical(value) {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']';
  if (value && typeof value === 'object') {
    return '{' + Object.keys(value).sort().map(k => JSON.stringify(k) + ':' + canonical(value[k])).join(',') + '}';
  }
  return JSON.stringify(value);
}
const equal = (a, b) => canonical(a) === canonical(b);

export class CloudflareApi {
  #token;
  constructor(token) {
    if (typeof token !== 'string' || !token || /\s/.test(token)) {
      throw new CFError('Configura CLOUDFLARE_API_TOKEN en el entorno privado.');
    }
    this.#token = token;
  }
  async request(method, pathname, payload = undefined, allow_missing = false) {
    if (!['GET', 'POST', 'PATCH'].includes(method) || !pathname.startsWith('/zones/')
        || /[\\\r\n#]/.test(pathname) || pathname.includes('..')) {
      throw new CFError('Operacion fuera del alcance permitido.');
    }
    if (allow_missing && (method !== 'GET' || !pathname.endsWith(`/${PHASE}/entrypoint`))) {
      throw new CFError('Solo el entrypoint de redireccion puede estar ausente.');
    }
    let response;
    try {
      response = await fetch(API_ORIGIN + pathname, {
        method, redirect: 'manual', signal: AbortSignal.timeout(30_000),
        headers: { Authorization: 'Bearer ' + this.#token, Accept: 'application/json',
          'Content-Type': 'application/json', 'User-Agent': 'Envirapido-Https-Installer/1' },
        body: payload === undefined ? undefined : JSON.stringify(payload)
      });
    } catch {
      throw new CFError('Respuesta Cloudflare incierta. No se reintenta automaticamente; consulta el plan antes de repetir.');
    }
    if (response.status === 404 && allow_missing) return null;
    if (!response.ok) {
      throw new CFError(`Cloudflare HTTP ${response.status}. No se reintenta automaticamente; consulta el plan antes de repetir.`);
    }
    let body;
    try {
      const text = await response.text();
      if (Buffer.byteLength(text, 'utf8') > 4 * 1024 * 1024) throw new Error('large');
      body = JSON.parse(text);
    } catch {
      throw new CFError('Respuesta Cloudflare invalida; inspecciona el estado antes de repetir.');
    }
    if (!body || body.success !== true || body.result == null) {
      throw new CFError('Cloudflare no confirmo la operacion; inspecciona el estado antes de repetir.');
    }
    return body.result;
  }
}

export function loadRule() {
  const rule = JSON.parse(fs.readFileSync(RULE_FILE, 'utf8').replace(/^\uFEFF/, ''));
  const parameters = { from_value: { status_code: 308,
    target_url: { expression: 'concat("https://", http.host, raw.http.request.uri.path)' },
    preserve_query_string: true } };
  if (!equal(Object.keys(rule).sort(), [...FIELDS].sort()) || rule.ref !== REF || rule.action !== 'redirect'
      || rule.enabled !== true || rule.expression !== '(http.host in {"envirapido.com" "portal.envirapido.com"} and not ssl)'
      || !equal(rule.action_parameters, parameters)) {
    throw new CFError('La regla versionada no coincide con los hosts, TLS y redireccion 308 esperados.');
  }
  return rule;
}

const definition = rule => Object.fromEntries(FIELDS.map(k => [k, rule[k] ?? null]));
function validateEntrypoint(entrypoint) {
  if (entrypoint === null) return;
  if (!entrypoint || entrypoint.kind !== 'zone' || entrypoint.phase !== PHASE
      || !Array.isArray(entrypoint.rules) || typeof entrypoint.version !== 'string' || !entrypoint.version) {
    throw new CFError('El entrypoint no corresponde a la fase y tipo esperados.');
  }
  identifier(entrypoint.id);
  const ids = entrypoint.rules.map(rule => identifier(rule?.id));
  if (new Set(ids).size !== ids.length) throw new CFError('Reglas Cloudflare ambiguas o invalidas.');
}
export function buildPlan(entrypoint, rule) {
  validateEntrypoint(entrypoint);
  if (entrypoint === null) return { action: 'create_phase', ruleset_id: null, rule_id: null };
  const matches = entrypoint.rules.map((current, index) => ({ current, index })).filter(x => x.current.ref === REF);
  if (matches.length > 1) throw new CFError('Hay mas de una regla con la referencia de Envirapido; no se modifica ninguna.');
  if (!matches.length) return { action: 'add_rule', ruleset_id: entrypoint.id, rule_id: null };
  const { current, index } = matches[0];
  if (current.action !== 'redirect') throw new CFError('La referencia de Envirapido pertenece a otro tipo de regla; requiere revision.');
  return { action: index === 0 && equal(definition(current), rule) ? 'none' : 'update_rule',
    ruleset_id: entrypoint.id, rule_id: current.id };
}
function otherRules(entrypoint) {
  return (entrypoint?.rules ?? []).filter(rule => rule.ref !== REF).map(rule =>
    Object.fromEntries(Object.entries(rule).filter(([key]) => !['version', 'last_updated'].includes(key))));
}
function outsideGit(directory) {
  const relative = path.relative(REPO, directory);
  if (!relative || (!relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative))) {
    throw new CFError('El respaldo debe quedar fuera de cualquier checkout Git.');
  }
  for (let cursor = directory;; cursor = path.dirname(cursor)) {
    if (fs.existsSync(path.join(cursor, '.git'))) throw new CFError('El respaldo debe quedar fuera de cualquier checkout Git.');
    if (path.dirname(cursor) === cursor) break;
  }
}
function backup(directory, zoneId, before, plan) {
  if (!directory || !path.isAbsolute(directory)) throw new CFError('--backup-dir absoluto y privado es obligatorio antes de modificar Cloudflare.');
  let target = path.resolve(directory);
  outsideGit(target);
  fs.mkdirSync(target, { recursive: true, mode: 0o700 });
  target = fs.realpathSync(target);
  outsideGit(target);
  if (process.platform !== 'win32' && (fs.statSync(target).mode & 0o077)) {
    throw new CFError('El directorio de respaldo debe tener permisos 0700.');
  }
  const stamp = new Date().toISOString().replace(/[-:.]/g, '');
  const output = path.join(target, `envirapido-https-${stamp}-${randomUUID()}.json`);
  const snapshot = { schema: 1, zone_id: zoneId, zone_name: ZONE, phase: PHASE, ref: REF,
    captured_at_utc: new Date().toISOString(), before, plan };
  const fd = fs.openSync(output, 'wx', 0o600);
  try {
    fs.writeFileSync(fd, JSON.stringify(snapshot, null, 2), 'utf8');
    fs.fsyncSync(fd);
  } finally { fs.closeSync(fd); }
  return output;
}

// api.request may be a fake in tests. Real requests always use the fixed HTTPS API.
export async function runInstall(api, zoneId, { apply = false, backupDir, rule } = {}) {
  zoneId = identifier(zoneId);
  const desired = loadRule();
  if (rule !== undefined && !equal(rule, desired)) throw new CFError('No se permite sustituir la regla versionada.');
  rule = desired;
  const prefix = '/zones/' + zoneId;
  const zone = await api.request('GET', prefix);
  if (!zone || zone.id !== zoneId || zone.name !== ZONE || zone.status !== 'active') {
    throw new CFError('La zona debe ser envirapido.com y estar activa.');
  }
  for (const host of HOSTS) {
    const records = await api.request('GET', prefix + '/dns_records?' + new URLSearchParams({ name: host, per_page: '100' }));
    if (!Array.isArray(records) || !records.length || records.length >= 100) throw new CFError('No se puede confirmar el DNS exacto de los dos hosts.');
    const addresses = records.filter(r => ['A', 'AAAA', 'CNAME'].includes(r?.type));
    if (!addresses.length || addresses.some(r => r.name !== host || r.proxied !== true)) {
      throw new CFError('Ambos hosts deben existir y estar proxificados; este instalador no cambia DNS.');
    }
  }
  const endpoint = prefix + '/rulesets/phases/' + PHASE + '/entrypoint';
  const before = structuredClone(await api.request('GET', endpoint, undefined, true));
  const plan = buildPlan(before, rule);
  const result = { zone: ZONE, hosts: HOSTS, phase: PHASE, ref: REF, ...plan,
    ruleset_version: before?.version ?? null,
    mode: apply ? 'apply' : 'plan', changed: false, other_rules_count: otherRules(before).length,
    public_verified: false };
  if (!apply || plan.action === 'none') return result;
  const current = await api.request('GET', endpoint, undefined, true);
  if (!equal(current, before)) throw new CFError('Las reglas cambiaron durante el plan; vuelve a consultar antes de aplicar.');
  const backupFile = backup(backupDir, zoneId, before, plan);
  let payload = structuredClone(rule);
  if (plan.action === 'create_phase') {
    payload = { name: 'Envirapido HTTPS redirects', kind: 'zone', phase: PHASE, rules: [payload] };
    await api.request('POST', prefix + '/rulesets', payload);
  } else {
    payload.position = { before: '' };
    let pathname = prefix + '/rulesets/' + plan.ruleset_id + '/rules';
    if (plan.action === 'update_rule') pathname += '/' + plan.rule_id;
    await api.request(plan.action === 'update_rule' ? 'PATCH' : 'POST', pathname, payload);
  }
  const after = await api.request('GET', endpoint, undefined, true);
  const checked = buildPlan(after, rule);
  if (checked.action !== 'none' || (before !== null && after.id !== before.id) || !equal(otherRules(after), otherRules(before))) {
    throw new CFError('La comprobacion posterior no coincide. Conserva el respaldo e inspecciona el estado; no reapliques a ciegas.');
  }
  return { ...result, changed: true, backup_file: backupFile, ruleset_id: after.id, rule_id: checked.rule_id,
    ruleset_version: after.version, other_rules_preserved: true };
}

export async function main(argv = process.argv.slice(2)) {
  let apply = false;
  let backupDir;
  try {
    for (let i = 0; i < argv.length; i++) {
      if (argv[i] === '--apply') apply = true;
      else if (argv[i] === '--backup-dir' && argv[i + 1]) backupDir = argv[++i];
      else if (argv[i] === '--help') {
        console.log('node tools/Apply-PortalHttps.mjs [--apply --backup-dir DIRECTORIO_ABSOLUTO_PRIVADO]\nSin --apply solo consulta. Variables privadas: CLOUDFLARE_API_TOKEN y CLOUDFLARE_ZONE_ID.');
        return 0;
      } else throw new CFError('Argumento invalido; usa --help.');
    }
    const api = new CloudflareApi(process.env.CLOUDFLARE_API_TOKEN);
    console.log(JSON.stringify(await runInstall(api, process.env.CLOUDFLARE_ZONE_ID, { apply, backupDir }), null, 2));
    return 0;
  } catch (error) {
    console.error(JSON.stringify({ error: error instanceof CFError ? error.message
      : 'No se pudo preparar la operacion; revisa configuracion y respaldo privados.', public_verified: false }));
    return 1;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = await main();
}
