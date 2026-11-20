import test from "node:test";
import assert from "node:assert/strict";
import { accessPresentation, canOpenCrm, formatDate, formatInclusiveEnd, formatLimit, paymentStatus, resolveSelectedPlan, safeDownloadPath } from "../lib/access.ts";
import type { Access, DownloadRelease, PortalPlan } from "../lib/types.ts";
const active: Access = { allowed: true, reason: "ACTIVE", startsAt: "2026-11-17T04:00:00Z", endsAt: "2026-12-18T04:00:00Z", crmUrl: "https://envirapido.com/Identity/Account/Login" };
test("CRM link requires the exact server grant and immutable HTTPS login URL", () => {
  assert.equal(canOpenCrm(active), true);
  for (const change of [{ allowed: false }, { reason: "EXPIRED" }, { reason: "NEW_UNKNOWN_REASON" }, { crmUrl: "http://envirapido.com/Identity/Account/Login" }, { crmUrl: "https://envirapido.com.attacker.test/Identity/Account/Login" }, { crmUrl: null }]) assert.equal(canOpenCrm({ ...active, ...change }), false);
  assert.notEqual(accessPresentation({ ...active, allowed: false }).tone, "success");
  assert.notEqual(accessPresentation({ ...active, reason: "UNKNOWN" }).tone, "success");
});
test("APK link is authorized, relative and bound to the current release ID", () => {
  const id = "df4e81c7-21ef-4c5e-9f88-a4ef00c705bd";
  const release: DownloadRelease = { id, versionName: "test", versionCode: 1, bytes: 100, sha256: "x", fileName: "test.apk", downloadPath: `/portal-api/v1/downloads/${id}/file`, publishedAt: "2026-11-17T04:00:00Z" };
  assert.equal(safeDownloadPath(release, active), release.downloadPath);
  for (const path of ["https://example.test/test.apk", "//example.test/test.apk", `/portal-api/v1/downloads/${id}/file?next=bad`, "/portal-api/v1/downloads/../file", "/api/downloads/file", "/portal-api/v1/downloads/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/file"]) assert.equal(safeDownloadPath({ ...release, downloadPath: path }, active), null);
  assert.equal(safeDownloadPath(release, { ...active, allowed: false }), null);
  assert.equal(safeDownloadPath(release, { ...active, reason: "EXPIRED" }), null);
});
test("catalog selection keeps the requested published plan and never invents a default plan", () => {
  const plan: PortalPlan = { code: "real-code", name: "Published", description: "", monthlyPrice: 0, currency: "DOP", maxStores: 0, maxPilots: 0, maxOrdersPerMonth: 0, version: "version1", featured: false };
  assert.equal(resolveSelectedPlan([plan], "real-code"), plan);
  for (const code of [null, "unpublished", ""]) assert.equal(resolveSelectedPlan([plan], code), null);
  assert.equal(resolveSelectedPlan([], "real-code"), null);
  assert.equal(formatLimit(0), "Sin límite");
});
test("exclusive UTC end shows the final authorized RD day, and reported payment remains unconfirmed", () => {
  assert.equal(formatInclusiveEnd("2026-12-18T04:00:00Z"), formatDate("2026-12-17T16:00:00Z"));
  assert.notEqual(formatInclusiveEnd("2026-12-18T04:00:00Z"), formatDate("2026-12-18T16:00:00Z"));
  assert.equal(formatInclusiveEnd(null), "Por definir");
  assert.equal(paymentStatus("Reported"), "Reportado · por validar");
  assert.equal(paymentStatus("InReview"), "En revisión de pago");
  assert.equal(paymentStatus("Rejected"), "Reporte no aprobado");
  assert.notEqual(paymentStatus("Unrecognized"), "Confirmado");
});
