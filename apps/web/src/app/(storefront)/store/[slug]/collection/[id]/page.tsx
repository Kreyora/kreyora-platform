"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductCard } from "@/components/storefront/product-card";
import type { Product, Collection } from "@/lib/types";

export default function CollectionPage() {
  const { slug, id } = useParams<{ slug: string; id: string }>();
  const { catalog } = useClients();
  const [products, setProducts] = useState<Product[]>([]);
  const [collections, setCollections] = useState<Collection[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      catalog.listProducts({ collection: id, publishState: "published" }),
      catalog.getCollections(),
    ]).then(([p, c]) => {
      if (!cancelled) {
        setProducts(p.items);
        setCollections(c);
        setIsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, [catalog, id]);

  const collection = collections.find((c) => c.id === id);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-4 w-48" />
        <Skeleton className="mb-6 h-8 w-40" />
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="aspect-square w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href={`/store/${slug}`} className="hover:underline">
          Home
        </Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">
          {collection?.name ?? "Collection"}
        </span>
      </nav>

      <h1 className="text-xl font-bold text-[var(--color-ink-primary)] sm:text-2xl">
        {collection?.name ?? "Collection"}
      </h1>
      {collection?.description && (
        <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
          {collection.description}
        </p>
      )}

      {products.length === 0 ? (
        <div className="mt-8 rounded-[var(--radius-lg)] border border-dashed border-[var(--color-border)] p-8 text-center">
          <p className="text-sm text-[var(--color-ink-secondary)]">
            No products in this collection yet.
          </p>
          <Link
            href={`/store/${slug}`}
            className="mt-3 inline-block text-sm font-medium text-[var(--color-ink-primary)] hover:underline"
          >
            Browse all products
          </Link>
        </div>
      ) : (
        <div className="mt-6 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
          {products.map((p) => (
            <ProductCard key={p.id} product={p} storeSlug={slug} />
          ))}
        </div>
      )}
    </div>
  );
}
