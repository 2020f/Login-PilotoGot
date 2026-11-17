import Image from "next/image";
import type { ReactNode } from "react";
import { Brand, BoxMark } from "./ui";
import { Icon } from "./icon";
import { siteUrl } from "@/lib/config";

export function AuthShell({ children, title = "Cada entrega, más simple.", description = "Elige tu plan y conecta tu negocio con Envirapido.", wide = false }: { children: ReactNode; title?: string; description?: string; wide?: boolean }) {
  return <div className="auth-page"><div className="auth-top container"><Brand /><a className="text-link" href={siteUrl("/")}>Volver al inicio <Icon name="arrow" /></a></div><main id="contenido" className={`auth-main container ${wide ? "wide" : ""}`}><section className="auth-story"><span className="eyebrow">TU OPERACIÓN COMIENZA AQUÍ</span><h1>{title}</h1><p>{description}</p><div className="auth-benefits"><span><Icon name="route" />Solicitudes claras</span><span><Icon name="shield" />Acceso bajo aprobación</span></div><Image src="/illustrations/ultima-milla-ciudad.png" alt="Ilustración de comercios y un piloto conectados por una ruta" width={1536} height={1024} className="auth-illustration" /></section><section className="auth-form-area"><div className="auth-card"><BoxMark />{children}</div><div className="auth-separation"><Icon name="info" /><span>Tu cuenta web y tu acceso al CRM son independientes.</span></div></section></main><footer className="auth-footer container"><a href={siteUrl("/privacidad")}>Privacidad</a><span aria-hidden="true">|</span><a href={siteUrl("/terminos")}>Términos</a></footer></div>;
}
