/**
 * Provisional Viewer role indicator (M01 only).
 * Rendered on surfaces that will support read-only Viewer access.
 * Not an authorization boundary — visual planning cue only.
 */
export function ViewerBadge() {
  return (
    <span className="inline-flex items-center gap-1 rounded-full border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] px-2.5 py-0.5 text-xs text-[var(--color-ink-secondary)]">
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width="12"
        height="12"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z" />
        <circle cx="12" cy="12" r="3" />
      </svg>
      Viewer: read-only
    </span>
  );
}
