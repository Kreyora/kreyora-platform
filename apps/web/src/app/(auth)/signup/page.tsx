"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuthClient } from "@/lib/providers/client-provider";

export default function SignUpPage() {
  const auth = useAuthClient();
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setLoading(true); setError(null);
    try {
      await auth.register({ displayName: String(data.get("displayName")), email: String(data.get("email")), password: String(data.get("password")), tenantDisplayName: String(data.get("tenantDisplayName")), tenantSlug: String(data.get("tenantSlug")) });
      router.push("/workspaces");
    } catch { setError("We could not create your account. Check the details and try again."); }
    finally { setLoading(false); }
  }
  return <>
    <h1 className="text-heading-page text-[var(--color-ink-primary)]">Create your workspace</h1>
    <form onSubmit={submit} className="mt-6 flex flex-col gap-[var(--space-4)]">
      <Input label="Your name" name="displayName" required autoComplete="name" />
      <Input label="Email" name="email" type="email" required autoComplete="email" />
      <Input label="Password" name="password" type="password" required minLength={12} autoComplete="new-password" />
      <Input label="Workspace name" name="tenantDisplayName" required />
      <Input label="Workspace slug" name="tenantSlug" required pattern="[a-z0-9]+(-[a-z0-9]+)*" />
      {error && <p role="alert" className="text-sm text-[var(--color-danger)]">{error}</p>}
      <Button type="submit" loading={loading}>Create account</Button>
    </form>
    <p className="mt-5 text-sm">Already have an account? <Link href="/signin" className="underline">Sign in</Link></p>
  </>;
}
