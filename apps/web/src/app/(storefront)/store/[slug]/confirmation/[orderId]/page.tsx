"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Badge } from "@/components/ui/badge";
import type { PublicOrderConfirmation } from "@/lib/types/public-storefront";

export default function ConfirmationPage() {
  const { slug, orderId } = useParams<{ slug: string; orderId: string }>();
  const [confirmation, setConfirmation] = useState<PublicOrderConfirmation | null>(null);

  useEffect(() => {
    Promise.resolve().then(() => {
      try {
        const raw = sessionStorage.getItem(`kreyora:public-confirmation:v1:${slug}:${orderId}`);
        setConfirmation(raw ? JSON.parse(raw) as PublicOrderConfirmation : null);
      } catch { setConfirmation(null); }
    });
  }, [slug, orderId]);

  return <div className="mx-auto max-w-lg"><div className="flex flex-col items-center py-8 text-center"><div className="flex h-14 w-14 items-center justify-center rounded-full bg-[var(--color-canvas-subtle)] text-2xl text-[var(--color-success)]" aria-hidden="true">✓</div><h1 className="mt-4 text-xl font-bold">Order submitted</h1><p className="mt-1 text-sm text-[var(--color-ink-secondary)]">Order number: <span className="font-semibold text-[var(--color-ink-primary)]">{orderId}</span></p></div>
    <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5"><p className="text-sm text-[var(--color-ink-secondary)]">Your cash-on-delivery order has been sent to the store. Keep this order number for your records.</p>{confirmation ? <div className="mt-4 space-y-3 border-t pt-4 text-sm"><div className="flex justify-between"><span>Status</span><Badge variant="info">{String(confirmation.status).replace(/_/g, " ")}</Badge></div><div className="flex justify-between"><span>Payment</span><span>Cash on delivery</span></div><div className="flex justify-between font-semibold"><span>Total</span><span>{confirmation.currency} {confirmation.totalNpr.toLocaleString("en-IN")}</span></div></div> : <p className="mt-4 border-t pt-4 text-xs text-[var(--color-ink-secondary)]">This confirmation is a reference only. A live order-status lookup is not available yet.</p>}</div>
    <div className="mt-6 text-center"><Link href={`/store/${slug}`} className="text-sm underline">Continue shopping</Link></div></div>;
}
