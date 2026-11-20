import { existsSync } from "node:fs";

const server = new URL("../.next/standalone/server.js", import.meta.url);
if (!existsSync(server)) throw new Error("No hay un bundle standalone. Ejecuta npm run build antes de iniciar.");
process.env.HOSTNAME ||= "127.0.0.1";
process.env.PORT ||= "3000";
await import(server.href);
