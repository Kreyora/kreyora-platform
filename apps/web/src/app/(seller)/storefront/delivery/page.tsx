"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { DeliveryRule } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const NAV_ITEMS = [
  { label: "Profile", href: "/storefront" },
  { label: "Delivery", href: "/storefront/delivery" },
  { label: "Payments", href: "/storefront/payments" },
  { label: "Preview", href: "/storefront/preview" },
];

export default function DeliveryRulesPage() {
  const { storefront } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [rules, setRules] = useState<DeliveryRule[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    storefront.getDeliveryRules(DEMO_TENANT_ID).then((r) => {
      setRules(r);
      setIsLoading(false);
    });
  }, [storefront]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-48" />
        <div className="mt-6 space-y-3">
          {Array.from({ length: 2 }).map((_, i) => (
            <Skeleton key={i} className="h-28 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Storefront</h1>
          {isViewer && <ViewerBadge />}
        </div>
        {!isViewer && (
          <Button variant="outline" disabled>Add Rule</Button>
        )}
      </div>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Storefront navigation">
        {NAV_ITEMS.map((item) => (
          <Link key={item.href} href={item.href} className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${item.href === "/storefront/delivery" ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]" : "border-transparent text-[var(--color-ink-secondary)]"}`}>
            {item.label}
          </Link>
        ))}
      </nav>

      <h2 className="mt-6 text-base font-semibold text-[var(--color-ink-primary)]">Delivery Rules</h2>

      {rules.length === 0 ? (
        <div className="mt-4"><EmptyState title="No delivery rules" description="Add delivery rules to enable checkout." /></div>
      ) : (
        <div className="mt-4 flex flex-col gap-4">
          {rules.map((rule) => (
            <div key={rule.id} className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-medium text-[var(--color-ink-primary)]">{rule.name}</p>
                  <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
                    Zones: {rule.zones.join(", ")}
                  </p>
                </div>
                <div className="flex gap-2">
                  <Badge variant={rule.isActive ? "success" : "neutral"}>{rule.isActive ? "Active" : "Inactive"}</Badge>
                  <Badge variant={rule.codAvailable ? "info" : "neutral"}>{rule.codAvailable ? "COD" : "No COD"}</Badge>
                </div>
              </div>
              <div className="mt-3 grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                <div>
                  <p className="text-xs text-[var(--color-ink-secondary)]">Fee type</p>
                  <p className="text-[var(--color-ink-primary)] capitalize">{rule.feeType}</p>
                </div>
                {rule.flatFee && (
                  <div>
                    <p className="text-xs text-[var(--color-ink-secondary)]">Fee</p>
                    <p className="text-[var(--color-ink-primary)]">Rs. {rule.flatFee.amount}</p>
                  </div>
                )}
                {rule.freeAbove && (
                  <div>
                    <p className="text-xs text-[var(--color-ink-secondary)]">Free above</p>
                    <p className="text-[var(--color-ink-primary)]">Rs. {rule.freeAbove.amount.toLocaleString("en-IN")}</p>
                  </div>
                )}
                {rule.estimatedDays && (
                  <div>
                    <p className="text-xs text-[var(--color-ink-secondary)]">Estimated</p>
                    <p className="text-[var(--color-ink-primary)]">{rule.estimatedDays}</p>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        Delivery rules are simulated. Changes will not be persisted.
      </p>
    </div>
  );
}
