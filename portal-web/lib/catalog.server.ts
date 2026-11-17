import type { Catalog } from "./types";

function catalogOrigin(value: string): URL {
  const authority = /^https?:\/\/([^/?#]+)\/?$/i.exec(value)?.[1];
  const origin = new URL(value);
  const loopback = origin.hostname === "localhost" || origin.hostname === "[::1]" || /^127\.\d+\.\d+\.\d+$/.test(origin.hostname);
  if (!authority || authority.includes("@") || value.includes("\\") || value.trim() !== value
      || origin.username || origin.password || /[?#]/.test(value) || origin.pathname !== "/"
      || (origin.protocol !== "https:" && !(origin.protocol === "http:" && loopback))) {
    throw new Error("El origen del catálogo requiere HTTPS o HTTP de loopback, sin credenciales, ruta, consulta ni fragmento.");
  }
  return origin;
}

export async function loadCatalog(
  configuredOrigin = process.env.PORTAL_API_ORIGIN || "http://127.0.0.1:5031",
  fetcher: typeof fetch = globalThis.fetch
): Promise<{ catalog: Catalog | null; unavailable: boolean }> {
  try {
    const origin = catalogOrigin(configuredOrigin);
    const headers: Record<string, string> = { Accept: "application/json" };
    // The local API trusts only its exact loopback proxy. Keep TLS enforcement
    // enabled there without forwarding browser cookies or following redirects.
    if (origin.protocol === "http:") headers["X-Forwarded-Proto"] = "https";
    const response = await fetcher(new URL("/portal-api/v1/catalog", origin), {
      cache: "no-store", credentials: "omit", redirect: "error", headers, signal: AbortSignal.timeout(8000)
    });
    if (!response.ok) return { catalog: null, unavailable: true };
    const catalog = await response.json() as Catalog;
    if (!Array.isArray(catalog.plans) || !catalog.policyVersions?.terms || !catalog.policyVersions?.privacy) return { catalog: null, unavailable: true };
    return { catalog, unavailable: false };
  } catch { return { catalog: null, unavailable: true }; }
}
