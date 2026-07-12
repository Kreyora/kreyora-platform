"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";

function GoogleIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
      <path
        d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z"
        fill="#4285F4"
      />
      <path
        d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
        fill="#34A853"
      />
      <path
        d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"
        fill="#FBBC05"
      />
      <path
        d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"
        fill="#EA4335"
      />
    </svg>
  );
}

function FacebookIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
      <path
        d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z"
        fill="#1877F2"
      />
    </svg>
  );
}

export default function SignInPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setLoading(true);
    setTimeout(() => {
      setLoading(false);
      router.push("/workspaces");
    }, 800);
  }

  function handleSocialLogin() {
    setLoading(true);
    setTimeout(() => {
      setLoading(false);
      router.push("/workspaces");
    }, 800);
  }

  return (
    <>
      <h1 className="text-heading-page text-[var(--color-ink-primary)]">
        Sign in to your account
      </h1>
      <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
        Enter your credentials to access your seller workspace.
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
        <div>
          <Input
            label="Password"
            name="password"
            type={showPassword ? "text" : "password"}
            placeholder="Enter your password"
            required
            autoComplete="current-password"
          />
          <div className="mt-2 flex items-center justify-between">
            <label className="flex items-center gap-2 text-xs text-[var(--color-ink-secondary)]">
              <input
                type="checkbox"
                checked={showPassword}
                onChange={(e) => setShowPassword(e.target.checked)}
                className="h-3.5 w-3.5 rounded border-[var(--color-border)]"
              />
              Show password
            </label>
            <Link
              href="/recover"
              className="text-xs font-medium text-[var(--color-ink-secondary)] underline underline-offset-2 hover:text-[var(--color-ink-primary)]"
            >
              Forgot password?
            </Link>
          </div>
        </div>

        <Button type="submit" loading={loading} className="mt-2 w-full">
          Sign in
        </Button>
      </form>

      {/* Divider */}
      <div className="my-6 flex items-center gap-3">
        <div className="h-px flex-1 bg-[var(--color-border)]" />
        <span className="text-xs text-[var(--color-ink-secondary)]">
          or continue with
        </span>
        <div className="h-px flex-1 bg-[var(--color-border)]" />
      </div>

      {/* Social login */}
      <div className="flex flex-col gap-3">
        <Button
          variant="outline"
          onClick={handleSocialLogin}
          disabled={loading}
          className="w-full"
        >
          <GoogleIcon />
          Google
        </Button>
        <Button
          variant="outline"
          onClick={handleSocialLogin}
          disabled={loading}
          className="w-full"
        >
          <FacebookIcon />
          Facebook
        </Button>
      </div>

      <p className="mt-6 text-center text-xs text-[var(--color-ink-secondary)]">
        Don&apos;t have an account? Registration will be available soon.
      </p>
    </>
  );
}
