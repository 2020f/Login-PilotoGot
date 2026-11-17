import test from "node:test";
import assert from "node:assert/strict";
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { loadCatalog } from "../lib/catalog.server.ts";

const fixture = { plans: [], updatedAt: "2026-11-17T12:00:00Z", policyVersions: { terms: "fixture", privacy: "fixture" } };
async function server(handler: (request: IncomingMessage, response: ServerResponse) => void) {
  const http = createServer(handler);
  await new Promise<void>((resolve, reject) => { http.once("error", reject); http.listen(0, "127.0.0.1", resolve); });
  return {
    origin: `http://127.0.0.1:${(http.address() as AddressInfo).port}`,
    close: () => new Promise<void>((resolve, reject) => http.close(error => error ? reject(error) : resolve()))
  };
}
test("SSR reads the production-style API over loopback without triggering its HTTPS redirect", async () => {
  const received: IncomingMessage["headers"][] = [];
  const local = await server((request, response) => {
    received.push(request.headers);
    if (request.headers["x-forwarded-proto"] !== "https") { response.writeHead(307, { Location: "https://portal.example.test/portal-api/v1/catalog" }); response.end(); return; }
    assert.equal(request.url, "/portal-api/v1/catalog");
    response.writeHead(200, { "Content-Type": "application/json" }); response.end(JSON.stringify(fixture));
  });
  try {
    const unforwarded = await fetch(`${local.origin}/portal-api/v1/catalog`, { redirect: "manual" });
    assert.equal(unforwarded.status, 307); await unforwarded.arrayBuffer();
    const result = await loadCatalog(local.origin);
    assert.deepEqual(result, { catalog: fixture, unavailable: false });
    assert.equal(received.length, 2);
    assert.equal(received[0]["x-forwarded-proto"], undefined);
    assert.equal(received[1]["x-forwarded-proto"], "https");
    for (const key of ["cookie", "authorization", "x-envirapido-portal-key", "x-envirapido-catalog-key"]) assert.equal(received[1][key], undefined);
  } finally { await local.close(); }
});
test("remote HTTP and malformed origins are rejected before any network request", async () => {
  let calls = 0;
  const fetcher: typeof fetch = async () => { calls++; return Response.json(fixture); };
  for (const origin of ["http://portal.example.test", "http://127.0.0.1.example.test", "http://192.168.1.10", "ftp://127.0.0.1", "https://user:password@portal.example.test", "http://user:password@127.0.0.1", "http://@127.0.0.1", "https://portal.example.test/api", "https://portal.example.test/api/..", "http://127.0.0.1?key=value", "https://portal.example.test#fragment", "https://portal.example.test?", "invalid origin"]) {
    assert.deepEqual(await loadCatalog(origin, fetcher), { catalog: null, unavailable: true }, origin);
  }
  assert.equal(calls, 0);
});
test("HTTPS sends no forwarded protocol or browser credentials and retains no-store/redirect-error semantics", async () => {
  const fetcher: typeof fetch = async (input, init) => {
    assert.equal(String(input), "https://portal.example.test/portal-api/v1/catalog");
    assert.equal(init?.cache, "no-store"); assert.equal(init?.redirect, "error"); assert.equal(init?.credentials, "omit");
    assert.ok(init?.signal instanceof AbortSignal);
    assert.deepEqual([...new Headers(init?.headers).entries()], [["accept", "application/json"]]);
    return Response.json(fixture);
  };
  assert.deepEqual(await loadCatalog("https://portal.example.test", fetcher), { catalog: fixture, unavailable: false });
});
test("localhost and IPv6 loopback receive the internal protocol header", async () => {
  let calls = 0;
  const fetcher: typeof fetch = async (_input, init) => { calls++; assert.equal(new Headers(init?.headers).get("X-Forwarded-Proto"), "https"); return Response.json(fixture); };
  for (const origin of ["http://localhost:5031", "http://[::1]:5031"]) assert.equal((await loadCatalog(origin, fetcher)).unavailable, false);
  assert.equal(calls, 2);
});
test("unexpected redirects remain unavailable and are never followed even on loopback", async () => {
  let calls = 0;
  const local = await server((request, response) => {
    calls++;
    if (request.url === "/should-not-follow") { response.writeHead(200, { "Content-Type": "application/json" }); response.end(JSON.stringify(fixture)); }
    else { response.writeHead(307, { Location: "/should-not-follow" }); response.end(); }
  });
  try { assert.deepEqual(await loadCatalog(local.origin), { catalog: null, unavailable: true }); assert.equal(calls, 1); }
  finally { await local.close(); }
});
