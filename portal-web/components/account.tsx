"use client";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import Image from "next/image";
import type { Access, Account, Catalog, DownloadRelease, PaymentReportInput, PlanRequest, Profile } from "@/lib/types";
import { portalApi, PortalApiError, MutationIntent } from "@/lib/api";
import { contactEmailBadge } from "@/lib/auth-policy";
import { accessPresentation, canOpenCrm, formatDate, formatInclusiveEnd, formatMoney, paymentStatus, requestStatus, resolveSelectedPlan, safeDownloadPath } from "@/lib/access";
import { CONTACT, CRM_LOGIN_URL, portalUrl, siteUrl } from "@/lib/config";
import { Brand, BoxMark, Button, ContactLinks, EmptyState, Field, Notice } from "./ui";
import { Icon, type IconName } from "./icon";
import { ProfileFields } from "./profile-fields";

export type AccountSection = "inicio" | "solicitudes" | "suscripcion" | "descargas" | "perfil" | "ayuda";
const sections: { key: AccountSection; href: string; title: string; icon: IconName }[] = [
  { key: "inicio", href: "/cuenta", title: "Mi cuenta", icon: "user" },
  { key: "solicitudes", href: "/cuenta/solicitudes", title: "Mis solicitudes", icon: "box" },
  { key: "suscripcion", href: "/cuenta/suscripcion", title: "Mi suscripción", icon: "layers" },
  { key: "descargas", href: "/cuenta/descargas", title: "Descargas", icon: "download" },
  { key: "perfil", href: "/cuenta/perfil", title: "Datos del negocio", icon: "building" },
  { key: "ayuda", href: "/cuenta/ayuda", title: "Ayuda", icon: "help" }
];
function errorText(error: unknown) { return error instanceof Error ? error.message : "No se pudo completar la consulta."; }
function Badge({ children, tone = "neutral" }: { children: React.ReactNode; tone?: string }) { return <span className={`badge ${tone}`}>{children}</span>; }

