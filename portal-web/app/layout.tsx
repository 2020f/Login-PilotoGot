import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import { PUBLIC_SITE_URL } from "@/lib/config";
import "./globals.css";

export const metadata: Metadata = {
  metadataBase: new URL(PUBLIC_SITE_URL),
  title: { default: "Envirapido · Cada entrega, bajo control", template: "%s · Envirapido" },
  description: "Software de última milla para conectar comercios, pilotos y tu centro de operaciones. Pedidos, rutas, incidencias y conciliación en un solo lugar.",
  applicationName: "Envirapido",
  openGraph: { type: "website", locale: "es_DO", siteName: "Envirapido", images: [{ url: "/illustrations/operacion-conectada.png", width: 1536, height: 1024, alt: "Una operación de última milla conectada" }] },
  twitter: { card: "summary_large_image" },
  referrer: "no-referrer"
};
export const viewport: Viewport = { width: "device-width", initialScale: 1, themeColor: "#008e89" };
export default function RootLayout({ children }: { children: ReactNode }) {
  return <html lang="es"><body><a className="skip-link" href="#contenido">Saltar al contenido</a>{children}</body></html>;
}
