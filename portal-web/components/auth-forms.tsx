"use client";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { portalApi, PortalApiError, MutationIntent } from "@/lib/api";
import type { Account, Catalog, PortalAuthOptions, RegisterInput } from "@/lib/types";
import { canRecoverPassword, readAuthOptions, registrationBlockedByEmail, REGISTRATION_EMAIL_UNAVAILABLE } from "@/lib/auth-policy";
import { formatMoney, resolveSelectedPlan } from "@/lib/access";
import { portalUrl, siteUrl } from "@/lib/config";
import { Button, ContactLinks, Field, Notice, PasswordField, RefreshButton } from "./ui";
import { ProfileFields, EMPTY_PROFILE } from "./profile-fields";
import { Icon } from "./icon";

function message(error: unknown): string { return error instanceof Error ? error.message : "No se pudo completar la solicitud. Inténtalo nuevamente."; }
function completedAuthentication() { portalApi.invalidateCsrf(); }

export function LoginForm({ selectedPlan }: { selectedPlan?: string }) {
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const data = new FormData(event.currentTarget);
    setBusy(true); setError("");
    try {
      await portalApi.mutate<Account>("/auth/login", { email: String(data.get("email") || "").trim(), password: String(data.get("password") || ""), rememberMe: data.get("rememberMe") === "on" });
      completedAuthentication();
      window.location.assign(portalUrl(selectedPlan ? `/cuenta/solicitudes?plan=${encodeURIComponent(selectedPlan)}` : "/cuenta"));
    } catch (problem) { setError(message(problem)); setBusy(false); }
  }
  return <><span className="eyebrow">MI CUENTA ENVIRAPIDO</span><h2>Inicia sesión</h2><p className="auth-description">Consulta tu solicitud, tu plan y tus descargas.</p><form onSubmit={submit} className="stack-form">{error && <Notice tone="danger">{error}</Notice>}<Field label="Correo electrónico"><input type="email" name="email" autoComplete="email" placeholder="tu@empresa.com" maxLength={254} required /></Field><PasswordField name="password" autoComplete="current-password" required maxLength={100} /><div className="form-inline"><label className="check-field"><input type="checkbox" name="rememberMe" />Recordar sesión</label><a href={portalUrl("/recuperar-acceso")}>¿Olvidaste tu contraseña?</a></div><Button busy={busy} type="submit" className="full-width">Entrar <Icon name="arrow" /></Button></form><p className="auth-switch">¿Aún no tienes cuenta? <a href={portalUrl(selectedPlan ? `/registro?plan=${encodeURIComponent(selectedPlan)}` : "/registro")}>Crear cuenta</a></p></>;
}

