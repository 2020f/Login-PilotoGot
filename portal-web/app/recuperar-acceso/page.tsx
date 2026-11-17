import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { ForgotPasswordForm } from "@/components/auth-forms";
export const metadata: Metadata = { title: "Recupera tu acceso web", robots: { index: false, follow: false } };
export default function RecoveryPage() { return <AuthShell><ForgotPasswordForm /></AuthShell>; }