export function AccountArea({ section, selectedPlan }: { section: AccountSection; selectedPlan?: string }) {
  const [account, setAccount] = useState<Account | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState("");
  const [unauthenticated, setUnauthenticated] = useState(false), [loggingOut, setLoggingOut] = useState(false), [menuOpen, setMenuOpen] = useState(false);
  const generation = useRef(0);
  const refresh = useCallback(async () => {
    const version = ++generation.current;
    try {
      const next = await portalApi.get<Account>("/account");
      if (version === generation.current) { setAccount(next); setError(""); setUnauthenticated(false); }
    } catch (problem) {
      if (version === generation.current) {
        if (problem instanceof PortalApiError && problem.status === 401) { setUnauthenticated(true); setAccount(null); }
        else setError(errorText(problem));
      }
    } finally { if (version === generation.current) setLoading(false); }
  }, []);
  const applyAccount = useCallback((next: Account) => { generation.current++; setAccount(next); setError(""); }, []);
  useEffect(() => { const close = (event: KeyboardEvent) => { if (event.key === "Escape") setMenuOpen(false); }; document.addEventListener("keydown", close); return () => document.removeEventListener("keydown", close); }, []);
  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => { if (!document.hidden && !document.activeElement?.closest("form")) void refresh(); }, 30000);
    const resume = () => { if (!document.hidden) void refresh(); };
    document.addEventListener("visibilitychange", resume);
    return () => { generation.current++; window.clearInterval(timer); document.removeEventListener("visibilitychange", resume); };
  }, [refresh]);
  async function logout() {
    if (loggingOut) return; setLoggingOut(true);
    try { await portalApi.mutate<void>("/auth/logout", {}); portalApi.invalidateCsrf(); window.location.assign(portalUrl("/acceso")); }
    catch (problem) { setError(errorText(problem)); setLoggingOut(false); }
  }
  if (loading) return <main id="contenido" className="account-loading"><Brand /><span className="spinner" aria-hidden="true" /><p role="status">Consultando tu cuenta…</p></main>;
  if (!account) return <main id="contenido" className="account-loading"><Brand /><BoxMark /><h1>{unauthenticated ? "Tu cuenta te espera." : "No pudimos consultar tu cuenta."}</h1>{error && <Notice tone="warning">{error}</Notice>}<p>{unauthenticated ? "Inicia sesión con tu acceso del portal para continuar." : "Revisa tu conexión y vuelve a intentarlo."}</p><div className="button-row"><a className="button" href={portalUrl("/acceso")}>Iniciar sesión</a>{!unauthenticated && <Button className="secondary" onClick={() => void refresh()}>Volver a intentar</Button>}</div></main>;
  const presentation = accessPresentation(account.access);
  const sectionTitle = sections.find(item => item.key === section)!.title;
  return <div className="account-page"><aside className={menuOpen ? "account-sidebar open" : "account-sidebar"}><Brand /><nav id="account-navigation" aria-label="Tu cuenta">{sections.map(item => <a href={portalUrl(item.href)} key={item.key} aria-current={section === item.key ? "page" : undefined}><Icon name={item.icon} />{item.title}</a>)}</nav><div className="sidebar-account"><span>{account.profile.businessName}</span><small>{account.user.email}</small><button type="button" className="sidebar-logout" disabled={loggingOut} onClick={() => void logout()}><Icon name="logout" />{loggingOut ? "Cerrando sesión…" : "Cerrar sesión"}</button></div><a className="sidebar-home" href={siteUrl("/")}>Volver al sitio <Icon name="external" /></a></aside><div className="account-body"><header className="account-mobile-header"><Brand compact /><button className="mobile-toggle" type="button" onClick={() => setMenuOpen(value => !value)} aria-controls="account-navigation" aria-expanded={menuOpen} aria-label={menuOpen ? "Cerrar menú" : "Abrir menú"}><Icon name={menuOpen ? "close" : "menu"} /></button></header><main id="contenido" className="account-main"><header className="account-heading"><div><span className="eyebrow">MI ESPACIO ENVIRAPIDO</span><h1>{section === "inicio" ? "Tu espacio Envirapido." : sectionTitle}</h1><p>{section === "inicio" ? `Bienvenido a tu cuenta, ${account.profile.responsibleName}.` : "Tu solicitud, tu acceso y tus recursos en un mismo lugar."}</p></div><Badge tone={presentation.tone}><Icon name={account.access.allowed ? "check" : "clock"} />{presentation.title}</Badge></header>{error && <Notice tone="warning"><p>{error} <button type="button" className="inline-button" onClick={() => void refresh()}>Actualizar</button></p></Notice>}{!account.access.allowed && <Notice tone={presentation.tone} title={presentation.title}><p>{presentation.message}</p>{account.access.reason === "EMAIL_UNCONFIRMED" && <ResendConfirmation onConfirmedCheck={refresh} />}</Notice>}{section === "inicio" && <Overview account={account} />}{section === "solicitudes" && <Requests account={account} selectedPlan={selectedPlan} onChange={refresh} />}{section === "suscripcion" && <Subscription account={account} />}{section === "descargas" && <Downloads access={account.access} onAccessChange={refresh} />}{section === "perfil" && <ProfileEditor account={account} onSaved={applyAccount} />}{section === "ayuda" && <Help />}<div className="account-support"><strong>¿Necesitas ayuda?</strong><ContactLinks compact /></div></main></div></div>;
}

