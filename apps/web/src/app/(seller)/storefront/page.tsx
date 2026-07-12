"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Store } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const NAV_ITEMS = [
  { label: "Profile", href: "/storefront" },
  { label: "Delivery", href: "/storefront/delivery" },
  { label: "Payments", href: "/storefront/payments" },
  { label: "Preview", href: "/storefront/preview" },
];

export default function StorefrontEditorPage() {
  const { storefront } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
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
        <Skeleton className="mt-6 h-64 w-full rounded-[var(--radius-lg)]" />
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
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">Storefront</h1>
          {isViewer && <ViewerBadge />}
        </div>
        {!isViewer && (
          <Button variant="outline" disabled>Save Changes</Button>
        )}
      </div>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Storefront navigation">
        {NAV_ITEMS.map((item) => (
          <Link key={item.href} href={item.href} className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${item.href === "/storefront" ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]" : "border-transparent text-[var(--color-ink-secondary)]"}`}>
            {item.label}
          </Link>
        ))}
      </nav>

      <div className="mt-8 grid gap-8 lg:grid-cols-3">
        <div className="space-y-8 lg:col-span-2">
          <section>
            <h2 className="mb-4 text-base font-semibold text-[var(--color-ink-primary)]">Store Profile</h2>
            <div className="space-y-3">
              <InfoRow label="Name" value={store.profile.name} />
              <InfoRow label="Tagline" value={store.profile.tagline ?? "—"} />
              <InfoRow label="Description" value={store.profile.description ?? "—"} />
              <InfoRow label="Contact email" value={store.profile.contactEmail ?? "—"} />
              <InfoRow label="Contact phone" value={store.profile.contactPhone ?? "—"} />
            </div>
            {Object.keys(store.profile.socialLinks).length > 0 && (
              <div className="mt-4">
                <p className="text-xs font-medium text-[var(--color-ink-secondary)]">Social Links</p>
                <div className="mt-2 space-y-1">
                  {Object.entries(store.profile.socialLinks).map(([platform, url]) => (
                    <div key={platform} className="flex items-center gap-2 text-sm">
                      <span className="capitalize text-[var(--color-ink-secondary)]">{platform}</span>
                      <span className="truncate text-[var(--color-ink-primary)]">{url}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </section>

          <section>
            <h2 className="mb-4 text-base font-semibold text-[var(--color-ink-primary)]">Theme</h2>
            <div className="space-y-3">
              <div className="flex items-center gap-3">
                <span className="text-xs text-[var(--color-ink-secondary)]">Accent color</span>
                {store.theme.accentColor && (
                  <span className="flex items-center gap-2">
                    <span className="inline-block h-5 w-5 rounded-full border border-[var(--color-border)]" style={{ backgroundColor: store.theme.accentColor }} />
                    <span className="text-sm text-[var(--color-ink-primary)]">{store.theme.accentColor}</span>
                  </span>
                )}
              </div>
              <InfoRow label="Logo URL" value={store.theme.logoUrl ?? "—"} />
              <InfoRow label="Banner URL" value={store.theme.bannerUrl ?? "—"} />
            </div>
          </section>
        </div>

        <div className="space-y-6 lg:self-start">
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Status</h3>
            <div className="mt-3 space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Published</span>
                <Badge variant={store.isPublished ? "success" : "neutral"}>
                  {store.isPublished ? "Live" : "Draft"}
                </Badge>
              </div>
              {store.publishedAt && (
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Since</span>
                  <span className="text-[var(--color-ink-primary)]">{new Date(store.publishedAt).toLocaleDateString()}</span>
                </div>
              )}
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Slug</span>
                <span className="text-[var(--color-ink-primary)]">{store.slug}</span>
              </div>
            </div>
          </div>

          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Readiness</h3>
            <div className="mt-3 space-y-2">
              {readinessChecks.map((c) => (
                <div key={c.label} className="flex items-center gap-2 text-sm">
                  <span className={c.ok ? "text-[var(--color-success)]" : "text-[var(--color-danger)]"}>{c.ok ? "✓" : "✗"}</span>
                  <span className="text-[var(--color-ink-primary)]">{c.label}</span>
                </div>
              ))}
            </div>
            <Badge variant={store.readiness.isReady ? "success" : "warning"} className="mt-3">
              {store.readiness.isReady ? "Ready" : "Not Ready"}
            </Badge>
          </div>
        </div>
      </div>

      <p className="mt-8 text-[10px] text-[var(--color-ink-secondary)]">
        Storefront configuration is simulated. Changes will not be persisted.
      </p>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start gap-3 border-b border-[var(--color-border)] pb-2 last:border-b-0">
      <span className="w-28 shrink-0 text-xs text-[var(--color-ink-secondary)]">{label}</span>
      <span className="text-sm text-[var(--color-ink-primary)]">{value}</span>
    </div>
  );
}
