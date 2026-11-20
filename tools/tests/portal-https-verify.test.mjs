import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, unlink, rmdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { assertSupportedNode, parseCliArgs, makeProbePlan, inspectRedirect, inspectCookieFlags,
  evaluateProbe, verifyPublicHttps, writeReceipt, PUBLIC_HOSTS } from "../Verify-PortalHttps.mjs";

const plan = makeProbePlan();
const byId = id => plan.find(item => item.id === id);
const expectedCookies = [".AspNetCore.Antiforgery.qa=DO_NOT_PRINT_COOKIE; Path=/; Secure; HttpOnly; SameSite=Strict"];
const goodResponse = item => {
  if (item.kind === "https-redirect") {
    const target = new URL(item.url); target.protocol = "https:";
    return new Response(null, { status: item.statuses[0], headers: { Location: target.href } });
  }
  if (item.kind === "legacy-root") return new Response(null, { status: 302,
    headers: { Location: `https://${PUBLIC_HOSTS.legacy}/Identity/Account/Login` } });
  if (item.kind === "anonymous-api") return Response.json({ error: "Unauthorized" }, { status: 401 });
  const headers = new Headers({ "Content-Type": "text/html; charset=utf-8" });
  if (item.kind === "crm-login") for (const cookie of expectedCookies) headers.append("Set-Cookie", cookie);
  return new Response("<!doctype html><title>Envirapido</title>", { status: 200, headers });
};

test("Node runtime minimum is explicit and modern LTS/runtime versions work", () => {
  for (const version of ["22.18.0", "22.18.1", "24.0.0", "26.12.1"]) assert.doesNotThrow(() => assertSupportedNode(version));
  for (const version of ["20.99.9", "22.17.9", "bad", ""]) assert.throws(() => assertSupportedNode(version));
});

test("CLI permits only an optional new receipt, never a host, origin or apply mode", () => {
  assert.deepEqual(parseCliArgs([]), { help: false, receiptPath: null });
  assert.deepEqual(parseCliArgs(["--receipt", "/tmp/result.json"]), { help: false, receiptPath: "/tmp/result.json" });
  assert.equal(parseCliArgs(["--help"]).help, true);
  for (const args of [["--base-url", "https://attacker.test"], ["--apply"], ["--receipt"], ["--receipt", ""],
    ["--receipt", "--help"], ["--receipt", "a\0b"], ["--receipt", "a", "--receipt", "b"], ["--help", "--apply"]]) {
    assert.throws(() => parseCliArgs(args));
  }
});

test("Plan covers only the three exact hosts with one empty POST to an unknown path", () => {
  assert.equal(new Set(plan.map(item => item.id)).size, plan.length);
  assert.deepEqual(new Set(plan.map(item => new URL(item.url).hostname)), new Set(Object.values(PUBLIC_HOSTS)));
  for (const item of plan) {
    assert.equal(Object.isFrozen(item), true);
    assert.equal(Object.isFrozen(item.headers), true);
    assert.equal(new URL(item.url).username, "");
    assert.equal(new URL(item.url).password, "");
    assert.equal(Object.hasOwn(item.headers, "Cookie"), false);
    assert.equal(Object.hasOwn(item.headers, "Authorization"), false);
  }
  const posts = plan.filter(item => item.method === "POST");
  assert.equal(posts.length, 1);
  assert.equal(new URL(posts[0].url).pathname, "/__envirapido_https_probe__");
  assert.equal(posts[0].host, PUBLIC_HOSTS.apex);
  assert.deepEqual(posts[0].statuses, [308]);
});

test("HTTP redirect preserves encoded slash and exact query, uses HTTPS443 and keeps method", () => {
  const sourceUrl = "http://envirapido.com/funciones%2Fqa?qa=env%2Frapido%20test";
  const good = "https://envirapido.com/funciones%2Fqa?qa=env%2Frapido%20test";
  assert.equal(inspectRedirect({ sourceUrl, status: 308, location: good }).passed, true);
  assert.equal(inspectRedirect({ sourceUrl, status: 308, location: good.replace("envirapido.com", "ENVIRAPIDO.COM:443") }).passed, true);
  for (const location of [null, "", "/funciones%2Fqa?qa=env%2Frapido%20test", "http://envirapido.com/funciones%2Fqa?qa=env%2Frapido%20test",
    good.replace("envirapido.com", "envirapido.com.attacker.test"), good.replace("envirapido.com", "www.envirapido.com"),
    good.replace("envirapido.com", "envirapido.com:8443"), good.replace("https://", "https://user:secret@"), `${good}#x`,
    good.replace("%2Fqa", "/qa"), good.replace("%20test", "+test"), good.split("?")[0], "https://[",
    ` ${good}`, `${good}\r\n`, good.replace("https://", "https:\\\\")]) {
    assert.equal(inspectRedirect({ sourceUrl, status: 308, location }).passed, false, String(location));
  }
  for (const status of [200, 301, 302, 307, 404, 500]) assert.equal(inspectRedirect({ sourceUrl, status, location: good }).passed, false);
});