function AccessDates({ access }: { access: Access }) {
  return <div className="access-dates"><div><small>Inicio de la vigencia</small><strong>{formatDate(access.startsAt)}</strong></div><div><small>Último día de la vigencia</small><strong>{formatInclusiveEnd(access.endsAt)}</strong></div></div>;
}
function Overview({ account }: { account: Account }) {
  const latest = [...account.requests].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))[0];
  const presentation = accessPresentation(account.access);
  return <><section className="panel subscription-strip"><span className="icon-tile"><Icon name="layers" /></span><div><h2>Tu acceso al portal</h2><p>{latest ? `Último plan solicitado: ${latest.plan.name}` : "Consulta el estado de tu solicitud."}</p></div><AccessDates access={account.access} /></section><div className="account-action-grid"><article className="panel action-card"><Image src="/illustrations/ultima-milla-ciudad.png" alt="Ilustración de la app y su operación de entregas" width={1536} height={1024} /><div><h2>App para pilotos y comercios</h2><p>Consulta y descarga la versión Android privada que se encuentre publicada para tu acceso.</p>{account.access.allowed && account.access.reason === "ACTIVE" ? <a href={portalUrl("/cuenta/descargas")} className="button full-width"><Icon name="download" />Ver descargas</a> : <Button className="full-width" disabled title={presentation.message} icon="lock">Descarga pendiente de habilitación</Button>}</div></article><article className="panel action-card"><Image src="/illustrations/operacion-conectada.png" alt="Ilustración del centro de operaciones" width={1536} height={1024} /><div><h2>Centro de operaciones</h2><p>Ingresa con las credenciales operativas que te entregó administración.</p>{canOpenCrm(account.access) ? <a className="button secondary full-width" href={CRM_LOGIN_URL} target="_blank" rel="noopener noreferrer"><Icon name="external" />Entrar al CRM</a> : <Button className="secondary full-width" disabled title={presentation.message} icon="lock">Acceso pendiente de habilitación</Button>}</div></article></div><Notice><p>Tu cuenta web y tu acceso al CRM son independientes. Las credenciales de la operación se gestionan y entregan por separado.</p></Notice>{latest && <section className="panel"><div className="panel-heading"><h2>Tu solicitud más reciente</h2><a className="text-link" href={portalUrl("/cuenta/solicitudes")}>Ver solicitudes <Icon name="arrow" /></a></div><div className="request-summary"><strong>{latest.plan.name}</strong><Badge tone={latest.status === "Approved" ? "success" : "neutral"}>{requestStatus(latest.status)}</Badge><span>{paymentStatus(latest.paymentStatus)}</span></div>{latest.publicMessage && <p className="public-message">{latest.publicMessage}</p>}</section>}</>;
}
function ResendConfirmation({ onConfirmedCheck }: { onConfirmedCheck: () => Promise<void> }) {
  const [busy, setBusy] = useState(false), [result, setResult] = useState("");
  async function resend() { setBusy(true); try { const response = await portalApi.mutate<{ message: string }>("/auth/resend-confirmation", {}); setResult(response.message); } catch (error) { setResult(errorText(error)); } finally { setBusy(false); } }
  return <div className="confirmation-actions"><Button type="button" className="secondary small" busy={busy} onClick={() => void resend()}>Enviar de nuevo</Button><button type="button" className="inline-button" onClick={() => void onConfirmedCheck()}>Ya confirmé mi correo</button>{result && <p>{result}</p>}</div>;
}

