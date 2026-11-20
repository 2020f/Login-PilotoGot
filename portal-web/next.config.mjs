const configuredOrigin = process.env.PORTAL_API_ORIGIN || "http://127.0.0.1:5031";
const authority = /^https?:\/\/([^/?#]+)\/?$/i.exec(configuredOrigin)?.[1];
const apiOrigin = new URL(configuredOrigin);
const loopback = apiOrigin.hostname === "localhost" || apiOrigin.hostname === "[::1]" || /^127\.\d+\.\d+\.\d+$/.test(apiOrigin.hostname);
if (!authority || authority.includes("@") || configuredOrigin.includes("\\") || configuredOrigin.trim() !== configuredOrigin
    || apiOrigin.username || apiOrigin.password || /[?#]/.test(configuredOrigin) || apiOrigin.pathname !== "/"
    || (apiOrigin.protocol !== "https:" && !(apiOrigin.protocol === "http:" && loopback))) {
  throw new Error("PORTAL_API_ORIGIN requiere HTTPS o HTTP loopback, sin credenciales, ruta, consulta ni fragmento.");
}

/** @type {import('next').NextConfig} */
const config = {
  poweredByHeader: false,
  output: "standalone",
  images: { unoptimized: true },
  async rewrites() {
    // The operational /api and Identity/CRM routes are deliberately not rewritten.
    return [{ source: "/portal-api/v1/:path*", destination: `${apiOrigin.origin}/portal-api/v1/:path*` }];
  },
  async headers() {
    return [{
      source: "/:path*",
      headers: [
        { key: "X-Content-Type-Options", value: "nosniff" },
        { key: "Referrer-Policy", value: "no-referrer" },
        { key: "X-Frame-Options", value: "DENY" },
        { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" }
      ]
    }, {
      source: "/:path(cuenta|acceso|registro|confirmar-correo|recuperar-acceso|restablecer-acceso)/:rest*",
      headers: [{ key: "Cache-Control", value: "private, no-store" }, { key: "X-Robots-Tag", value: "noindex, nofollow" }]
    }];
  }
};
export default config;
