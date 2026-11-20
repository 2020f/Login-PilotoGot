#!/usr/bin/env node
import { writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

export const PUBLIC_HOSTS = Object.freeze({
  apex: "envirapido.com",
  buyer: "portal.envirapido.com",
  legacy: "pilotogoapp.zapflow77.dpdns.org"
});
const TIMEOUT_MS = 15_000;
const MAX_BODY_BYTES = 65_536;
const PROBE_POST_PATH = "/__envirapido_https_probe__";

export function assertSupportedNode(version = process.versions.node) {
  const parts = /^(\d+)\.(\d+)\.(\d+)(?:[-+].*)?$/.exec(version);
  if (!parts || Number(parts[1]) < 22 || (Number(parts[1]) === 22 && Number(parts[2]) < 18)) {
    throw new Error("Node >=22.18.0 es necesario para este verificador.");
  }
}

export function parseCliArgs(args) {
  if (!Array.isArray(args) || args.some(arg => typeof arg !== "string")) throw new Error("Argumentos no válidos.");
  if (args.length === 1 && args[0] === "--help") return { help: true, receiptPath: null };
  if (args.length === 0) return { help: false, receiptPath: null };
  if (args.length === 2 && args[0] === "--receipt" && args[1].trim() && !args[1].startsWith("-") && !args[1].includes("\0")) {
    return { help: false, receiptPath: args[1] };
  }
  throw new Error("Uso: node tools/Verify-PortalHttps.mjs [--receipt ARCHIVO_NUEVO.json]");
}

function probe(id, host, scheme, path, kind, { method = "GET", spoof = null, statuses = [308] } = {}) {
  const headers = { Accept: kind === "anonymous-api" ? "application/json" : "text/html,application/json;q=0.9" };
  if (spoof) headers["X-Forwarded-Proto"] = spoof;
  return Object.freeze({ id, host, url: `${scheme}://${host}${path}`, kind, method,
    headers: Object.freeze(headers), spoofedForwardedProto: Boolean(spoof), statuses: Object.freeze(statuses) });
}

// These are fixed public probes, never credentials, account creation, orders or
// arbitrary destinations. POST has no body and uses only this nonexistent path.
export function makeProbePlan() {
  const { apex, buyer, legacy } = PUBLIC_HOSTS;
  return Object.freeze([
    probe("apex-http-root", apex, "http", "/", "https-redirect"),
    probe("apex-http-root-query", apex, "http", "/?qa_https_probe=env%2Frapido%20test", "https-redirect"),
    probe("apex-http-plans", apex, "http", "/planes?qa_https_probe=env%2Frapido", "https-redirect"),
    probe("apex-http-encoded-path", apex, "http", "/funciones%2Fqa?qa_https_probe=1", "https-redirect"),
    probe("apex-http-login", apex, "http", "/Identity/Account/Login?ReturnUrl=%2FOperaciones%3Ftab%3Dmonitoreo", "https-redirect"),
    probe("apex-http-api", apex, "http", "/api/piloto/capacidades?qa_https_probe=1", "https-redirect"),
    probe("apex-http-spoof", apex, "http", "/", "https-redirect", { spoof: "https" }),
    probe("apex-http-empty-post", apex, "http", `${PROBE_POST_PATH}?qa_https_probe=empty`, "https-redirect", { method: "POST" }),
    probe("apex-https-root", apex, "https", "/", "marketing"),
    probe("apex-https-spoof", apex, "https", "/", "marketing", { spoof: "http" }),
    probe("apex-https-login", apex, "https", "/Identity/Account/Login", "crm-login"),
    probe("apex-https-api-me", apex, "https", "/api/auth/me", "anonymous-api"),
    probe("apex-https-api-pilot", apex, "https", "/api/piloto/capacidades", "anonymous-api"),
    probe("apex-https-api-shop", apex, "https", "/api/tienda/capacidades", "anonymous-api"),
    probe("buyer-http-root", buyer, "http", "/", "https-redirect"),
    probe("buyer-http-access-query", buyer, "http", "/acceso?qa_https_probe=env%2Frapido%20test", "https-redirect"),
    probe("buyer-http-spoof", buyer, "http", "/", "https-redirect", { spoof: "https" }),
    probe("buyer-https-access", buyer, "https", "/acceso", "buyer-access"),
    probe("legacy-http-root", legacy, "http", "/", "https-redirect", { statuses: [307, 308] }),
    probe("legacy-http-login", legacy, "http", "/Identity/Account/Login?ReturnUrl=%2FOperaciones", "https-redirect", { statuses: [307, 308] }),
    probe("legacy-https-root", legacy, "https", "/", "legacy-root"),
    probe("legacy-https-login", legacy, "https", "/Identity/Account/Login", "crm-login"),
    probe("legacy-https-api-me", legacy, "https", "/api/auth/me", "anonymous-api"),
    probe("legacy-https-api-pilot", legacy, "https", "/api/piloto/capacidades", "anonymous-api"),
    probe("legacy-https-api-shop", legacy, "https", "/api/tienda/capacidades", "anonymous-api")
  ]);
}

export function inspectRedirect({ sourceUrl, status, location, statuses = [308], expectedPath = null }) {
  const source = new URL(sourceUrl);
  let target = null;
  try {
    if (typeof location === "string" && location && !/[\\\u0000-\u0020\u007f]/.test(location)) target = new URL(location, source);
  } catch { /* Invalid Location fails below. */ }
  const checks = {
    expectedStatus: statuses.includes(status),
    hasLocation: Boolean(target),
    httpsSameHost443: Boolean(target && target.protocol === "https:" && target.hostname === source.hostname && target.port === ""),
    noCredentialsOrFragment: Boolean(target && !target.username && !target.password && !target.hash),
    pathPreserved: Boolean(target && target.pathname === (expectedPath ?? source.pathname)),
    queryPreserved: Boolean(target && (expectedPath !== null || target.search === source.search)),
    methodPreserving: expectedPath !== null || status === 307 || status === 308
  };
  return { passed: Object.values(checks).every(Boolean), checks };
}

// Cookie names and values are deliberately absent from reports. Even a failed
// flag check must never print a Set-Cookie header or an authentication token.
export function inspectCookieFlags(lines) {
  if (!Array.isArray(lines) || lines.some(line => typeof line !== "string")) {
    return { count: 0, antiforgeryCount: 0, malformedCount: 1, allSecureHttpOnly: false };
  }
  let malformedCount = 0, antiforgeryCount = 0, secureHttpOnlyCount = 0;
  for (const line of lines) {
    const parts = line.split(";");
    const firstEquals = parts[0].indexOf("=");
    const name = firstEquals >= 0 ? parts[0].slice(0, firstEquals).trim() : "";
    if (!/^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/.test(name)) malformedCount++;
    if (name.startsWith(".AspNetCore.Antiforgery.")) antiforgeryCount++;
    const attributes = new Set(parts.slice(1).map(part => part.trim().toLowerCase()));
    if (attributes.has("secure") && attributes.has("httponly")) secureHttpOnlyCount++;
  }
  return { count: lines.length, antiforgeryCount, malformedCount,
    allSecureHttpOnly: lines.length > 0 && malformedCount === 0 && secureHttpOnlyCount === lines.length };
}

function validJsonObject(text, truncated) {
  if (truncated) return false;
  try { const value = JSON.parse(text); return value !== null && typeof value === "object" && !Array.isArray(value); }
  catch { return false; }
}

export function evaluateProbe(item, response) {
  const url = new URL(item.url);
  const mediaType = String(response.contentType || "").split(";", 1)[0].trim().toLowerCase();
  const html = mediaType === "text/html";
  const json = mediaType === "application/json" || mediaType.endsWith("+json");
  const status = Number.isInteger(response.status) && response.status >= 100 && response.status <= 599 ? response.status : null;
  const common = { redirectsNotFollowed: response.redirected !== true, responseMatchesRequest: response.urlMatchesRequest !== false };
  let checks;
  let cookieFlags;
  switch (item.kind) {
    case "https-redirect":
      checks = { ...common, ...inspectRedirect({ sourceUrl: item.url, status, location: response.location, statuses: item.statuses }).checks };
      break;
    case "legacy-root":
      checks = { ...common, ...inspectRedirect({ sourceUrl: item.url, status, location: response.location,
        statuses: [302], expectedPath: "/Identity/Account/Login" }).checks };
      break;
    case "crm-login":
      cookieFlags = inspectCookieFlags(response.cookies);
      checks = { ...common, status200: status === 200, noRedirect: !response.location, html,
        antiforgeryPresent: cookieFlags.antiforgeryCount > 0, cookiesSecureHttpOnly: cookieFlags.allSecureHttpOnly };
      break;
    case "marketing":
      checks = { ...common, status200: status === 200, noRedirect: !response.location, html,
        envirapidoPage: /envirapido/i.test(response.bodyPrefix || "") };
      break;
    case "buyer-access":
      checks = { ...common, status200: status === 200, noRedirect: !response.location, html };
      break;
    case "anonymous-api":
      checks = { ...common, status401: status === 401, noRedirect: !response.location, json,
        validJsonObject: validJsonObject(response.bodyPrefix || "", response.bodyTruncated) };
      break;
    default:
      checks = { ...common, knownProbeKind: false };
  }
  return { id: item.id, host: item.host, scheme: url.protocol.slice(0, -1), path: url.pathname,
    hasQuery: Boolean(url.search), method: item.method, spoofedForwardedProto: item.spoofedForwardedProto,
    status, passed: Object.values(checks).every(Boolean), checks, ...(cookieFlags ? { cookieFlags } : {}) };
}

async function readBodyPrefix(response) {
  if (!response.body) return { bodyPrefix: "", bodyTruncated: false };
  const reader = response.body.getReader();
  const chunks = [];
  let length = 0, bodyTruncated = false;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      const remaining = MAX_BODY_BYTES - length;
      chunks.push(value.subarray(0, remaining));
      length += Math.min(value.byteLength, remaining);
      if (value.byteLength > remaining) { bodyTruncated = true; await reader.cancel(); break; }
    }
    return { bodyPrefix: new TextDecoder().decode(Buffer.concat(chunks, length)), bodyTruncated };
  } finally { reader.releaseLock(); }
}

