"use client";

import { useEffect, useState } from "react";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Plan, Subscription, QuotaStatus, UsageEvent, QuotaLevel } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const QUOTA_COLORS: Record<QuotaLevel, { bar: string; text: string }> = {
  normal: { bar: "bg-[var(--color-success)]", text: "text-[var(--color-success)]" },
  warning_70: { bar: "bg-[var(--color-warning)]", text: "text-[var(--color-warning)]" },
  warning_90: { bar: "bg-orange-500", text: "text-orange-500" },
  exceeded: { bar: "bg-[var(--color-danger)]", text: "text-[var(--color-danger)]" },
};

const QUOTA_LABELS: Record<QuotaLevel, string> = {
  normal: "Normal",
  warning_70: "70% used",
  warning_90: "90% used",
  exceeded: "Exceeded",
};

export default function BillingPage() {
  const { billing } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [plan, setPlan] = useState<Plan | null>(null);
  const [subscription, setSubscription] = useState<Subscription | null>(null);
  const [quotas, setQuotas] = useState<QuotaStatus[]>([]);
  const [usageEvents, setUsageEvents] = useState<UsageEvent[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      billing.getPlan(DEMO_TENANT_ID),
      billing.getQuotaStatus(DEMO_TENANT_ID),
      billing.getUsage(DEMO_TENANT_ID),
    ]).then(([planData, quotaData, usageData]) => {
      setPlan(planData.plan);
      setSubscription(planData.subscription);
      setQuotas(quotaData);
      setUsageEvents(usageData.items);
      setIsLoading(false);
    });
  }, [billing]);

  if (isLoading || !plan || !subscription) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-48" />
        <Skeleton className="mt-6 h-48 w-full rounded-[var(--radius-lg)]" />
        <Skeleton className="mt-6 h-48 w-full rounded-[var(--radius-lg)]" />
      </div>
    );
  }

  const statusVariant = subscription.status === "active" ? "success" : subscription.status === "past_due" ? "danger" : "neutral";

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Billing</h1>
          {isViewer && <ViewerBadge />}
        </div>
        {!isViewer && (
          <Button variant="outline" disabled>Manage Subscription</Button>
        )}
      </div>

      <div className="mt-8 grid gap-8 lg:grid-cols-3">
        <div className="space-y-8 lg:col-span-2">
          <section className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <div className="flex items-start justify-between gap-3">
              <div>
                <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">{plan.name} Plan</h2>
                <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
                  Rs. {plan.monthlyPrice.amount.toLocaleString("en-IN")} / month &middot; {plan.platformFeePercent}% platform fee
                </p>
              </div>
              <Badge variant={statusVariant}>{subscription.status}</Badge>
            </div>
            <div className="mt-4 grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
              <div>
                <p className="text-xs text-[var(--color-ink-secondary)]">Period start</p>
                <p className="text-[var(--color-ink-primary)]">{new Date(subscription.currentPeriodStart).toLocaleDateString()}</p>
              </div>
              <div>
                <p className="text-xs text-[var(--color-ink-secondary)]">Period end</p>
                <p className="text-[var(--color-ink-primary)]">{new Date(subscription.currentPeriodEnd).toLocaleDateString()}</p>
              </div>
            </div>
          </section>

          <section>
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">Quota Usage</h2>
            <div className="mt-4 space-y-4">
              {quotas.map((q) => (
                <QuotaBar key={q.metric} quota={q} />
              ))}
            </div>
          </section>

          <section>
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">Usage Events</h2>
            {usageEvents.length === 0 ? (
              <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">No usage events recorded.</p>
            ) : (
              <div className="mt-4 overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-[var(--color-border)]">
                      <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Metric</th>
                      <th className="pb-2 text-right font-medium text-[var(--color-ink-secondary)]">Qty</th>
                      <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Source</th>
                      <th className="pb-2 text-left font-medium text-[var(--color-ink-secondary)]">Date</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usageEvents.map((e) => (
                      <tr key={e.id} className="border-b border-[var(--color-border)] last:border-b-0">
                        <td className="py-2 capitalize text-[var(--color-ink-primary)]">{e.metric.replace(/_/g, " ")}</td>
                        <td className="py-2 text-right text-[var(--color-ink-primary)]">{e.quantity}</td>
                        <td className="py-2 capitalize text-[var(--color-ink-secondary)]">{e.source}</td>
                        <td className="py-2 text-[var(--color-ink-secondary)]">{new Date(e.createdAt).toLocaleDateString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>

        <div className="lg:self-start">
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Plan Limits</h3>
            <div className="mt-3 space-y-2 text-sm">
              <LimitRow label="Products" value={plan.limits.products.toLocaleString()} />
              <LimitRow label="AI credits" value={plan.limits.aiCredits.toLocaleString()} />
              <LimitRow label="Orders/month" value={plan.limits.ordersPerMonth.toLocaleString()} />
              <LimitRow label="Integrations" value={plan.limits.socialIntegrations.toLocaleString()} />
              <LimitRow label="Team seats" value={plan.limits.teamSeats.toLocaleString()} />
            </div>
          </div>
        </div>
      </div>

      <p className="mt-8 text-[10px] text-[var(--color-ink-secondary)]">
        Billing is simulated. No real subscription or payment collection is configured.
      </p>
    </div>
  );
}

function QuotaBar({ quota }: { quota: QuotaStatus }) {
  const colors = QUOTA_COLORS[quota.level];
  const percent = Math.min(quota.percentUsed, 100);

  return (
    <div>
      <div className="flex items-center justify-between">
        <span className="text-sm capitalize text-[var(--color-ink-primary)]">{quota.metric.replace(/_/g, " ")}</span>
        <span className="flex items-center gap-2 text-xs">
          <span className="text-[var(--color-ink-secondary)]">{quota.used} / {quota.limit}</span>
          <span className={`font-medium ${colors.text}`}>{QUOTA_LABELS[quota.level]}</span>
        </span>
      </div>
      <div className="mt-1.5 h-2 w-full overflow-hidden rounded-full bg-[var(--color-surface-secondary)]">
        <div
          className={`h-full rounded-full transition-all ${colors.bar}`}
          style={{ width: `${percent}%` }}
          role="progressbar"
          aria-valuenow={quota.used}
          aria-valuemin={0}
          aria-valuemax={quota.limit}
          aria-label={`${quota.metric}: ${quota.percentUsed}% used`}
        />
      </div>
    </div>
  );
}

function LimitRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between border-b border-[var(--color-border)] pb-2 last:border-b-0">
      <span className="text-[var(--color-ink-secondary)]">{label}</span>
      <span className="text-[var(--color-ink-primary)]">{value}</span>
    </div>
  );
}
