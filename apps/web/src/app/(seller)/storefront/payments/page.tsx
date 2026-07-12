"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { PaymentMethod } from "@/lib/types/payments";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const NAV_ITEMS = [
  { label: "Profile", href: "/storefront" },
  { label: "Delivery", href: "/storefront/delivery" },
  { label: "Payments", href: "/storefront/payments" },
  { label: "Preview", href: "/storefront/preview" },
];

const METHOD_LABEL: Record<string, string> = {
  cod: "Cash on Delivery",
  merchant_qr: "Merchant QR",
};

export default function PaymentMethodsPage() {
  const { storefront } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [methods, setMethods] = useState<PaymentMethod[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    storefront.getPaymentMethods(DEMO_TENANT_ID).then((m) => {
      setMethods(m);
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
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">Storefront</h1>
        {isViewer && <ViewerBadge />}
      </div>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Storefront navigation">
        {NAV_ITEMS.map((item) => (
          <Link key={item.href} href={item.href} className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${item.href === "/storefront/payments" ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]" : "border-transparent text-[var(--color-ink-secondary)]"}`}>
            {item.label}
          </Link>
        ))}
      </nav>

      <h2 className="mt-6 text-base font-semibold text-[var(--color-ink-primary)]">Payment Methods</h2>

      {methods.length === 0 ? (
        <div className="mt-4"><EmptyState title="No payment methods" description="Configure payment methods to accept orders." /></div>
      ) : (
        <div className="mt-4 flex flex-col gap-4">
          {methods.map((m) => (
            <div key={m.id} className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <div className="flex items-center gap-2">
                    <p className="text-sm font-medium text-[var(--color-ink-primary)]">{m.label}</p>
                    <Badge variant="neutral">{METHOD_LABEL[m.type] ?? m.type}</Badge>
                  </div>
                  {m.instructions && (
                    <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">{m.instructions}</p>
                  )}
                </div>
                <Badge variant={m.isEnabled ? "success" : "neutral"}>
                  {m.isEnabled ? "Enabled" : "Disabled"}
                </Badge>
              </div>
              {m.qrImageUrl && (
                <p className="mt-2 text-xs text-[var(--color-ink-secondary)]">
                  QR image: {m.qrImageUrl}
                </p>
              )}
              <p className="mt-2 text-[10px] text-[var(--color-ink-secondary)]">
                Updated: {new Date(m.updatedAt).toLocaleDateString()}
              </p>
            </div>
          ))}
        </div>
      )}

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        Payment configuration is simulated. No real payment processing is configured.
      </p>
    </div>
  );
}
