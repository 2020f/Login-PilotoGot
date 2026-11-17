import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { LoginForm } from "@/components/auth-forms";
export const metadata: Metadata = { title: "Accede a tu cuenta web", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";
export default async function LoginPage({ searchParams }: { searchParams: Promise<{ plan?: string }> }) {
  const { plan } = await searchParams;
  return <AuthShell><LoginForm selectedPlan={typeof plan === "string" ? plan : undefined} /></AuthShell>;
}
