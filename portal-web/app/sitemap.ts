import type { MetadataRoute } from "next";
import { PUBLIC_SITE_URL } from "@/lib/config";
export default function sitemap(): MetadataRoute.Sitemap {
  return ["", "/funciones", "/planes", "/contacto", "/privacidad", "/terminos"].map(path => ({ url: `${PUBLIC_SITE_URL}${path}`, changeFrequency: path === "/planes" ? "weekly" : "monthly", priority: path === "" ? 1 : path === "/planes" ? 0.9 : 0.6 }));
}
