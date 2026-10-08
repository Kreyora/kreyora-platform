import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { OUTCOME_LABEL, formatWhen, reasonLabel } from "@/components/assistant/assistant-display";
import type { AssistantTurn } from "@/lib/types";

/** The assistant's latest decisions in this chat (M09-S08): outcome, reason and tools; never message text. */
export function AiActivity({ conversationId, turns }: { conversationId: string; turns: AssistantTurn[] }) {
  return (
    <section className="mt-6 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-labelledby="ai-activity-title">
      <div className="flex flex-wrap items-center gap-2">
        <h2 id="ai-activity-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Assistant activity</h2>
        <Link href={`/assistant/history?conversationId=${encodeURIComponent(conversationId)}`} className="ml-auto inline-flex min-h-11 items-center text-xs underline">
          Full history
        </Link>
      </div>
      {turns.length === 0 ? (
        <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">The assistant hasn&apos;t acted in this chat.</p>
      ) : (
        <ul className="mt-2 space-y-2 text-sm" aria-label="Recent assistant decisions">
          {turns.map((turn) => (
            <li key={turn.id} className="flex flex-wrap items-center gap-2">
              <Badge variant={OUTCOME_LABEL[turn.outcome].variant}>{OUTCOME_LABEL[turn.outcome].label}</Badge>
              <span className="text-[var(--color-ink-primary)]">{reasonLabel(turn.reasonCode)}</span>
              {turn.tools.length > 0 && <span className="text-xs text-[var(--color-ink-secondary)]">{turn.tools.map((t) => t.tool).join(" → ")}</span>}
              <span className="ml-auto text-xs text-[var(--color-ink-secondary)]">{formatWhen(turn.startedAt)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
