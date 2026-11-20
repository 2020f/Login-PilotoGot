"use client";

import { useState, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode } from "react";
import Image from "next/image";
import { Icon, type IconName } from "./icon";
import { CONTACT, siteUrl } from "@/lib/config";

export function Brand({ compact = false }: { compact?: boolean }) {
  return <a className={compact ? "brand compact" : "brand"} href={siteUrl("/")} aria-label="Envirapido, inicio"><Image src="/branding/logo-original.png" alt="Envirapido" width={2172} height={724} priority /></a>;
}
export function BoxMark({ className = "" }: { className?: string }) {
  return <Image className={`box-mark ${className}`} src="/branding/simbolo-caja.png" alt="" aria-hidden="true" width={1254} height={1254} />;
}
export function Button({ children, className = "", busy, icon, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { busy?: boolean; icon?: IconName }) {
  return <button {...props} className={`button ${className}`} disabled={busy || props.disabled} aria-busy={busy || undefined}>{busy ? <span className="spinner" aria-hidden="true" /> : icon ? <Icon name={icon} /> : null}{children}</button>;
}
export function Notice({ children, tone = "neutral", title }: { children: ReactNode; tone?: "neutral" | "success" | "warning" | "danger"; title?: string }) {
  return <div className={`notice ${tone}`} role={tone === "danger" ? "alert" : "status"}><Icon name={tone === "success" ? "check" : tone === "danger" || tone === "warning" ? "alert" : "info"} /><div>{title && <strong>{title}</strong>}{children}</div></div>;
}
export function EmptyState({ title, children, icon = "box" }: { title: string; children?: ReactNode; icon?: IconName }) {
  return <div className="empty-state"><span className="icon-tile"><Icon name={icon} /></span><h3>{title}</h3>{children && <p>{children}</p>}</div>;
}
export function Field({ label, children, hint, className = "" }: { label: string; children: ReactNode; hint?: string; className?: string }) {
  return <label className={`field ${className}`}><span>{label}</span>{children}{hint && <small>{hint}</small>}</label>;
}
export function PasswordField({ label = "Contraseña", ...props }: InputHTMLAttributes<HTMLInputElement> & { label?: string }) {
  const [visible, setVisible] = useState(false);
  return <Field label={label}><span className="password-input"><input {...props} type={visible ? "text" : "password"} /><button type="button" onClick={() => setVisible(value => !value)} aria-label={visible ? "Ocultar contraseña" : "Mostrar contraseña"} aria-pressed={visible}><Icon name={visible ? "eyeOff" : "eye"} /></button></span></Field>;
}
export function ContactLinks({ compact = false }: { compact?: boolean }) {
  return <div className={`contact-links ${compact ? "compact" : ""}`}><a href={CONTACT.phoneHref}><Icon name="phone" /><span>{CONTACT.phone}</span></a><a href={`mailto:${CONTACT.email}`}><Icon name="mail" /><span>{CONTACT.email}</span></a></div>;
}
export function RefreshButton() {
  return <Button className="secondary" icon="refresh" onClick={() => window.location.reload()}>Volver a intentar</Button>;
}