test("Legacy 307 is accepted without permitting a 301/302 method switch on HTTP", () => {
  const sourceUrl = `http://${PUBLIC_HOSTS.legacy}/Identity/Account/Login?ReturnUrl=%2FOperaciones`;
  const location = sourceUrl.replace("http:", "https:");
  for (const status of [307, 308]) assert.equal(inspectRedirect({ sourceUrl, location, status, statuses: [307, 308] }).passed, true);
  for (const status of [301, 302]) assert.equal(inspectRedirect({ sourceUrl, location, status, statuses: [307, 308] }).passed, false);
});

test("Legacy HTTPS root remains a same-host HTTPS login redirect, never another scheme or target", () => {
  const item = byId("legacy-https-root");
  const response = { status: 302, location: "/Identity/Account/Login?ReturnUrl=%2F", redirected: false };
  assert.equal(evaluateProbe(item, response).passed, true);
  for (const location of ["/acceso", `http://${PUBLIC_HOSTS.legacy}/Identity/Account/Login`,
    "https://envirapido.com/Identity/Account/Login", "https://attacker.test/Identity/Account/Login"]) {
    assert.equal(evaluateProbe(item, { ...response, location }).passed, false);
  }
});

test("Cookie inspection handles Expires commas, requires every flag and never returns values or names", () => {
  const lines = [...expectedCookies, "session=SECRET_SESSION; Expires=Wed, 21 Oct 2030 07:28:00 GMT; Secure; HttpOnly"];
  const result = inspectCookieFlags(lines);
  assert.equal(result.count, 2); assert.equal(result.antiforgeryCount, 1); assert.equal(result.allSecureHttpOnly, true);
  const output = JSON.stringify(result);
  for (const secret of ["DO_NOT_PRINT_COOKIE", "SECRET_SESSION", ".AspNetCore.Antiforgery", "session="]) assert.equal(output.includes(secret), false);
  for (const unsafe of [[...expectedCookies, "bad=secret; Secure"], [...expectedCookies, "bad=secret; HttpOnly"],
    ["broken"], [], null]) assert.equal(inspectCookieFlags(unsafe).allSecureHttpOnly, false);
});

test("Login requires an anonymous antiforgery cookie and all Secure/HttpOnly flags", () => {
  const item = byId("apex-https-login");
  const response = { status: 200, contentType: "text/html; charset=utf-8", cookies: expectedCookies };
  assert.equal(evaluateProbe(item, response).passed, true);
  for (const change of [{ cookies: [] }, { cookies: ["session=secret; Secure; HttpOnly"] },
    { cookies: [".AspNetCore.Antiforgery.qa=secret; HttpOnly"] }, { cookies: [...expectedCookies, "bad=secret; Secure"] },
    { status: 302 }, { contentType: "application/json" }, { location: "/Identity/Account/Login" }]) {
    assert.equal(evaluateProbe(item, { ...response, ...change }).passed, false);
  }
});

test("API must remain401 with valid JSON and no redirects; HTML login fallback is rejected", () => {
  const item = byId("apex-https-api-pilot");
  const response = { status: 401, contentType: "application/json; charset=utf-8", bodyPrefix: '{"error":"Unauthorized"}' };
  assert.equal(evaluateProbe(item, response).passed, true);
  for (const change of [{ status: 200 }, { status: 302, location: "/Identity/Account/Login" }, { contentType: "text/html" },
    { bodyPrefix: "<html>login</html>" }, { bodyPrefix: "null" }, { bodyPrefix: "[]" }, { bodyTruncated: true }]) {
    assert.equal(evaluateProbe(item, { ...response, ...change }).passed, false);
  }
});

test("Buyer access and HTTPS root cannot report success for a redirect loop or downgrade", () => {
  const response = { status: 200, contentType: "text/html", bodyPrefix: "<title>Envirapido</title>" };
  for (const item of [byId("buyer-https-access"), byId("apex-https-root"), byId("apex-https-spoof")]) {
    assert.equal(evaluateProbe(item, response).passed, true);
    for (const location of [item.url, item.url.replace("https:", "http:"), "https://attacker.test/"]) {
      assert.equal(evaluateProbe(item, { ...response, status: 308, location }).passed, false);
    }
    assert.equal(evaluateProbe(item, { ...response, redirected: true }).passed, false);
    assert.equal(evaluateProbe(item, { ...response, urlMatchesRequest: false }).passed, false);
  }
  assert.equal(evaluateProbe(byId("apex-https-root"), { ...response, bodyPrefix: "another website" }).passed, false);
});

