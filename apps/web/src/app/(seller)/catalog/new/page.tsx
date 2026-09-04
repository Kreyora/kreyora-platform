"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductForm } from "@/components/seller/product-form";
import type { Collection } from "@/lib/types";

export default function NewProductPage() {
  const { catalog } = useClients();
  const router = useRouter();
  const [collections, setCollections] = useState<Collection[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    catalog.getCollections().then((c) => {
      setCollections(c);
      setIsLoading(false);
    });
  }, [catalog]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-48" />
        <Skeleton className="mb-6 h-8 w-40" />
        <div className="space-y-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-11 w-full" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/catalog" className="hover:underline">
          Catalog
        </Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">New Product</span>
      </nav>

      <h1 className="mb-6 text-heading-page text-[var(--color-ink-primary)]">
        Create Product
      </h1>

      <div className="max-w-2xl">
        <ProductForm
          collections={collections}
          onSave={async (input) => {
            if (!input.initialVariant) throw new Error("Add a first variant before creating the product.");
            const created = await catalog.createProduct({ ...input, variants: [input.initialVariant] });
            router.push(`/catalog/${created.id}`);
          }}
        />
      </div>
    </div>
  );
}