async function runProbe(item, fetchImpl) {
  try {
    const response = await fetchImpl(item.url, { method: item.method, headers: item.headers,
      credentials: "omit", redirect: "manual", cache: "no-store", signal: AbortSignal.timeout(TIMEOUT_MS) });
    const needsBody = item.kind === "marketing" || item.kind === "anonymous-api";
    const body = needsBody ? await readBodyPrefix(response) : { bodyPrefix: "", bodyTruncated: false };
    if (!needsBody && response.body) await response.body.cancel();
    let urlMatchesRequest = true;
    if (response.url) {
      try { urlMatchesRequest = new URL(response.url).href === new URL(item.url).href; }
      catch { urlMatchesRequest = false; }
    }
    return evaluateProbe(item, { status: response.status, location: response.headers.get("location"),
      contentType: response.headers.get("content-type"),
      cookies: typeof response.headers.getSetCookie === "function" ? response.headers.getSetCookie() : null,
      redirected: response.redirected, urlMatchesRequest, ...body });
  } catch (error) {
    const url = new URL(item.url);
    return { id: item.id, host: item.host, scheme: url.protocol.slice(0, -1), path: url.pathname,
      hasQuery: Boolean(url.search), method: item.method, spoofedForwardedProto: item.spoofedForwardedProto,
      status: null, passed: false, error: error?.name === "TimeoutError" || error?.name === "AbortError" ? "timeout" : "request_failed" };
  }
}

