import test from "node:test";
import assert from "node:assert/strict";
import { PortalApi, PortalApiError, MutationIntent } from "../lib/api.ts";

function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } }); }
type Call = { path: string; init: RequestInit };
function transport(handler: (call: Call) => Response | Promise<Response>) {
  const calls: Call[] = [];
  const fetcher: typeof fetch = async (input, init) => { const call = { path: String(input), init: init || {} }; calls.push(call); return handler(call); };
  return { calls, fetcher };
}
test("anonymous registration and authenticated PUT both require a CSRF token and same-origin cookie transport", async () => {
  const wire = transport(call => call.path.endsWith("/csrf") ? json({ requestToken: "csrf-one" }) : json({ ok: true }));
  const api = new PortalApi(wire.fetcher);
  await api.mutate("/auth/register", { email: "fixture@example.test" }, { idempotencyKey: "stable-uuid" });
  await api.mutate("/account", { businessName: "Example" }, { method: "PUT" });
  assert.equal(wire.calls.filter(call => call.path.endsWith("/csrf")).length, 1);
  for (const call of wire.calls) {
    assert.equal(call.init.credentials, "include"); assert.equal(call.init.cache, "no-store"); assert.equal(call.init.redirect, "error");
  }
  const registration = wire.calls[1];
  assert.equal(new Headers(registration.init.headers).get("X-CSRF-TOKEN"), "csrf-one");
  assert.equal(new Headers(registration.init.headers).get("Idempotency-Key"), "stable-uuid");
  assert.equal(wire.calls[2].init.method, "PUT");
});
test("parallel forms share CSRF preparation but invalidation after auth forces a new token", async () => {
  let token = 0;
  const wire = transport(call => call.path.endsWith("/csrf") ? json({ requestToken: `csrf-${++token}` }) : json({ ok: true }));
  const api = new PortalApi(wire.fetcher);
  await Promise.all([api.mutate("/auth/login", {}), api.mutate("/auth/forgot-password", {})]);
  assert.equal(token, 1);
  api.invalidateCsrf();
  await api.mutate("/auth/logout", {});
  assert.equal(token, 2);
  assert.equal(new Headers(wire.calls.at(-1)!.init.headers).get("X-CSRF-TOKEN"), "csrf-2");
});
test("an old CSRF response cannot repopulate the cache after an authentication boundary", async () => {
  let resolveFirst!: (response: Response) => void;
  let csrfCalls = 0;
  const wire = transport(call => {
    if (!call.path.endsWith("/csrf")) return json({ ok: true });
    csrfCalls++;
    return csrfCalls === 1 ? new Promise(resolve => { resolveFirst = resolve; }) : json({ requestToken: "new-token" });
  });
  const api = new PortalApi(wire.fetcher);
  const pending = api.mutate("/requests", {});
  api.invalidateCsrf(); resolveFirst(json({ requestToken: "stale-token" }));
  await pending;
  assert.equal(csrfCalls, 2);
  assert.equal(new Headers(wire.calls.at(-1)!.init.headers).get("X-CSRF-TOKEN"), "new-token");
});
test("lost response does not retry automatically, and manual retry keeps the original UUID and exact payload", async () => {
  let writes = 0;
  const wire = transport(call => { if (call.path.endsWith("/csrf")) return json({ requestToken: "csrf" }); if (++writes === 1) throw new TypeError("connection lost"); return json({ id: "created-once" }, 201); });
  const api = new PortalApi(wire.fetcher), intent = new MutationIntent<{ planCode: string; planVersion: string }>();
  const first = intent.prepare({ planCode: "fixture", planVersion: "v1" });
  await assert.rejects(api.mutate("/requests", first.body, { idempotencyKey: first.key }), error => error instanceof PortalApiError && error.code === "NETWORK_UNCERTAIN");
  assert.equal(writes, 1);
  first.body.planVersion = "modified-client-copy";
  const retry = intent.prepare({ planCode: "fixture", planVersion: "v1" });
  assert.equal(retry.key, first.key); assert.equal(retry.body.planVersion, "v1");
  await api.mutate("/requests", retry.body, { idempotencyKey: retry.key });
  const posts = wire.calls.filter(call => call.init.method === "POST");
  assert.equal(posts[0].init.body, posts[1].init.body);
  assert.equal(new Headers(posts[0].init.headers).get("Idempotency-Key"), new Headers(posts[1].init.headers).get("Idempotency-Key"));
  assert.throws(() => intent.prepare({ planCode: "another", planVersion: "v1" }), error => error instanceof PortalApiError && error.code === "PENDING_INTENT");
  intent.clear(); assert.notEqual(intent.prepare({ planCode: "another", planVersion: "v1" }).key, first.key);
});
test("401 and 409 remain explicit JSON errors; successful logout accepts 204 without parsing", async () => {
  const wire = transport(call => call.path.endsWith("/csrf") ? json({ requestToken: "csrf" }) : call.path.endsWith("/logout") ? new Response(null, { status: 204 }) : json({ error: "Inicia sesión", code: "AUTH_REQUIRED" }, 401));
  const api = new PortalApi(wire.fetcher);
  await assert.rejects(api.get("/account"), error => error instanceof PortalApiError && error.status === 401 && error.code === "AUTH_REQUIRED");
  assert.equal(await api.mutate("/auth/logout", {}), undefined);
  const conflict = new PortalApi(transport(() => json({ error: "El plan cambió", code: "PLAN_CHANGED" }, 409)).fetcher);
  await assert.rejects(conflict.get("/catalog"), error => error instanceof PortalApiError && error.status === 409 && error.message === "El plan cambió");
});
test("HTML login redirects and malformed responses never become an authenticated account", async () => {
  const api = new PortalApi(transport(() => new Response("<html>login</html>", { status: 200 })).fetcher);
  await assert.rejects(api.get("/account"), error => error instanceof PortalApiError && error.status === 502 && error.code === "INVALID_RESPONSE");
});
test("external, protocol-relative and traversal paths are rejected without network access", async () => {
  const wire = transport(() => json({})), api = new PortalApi(wire.fetcher);
  for (const path of ["https://example.test/account", "//example.test", "/../api", "/x\\api", "/x\r\nInjected: true"]) {
    await assert.rejects(api.get(path), error => error instanceof PortalApiError && error.code === "INVALID_PATH");
  }
  assert.equal(wire.calls.length, 0);
});
test("timeout leaves a mutation uncertain while caller cancellation remains an AbortError", async () => {
  const wait: typeof fetch = async (_input, init) => new Promise((_resolve, reject) => { init!.signal!.addEventListener("abort", () => reject(init!.signal!.reason), { once: true }); });
  const api = new PortalApi(wait, 15);
  const keepAlive = setTimeout(() => {}, 100);
  try { await assert.rejects(api.get("/account"), error => error instanceof PortalApiError && error.code === "NETWORK_UNCERTAIN"); }
  finally { clearTimeout(keepAlive); }
  const abort = new AbortController(); const pending = api.get("/account", abort.signal); abort.abort();
  await assert.rejects(pending, error => error instanceof DOMException && error.name === "AbortError");
});
test("antiforgery failure is not blindly replayed and a deliberate retry obtains a fresh token", async () => {
  let tokens = 0, writes = 0;
  const wire = transport(call => call.path.endsWith("/csrf") ? json({ requestToken: `t${++tokens}` }) : ++writes === 1 ? json({ error: "Formulario vencido", code: "CSRF_INVALID" }, 400) : json({ ok: true }));
  const api = new PortalApi(wire.fetcher);
  await assert.rejects(api.mutate("/requests", {})); assert.equal(writes, 1);
  await api.mutate("/requests", {}); assert.equal(tokens, 2);
});
