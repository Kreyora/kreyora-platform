"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useCart } from "@/hooks/use-cart";
import { usePublicCheckoutClient } from "@/lib/providers/client-provider";
import { saveAssistantLink } from "@/lib/storefront/assistant-link";
import type { PublicAssistantLink, PublicCartItem } from "@/lib/types/public-storefront";

type State =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "partial"; link: PublicAssistantLink };

const toCartItems = (link: PublicAssistantLink): PublicCartItem[] =>
  link.items.filter((item) => item.available).map((item) => ({
    variantId: item.variantId, productTitle: item.productTitle, productSlug: item.productSlug, variantName: item.variantName,
    unitPriceNpr: item.unitPriceNpr, quantity: item.quantity,
  }));

/**
 * Landing page for a checkout link sent by the shop's assistant (M09-S05): fills the cart with the link's items at
 * current prices and opens the normal checkout, where the customer enters their own details and places the order.
 */
export default function AssistantLinkPage() {
  const { slug, token } = useParams<{ slug: string; token: string }>();
  const router = useRouter();
  const checkout = usePublicCheckoutClient();
  const { replaceItems } = useCart();
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    let active = true;
    checkout.getAssistantLink({ slug, token })
      .then((link) => {
        if (!active) return;
        const items = toCartItems(link);
        if (items.length === 0) { setState({ kind: "error", message: "The items in this link are no longer available." }); return; }
        if (items.length < link.items.length) { setState({ kind: "partial", link }); return; }
        replaceItems(items); saveAssistantLink(slug, token, link.expiresAt); router.replace(`/store/${slug}/checkout`);
      })
      .catch(() => { if (active) setState({ kind: "error", message: "This checkout link has expired or is not valid. Ask the shop for a new one." }); });
    return () => { active = false; };
  }, [checkout, replaceItems, router, slug, token]);

  if (state.kind === "loading") {
    return <div role="status" className="py-16 text-center text-sm text-[var(--color-ink-secondary)]">Preparing your cart…</div>;
  }

  if (state.kind === "error") {
    return <div className="py-16 text-center"><h1 className="text-lg font-semibold">Checkout link unavailable</h1><p role="alert" className="mt-2 text-sm text-[var(--color-ink-secondary)]">{state.message}</p><Link href={`/store/${slug}`} className="mt-4 inline-block text-sm underline">Browse the store</Link></div>;
  }

  const unavailable = state.link.items.filter((item) => !item.available);
  return <div className="mx-auto max-w-lg py-12"><h1 className="text-lg font-semibold">Some items are no longer available</h1>
    <ul className="mt-3 list-disc pl-5 text-sm text-[var(--color-ink-secondary)]">{unavailable.map((item) => <li key={item.variantId}>{item.productTitle} · {item.variantName}</li>)}</ul>
    <div className="mt-6 flex flex-wrap gap-3"><Button onClick={() => { replaceItems(toCartItems(state.link)); saveAssistantLink(slug, token, state.link.expiresAt); router.replace(`/store/${slug}/checkout`); }}>Continue with available items</Button><Link href={`/store/${slug}`} className="inline-flex min-h-11 items-center text-sm underline">Browse the store</Link></div>
  </div>;
}