function Requests({ account, selectedPlan, onChange }: { account: Account; selectedPlan?: string; onChange: () => Promise<void> }) {
  return <><section className="panel"><div className="panel-heading"><div><h2>Solicita un plan o una renovación</h2><p>Selecciona una opción del catálogo para que administración la revise.</p></div></div><NewRequestForm disabled={account.user.status !== "Active"} selectedPlan={selectedPlan} onCreated={onChange} /></section><section className="requests-list" aria-label="Historial de solicitudes">{account.requests.length ? account.requests.map(request => <article className="panel request-card" key={request.id}><div className="panel-heading"><div><span className="eyebrow">SOLICITUD · {formatDate(request.createdAt)}</span><h2>{request.plan.name}</h2></div><Badge tone={request.status === "Approved" ? "success" : request.status === "Rejected" ? "danger" : "neutral"}>{requestStatus(request.status)}</Badge></div><div className="request-amounts"><div><small>Precio mensual solicitado</small><strong>{formatMoney(request.plan.monthlyPrice, request.plan.currency)}</strong></div><div><small>Importe confirmado</small><strong>{formatMoney(request.confirmedAmount, request.plan.currency)}</strong></div><div><small>Pendiente de confirmar</small><strong>{formatMoney(request.amountDue, request.plan.currency)}</strong></div></div><div className="request-payment-status"><Icon name="wallet" />{paymentStatus(request.paymentStatus)}</div>{request.paymentReviewMessage && <Notice tone={request.paymentStatus === "Rejected" ? "warning" : "neutral"} title="Revisión del pago"><p>{request.paymentReviewMessage}</p></Notice>}{request.publicMessage && <Notice><p>{request.publicMessage}</p></Notice>}{["Rejected", "Cancelled"].includes(request.status) ? null : request.amountDue > 0 && account.user.status === "Active" ? <details className="payment-details"><summary>Reportar un pago realizado <Icon name="arrow" /></summary><PaymentReport request={request} onReported={onChange} /></details> : null}<small className="request-id">Referencia de solicitud: {request.id}</small></article>) : <div className="panel"><EmptyState title="Aún no hay solicitudes">Tu historial aparecerá aquí cuando envíes una solicitud de plan.</EmptyState></div>}</section></>;
}
function NewRequestForm({ selectedPlan, onCreated, disabled }: { selectedPlan?: string; onCreated: () => Promise<void>; disabled: boolean }) {
  const [catalog, setCatalog] = useState<Catalog | null>(null), [code, setCode] = useState(""), [busy, setBusy] = useState(false), [error, setError] = useState(""), [result, setResult] = useState(""), [locked, setLocked] = useState(false);
  const intent = useRef(new MutationIntent<{ planCode: string; planVersion: string }>());
  useEffect(() => { const abort = new AbortController(); portalApi.get<Catalog>("/catalog", abort.signal).then(next => { setCatalog(next); setCode(resolveSelectedPlan(next.plans, selectedPlan || null)?.code || ""); }).catch(problem => { if (!abort.signal.aborted) setError(errorText(problem)); }); return () => abort.abort(); }, [selectedPlan]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const plan = resolveSelectedPlan(catalog?.plans || [], code); if (!plan || busy || disabled) return;
    setBusy(true); setError(""); setResult("");
    try { const current = intent.current.prepare({ planCode: plan.code, planVersion: plan.version }); await portalApi.mutate<PlanRequest>("/requests", current.body, { idempotencyKey: current.key }); intent.current.clear(); setLocked(false); setResult("Tu solicitud fue recibida y está pendiente de revisión."); await onCreated(); }
    catch (problem) {
      if (problem instanceof PortalApiError && problem.status === 409 && ["PLAN_CHANGED", "PLAN_UNAVAILABLE"].includes(problem.code)) {
        intent.current.clear(); setLocked(false); setCode("");
        try { setCatalog(await portalApi.get<Catalog>("/catalog")); } catch { setCatalog(null); }
      } else if (problem instanceof PortalApiError && problem.status === 409 && problem.code === "REQUEST_PENDING") { intent.current.clear(); setLocked(false); await onCreated(); }
      else if (problem instanceof PortalApiError && problem.status === 400) { intent.current.clear(); setLocked(false); }
      else setLocked(intent.current.pending);
      setError(errorText(problem));
    } finally { setBusy(false); }
  }
  return <form onSubmit={submit} className="stack-form">{error && <Notice tone="warning">{error}</Notice>}{result && <Notice tone="success">{result}</Notice>}{catalog && !catalog.plans.length ? <p>Aún no hay planes publicados. Contacta a administración para recibir orientación.</p> : <div className="new-request-row"><Field label="Plan a solicitar"><select value={code} required onChange={event => setCode(event.target.value)} disabled={disabled || busy || locked || !catalog}><option value="">{catalog ? "Selecciona un plan" : "Consultando catálogo…"}</option>{catalog?.plans.map(plan => <option value={plan.code} key={plan.code}>{plan.name} · {formatMoney(plan.monthlyPrice, plan.currency)} / mes</option>)}</select></Field><Button type="submit" busy={busy} disabled={disabled || !code}>{locked ? "Reintentar solicitud" : "Enviar solicitud"}<Icon name="arrow" /></Button></div>}{locked && <p className="form-hint">El intento conserva sus datos e identificador. Reintenta la misma solicitud antes de cambiar de plan.</p>}</form>;
}

