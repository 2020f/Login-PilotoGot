export const CONTACT = {
  phone: "829 641 1308",
  phoneHref: "tel:+18296411308",
  whatsappHref: "https://wa.me/18296411308",
  email: "morasanchezfernelis21@gmail.com"
} as const;
export const CRM_LOGIN_URL = "https://envirapido.com/Identity/Account/Login";
export const PUBLIC_SITE_URL = process.env.NEXT_PUBLIC_SITE_ORIGIN || "https://envirapido.com";

function withOrigin(origin: string | undefined, path: string): string {
  if (!path.startsWith("/") || path.startsWith("//")) throw new Error("Ruta del portal no válida.");
  if (!origin) return path;
  const url = new URL(origin);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) throw new Error("Origen público no válido.");
  return new URL(path, url.origin).toString();
}
export function portalUrl(path: string): string { return withOrigin(process.env.NEXT_PUBLIC_PORTAL_ORIGIN, path); }
export function siteUrl(path: string): string { return withOrigin(process.env.NEXT_PUBLIC_SITE_ORIGIN, path); }
