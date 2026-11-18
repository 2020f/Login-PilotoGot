import test from "node:test";
import assert from "node:assert/strict";
import { PortalApi, PortalApiError, MutationIntent } from "../lib/api.ts";
import { canRecoverPassword, contactEmailBadge, readAuthOptions, registrationBlockedByEmail } from "../lib/auth-policy.ts";
import { canOpenCrm, safeDownloadPath } from "../lib/access.ts";
import type { Access, DownloadRelease } from "../lib/types.ts";

test("manual approval identifies a contact email without inventing email verification", () => {
  const user = { emailConfirmed: false, emailConfirmationRequired: false };
  assert.deepEqual(contactEmailBadge(user), { label: "Correo de contacto", tone: "neutral" });
  assert.equal(user.emailConfirmed, false);
  assert.deepEqual(contactEmailBadge({ emailConfirmed: false }), { label: "Por confirmar", tone: "warning" });
  assert.deepEqual(contactEmailBadge({ emailConfirmed: false, emailConfirmationRequired: true }), { label: "Por confirmar", tone: "warning" });
  assert.deepEqual(contactEmailBadge({ emailConfirmed: true, emailConfirmationRequired: true }), { label: "Correo confirmado", tone: "success" });
});

test("recovery capability is explicit and independent from email confirmation policy", () => {
  for (const malformed of [null, undefined, false, [], {}, { passwordRecoveryAvailable: true }, { emailConfirmationRequired: false, passwordRecoveryAvailable: "true" }]) {
    assert.equal(readAuthOptions(malformed), null);
    assert.equal(canRecoverPassword(readAuthOptions(malformed)), false);
  }
  const manualWithMail = readAuthOptions({ emailConfirmationRequired: false, passwordRecoveryAvailable: true });
  assert.equal(manualWithMail?.emailConfirmationRequired, false);
  assert.equal(canRecoverPassword(manualWithMail), true);
  assert.equal(canRecoverPassword(readAuthOptions({ emailConfirmationRequired: true, passwordRecoveryAvailable: false })), false);
  assert.equal(canRecoverPassword(readAuthOptions({ emailConfirmationRequired: false, passwordRecoveryAvailable: false })), false);
});

test("anonymous auth metadata is read-only and malformed or unavailable metadata never enables recovery", async () => {
  const calls: { path: string; init: RequestInit }[] = [];
  const api = new PortalApi(async (input, init) => {
    calls.push({ path: String(input), init: init || {} });
    return new Response(JSON.stringify({ emailConfirmationRequired: false, passwordRecoveryAvailable: false }), {
      status: 200, headers: { "Content-Type": "application/json" }
    });
  });
  const options = readAuthOptions(await api.get<unknown>("/auth/options"));
  assert.equal(canRecoverPassword(options), false);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].path, "/portal-api/v1/auth/options");
  assert.equal(calls[0].init.method, "GET");
  assert.equal(calls[0].init.credentials, "include");
  assert.equal(calls[0].init.body, undefined);
});

test("a contact email and portal account do not authorize CRM or a private APK", () => {
  assert.equal(contactEmailBadge({ emailConfirmed: false, emailConfirmationRequired: false }).tone, "neutral");
  const release: DownloadRelease = { id: "df4e81c7-21ef-4c5e-9f88-a4ef00c705bd", versionName: "test", versionCode: 1, bytes: 100, sha256: "qa", fileName: "test.apk", downloadPath: "/portal-api/v1/downloads/df4e81c7-21ef-4c5e-9f88-a4ef00c705bd/file", publishedAt: "2026-11-17T04:00:00Z" };
  for (const reason of ["REQUEST_PENDING", "PAYMENT_PENDING", "NOT_CONFIGURED", "NOT_STARTED", "EXPIRED", "SUSPENDED", "REVOKED", "ACCOUNT_SUSPENDED"]) {
    const access: Access = { allowed: false, reason, startsAt: null, endsAt: null, crmUrl: null };
    assert.equal(canOpenCrm(access), false);
    assert.equal(safeDownloadPath(release, access), null);
  }
});

test("only the typed precommit mail rejection is conclusive; other failures retain the mutation intent", () => {
  assert.equal(registrationBlockedByEmail(new PortalApiError(503, "Mail unavailable", "EMAIL_UNAVAILABLE")), true);
  for (const error of [new PortalApiError(503, "Upstream unavailable"), new PortalApiError(0, "No response", "NETWORK_UNCERTAIN"), new PortalApiError(503, "Malformed response", "INVALID_RESPONSE"), new PortalApiError(500, "Unexpected", "EMAIL_UNAVAILABLE"), { status: 503, code: "EMAIL_UNAVAILABLE" }, new Error("EMAIL_UNAVAILABLE")]) {
    const intent = new MutationIntent<{ planCode: string }>();
    const original = intent.prepare({ planCode: "qa" });
    assert.equal(registrationBlockedByEmail(error), false);
    assert.equal(intent.prepare({ planCode: "qa" }).key, original.key);
    assert.throws(() => intent.prepare({ planCode: "changed" }), /intento pendiente/);
  }
});
