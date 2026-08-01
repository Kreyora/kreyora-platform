"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuthClient } from "@/lib/providers/client-provider";

export default function RecoverPage() {
  const auth = useAuthClient();
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    await auth.requestPasswordReset(email).catch(() => undefined);
    setResult("requested");
    setLoading(false);
  }
  if (result !== null) return <>
    <h1 className="text-heading-page text-[var(--color-ink-primary)]">Check your email</h1>
    <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">If an account exists for that email address, password reset instructions will be sent.</p>
    <Link className="mt-5 block underline" href="/signin">Back to sign in</Link>
  </>;
  return <>
    <h1 className="text-heading-page text-[var(--color-ink-primary)]">Account recovery</h1>
    <form onSubmit={submit} className="mt-6 flex flex-col gap-[var(--space-4)]">
      <Input label="Email" name="email" type="email" required value={email} onChange={(event) => setEmail(event.target.value)} />
      <Button type="submit" loading={loading}>Send recovery link</Button>
    </form>
    <Link className="mt-6 block text-sm underline" href="/signin">Back to sign in</Link>
  </>;
}
