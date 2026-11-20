import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { RegisterForm } from "@/components/auth-forms";
import { loadCatalog } from "@/lib/catalog.server";
export const metadata: Metadata = { title: "Solicita tu plan", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";
export default async function RegisterPage({ searchParams }: { searchParams: Promise<{ plan?: string }> }) {
  const [{ plan }, { catalog, unavailable }] = await Promise.all([searchParams, loadCatalog()]);
  return <AuthShell title="El espacio para tu negocio." description="Cuéntanos sobre tu operación. Nosotros te acompañamos en el siguiente paso." wide><RegisterForm catalog={catalog} selectedCode={typeof plan === "string" ? plan : null} unavailable={unavailable} /></AuthShell>;
}
