"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";

export default function RecoverPage() {
  const [loading, setLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setLoading(true);
    setTimeout(() => {
      setLoading(false);
      setSubmitted(true);
    }, 800);
  }

  if (submitted) {
    return (
      <>
        <div className="flex flex-col items-center text-center">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-[var(--color-success-subtle)]">
            <svg
              width="24"
              height="24"
              viewBox="0 0 24 24"
              fill="none"
              stroke="var(--color-success)"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M20 6 9 17l-5-5" />
            </svg>
          </div>
          <h1 className="mt-4 text-heading-page text-[var(--color-ink-primary)]">
            Check your email
          </h1>
          <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
            If an account exists with that email, we&apos;ve sent a password
            reset link. Check your inbox and follow the instructions.
          </p>
          <p className="mt-4 text-xs text-[var(--color-ink-secondary)]">
            This is simulated — no email was actually sent.
          </p>
        </div>
        <div className="mt-6">
          <Link
            href="/signin"
            className="inline-flex w-full min-h-11 items-center justify-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]"
          >
            Back to sign in
          </Link>
        </div>
      </>
    );
  }

  return (
    <>
      <h1 className="text-heading-page text-[var(--color-ink-primary)]">
        Account recovery
      </h1>
      <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
        Enter the email address associated with your account and we&apos;ll
        send you a link to reset your password.
      </p>

      <form onSubmit={handleSubmit} className="mt-6 flex flex-col gap-[var(--space-4)]">
        <Input
          label="Email"
          name="email"
          type="email"
          placeholder="you@example.com"
          required
          autoComplete="email"
        />
        <Button type="submit" loading={loading} className="w-full">
          Send recovery link
        </Button>
      </form>

      <div className="mt-6 text-center">
        <Link
          href="/signin"
          className="text-sm text-[var(--color-ink-secondary)] underline underline-offset-2 hover:text-[var(--color-ink-primary)]"
        >
          Back to sign in
        </Link>
      </div>
    </>
  );
}
