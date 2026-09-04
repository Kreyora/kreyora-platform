"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useRouter } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductForm } from "@/components/seller/product-form";
import { Button } from "@/components/ui/button";
import * as TabsPrimitive from "@radix-ui/react-tabs";
import type { Product, Collection } from "@/lib/types";

export default function EditProductPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { catalog } = useClients();
  const [product, setProduct] = useState<Product | null>(null);
  const [collections, setCollections] = useState<Collection[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);

  useEffect(() => {
    let cancelled = false;
    Promise.all([catalog.getProduct(id), catalog.getCollections()]).then(
      ([p, c]) => {
        if (!cancelled) {
          setProduct(p);
          setCollections(c);
          setIsLoading(false);
        }
      },
    );
    return () => {
      cancelled = true;
    };
  }, [catalog, id]);

  const archiveProduct = useCallback(async () => {
    if (!product) return;
    await catalog.archiveProduct(product);
    router.push("/catalog");
  }, [catalog, product, router]);

  const handleDelete = useCallback(async () => {
    setShowDeleteDialog(true);
  }, []);

  if (isLoading || !product) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-64" />
        <Skeleton className="mb-6 h-8 w-48" />
        <Skeleton className="mb-4 h-10 w-full max-w-md" />
        <div className="space-y-4">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-11 w-full max-w-2xl" />
          ))}
        </div>
      </div>
    );
  }

  const tabTriggerClasses =
    "px-4 py-2.5 text-sm font-medium text-[var(--color-ink-secondary)] border-b-2 border-transparent transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:text-[var(--color-ink-primary)] data-[state=active]:text-[var(--color-ink-primary)] data-[state=active]:border-[var(--color-surface-dark)]";

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href="/catalog" className="hover:underline">
          Catalog
        </Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">{product.title}</span>
      </nav>

      <h1 className="mb-6 text-heading-page text-[var(--color-ink-primary)]">
        {product.title}
      </h1>

      <TabsPrimitive.Root defaultValue="details">
        <TabsPrimitive.List
          className="mb-6 flex border-b border-[var(--color-border)]"
          aria-label="Product sections"
        >
          <TabsPrimitive.Trigger value="details" className={tabTriggerClasses}>
            Details
          </TabsPrimitive.Trigger>
          <TabsPrimitive.Trigger value="variants" className={tabTriggerClasses}>
            Variants
          </TabsPrimitive.Trigger>
          <TabsPrimitive.Trigger value="media" className={tabTriggerClasses}>
            Media
          </TabsPrimitive.Trigger>
          <TabsPrimitive.Trigger value="inventory" className={tabTriggerClasses}>
            <Link href={`/catalog/${id}/inventory`} className="pointer-events-auto">
              Inventory
            </Link>
          </TabsPrimitive.Trigger>
        </TabsPrimitive.List>

        {/* Details tab */}
        <TabsPrimitive.Content value="details">
          <div className="max-w-2xl">
            <ProductForm
              product={product}
              collections={collections}
              isEdit
              onDelete={handleDelete}
              onSave={async (input) => {
                const updated = await catalog.updateProduct(product, input);
                setProduct(updated);
              }}
              onUploadMedia={async (file, altText) => setProduct(await catalog.uploadMedia(product.id, file, altText))}
              onDeleteMedia={async (mediaId) => setProduct(await catalog.deleteMedia(product.id, mediaId))}
            />
          </div>
        </TabsPrimitive.Content>

        {/* Variants tab */}
        <TabsPrimitive.Content value="variants">
          <div className="max-w-3xl">
            <VariantsTabContent product={product} />
          </div>
        </TabsPrimitive.Content>

        {/* Media tab */}
        <TabsPrimitive.Content value="media">
          <div className="max-w-3xl">
            <MediaTabContent product={product} />
          </div>
        </TabsPrimitive.Content>

        {/* Inventory tab — redirects */}
        <TabsPrimitive.Content value="inventory">
          <div className="py-6">
            <p className="text-sm text-[var(--color-ink-secondary)]">
              View detailed inventory for this product.
            </p>
            <Link
              href={`/catalog/${id}/inventory`}
              className="mt-3 inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors hover:bg-[var(--color-canvas-subtle)]"
            >
              Go to inventory
            </Link>
          </div>
        </TabsPrimitive.Content>
      </TabsPrimitive.Root>

      {/* Delete confirmation */}
      {showDeleteDialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="mx-4 w-full max-w-sm rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-6 shadow-lg">
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">
              Delete product?
            </h2>
            <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">Archiving removes this product from seller catalog workflows. It does not erase its audit history.</p>
            <div className="mt-4 flex justify-end gap-3">
              <Button variant="outline" onClick={() => setShowDeleteDialog(false)}>
                Cancel
              </Button>
              <Button onClick={() => void archiveProduct()}>
                Archive product
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function VariantsTabContent({ product }: { product: Product }) {
  if (product.variants.length === 0) {
    return (
      <div className="rounded-[var(--radius-md)] border border-dashed border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-8 text-center text-sm text-[var(--color-ink-secondary)]">
        No variants yet.
      </div>
    );
  }

  return (
    <div className="overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)]">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Name</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">SKU</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Options</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Price</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Compare</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Published</th>
          </tr>
        </thead>
        <tbody>
          {product.variants.map((v) => (
            <tr key={v.id} className="border-b border-[var(--color-border)] last:border-b-0">
              <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">{v.name}</td>
              <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{v.sku}</td>
              <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                {Object.entries(v.options).map(([k, val]) => `${k}: ${val}`).join(", ") || "—"}
              </td>
              <td className="px-4 py-3">Rs. {v.price.amount.toLocaleString("en-IN")}</td>
              <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                {v.compareAtPrice ? `Rs. ${v.compareAtPrice.amount.toLocaleString("en-IN")}` : "—"}
              </td>
              <td className="px-4 py-3">
                <span className={v.isPublished ? "text-[var(--color-success)]" : "text-[var(--color-ink-secondary)]"}>
                  {v.isPublished ? "Yes" : "No"}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MediaTabContent({ product }: { product: Product }) {
  if (product.media.length === 0) {
    return (
      <div className="rounded-[var(--radius-md)] border border-dashed border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-8 text-center text-sm text-[var(--color-ink-secondary)]">
        No media uploaded.
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
      {product.media.map((m) => (
        <div
          key={m.id}
          className="flex aspect-square flex-col items-center justify-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-4"
        >
          <svg
            width="32"
            height="32"
            viewBox="0 0 24 24"
            fill="none"
            stroke="var(--color-ink-secondary)"
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
            <circle cx="8.5" cy="8.5" r="1.5" />
            <path d="m21 15-5-5L5 21" />
          </svg>
          <span className="mt-3 text-center text-xs text-[var(--color-ink-secondary)]">
            {m.altText || "Product image"}
          </span>
          <span className="mt-1 text-[10px] text-[var(--color-ink-secondary)]">
            {m.width}×{m.height} • #{m.sortOrder}
          </span>
        </div>
      ))}
    </div>
  );
}
