import type { MetadataRoute } from "next";
import { PUBLIC_SITE_URL } from "@/lib/config";
export default function robots(): MetadataRoute.Robots {
  return { rules: { userAgent: "*", allow: "/", disallow: ["/cuenta", "/acceso", "/registro", "/confirmar-correo", "/recuperar-acceso", "/restablecer-acceso", "/portal-api/", "/api/", "/Identity/", "/SuperAdmin/", "/Operaciones/"] }, sitemap: `${PUBLIC_SITE_URL}/sitemap.xml` };
}
