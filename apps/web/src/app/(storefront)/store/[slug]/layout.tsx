"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { CartProvider } from "@/hooks/use-cart";
import { StoreFooter } from "@/components/storefront/store-footer";
import { StoreHeader } from "@/components/storefront/store-header";
import { Skeleton } from "@/components/ui/skeleton";
import { usePublicStorefrontClient } from "@/lib/providers/client-provider";
import type { PublicStorefront } from "@/lib/types/public-storefront";

export default function PublicStoreLayout({ children }: { children: React.ReactNode }) {
  const { slug } = useParams<{ slug: string }>();
  const storefront = usePublicStorefrontClient();
  const [store, setStore] = useState<PublicStorefront | null>(null);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let active = true;
    Promise.resolve().then(() => {
      if (active) { setStore(null); setUnavailable(false); }
      return storefront.getStore(slug);
    }).then((value) => active && setStore(value)).catch(() => active && setUnavailable(true));
    return () => { active = false; };
  }, [slug, storefront]);

  if (unavailable) return <main className="mx-auto max-w-5xl px-4 py-16 text-center"><h1 className="text-xl font-bold">This storefront is unavailable</h1><p className="mt-2 text-sm text-[var(--color-ink-secondary)]">Please check the link or try again later.</p></main>;
  if (!store) return <div className="mx-auto max-w-5xl px-4 py-6"><Skeleton className="mb-4 h-8 w-48" /><Skeleton className="h-64 w-full rounded-[var(--radius-lg)]" /></div>;
  return <CartProvider key={slug} storeSlug={slug}><div className="flex min-h-full flex-col"><StoreHeader store={store} /><main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6">{children}</main><StoreFooter store={store} /></div></CartProvider>;
}