function PaymentReport({ request, onReported }: { request: PlanRequest; onReported: () => Promise<void> }) {
  const [busy, setBusy] = useState(false), [locked, setLocked] = useState(false), [error, setError] = useState(""), [result, setResult] = useState("");
  const intent = useRef(new MutationIntent<PaymentReportInput>());
  const payload = useRef<PaymentReportInput | null>(null);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const data = new FormData(event.currentTarget);
    const input = payload.current || { amount: Number(data.get("amount")), paidOn: String(data.get("paidOn") || ""), method: String(data.get("method") || ""), reference: String(data.get("reference") || "").trim(), notes: String(data.get("notes") || "").trim() || null };
    setBusy(true); setError("");
    try { const current = intent.current.prepare(input); payload.current = current.body; await portalApi.mutate(`/requests/${request.id}/payment-reports`, current.body, { idempotencyKey: current.key }); intent.current.clear(); payload.current = null; setLocked(false); setResult("Reporte recibido. Administración debe verificar el pago; todavía no representa una confirmación ni una activación."); await onReported(); }
    catch (problem) { if (problem instanceof PortalApiError && (problem.status === 400 || (problem.status === 409 && problem.code === "PAYMENT_CONFLICT"))) { intent.current.clear(); payload.current = null; setLocked(false); if (problem.status === 409) await onReported(); } else setLocked(intent.current.pending); setError(errorText(problem)); } finally { setBusy(false); }
  }
  return <form className="stack-form payment-form" onSubmit={submit}><p>Registra únicamente un pago que ya realizaste fuera del portal. Conserva su referencia para la revisión.</p>{error && <Notice tone="danger">{error}</Notice>}{result && <Notice tone="success">{result}</Notice>}{!result && <><fieldset disabled={busy || locked}><div className="form-grid"><Field label={`Importe (${request.plan.currency})`}><input name="amount" type="number" min="0.01" step="0.01" defaultValue={request.amountDue} required /></Field><Field label="Fecha del pago"><input name="paidOn" type="date" required /></Field><Field label="Método"><select name="method" required><option value="">Selecciona el método</option><option value="Transferencia">Transferencia</option><option value="Efectivo">Efectivo</option><option value="Tarjeta">Tarjeta (pago externo)</option><option value="Otro">Otro</option></select></Field><Field label="Referencia del pago"><input name="reference" maxLength={100} required /></Field><Field label="Nota (opcional)" className="full"><textarea name="notes" rows={2} maxLength={500} /></Field></div></fieldset><Button type="submit" busy={busy}>{locked ? "Reintentar el mismo reporte" : "Enviar reporte para revisión"}</Button></>}</form>;
}

function Subscription({ account }: { account: Account }) {
  const presentation = accessPresentation(account.access);
  return <><section className="panel"><div className="panel-heading"><h2>Vigencia de tu acceso al portal</h2><Badge tone={presentation.tone}>{presentation.title}</Badge></div><AccessDates access={account.access} /><p className="form-hint">Las fechas se muestran en hora de República Dominicana. El último día está incluido en la vigencia autorizada.</p><Notice><p>Esta vigencia pertenece al portal. No cambia automáticamente la cobertura, pagos, pedidos o credenciales de la operación en el CRM.</p></Notice><a className="button secondary" href={portalUrl("/cuenta/solicitudes")}>Solicitar un plan o una renovación <Icon name="arrow" /></a></section><section className="panel"><h2>Planes solicitados</h2>{account.requests.length ? <ul className="subscription-history">{account.requests.map(request => <li key={request.id}><div><strong>{request.plan.name}</strong><small>{formatDate(request.createdAt)}</small></div><Badge>{requestStatus(request.status)}</Badge><span>{paymentStatus(request.paymentStatus)}</span></li>)}</ul> : <EmptyState title="Aún no hay un plan solicitado" icon="layers" />}</section></>;
}

