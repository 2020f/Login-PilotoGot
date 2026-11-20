import { PortalApiError } from "./api.ts";
import type { Account, PortalAuthOptions } from "./types.ts";

/** Only explicit server capabilities enable automated recovery. */
export function readAuthOptions(value: unknown): PortalAuthOptions | null {
  if (!value || typeof value !== "object") return null;
  const options = value as Record<string, unknown>;
  if (typeof options.emailConfirmationRequired !== "boolean" || typeof options.passwordRecoveryAvailable !== "boolean") return null;
  return { emailConfirmationRequired: options.emailConfirmationRequired, passwordRecoveryAvailable: options.passwordRecoveryAvailable };
}

export function canRecoverPassword(options: PortalAuthOptions | null): boolean {
  return options?.passwordRecoveryAvailable === true;
}

export function contactEmailBadge(user: Pick<Account["user"], "emailConfirmed" | "emailConfirmationRequired">): { label: string; tone: "neutral" | "success" | "warning" } {
  if (user.emailConfirmationRequired === false) return { label: "Correo de contacto", tone: "neutral" };
  return user.emailConfirmed ? { label: "Correo confirmado", tone: "success" } : { label: "Por confirmar", tone: "warning" };
}

/** This exact failure occurs before the registration transaction commits. */
export function registrationBlockedByEmail(error: unknown): boolean {
  return error instanceof PortalApiError && error.status === 503 && error.code === "EMAIL_UNAVAILABLE";
}

export const REGISTRATION_EMAIL_UNAVAILABLE = "El registro no se completó porque el servicio de correo no está disponible. Tus datos siguen en este formulario; puedes reintentar cuando administración restablezca el servicio.";
