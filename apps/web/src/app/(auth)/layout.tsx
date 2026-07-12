import Link from "next/link";

export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-full flex-col bg-[var(--color-canvas-subtle)]">
      <div className="px-5 pt-4">
        <Link
          href="/"
          className="inline-flex items-center gap-1.5 text-sm text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:text-[var(--color-ink-primary)]"
        >
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="m15 18-6-6 6-6" />
          </svg>
          Back to home
        </Link>
      </div>

      <div className="flex flex-1 items-center justify-center px-5 py-12">
        <div className="w-full max-w-md">
          <div className="mb-8 text-center">
            <Link
              href="/"
              className="text-xl font-bold text-[var(--color-ink-primary)]"
            >
              Kreyora
            </Link>
          </div>

          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-8 shadow-[var(--shadow-sm)]">
            {children}
          </div>

          <p className="mt-6 text-center text-xs text-[var(--color-ink-secondary)]">
            Simulated authentication — no real credentials are verified or
            stored.
          </p>
        </div>
      </div>
    </div>
  );
}