export function RegisterForm({ catalog: initialCatalog, selectedCode, unavailable: initiallyUnavailable }: { catalog: Catalog | null; selectedCode: string | null; unavailable: boolean }) {
  const [catalog, setCatalog] = useState(initialCatalog);
  const [unavailable, setUnavailable] = useState(initiallyUnavailable);
  const [step, setStep] = useState(0);
  const [planCode, setPlanCode] = useState(resolveSelectedPlan(catalog?.plans || [], selectedCode)?.code || "");
  const [profile, setProfile] = useState(EMPTY_PROFILE);
  const [email, setEmail] = useState(""), [password, setPassword] = useState(""), [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false), [uncertain, setUncertain] = useState(false), [error, setError] = useState("");
  const attempt = useRef(new MutationIntent<RegisterInput>());
  const plan = resolveSelectedPlan(catalog?.plans || [], planCode);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return; setError("");
    if (!plan || !catalog) { setError("Selecciona un plan disponible para continuar."); return; }
    if (step < 2) { setStep(value => value + 1); return; }
    if (password !== confirmation) { setError("La confirmación de la contraseña no coincide."); return; }
    const input: RegisterInput = { email: email.trim(), password, confirmPassword: confirmation, planCode: plan.code, planVersion: plan.version, profile, termsVersion: catalog.policyVersions.terms, privacyVersion: catalog.policyVersions.privacy };
    setBusy(true);
    try {
      const intent = attempt.current.prepare(input);
      await portalApi.mutate<Account>("/auth/register", intent.body, { idempotencyKey: intent.key });
      attempt.current.clear(); completedAuthentication(); setPassword(""); setConfirmation("");
      window.location.assign(portalUrl("/cuenta"));
    } catch (problem) {
      if (problem instanceof PortalApiError && problem.status === 409 && ["PLAN_CHANGED", "PLAN_UNAVAILABLE", "POLICIES_CHANGED"].includes(problem.code)) {
        attempt.current.clear(); setUncertain(false); setStep(0); setPlanCode("");
        try { setCatalog(await portalApi.get<Catalog>("/catalog")); setUnavailable(false); } catch { setCatalog(null); setUnavailable(true); }
      }
      else if (problem instanceof PortalApiError && problem.status === 400) { attempt.current.clear(); setUncertain(false); }
      else if (registrationBlockedByEmail(problem)) { attempt.current.clear(); setUncertain(false); }
      else setUncertain(attempt.current.pending);
      setError(registrationBlockedByEmail(problem) ? REGISTRATION_EMAIL_UNAVAILABLE : message(problem)); setBusy(false);
    }
  }
  return <><span className="eyebrow">TU CUENTA WEB</span><h2>Comencemos.</h2><p className="auth-description">Crea tu cuenta y solicita un plan. Administración revisará tu negocio y activará tu acceso.</p><ol className="form-steps" aria-label="Pasos del registro">{["Plan", "Negocio", "Cuenta"].map((label, index) => <li key={label} aria-current={step === index ? "step" : undefined} className={step >= index ? "active" : ""}><span>{index + 1}</span>{label}</li>)}</ol><form onSubmit={submit} className="stack-form">{error && <Notice tone="danger">{error}</Notice>}{uncertain && <Notice tone="warning"><p>Se conserva el mismo intento y sus datos para reintentar. También puedes <a href={portalUrl("/cuenta")}>consultar si tu cuenta ya fue creada</a>.</p></Notice>}<fieldset disabled={busy || uncertain}>{step === 0 && <>{unavailable ? <Notice tone="warning">No se pudo consultar el catálogo. Vuelve a cargarlo antes de enviar tu solicitud.</Notice> : !catalog?.plans.length ? <Notice>Aún no hay planes publicados. Contáctanos para recibir orientación.</Notice> : <Field label="Plan que deseas solicitar"><select value={planCode} onChange={event => setPlanCode(event.target.value)} required><option value="">Selecciona un plan</option>{catalog.plans.map(item => <option value={item.code} key={item.code}>{item.name} · {formatMoney(item.monthlyPrice, item.currency)} / mes</option>)}</select></Field>}{plan && <div className="selected-plan"><Icon name="layers" /><div><strong>{plan.name}</strong><span>{formatMoney(plan.monthlyPrice, plan.currency)} / mes</span></div><span className="badge neutral">Solicitud</span></div>}<p className="form-hint">Tu solicitud quedará pendiente de revisión. Administración confirma el plan y las fechas de vigencia antes de habilitar el CRM y las descargas.</p></>}{step === 1 && <ProfileFields profile={profile} onChange={setProfile} />}{step === 2 && <><div className="selected-plan"><Icon name="layers" /><div><strong>{plan?.name}</strong><span>{profile.businessName}</span></div></div><Field label="Correo electrónico"><input type="email" name="email" autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} required maxLength={254} /></Field><PasswordField autoComplete="new-password" name="password" value={password} onChange={event => setPassword(event.target.value)} minLength={10} maxLength={100} required /><p className="form-hint">Usa entre 10 y 100 caracteres. Esta contraseña permite consultar tu solicitud; no activa el CRM ni las descargas.</p><PasswordField label="Confirmar contraseña" autoComplete="new-password" name="confirmPassword" value={confirmation} onChange={event => setConfirmation(event.target.value)} minLength={10} maxLength={100} required /><label className="check-field terms-check"><input type="checkbox" required /><span>Acepto los <a href={siteUrl("/terminos")} target="_blank" rel="noopener noreferrer">Términos</a> y la <a href={siteUrl("/privacidad")} target="_blank" rel="noopener noreferrer">Política de privacidad</a>.</span></label></>}</fieldset><div className="button-row form-buttons">{step > 0 && <Button type="button" className="secondary" disabled={busy || uncertain} onClick={() => { setStep(value => value - 1); setError(""); }}>Atrás</Button>}<Button type="submit" busy={busy} disabled={!plan || unavailable}>{step < 2 ? "Continuar" : uncertain ? "Reintentar la misma solicitud" : "Crear cuenta y solicitar plan"}<Icon name="arrow" /></Button></div>{unavailable && <RefreshButton />}</form><p className="auth-switch">¿Ya tienes una cuenta? <a href={portalUrl(planCode ? `/acceso?plan=${encodeURIComponent(planCode)}` : "/acceso")}>Inicia sesión</a></p></>;
}

