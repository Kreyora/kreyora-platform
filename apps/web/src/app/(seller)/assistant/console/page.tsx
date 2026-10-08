"use client";

import { useState, type FormEvent } from "react";
import { useClients, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  AssistantHeader,
  ErrorNote,
  OUTCOME_LABEL,
  canEditAssistant,
  describeAssistantError,
  formatUsd,
  reasonLabel,
  type ScreenError,
} from "@/components/assistant/assistant-display";
import type { PlaygroundMessage, PlaygroundResult } from "@/lib/types";

const MAX_MESSAGES = 20;
const MAX_LENGTH = 1000;

export default function ConsolePage() {
  const { assistant } = useClients();
  const { effectiveRole } = useSession();
  const allowed = canEditAssistant(effectiveRole);
  const [messages, setMessages] = useState<PlaygroundMessage[]>([]);
  const [draft, setDraft] = useState("");
  const [result, setResult] = useState<PlaygroundResult | null>(null);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<ScreenError | null>(null);

  async function send(event: FormEvent) {
    event.preventDefault();
    const text = draft.trim();
    if (!text || running) return;
    if (messages.length >= MAX_MESSAGES) {
      setError({ kind: "validation", message: `A test conversation can have up to ${MAX_MESSAGES} messages. Clear it to start again.` });
      return;
    }

    const next = [...messages, { from: "customer" as const, text }];
    setMessages(next);
    setDraft("");
    setRunning(true);
    setError(null);
    try {
      const outcome = await assistant.runPlayground(next);
      setResult(outcome);
      if (outcome.reply) setMessages([...next, { from: "shop", text: outcome.reply }]);
    } catch (failure) {
      setError(describeAssistantError(failure));
      setMessages(messages); // the customer's line returns to the box
      setDraft(text);
    } finally {
      setRunning(false);
    }
  }

  return (
    <div>
      <AssistantHeader title="Test console" description="Type messages as if you were a customer and see exactly what the assistant would do with your real catalog and settings." />
      <p role="note" className="mt-4 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-3 text-sm text-[var(--color-ink-primary)]">
        Test only: nothing is sent to anyone, and no stock is held or link created. Use made-up messages — never paste a real customer&apos;s details.
        {USING_FIXTURE_ADAPTERS && " Demo mode: replies are samples, not the real assistant."}
      </p>

      {!allowed ? (
        <ErrorNote error={{ kind: "denied", message: "Only the shop owner or an admin can use the test console." }} />
      ) : (
        <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)]">
          <section aria-labelledby="chat-title" className="min-w-0 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <div className="flex items-center gap-2">
              <h2 id="chat-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Conversation</h2>
              {messages.length > 0 && <Button size="sm" variant="ghost" className="ml-auto" onClick={() => { setMessages([]); setResult(null); setError(null); }}>Clear</Button>}
            </div>
            <ol className="mt-3 min-h-40 space-y-2" aria-label="Test messages" aria-live="polite">
              {messages.length === 0 && <li className="text-sm text-[var(--color-ink-secondary)]">Try: “Red kurta ko price kati ho?” or “Pokhara ma delivery huncha?”</li>}
              {messages.map((m, i) => (
                <li key={i} className={`max-w-[85%] whitespace-pre-wrap rounded-[var(--radius-md)] px-3 py-2 text-sm ${m.from === "customer" ? "bg-[var(--color-canvas-subtle)] text-[var(--color-ink-primary)]" : "ml-auto bg-[var(--color-surface-dark)] text-[var(--color-on-dark)]"}`}>
                  <span className="sr-only">{m.from === "customer" ? "Customer: " : "Assistant: "}</span>
                  {m.text}
                </li>
              ))}
              {running && <li className="text-sm text-[var(--color-ink-secondary)]" aria-busy="true">The assistant is thinking…</li>}
            </ol>
            <form onSubmit={send} className="mt-4 flex gap-2">
              <label htmlFor="console-input" className="sr-only">Customer message</label>
              <input
                id="console-input"
                value={draft}
                maxLength={MAX_LENGTH}
                onChange={(e) => setDraft(e.target.value)}
                placeholder="Write as the customer…"
                className="min-h-11 flex-1 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm"
              />
              <Button type="submit" disabled={running || !draft.trim()}>Send</Button>
            </form>
            {error && <ErrorNote error={error} />}
          </section>

          <section aria-labelledby="trace-title" className="min-w-0 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h2 id="trace-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">What happened</h2>
            {!result ? (
              <p className="mt-3 text-sm text-[var(--color-ink-secondary)]">Send a message to see the outcome, the tools used and the safety checks.</p>
            ) : (
              <div className="mt-3 space-y-3 text-sm">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant={OUTCOME_LABEL[result.outcome].variant}>{OUTCOME_LABEL[result.outcome].label}</Badge>
                  <span className="text-[var(--color-ink-secondary)]">{reasonLabel(result.reasonCode)}</span>
                </div>
                <div>
                  <h3 className="text-xs font-medium text-[var(--color-ink-secondary)]">Tools</h3>
                  {result.tools.length === 0 ? (
                    <p className="text-[var(--color-ink-secondary)]">None</p>
                  ) : (
                    <ol className="mt-1 space-y-1">
                      {result.tools.map((t, i) => (
                        <li key={i} className="flex flex-wrap gap-2">
                          <span className="font-medium text-[var(--color-ink-primary)]">{t.tool}</span>
                          <span className="text-[var(--color-ink-secondary)]">{t.outcome}</span>
                          {t.dryRun && <Badge variant="neutral">test only</Badge>}
                        </li>
                      ))}
                    </ol>
                  )}
                </div>
                {result.citations > 0 && <p className="text-[var(--color-ink-secondary)]">Used {result.citations} approved knowledge passage{result.citations === 1 ? "" : "s"}.</p>}
                {result.validationCodes.length > 0 && (
                  <div>
                    <h3 className="text-xs font-medium text-[var(--color-ink-secondary)]">Safety checks that fired</h3>
                    <p>{result.validationCodes.join(", ")}</p>
                  </div>
                )}
                <p className="text-xs text-[var(--color-ink-secondary)]">
                  {result.modelCalls} AI call{result.modelCalls === 1 ? "" : "s"} · {result.inputTokens + result.outputTokens} tokens · {formatUsd(result.estimatedCostUsd)}
                </p>
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
}
