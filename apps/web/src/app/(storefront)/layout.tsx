"use client";

import { useEffect, useState } from "react";
import { useClients } from "@/lib/providers/client-provider";
import { CartProvider } from "@/hooks/use-cart";
import { StoreHeader } from "@/components/storefront/store-header";
import { StoreFooter } from "@/components/storefront/store-footer";
import { Skeleton } from "@/components/ui/skeleton";
import type { Store } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

export default function StorefrontLayout({ children }: { children: React.ReactNode }) {
  const { storefront } = useClients();
  const [store, setStore] = useState<Store | null>(null);

  useEffect(() => {
    storefront.getStore(DEMO_TENANT_ID).then(setStore);
  }, [storefront]);

  if (!store) {
    return (
      <div className="flex min-h-full flex-col">
        <div className="border-b border-[var(--color-border)] px-4 py-3">
          <div className="mx-auto flex max-w-5xl items-center justify-between">
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-8 w-8 rounded-full" />
          </div>
        </div>
        <main className="mx-auto flex-1 w-full max-w-5xl px-4 py-6">
          <Skeleton className="mb-4 h-8 w-48" />
          <Skeleton className="h-64 w-full rounded-[var(--radius-lg)]" />
        </main>
      </div>
    );
  }

  return (
    <CartProvider>
      <div className="flex min-h-full flex-col">
        <StoreHeader store={store} />
        <main className="mx-auto flex-1 w-full max-w-5xl px-4 py-6">
          {children}
        </main>
        <StoreFooter store={store} />
      </div>
    </CartProvider>
  );
}