test("All fixed probes run exactly once with manual redirects, no credentials, no Cookie/Auth and no POST body", async () => {
  const calls = [];
  const result = await verifyPublicHttps({ now: () => new Date("2026-11-20T22:00:00Z"), fetchImpl: async (url, init) => {
    const item = plan.find(candidate => candidate.url === url && candidate.method === init.method && candidate.headers["X-Forwarded-Proto"] === init.headers["X-Forwarded-Proto"]);
    assert.ok(item, "Only the fixed public plan may make a request");
    calls.push(item.id);
    assert.equal(init.credentials, "omit"); assert.equal(init.redirect, "manual"); assert.equal(init.cache, "no-store");
    assert.equal(init.signal instanceof AbortSignal, true); assert.equal(init.signal.aborted, false);
    assert.equal(Object.hasOwn(init, "body"), false);
    const headers = new Headers(init.headers);
    assert.equal(headers.has("Cookie"), false); assert.equal(headers.has("Authorization"), false);
    return goodResponse(item);
  } });
  assert.equal(result.passed, true);
  assert.equal(result.probesRun, plan.length);
  assert.equal(result.passedProbes, plan.length);
  assert.equal(result.checkedAtUtc, "2026-11-20T22:00:00.000Z");
  assert.equal(new Set(calls).size, plan.length);
  for (const value of ["DO_NOT_PRINT_COOKIE", "SECRET_SESSION", "Set-Cookie", "Antiforgery.qa="]) assert.equal(JSON.stringify(result).includes(value), false);
});

test("An HTTP200 root or spoof bypass fails without fetching its Location destination", async () => {
  const calls = [];
  const result = await verifyPublicHttps({ fetchImpl: async (url, init) => {
    calls.push(url);
    const item = plan.find(candidate => candidate.url === url && candidate.method === init.method && candidate.headers["X-Forwarded-Proto"] === init.headers["X-Forwarded-Proto"]);
    if (item.id === "apex-http-spoof") return new Response("sensitive-marker", { status: 200, headers: { Location: "https://attacker.test/private" } });
    return goodResponse(item);
  } });
  assert.equal(result.passed, false);
  assert.equal(result.probes.find(item => item.id === "apex-http-spoof").passed, false);
  assert.equal(calls.length, plan.length);
  assert.equal(calls.some(url => new URL(url).hostname === "attacker.test"), false);
  assert.equal(JSON.stringify(result).includes("sensitive-marker"), false);
});

test("Network/timeout failures fail closed and omit exception messages and private URLs", async () => {
  const result = await verifyPublicHttps({ fetchImpl: async (url, init) => {
    const item = plan.find(candidate => candidate.url === url && candidate.method === init.method && candidate.headers["X-Forwarded-Proto"] === init.headers["X-Forwarded-Proto"]);
    if (item.id === "buyer-https-access") throw new DOMException("SECRET_TIMEOUT_URL", "TimeoutError");
    if (item.id === "apex-http-root-query") throw new Error("SECRET_COOKIE_OR_URL");
    return goodResponse(item);
  } });
  assert.equal(result.passed, false);
  assert.equal(result.probes.find(item => item.id === "buyer-https-access").error, "timeout");
  assert.equal(result.probes.find(item => item.id === "apex-http-root-query").error, "request_failed");
  assert.equal(JSON.stringify(result).includes("SECRET_"), false);
});

test("Unexpected followed-response URLs fail even if their body resembles a valid page", async () => {
  const result = await verifyPublicHttps({ fetchImpl: async (url, init) => {
    const item = plan.find(candidate => candidate.url === url && candidate.method === init.method && candidate.headers["X-Forwarded-Proto"] === init.headers["X-Forwarded-Proto"]);
    const response = goodResponse(item);
    if (item.id === "apex-https-root") Object.defineProperty(response, "url", { value: "https://attacker.test/SECRET_URL" });
    return response;
  } });
  assert.equal(result.passed, false);
  assert.equal(result.probes.find(item => item.id === "apex-https-root").checks.responseMatchesRequest, false);
  assert.equal(JSON.stringify(result).includes("SECRET_URL"), false);
});

test("Receipt creation is exclusive, compact JSON and cannot replace an existing file", async () => {
  const dir = await mkdtemp(join(tmpdir(), "envirapido-https-verify-"));
  const path = join(dir, "receipt.json");
  try {
    const receipt = { passed: true, credentialsSent: false };
    await writeReceipt(path, receipt);
    const original = await readFile(path, "utf8");
    assert.equal(original, `${JSON.stringify(receipt)}\n`);
    await assert.rejects(writeReceipt(path, { passed: false }), { code: "EEXIST" });
    assert.equal(await readFile(path, "utf8"), original);
  } finally { await unlink(path).catch(() => {}); await rmdir(dir); }
});