function Downloads({ access, onAccessChange }: { access: Access; onAccessChange: () => Promise<void> }) {
  const [release, setRelease] = useState<DownloadRelease | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState("");
  const allowed = access.allowed === true && access.reason === "ACTIVE";
  useEffect(() => {
    if (!allowed) { setLoading(false); setRelease(null); return; }
    const abort = new AbortController(); setLoading(true); setRelease(null); setError("");
    portalApi.get<DownloadRelease>("/downloads/current", abort.signal).then(setRelease).catch(problem => { if (abort.signal.aborted) return; if (problem instanceof PortalApiError && problem.status === 404) setError(""); else { setError(errorText(problem)); if (problem instanceof PortalApiError && problem.status === 403) void onAccessChange(); } }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [allowed, onAccessChange]);
  const path = release ? safeDownloadPath(release, access) : null;
  if (!allowed) return <section className="panel"><EmptyState title="Descargas pendientes de habilitación" icon="lock">{accessPresentation(access).message}</EmptyState><a className="button secondary" href={portalUrl("/cuenta/solicitudes")}>Consultar mi solicitud</a></section>;
  return <section className="panel downloads-panel"><div className="panel-heading"><div><span className="eyebrow">TU APP ANDROID</span><h2>Envirapido para tu equipo</h2></div><BoxMark /></div>{loading ? <p role="status">Consultando la versión disponible…</p> : error ? <Notice tone="warning">{error}</Notice> : !release ? <EmptyState title="Aún no hay una versión privada publicada" icon="download">Administración debe publicar una versión para que puedas descargarla desde tu cuenta.</EmptyState> : <><div className="release-meta"><div><small>Versión</small><strong>{release.versionName} ({release.versionCode})</strong></div><div><small>Tamaño</small><strong>{new Intl.NumberFormat("es-DO", { maximumFractionDigits: 1 }).format(release.bytes / 1048576)} MB</strong></div><div><small>Publicada</small><strong>{formatDate(release.publishedAt)}</strong></div></div>{path ? <a className="button" href={path}><Icon name="download" />Descargar APK</a> : <Notice tone="warning">La información de descarga necesita revisión. Contacta a administración.</Notice>}<details className="integrity-details"><summary>Información de integridad</summary><p>SHA-256 del archivo publicado:</p><code>{release.sha256}</code></details><div className="installation-note"><h3>Antes de instalar</h3><ol><li>Descarga el archivo desde tu cuenta.</li><li>En Android, abre el APK y permite la instalación desde el navegador si el sistema lo solicita.</li><li>Si ya tienes Envirapido, actualiza sobre la instalación existente, conservando sus datos.</li><li>Inicia la app con las credenciales operativas que te entregó administración.</li></ol></div></>}</section>;
}

function ProfileEditor({ account, onSaved }: { account: Account; onSaved: (account: Account) => void }) {
  const emailBadge = contactEmailBadge(account.user);
  const [profile, setProfile] = useState<Profile>(account.profile), [busy, setBusy] = useState(false), [error, setError] = useState(""), [result, setResult] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (busy) return; setBusy(true); setError(""); setResult(""); try { const next = await portalApi.mutate<Account>("/account", profile, { method: "PUT" }); onSaved(next); setProfile(next.profile); setResult("Los datos de tu negocio fueron actualizados."); } catch (problem) { setError(errorText(problem)); } finally { setBusy(false); } }
  return <section className="panel profile-panel"><div className="panel-heading"><div><h2>Datos de tu negocio</h2><p>Esta información ayuda a administración a atender tu solicitud.</p></div></div><div className="profile-email"><Icon name="mail" /><strong>{account.user.email}</strong><Badge tone={emailBadge.tone}>{emailBadge.label}</Badge></div><form onSubmit={submit} className="stack-form">{error && <Notice tone="danger">{error}</Notice>}{result && <Notice tone="success">{result}</Notice>}<fieldset disabled={busy || account.user.status !== "Active"}><ProfileFields profile={profile} onChange={setProfile} /></fieldset><Button type="submit" busy={busy} disabled={account.user.status !== "Active"}>Guardar datos <Icon name="check" /></Button></form></section>;
}
function Help() { return <section className="panel help-panel"><span className="icon-tile"><Icon name="help" /></span><h2>Estamos para acompañarte.</h2><p>Para consultas sobre tu solicitud, pago reportado, vigencia o descarga, contacta a administración con el correo de tu cuenta.</p><ContactLinks /><a className="button secondary" href={CONTACT.whatsappHref} target="_blank" rel="noopener noreferrer">Conversar por WhatsApp <Icon name="external" /></a><Notice><p>Para utilizar el CRM y la app necesitas las credenciales operativas entregadas por administración. Tu cuenta del portal permite consultar tu acceso comercial.</p></Notice></section>; }
