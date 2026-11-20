"use client";

import { useEffect, useState, type ReactNode } from "react";
import { usePathname } from "next/navigation";
import { Brand, ContactLinks } from "./ui";
import { Icon } from "./icon";
import { CONTACT, portalUrl, siteUrl } from "@/lib/config";

const navigation = [["/funciones", "Funciones"], ["/#como-funciona", "Cómo funciona"], ["/planes", "Planes"], ["/contacto", "Contacto"]];
export function SiteHeader() {
  const [open, setOpen] = useState(false);
  const pathname = usePathname();
  useEffect(() => { function close(event: KeyboardEvent) { if (event.key === "Escape") setOpen(false); } document.addEventListener("keydown", close); return () => document.removeEventListener("keydown", close); }, []);
  return <header className="site-header"><div className="container header-inner"><Brand /><button className="mobile-toggle" type="button" aria-label={open ? "Cerrar menú" : "Abrir menú"} aria-expanded={open} aria-controls="site-navigation" onClick={() => setOpen(value => !value)}><Icon name={open ? "close" : "menu"} /></button><nav id="site-navigation" className={open ? "site-nav open" : "site-nav"} aria-label="Navegación principal">{navigation.map(([href, label]) => <a key={href} href={siteUrl(href)} aria-current={pathname === href ? "page" : undefined} onClick={() => setOpen(false)}>{label}</a>)}<div className="header-actions"><a className="button secondary" href={portalUrl("/acceso")}>Acceder</a><a className="button" href={siteUrl("/planes")}>Ver planes</a></div></nav></div></header>;
}
export function SiteFooter() {
  return <footer className="site-footer"><div className="container"><div className="footer-main"><div><Brand /><p>Software para conectar comercios, pilotos<br />y tu operación de última milla.</p></div><div><h3>Conoce Envirapido</h3><a href={siteUrl("/funciones")}>Funciones</a><a href={siteUrl("/planes")}>Planes</a><a href={siteUrl("/contacto")}>Solicitar una demo</a></div><div><h3>Tu cuenta</h3><a href={portalUrl("/acceso")}>Acceder al portal</a><a href={portalUrl("/registro")}>Solicitar un plan</a><a href={portalUrl("/cuenta/ayuda")}>Ayuda</a></div></div><div className="footer-contact"><ContactLinks /></div><div className="footer-bottom"><span>© {new Date().getFullYear()} Envirapido</span><div><a href={siteUrl("/privacidad")}>Privacidad</a><a href={siteUrl("/terminos")}>Términos</a></div></div></div></footer>;
}
export function MarketingLayout({ children }: { children: ReactNode }) {
  return <><SiteHeader /><main id="contenido">{children}</main><SiteFooter /></>;
}
export function ContactCta() {
  return <section className="section"><div className="container"><div className="contact-cta"><div><span className="eyebrow">HABLEMOS DE TU OPERACIÓN</span><h2>El próximo paso<br />comienza contigo.</h2><p>Cuéntanos cómo trabajas y conoce Envirapido.</p></div><div className="cta-actions"><a className="button" href={siteUrl("/contacto")}>Solicitar una demo <Icon name="arrow" /></a><a className="text-link" href={CONTACT.whatsappHref} target="_blank" rel="noopener noreferrer">Escríbenos por WhatsApp <Icon name="external" /></a></div></div></div></section>;
}
