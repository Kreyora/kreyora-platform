"use client";

import { useEffect, useState, useMemo } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductCard } from "@/components/storefront/product-card";
import type { Product, Collection, Store } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

export default function StoreHomePage() {
  const { slug } = useParams<{ slug: string }>();
  const { storefront, catalog } = useClients();
  const [store, setStore] = useState<Store | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [collections, setCollections] = useState<Collection[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      storefront.getStore(DEMO_TENANT_ID),
      catalog.listProducts({ publishState: "published" }),
      catalog.getCollections(),
    ]).then(([s, p, c]) => {
      if (!cancelled) {
        setStore(s);
        setProducts(p.items);
        setCollections(c);
        setIsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, [storefront, catalog]);

  const filtered = useMemo(() => {
    if (!search) return products;
    const q = search.toLowerCase();
    return products.filter(
      (p) =>
        p.title.toLowerCase().includes(q) ||
        p.description.toLowerCase().includes(q) ||
        p.tags.some((t) => t.toLowerCase().includes(q)),
    );
  }, [products, search]);

  if (isLoading || !store) {
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
      {/* Hero banner */}
      <div className="relative flex flex-col items-center justify-center rounded-[var(--radius-lg)] bg-[var(--color-canvas-subtle)] px-6 py-12 text-center">
        <h1 className="text-2xl font-bold text-[var(--color-ink-primary)] sm:text-3xl">
          {store.profile.name}
        </h1>
        {store.profile.tagline && (
          <p className="mt-2 text-sm text-[var(--color-ink-secondary)] sm:text-base">
            {store.profile.tagline}
          </p>
        )}
      </div>

      {/* Search */}
      <div className="mt-6 max-w-md">
        <Input
          placeholder="Search products..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          aria-label="Search products"
        />
      </div>

      {/* Collections */}
      {collections.length > 0 && !search && (
        <div className="mt-8">
          <h2 className="text-lg font-semibold text-[var(--color-ink-primary)]">
            Collections
          </h2>
          <div className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-3">
            {collections.map((c) => (
              <Link
                key={c.id}
                href={`/store/${slug}/collection/${c.id}`}
                className="flex flex-col rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 transition-colors duration-[var(--duration-hover)] hover:bg-[var(--color-canvas-subtle)]"
              >
                <span className="text-sm font-semibold text-[var(--color-ink-primary)]">
                  {c.name}
                </span>
                {c.description && (
                  <span className="mt-1 text-xs text-[var(--color-ink-secondary)] line-clamp-2">
                    {c.description}
                  </span>
                )}
                <span className="mt-2 text-[11px] text-[var(--color-ink-secondary)]">
                  {c.productCount} product{c.productCount !== 1 ? "s" : ""}
                </span>
              </Link>
            ))}
          </div>
        </div>
      )}

      {/* Products */}
      <div className="mt-8">
        <h2 className="text-lg font-semibold text-[var(--color-ink-primary)]">
          {search ? `Results for "${search}"` : "All Products"}
        </h2>
        {filtered.length === 0 ? (
          <div className="mt-6 rounded-[var(--radius-lg)] border border-dashed border-[var(--color-border)] p-8 text-center">
            <p className="text-sm text-[var(--color-ink-secondary)]">
              {search ? "No products match your search." : "No products available yet."}
            </p>
          </div>
        ) : (
          <div className="mt-3 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
            {filtered.map((p) => (
              <ProductCard key={p.id} product={p} storeSlug={slug} />
            ))}
          </div>
        )}
      </div>

      <p className="mt-8 text-center text-[10px] text-[var(--color-ink-secondary)]">
        This is a demo storefront. No real transactions occur.
      </p>
    </div>
  );
}
