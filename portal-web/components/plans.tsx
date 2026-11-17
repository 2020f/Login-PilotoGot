import { formatLimit, formatMoney } from "@/lib/access";
import { portalUrl } from "@/lib/config";
import type { Catalog, PortalPlan } from "@/lib/types";
import { Icon } from "./icon";
import { EmptyState, Notice, RefreshButton } from "./ui";

export function PlanDetails({ plan }: { plan: PortalPlan }) {
  return <ul className="plan-limits"><li><Icon name="building" /><span>Comercios activos</span><strong>{formatLimit(plan.maxStores)}</strong></li><li><Icon name="truck" /><span>Pilotos activos</span><strong>{formatLimit(plan.maxPilots)}</strong></li><li><Icon name="box" /><span>Órdenes por mes</span><strong>{formatLimit(plan.maxOrdersPerMonth)}</strong></li></ul>;
}
export function Plans({ catalog, unavailable = false }: { catalog: Catalog | null; unavailable?: boolean }) {
  if (unavailable) return <div className="catalog-message"><Notice tone="warning" title="No pudimos consultar los planes."><p>Vuelve a intentarlo o contáctanos para conocer las opciones disponibles.</p></Notice><RefreshButton /></div>;
  if (!catalog?.plans.length) return <div className="catalog-message panel"><EmptyState title="Estamos preparando nuestras opciones" icon="layers">Contacta a nuestro equipo para conocer los planes disponibles para tu negocio.</EmptyState><a className="button secondary" href="/contacto">Hablar con Envirapido</a></div>;
  return <div className="plans-grid">{catalog.plans.map(plan => <article className={`plan-card ${plan.featured ? "featured" : ""}`} key={plan.code}>{plan.featured && <span className="plan-featured">Plan destacado</span>}<span className="eyebrow">TU OPERACIÓN, A TU MEDIDA</span><h3>{plan.name}</h3><p className="plan-description">{plan.description || "Organiza tus pedidos, comercios y pilotos en una misma plataforma."}</p><div className="plan-price"><strong>{formatMoney(plan.monthlyPrice, plan.currency)}</strong><span>/ mes</span></div><PlanDetails plan={plan} /><a className={`button ${plan.featured ? "" : "secondary"}`} href={portalUrl(`/registro?plan=${encodeURIComponent(plan.code)}`)}>Solicitar este plan <Icon name="arrow" /></a><small>Solicitud y validación manual del pago.</small></article>)}</div>;
}
