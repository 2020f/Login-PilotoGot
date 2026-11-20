import type { Metadata } from "next";
import { MarketingLayout, ContactCta } from "@/components/site";
import { Plans } from "@/components/plans";
import { loadCatalog } from "@/lib/catalog.server";
export const metadata: Metadata = { title: "Planes para tu operación", description: "Consulta los planes y límites publicados de Envirapido. Solicita el acceso que necesita tu negocio, con aprobación y validación manual.", alternates: { canonical: "/planes" } };
export const dynamic = "force-dynamic";
export default async function PlansPage() {
  const { catalog, unavailable } = await loadCatalog();
  return <MarketingLayout><section className="section page-intro"><div className="container"><span className="eyebrow">EL ESPACIO PARA TU NEGOCIO</span><h1>Planes para avanzar<br />con tu operación.</h1><p className="lead">Elige una opción del catálogo vigente. Nuestro equipo revisará los datos de tu negocio y te acompañará en la activación.</p></div></section><section className="section section-tight"><div className="container"><Plans catalog={catalog} unavailable={unavailable} /><div className="plan-footnote"><p>Los pagos se realizan fuera del portal y se confirman manualmente. El precio y los límites enviados en tu solicitud provienen del servidor.</p><p>La cuenta web y su vigencia son independientes de las credenciales de la operación.</p></div></div></section><ContactCta /></MarketingLayout>;
}
