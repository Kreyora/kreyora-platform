"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import { usePublicStorefrontClient, USING_PUBLIC_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductCard } from "@/components/storefront/product-card";
import type { PublicCatalogProduct } from "@/lib/types/public-storefront";

export default function StoreHomePage() {
  const { slug } = useParams<{ slug: string }>();
  const storefront = usePublicStorefrontClient();
  const [products, setProducts] = useState<PublicCatalogProduct[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");

  useEffect(() => {
    let cancelled = false;
    storefront.listProducts(slug, { q: search || undefined }).then((p) => {
      if (!cancelled) {
        setProducts(p.items);
        setIsLoading(false);
      }
    }).catch(() => { if (!cancelled) setIsLoading(false); });
    return () => { cancelled = true; };
  }, [storefront, slug, search]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-4 h-32 w-full rounded-[var(--radius-lg)]" />
        <Skeleton className="mb-6 h-6 w-48" />
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="aspect-square w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      {/* Search */}
      <div className="mt-6 max-w-md">
        <Input
          placeholder="Search products..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          aria-label="Search products"
        />
      </div>

      {/* Products */}
      <div className="mt-8">
        <h2 className="text-lg font-semibold text-[var(--color-ink-primary)]">
          {search ? `Results for "${search}"` : "All Products"}
        </h2>
        {products.length === 0 ? (
          <div className="mt-6 rounded-[var(--radius-lg)] border border-dashed border-[var(--color-border)] p-8 text-center">
            <p className="text-sm text-[var(--color-ink-secondary)]">
              {search ? "No products match your search." : "No products available yet."}
            </p>
          </div>
        ) : (
          <div className="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
            {products.map((p) => (
              <ProductCard key={p.id} product={p} storeSlug={slug} />
            ))}
          </div>
        )}
      </div>

      {USING_PUBLIC_FIXTURE_ADAPTERS && <p className="mt-8 text-center text-[10px] text-[var(--color-ink-secondary)]">Demo storefront — no real transactions occur.</p>}
    </div>
  );
}
