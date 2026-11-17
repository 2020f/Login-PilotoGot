import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { TokenForm } from "@/components/auth-forms";
export const metadata: Metadata = { title: "Restablece tu contraseña", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";
export default async function ResetPage({ searchParams }: { searchParams: Promise<{ userId?: string; token?: string }> }) {
  const values = await searchParams;
  return <AuthShell><TokenForm mode="reset" userId={typeof values.userId === "string" ? values.userId : ""} token={typeof values.token === "string" ? values.token : ""} /></AuthShell>;
}
