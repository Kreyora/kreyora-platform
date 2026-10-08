import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import type { AssistantReadiness } from "@/lib/types";
import { READINESS_FIX, READINESS_LABEL } from "./assistant-display";

/** Readiness checklist (server-computed). States use text and icons, not colour alone. */
export function ReadinessCard({ readiness }: { readiness: AssistantReadiness }) {
  const status = !readiness.platformEnabled
    ? { label: "Off for all shops", variant: "neutral" as const, text: "Kreyora has the assistant switched off for now. Your settings are kept." }
    : readiness.isActive
      ? { label: "Active", variant: "success" as const, text: "The assistant answers new customer messages. A person can take over any chat at any time." }
      : !readiness.sellerEnabled
        ? { label: "Off", variant: "neutral" as const, text: "You switched the assistant off. Customers get no automatic replies." }
        : { label: "Not active yet", variant: "warning" as const, text: "Finish the required items below and the assistant starts answering." };

  return (
    <section className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-labelledby="readiness-title">
      <div className="flex flex-wrap items-center gap-2">
        <h2 id="readiness-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Status</h2>
        <Badge variant={status.variant}>{status.label}</Badge>
      </div>
      <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">{status.text}</p>
      <ul className="mt-4 space-y-2" aria-label="Readiness checks">
        {readiness.checks.map((check) => {
          const fix = READINESS_FIX[check.code];
          return (
            <li key={check.code} className="flex flex-wrap items-start gap-2 text-sm">
              <span aria-hidden="true" className={check.passed ? "text-[var(--color-success)]" : "text-[var(--color-ink-secondary)]"}>{check.passed ? "✓" : "○"}</span>
              <span className="min-w-0 flex-1 text-[var(--color-ink-primary)]">
                {READINESS_LABEL[check.code] ?? check.code}
                <span className="sr-only">{check.passed ? " — done" : " — not done"}</span>
                {!check.required && <span className="ml-1 text-xs text-[var(--color-ink-secondary)]">(recommended)</span>}
              </span>
              {!check.passed && (
                <span className="basis-full pl-5 text-xs text-[var(--color-ink-secondary)]">
                  {check.detail}{" "}
                  {fix && <Link href={fix.href} className="inline-flex min-h-11 items-center underline">{fix.label}</Link>}
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
