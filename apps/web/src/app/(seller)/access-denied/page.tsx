import Link from "next/link";

export default function AccessDeniedPage() {
  return <div className="mx-auto max-w-lg py-16 text-center"><h1 className="text-heading-page text-[var(--color-ink-primary)]">Access denied</h1><p className="mt-3 text-sm text-[var(--color-ink-secondary)]">Your current workspace role cannot access this area.</p><Link className="mt-6 inline-block underline" href="/dashboard">Return to dashboard</Link></div>;
}
