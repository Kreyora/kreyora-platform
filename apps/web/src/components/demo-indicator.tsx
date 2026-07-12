"use client";

import { useState } from "react";

export function DemoIndicator() {
  const [expanded, setExpanded] = useState(true);

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={() => setExpanded(true)}
        role="status"
        aria-label="Demo mode active — click to expand"
        className="fixed top-2 right-2 z-50 flex items-center gap-1.5 rounded-full border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 py-1.5 text-xs font-medium text-[var(--color-ink-secondary)] shadow-[var(--shadow-sm)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)] cursor-pointer"
      >
        <span
          className="inline-block h-2 w-2 rounded-full bg-[var(--color-warning)]"
          aria-hidden="true"
        />
        Demo
      </button>
    );
  }

  return (
    <div
      role="status"
      aria-label="Demo data notice"
      className="sticky top-0 z-50 flex items-center justify-center gap-2 border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)] px-4 py-1.5 text-center text-xs text-[var(--color-ink-secondary)]"
    >
      <span
        className="inline-block h-2 w-2 rounded-full bg-[var(--color-warning)]"
        aria-hidden="true"
      />
      <span>Demo data — not connected to a live service</span>
      <button
        type="button"
        onClick={() => setExpanded(false)}
        aria-label="Collapse demo notice"
        className="ml-2 rounded-sm px-1.5 py-0.5 text-xs transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-border)] cursor-pointer"
      >
        ✕
      </button>
    </div>
  );
}