export async function verifyPublicHttps({ fetchImpl = globalThis.fetch, now = () => new Date() } = {}) {
  if (typeof fetchImpl !== "function") throw new Error("Fetch no está disponible.");
  const plan = makeProbePlan();
  const results = new Array(plan.length);
  let cursor = 0;
  async function worker() {
    while (cursor < plan.length) {
      const index = cursor++;
      results[index] = await runProbe(plan[index], fetchImpl);
    }
  }
  await Promise.all(Array.from({ length: Math.min(4, plan.length) }, worker));
  return { schemaVersion: 1, checkedAtUtc: now().toISOString(), passed: results.every(result => result.passed),
    hosts: Object.values(PUBLIC_HOSTS), probesRun: results.length, passedProbes: results.filter(result => result.passed).length,
    credentialsSent: false, redirectsFollowed: false, onlyPostIsEmptyUnknownProbePath: true, probes: results,
    scope: "Anónimo público: no acredita cookie autenticada, SHA desplegado, SQL ni flujos de pedidos/multitenant." };
}

export async function writeReceipt(path, result) {
  await writeFile(path, `${JSON.stringify(result)}\n`, { flag: "wx", mode: 0o600 });
}

export async function main(args = process.argv.slice(2)) {
  try {
    assertSupportedNode();
    const parsed = parseCliArgs(args);
    if (parsed.help) {
      process.stdout.write("Uso: node tools/Verify-PortalHttps.mjs [--receipt ARCHIVO_NUEVO.json]\nVerifica tres hosts públicos fijos, sin credenciales ni seguimiento de redirecciones.\nCódigos: 0 aprobado; 1 controles pendientes; 2 argumentos/runtime/archivo.\n");
      return 0;
    }
    const result = await verifyPublicHttps();
    if (parsed.receiptPath) await writeReceipt(parsed.receiptPath, result);
    process.stdout.write(`${JSON.stringify(result)}\n`);
    return result.passed ? 0 : 1;
  } catch (error) {
    const code = error?.code === "EEXIST" ? "receipt_already_exists" : "invalid_arguments_runtime_or_receipt";
    process.stderr.write(`${JSON.stringify({ passed: false, error: code })}\n`);
    return 2;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  process.exitCode = await main();
}
