import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { AccountArea, type AccountSection } from "@/components/account";
export const metadata: Metadata = { title: "Tu espacio Envirapido", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";
const allowed = new Set(["inicio", "solicitudes", "suscripcion", "descargas", "perfil", "ayuda"]);
export default async function AccountPage({ params, searchParams }: { params: Promise<{ section?: string[] }>; searchParams: Promise<{ plan?: string }> }) {
  const [{ section }, { plan }] = await Promise.all([params, searchParams]);
  const active = section?.[0] || "inicio";
  if ((section?.length || 0) > 1 || !allowed.has(active)) notFound();
  return <AccountArea section={active as AccountSection} selectedPlan={typeof plan === "string" ? plan : undefined} />;
}
