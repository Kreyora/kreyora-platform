"use client";

import { Button } from "@/components/ui/button";

export function StaleBanner({ isStale, lastSuccessAt, onRefresh }: { isStale: boolean; lastSuccessAt: Date | null; onRefresh: () => void }) {
  if (!isStale) return null;
  return (
    <div role="status" className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-[var(--radius-md)] border border-[var(--color-warning)] bg-[var(--color-canvas-subtle)] px-4 py-3 text-sm">
      <span className="text-[var(--color-ink-primary)]">
        Reconnecting… {lastSuccessAt ? `Data may be out of date since ${lastSuccessAt.toLocaleTimeString()}.` : "Data may be out of date."}
      </span>
      <Button variant="outline" size="sm" onClick={onRefresh}>Refresh now</Button>
    </div>
  );
}
