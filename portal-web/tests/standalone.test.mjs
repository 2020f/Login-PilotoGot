import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, realpathSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { prepareStandalone } from "../scripts/prepare-standalone.mjs";

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "envirapido-standalone-test-"));
  for (const path of ["public/branding", ".next/static/chunks", ".next/standalone"]) mkdirSync(join(root, path), { recursive: true });
  writeFileSync(join(root, ".next/standalone/server.js"), "// generated server fixture\n");
  writeFileSync(join(root, "public/branding/logo.png"), Buffer.from([137, 80, 78, 71, 0, 1, 2, 3]));
  writeFileSync(join(root, ".next/static/chunks/app.js"), "console.log('fixture');\n");
  return root;
}
const hash = path => createHash("sha256").update(readFileSync(path)).digest("hex");
function cleanup(root) {
  const target = realpathSync(root), parent = realpathSync(tmpdir());
  if (dirname(target) !== parent || !basename(target).startsWith("envirapido-standalone-test-")) throw new Error("Destino de limpieza fuera de la fixture temporal.");
  rmSync(target, { recursive: true, force: true });
}
test("standalone packaging includes public and runtime chunks and preserves source bytes on repeat", () => {
  const root = fixture();
  try {
    const originalLogo = hash(join(root, "public/branding/logo.png"));
    const originalChunk = hash(join(root, ".next/static/chunks/app.js"));
    const output = prepareStandalone(root);
    assert.equal(hash(join(output, "public/branding/logo.png")), originalLogo);
    assert.equal(hash(join(output, ".next/static/chunks/app.js")), originalChunk);
    prepareStandalone(root);
    assert.equal(hash(join(root, "public/branding/logo.png")), originalLogo);
    assert.equal(hash(join(root, ".next/static/chunks/app.js")), originalChunk);
    assert.ok(existsSync(join(output, "server.js")));
  } finally { cleanup(root); }
});
test("packaging fails before copying when no standalone server was generated", () => {
  const root = fixture();
  try {
    rmSync(join(root, ".next/standalone/server.js"));
    assert.throws(() => prepareStandalone(root), /next build/);
    assert.equal(existsSync(join(root, ".next/standalone/public")), false);
    assert.ok(existsSync(join(root, "public/branding/logo.png")));
  } finally { cleanup(root); }
});
