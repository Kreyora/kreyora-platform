import type { AssistantUsage } from "@/lib/types";
import { formatUsd } from "./assistant-display";

/** Usage summary (aggregates only). A notice appears from 80% of the daily cap. */
export function UsageCard({ usage }: { usage: AssistantUsage }) {
  const today = usage.daily[usage.daily.length - 1];
  const share = usage.dailyTurnCap > 0 ? usage.turnsToday / usage.dailyTurnCap : 0;
  const totals = usage.daily.reduce(
    (sum, d) => ({ turns: sum.turns + d.turns, replied: sum.replied + d.replied, handedOff: sum.handedOff + d.handedOff, cost: sum.cost + d.estimatedCostUsd }),
    { turns: 0, replied: 0, handedOff: 0, cost: 0 },
  );
  const reset = new Date(usage.nextResetAt).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });

  return (
    <section className="min-w-0 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-labelledby="usage-title">
      <h2 id="usage-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Usage</h2>
      <dl className="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-4">
        <div>
          <dt className="text-xs text-[var(--color-ink-secondary)]">Today</dt>
          <dd className="text-xl font-semibold text-[var(--color-ink-primary)]">{usage.turnsToday} <span className="text-sm font-normal text-[var(--color-ink-secondary)]">/ {usage.dailyTurnCap}</span></dd>
        </div>
        <div>
          <dt className="text-xs text-[var(--color-ink-secondary)]">Replies ({usage.days} days)</dt>
          <dd className="text-xl font-semibold text-[var(--color-ink-primary)]">{totals.replied}</dd>
        </div>
        <div>
          <dt className="text-xs text-[var(--color-ink-secondary)]">Handed to a person</dt>
          <dd className="text-xl font-semibold text-[var(--color-ink-primary)]">{totals.handedOff}</dd>
        </div>
        <div>
          <dt className="text-xs text-[var(--color-ink-secondary)]">Estimated cost</dt>
          <dd className="text-xl font-semibold text-[var(--color-ink-primary)]">{formatUsd(totals.cost)}</dd>
        </div>
      </dl>
      {share >= 0.8 && (
        <p role="status" className="mt-3 rounded-[var(--radius-md)] border border-[var(--color-warning)] p-2 text-sm text-[var(--color-ink-primary)]">
          {share >= 1 ? "Today's limit is reached: new messages wait for a person until the limit resets" : `You've used ${Math.round(share * 100)}% of today's limit`} (resets at {reset}).
        </p>
      )}
      <div className="mt-4 overflow-x-auto">
        <table className="w-full min-w-[20rem] text-left text-xs">
          <caption className="sr-only">Assistant usage per day</caption>
          <thead className="text-[var(--color-ink-secondary)]">
            <tr>
              <th scope="col" className="py-1 font-medium">Day (UTC)</th>
              <th scope="col" className="py-1 font-medium">Chats</th>
              <th scope="col" className="py-1 font-medium">Replied</th>
              <th scope="col" className="py-1 font-medium">Handed off</th>
              <th scope="col" className="py-1 font-medium">AI calls</th>
              <th scope="col" className="py-1 font-medium">Tests</th>
            </tr>
          </thead>
          <tbody>
            {usage.daily.map((d) => (
              <tr key={d.date} className="border-t border-[var(--color-border)]" aria-current={d === today ? "date" : undefined}>
                <td className="py-1">{d.date}</td>
                <td className="py-1">{d.turns}</td>
                <td className="py-1">{d.replied}</td>
                <td className="py-1">{d.handedOff}</td>
                <td className="py-1">{d.modelCalls}</td>
                <td className="py-1">{d.playgroundTurns}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