export function ForgotPasswordForm() {
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [result, setResult] = useState("");
  const [options, setOptions] = useState<PortalAuthOptions | null>(null), [checking, setChecking] = useState(true);
  useEffect(() => {
    const abort = new AbortController();
    portalApi.get<unknown>("/auth/options", abort.signal).then(value => {
      if (!abort.signal.aborted) setOptions(readAuthOptions(value));
    }).catch(() => {
      if (!abort.signal.aborted) setOptions(null);
    }).finally(() => { if (!abort.signal.aborted) setChecking(false); });
    return () => abort.abort();
  }, []);
  const automated = canRecoverPassword(options);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !automated) return; setError(""); setResult(""); setBusy(true);
    const data = new FormData(event.currentTarget);
    try { const response = await portalApi.mutate<{ message: string }>("/auth/forgot-password", { email: String(data.get("email") || "").trim() }); setResult(response.message); }
    catch (problem) { setError(message(problem)); } finally { setBusy(false); }
  }
  return <><span className="eyebrow">RECUPERA TU ACCESO WEB</span><h2>Volvamos a tu cuenta.</h2>
    {checking ? <Notice><p>Consultando las opciones de recuperación…</p></Notice> : automated ? <><p className="auth-description">Escribe el correo de tu cuenta del portal para recibir las indicaciones.</p><form className="stack-form" onSubmit={submit}>{error && <Notice tone="danger">{error}</Notice>}{result && <Notice tone="success">{result}</Notice>}<Field label="Correo electrónico"><input name="email" type="email" autoComplete="email" maxLength={254} required /></Field><Button type="submit" busy={busy}>Enviar indicaciones <Icon name="arrow" /></Button></form></> : <><p className="auth-description">Contacta con administración para recuperar el acceso a tu cuenta del portal.</p><Notice><p>La recuperación automática por correo no está disponible. Indica el correo con el que registraste tu negocio para que administración pueda ayudarte.</p></Notice><ContactLinks /></>}
    <p className="auth-switch"><a href={portalUrl("/acceso")}>Volver al acceso</a></p></>;
}

export function TokenForm({ mode, userId, token }: { mode: "confirm" | "reset"; userId: string; token: string }) {
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [result, setResult] = useState("");
  const valid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(userId) && token.length > 0 && token.length <= 4096;
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !valid) return; setBusy(true); setError("");
    const form = event.currentTarget;
    const data = new FormData(form);
    const password = String(data.get("password") || ""), confirmPassword = String(data.get("confirmPassword") || "");
    if (mode === "reset" && password !== confirmPassword) { setError("La confirmación de la contraseña no coincide."); setBusy(false); return; }
    try {
      const response = await portalApi.mutate<{ message: string }>(mode === "confirm" ? "/auth/confirm-email" : "/auth/reset-password", mode === "confirm" ? { userId, token } : { userId, token, password, confirmPassword });
      completedAuthentication(); setResult(response.message);
      window.history.replaceState(null, "", mode === "confirm" ? "/confirmar-correo" : "/restablecer-acceso");
      form.reset();
    } catch (problem) { setError(message(problem)); } finally { setBusy(false); }
  }
  return <><span className="eyebrow">TU CUENTA ENVIRAPIDO</span><h2>{mode === "confirm" ? "Confirma tu correo." : "Crea una nueva contraseña."}</h2><p className="auth-description">{mode === "confirm" ? "Este enlace verifica el correo de tu cuenta. Administración gestiona por separado la activación de tu plan." : "Esta contraseña corresponde únicamente a tu cuenta del portal."}</p>{!valid ? <Notice tone="warning"><p>El enlace está incompleto. Solicita uno nuevo desde tu cuenta o desde la recuperación de acceso.</p></Notice> : result ? <Notice tone="success">{result}</Notice> : <form className="stack-form" onSubmit={submit}>{error && <Notice tone="danger">{error}</Notice>}{mode === "reset" && <><PasswordField name="password" label="Nueva contraseña" autoComplete="new-password" minLength={10} maxLength={100} required /><PasswordField name="confirmPassword" label="Confirmar contraseña" autoComplete="new-password" minLength={10} maxLength={100} required /><p className="form-hint">Entre 10 y 100 caracteres.</p></>}<Button type="submit" busy={busy}>{mode === "confirm" ? "Confirmar correo" : "Guardar contraseña"}<Icon name="check" /></Button></form>}<p className="auth-switch"><a href={portalUrl(mode === "confirm" ? "/cuenta" : "/acceso")}>{mode === "confirm" ? "Consultar mi cuenta" : "Iniciar sesión"}</a></p></>;
}
