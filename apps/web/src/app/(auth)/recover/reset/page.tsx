"use client";

import { Suspense, useState, type FormEvent } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuthClient } from "@/lib/providers/client-provider";

function ResetPasswordForm() {
  const auth = useAuthClient(); const router = useRouter(); const query = useSearchParams();
  const [error, setError] = useState<string | null>(null); const [loading, setLoading] = useState(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setLoading(true); setError(null);
    try { await auth.resetPassword(query.get("email") ?? "", query.get("token") ?? "", String(new FormData(event.currentTarget).get("password"))); router.push("/signin"); }
    catch { setError("Unable to reset the password. Request a new link and try again."); } finally { setLoading(false); }
  }
  return <><h1 className="text-heading-page text-[var(--color-ink-primary)]">Choose a new password</h1><form onSubmit={submit} className="mt-6 flex flex-col gap-4"><Input label="New password" name="password" type="password" required minLength={12} autoComplete="new-password" />{error && <p role="alert" className="text-sm text-[var(--color-danger)]">{error}</p>}<Button type="submit" loading={loading}>Reset password</Button></form></>;
}

export default function ResetPasswordPage() {
  return <Suspense fallback={<p className="text-sm text-[var(--color-ink-secondary)]">Loading reset form...</p>}><ResetPasswordForm /></Suspense>;
}
