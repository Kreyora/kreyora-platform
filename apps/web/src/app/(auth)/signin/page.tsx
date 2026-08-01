"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuthClient } from "@/lib/providers/client-provider";

export default function SignInPage() {
  const router = useRouter();
  const auth = useAuthClient();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setLoading(true);
    setError(null);
    try {
      await auth.signIn(String(data.get("email")), String(data.get("password")));
      router.push("/workspaces");
    } catch {
      setError("We could not sign you in. Check your email and password, then try again.");
    } finally {
      setLoading(false);
    }
  }

  return <>
    <h1 className="text-heading-page text-[var(--color-ink-primary)]">Sign in to your account</h1>
    <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">Enter your credentials to access your seller workspace.</p>
    <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-[var(--space-4)]">
      <Input label="Email" name="email" type="email" placeholder="you@example.com" required autoComplete="email" />
      <Input label="Password" name="password" type="password" required autoComplete="current-password" />
      {error && <p role="alert" className="text-sm text-[var(--color-danger)]">{error}</p>}
      <Button type="submit" loading={loading} className="mt-2 w-full">Sign in</Button>
    </form>
    <div className="mt-5 flex justify-between text-sm">
      <Link href="/recover" className="underline underline-offset-2">Forgot password?</Link>
      <Link href="/signup" className="underline underline-offset-2">Create workspace</Link>
    </div>
  </>;
}
