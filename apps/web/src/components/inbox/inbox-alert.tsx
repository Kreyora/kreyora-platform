"use client";

import { Button } from "@/components/ui/button";
import type { InboxErrorCopy } from "@/lib/utils/conversation-errors";

export function InboxAlert({ error, onRefresh, onDismiss }: { error: InboxErrorCopy | null; onRefresh?: () => void; onDismiss?: () => void }) {
  if (!error) return null;
  return (
    <div role="alert" className="mb-4 rounded-[var(--radius-md)] border border-[var(--color-danger)] bg-[var(--color-canvas)] px-4 py-3 text-sm">
      <p className="font-semibold text-[var(--color-ink-primary)]">{error.title}</p>
      <p className="mt-1 text-[var(--color-ink-secondary)]">{error.detail}</p>
      <div className="mt-2 flex flex-wrap gap-2">
        {error.refresh && onRefresh && <Button size="sm" variant="outline" onClick={onRefresh}>Refresh</Button>}
        {onDismiss && <Button size="sm" variant="ghost" onClick={onDismiss}>Dismiss</Button>}
      </div>
    </div>
  );
}
