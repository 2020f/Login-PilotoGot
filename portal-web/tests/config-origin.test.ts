import test from "node:test";
import assert from "node:assert/strict";

let sequence = 0;
async function configFor(origin: string) {
  const prior = process.env.PORTAL_API_ORIGIN;
  process.env.PORTAL_API_ORIGIN = origin;
  const moduleUrl = new URL(`../next.config.mjs?origin-test=${++sequence}`, import.meta.url);
  try { return (await import(moduleUrl.href)).default; }
  finally { if (prior === undefined) delete process.env.PORTAL_API_ORIGIN; else process.env.PORTAL_API_ORIGIN = prior; }
}
test("Next rewrite refuses insecure remote or credentialed origins before forwarding browser requests", async () => {
  for (const origin of ["http://portal.example.test", "http://192.168.1.10:5031", "http://127.0.0.1.attacker.test", "http://user:password@localhost:5031", "https://user:password@portal.example.test", "http://@127.0.0.1:5031", "https://portal.example.test/api", "https://portal.example.test/api/..", "https://portal.example.test?", "https://portal.example.test#fragment", "https://portal.example.test\\", " https://portal.example.test"]) {
    await assert.rejects(configFor(origin), error => error instanceof Error, origin);
  }
  for (const origin of ["https://portal.example.test", "https://portal.example.test/", "http://127.0.0.1:5031", "http://localhost:5031", "http://[::1]:5031"]) {
    const config = await configFor(origin);
    assert.deepEqual(await config.rewrites(), [{ source: "/portal-api/v1/:path*", destination: `${new URL(origin).origin}/portal-api/v1/:path*` }]);
  }
});
