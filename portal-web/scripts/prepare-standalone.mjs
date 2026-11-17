import { cpSync, existsSync, realpathSync } from "node:fs";
import { isAbsolute, join, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const projectRoot = fileURLToPath(new URL("../", import.meta.url));
function inside(root, path) {
  const rel = relative(root, realpathSync(path));
  if (isAbsolute(rel) || rel === ".." || rel.startsWith(`..${process.platform === "win32" ? "\\" : "/"}`)) {
    throw new Error("La salida standalone debe permanecer dentro del proyecto.");
  }
}
export function prepareStandalone(root = projectRoot) {
  root = realpathSync(root);
  const output = join(root, ".next", "standalone");
  const sources = [[join(root, "public"), join(output, "public")], [join(root, ".next", "static"), join(output, ".next", "static")]];
  if (!existsSync(join(output, "server.js"))) throw new Error("Primero ejecuta next build con output:standalone.");
  inside(root, output);
  for (const [source, destination] of sources) {
    inside(root, source);
    if (existsSync(destination)) inside(output, destination);
  }
  // Only copy public assets into the generated bundle. Never delete or move sources.
  for (const [source, destination] of sources) cpSync(source, destination, { recursive: true, force: true, dereference: false });
  return output;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  prepareStandalone();
  process.stdout.write("Standalone listo con public y .next/static.\n");
}
