"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { Store } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const NAV_ITEMS = [
  { label: "Profile", href: "/storefront" },
  { label: "Delivery", href: "/storefront/delivery" },
  { label: "Payments", href: "/storefront/payments" },
  { label: "Preview", href: "/storefront/preview" },
];

export default function StorefrontPreviewPage() {
  const { storefront } = useClients();
  const [store, setStore] = useState<Store | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    storefront.getStore(DEMO_TENANT_ID).then((s) => {
      setStore(s);
      setIsLoading(false);
    });
  }, [storefront]);

  if (isLoading || !store) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-48" />
        <Skeleton className="mt-6 h-48 w-full rounded-[var(--radius-lg)]" />
      </div>
    );
  }

  const readinessChecks = [
    { label: "Store profile", ok: store.readiness.hasProfile },
    { label: "Published products", ok: store.readiness.hasPublishedProducts },
    { label: "Delivery rules", ok: store.readiness.hasDeliveryRules },
    { label: "Payment methods", ok: store.readiness.hasPaymentMethods },
  ];

  return (
    <div>
      <h1 className="text-heading-page text-[var(--color-ink-primary)]">Storefront</h1>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Storefront navigation">
        {NAV_ITEMS.map((item) => (
          <Link key={item.href} href={item.href} className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${item.href === "/storefront/preview" ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]" : "border-transparent text-[var(--color-ink-secondary)]"}`}>
            {item.label}
          </Link>
        ))}
      </nav>

      <div className="mt-8 space-y-6">
        <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-6 text-center">
          <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">{store.profile.name}</h2>
          {store.profile.tagline && (
            <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">{store.profile.tagline}</p>
          )}
          <div className="mt-4 flex items-center justify-center gap-2">
            <Badge variant={store.isPublished ? "success" : "neutral"}>
              {store.isPublished ? "Published" : "Draft"}
            </Badge>
            <Badge variant={store.readiness.isReady ? "success" : "warning"}>
              {store.readiness.isReady ? "Ready" : "Not Ready"}
            </Badge>
          </div>
          <p className="mt-3 text-xs text-[var(--color-ink-secondary)]">
            Public URL: /store/{store.slug}
          </p>
          <Link href={`/store/${store.slug}`}>
            <Button variant="solid" className="mt-4">
              Open Storefront
            </Button>
          </Link>
        </div>

        <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
          <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Readiness Checklist</h3>
          <div className="mt-3 space-y-2">
            {readinessChecks.map((c) => (
              <div key={c.label} className="flex items-center gap-2 text-sm">
                <span className={c.ok ? "text-[var(--color-success)]" : "text-[var(--color-danger)]"}>{c.ok ? "✓" : "✗"}</span>
                <span className="text-[var(--color-ink-primary)]">{c.label}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
