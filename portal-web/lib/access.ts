import type { Access, DownloadRelease, PortalPlan } from "./types.ts";
import { CRM_LOGIN_URL } from "./config.ts";

const reasons: Record<string, { title: string; message: string; tone: "success" | "warning" | "neutral" | "danger" }> = {
  ACTIVE: { title: "Acceso habilitado", message: "Tu cuenta tiene acceso a los recursos autorizados durante la vigencia indicada.", tone: "success" },
  EMAIL_UNCONFIRMED: { title: "Confirma tu correo", message: "Revisa tu bandeja de entrada para confirmar el correo de tu cuenta.", tone: "warning" },
  REQUEST_PENDING: { title: "Tu solicitud está en revisión", message: "Administración revisará los datos de tu negocio y te indicará cómo continuar.", tone: "warning" },
  PAYMENT_PENDING: { title: "Pago pendiente de validación", message: "Los pagos se revisan manualmente. Un reporte de pago todavía no habilita el acceso.", tone: "warning" },
  NOT_CONFIGURED: { title: "Activación pendiente", message: "Administración debe completar la aprobación y la vigencia de tu acceso.", tone: "neutral" },
  NOT_STARTED: { title: "Tu vigencia comenzará pronto", message: "Podrás utilizar los recursos cuando llegue la fecha de inicio definida por administración.", tone: "neutral" },
  EXPIRED: { title: "Tu vigencia ha finalizado", message: "Consulta tu historial y solicita una renovación para volver a habilitar los recursos.", tone: "warning" },
  SUSPENDED: { title: "Acceso pausado", message: "Contacta a administración para revisar el estado de tu acceso.", tone: "danger" },
  ACCOUNT_SUSPENDED: { title: "Cuenta suspendida", message: "Contacta a administración para recibir información sobre tu cuenta.", tone: "danger" },
  REVOKED: { title: "Acceso retirado", message: "Puedes consultar tu solicitud y contactar a administración.", tone: "danger" }
};
export function accessPresentation(access: Access) {
  // Never turn an unknown or inconsistent response into an authorization grant.
  if (access.allowed === true && access.reason === "ACTIVE") return reasons.ACTIVE;
  if (access.reason === "ACTIVE") return reasons.NOT_CONFIGURED;
  return reasons[access.reason] || reasons.NOT_CONFIGURED;
}
export function canOpenCrm(access: Access): boolean {
  return access.allowed === true && access.reason === "ACTIVE" && access.crmUrl === CRM_LOGIN_URL;
}
export function safeDownloadPath(release: DownloadRelease, access: Access): string | null {
  if (access.allowed !== true || access.reason !== "ACTIVE") return null;
  const id = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!id.test(release.id) || release.downloadPath !== `/portal-api/v1/downloads/${release.id}/file`) return null;
  return release.downloadPath;
}
export function resolveSelectedPlan(plans: PortalPlan[], code: string | null): PortalPlan | null {
  return plans.find(plan => plan.code === code) || null;
}
export function formatMoney(amount: number, currency = "DOP"): string {
  return new Intl.NumberFormat("es-DO", { style: "currency", currency, minimumFractionDigits: 2 }).format(amount);
}
export function formatLimit(value: number): string { return value === 0 ? "Sin límite" : new Intl.NumberFormat("es-DO").format(value); }
export function formatDate(value: string | null, withTime = false): string {
  if (!value) return "Por definir";
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return "Por definir";
  return new Intl.DateTimeFormat("es-DO", { timeZone: "America/Santo_Domingo", dateStyle: "medium", ...(withTime ? { timeStyle: "short" } : {}) }).format(date);
}
export function formatInclusiveEnd(value: string | null): string {
  if (!value) return "Por definir";
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? formatDate(new Date(timestamp - 1).toISOString()) : "Por definir";
}
export function requestStatus(status: string): string {
  return ({ Submitted: "Recibida", InReview: "En revisión", NeedsInformation: "Información requerida", Approved: "Aprobada", Rejected: "No aprobada", Cancelled: "Cancelada" } as Record<string, string>)[status] || "Por revisar";
}
export function paymentStatus(status: string): string {
  return ({ NotReported: "Por reportar", Reported: "Reportado · por validar", InReview: "En revisión de pago", Rejected: "Reporte no aprobado", Confirmed: "Confirmado", Voided: "Anulado", NotRequired: "No requerido" } as Record<string, string>)[status] || "Por revisar";
}
