"use client";

import { useEffect, useState, useMemo } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { useCart } from "@/hooks/use-cart";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { Product, ProductVariant, InventoryItem } from "@/lib/types";

export default function ProductDetailPage() {
  const { slug, id } = useParams<{ slug: string; id: string }>();
  const { catalog, inventory } = useClients();
  const { addItem } = useCart();

  const [product, setProduct] = useState<Product | null>(null);
  const [inventoryMap, setInventoryMap] = useState<Record<string, InventoryItem>>({});
  const [selectedVariantId, setSelectedVariantId] = useState<string | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [isLoading, setIsLoading] = useState(true);
  const [addedToCart, setAddedToCart] = useState(false);
  const [galleryIndex, setGalleryIndex] = useState(0);

  useEffect(() => {
    let cancelled = false;
    catalog.getProduct(id).then(async (p) => {
      if (cancelled) return;
      setProduct(p);

      const publishedVariants = p.variants.filter((v) => v.isPublished);
      if (publishedVariants.length > 0) {
        setSelectedVariantId(publishedVariants[0].id);
      }

      const invMap: Record<string, InventoryItem> = {};
      await Promise.all(
        p.variants.map(async (v) => {
          try {
            const inv = await inventory.getInventory(v.id);
            invMap[v.id] = inv;
          } catch {
            // variant may not have inventory data
          }
        }),
      );
      if (!cancelled) {
        setInventoryMap(invMap);
        setIsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, [catalog, inventory, id]);

  const selectedVariant = useMemo(
    () => product?.variants.find((v) => v.id === selectedVariantId) ?? null,
    [product, selectedVariantId],
  );

  const selectedInventory = selectedVariantId ? inventoryMap[selectedVariantId] : null;
  const isAvailable = selectedInventory ? selectedInventory.available > 0 : true;

  function handleAddToCart() {
    if (!selectedVariant || !product) return;
    addItem({
      variantId: selectedVariant.id,
      productTitle: product.title,
      variantName: selectedVariant.name,
      imageUrl: product.media[0]?.url,
      unitPrice: selectedVariant.price,
      quantity,
      available: isAvailable,
    });
    setAddedToCart(true);
    setTimeout(() => setAddedToCart(false), 2000);
  }

  if (isLoading || !product) {
    return (
      <div>
        <Skeleton className="mb-4 h-4 w-48" />
        <div className="grid gap-8 md:grid-cols-2">
          <Skeleton className="aspect-square w-full rounded-[var(--radius-lg)]" />
          <div className="space-y-4">
            <Skeleton className="h-8 w-3/4" />
            <Skeleton className="h-6 w-1/3" />
            <Skeleton className="h-24 w-full" />
            <Skeleton className="h-11 w-40" />
          </div>
        </div>
      </div>
    );
  }

  const publishedVariants = product.variants.filter((v) => v.isPublished);

  return (
    <div>
      {/* Breadcrumb */}
      <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb">
        <Link href={`/store/${slug}`} className="hover:underline">Home</Link>
        <span className="mx-2" aria-hidden="true">/</span>
        <span className="text-[var(--color-ink-primary)]">{product.title}</span>
      </nav>

      <div className="grid gap-8 md:grid-cols-2">
        {/* Gallery */}
        <div>
          <div className="relative flex aspect-square items-center justify-center rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
            {product.media.length > 0 ? (
              <div className="flex flex-col items-center gap-2 text-[var(--color-ink-secondary)]">
                <svg
                  width="48"
                  height="48"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  aria-hidden="true"
                >
                  <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                  <circle cx="8.5" cy="8.5" r="1.5" />
                  <path d="m21 15-5-5L5 21" />
                </svg>
                <span className="text-xs">
                  {product.media[galleryIndex]?.altText || "Product image"}
                </span>
              </div>
            ) : (
              <span className="text-sm text-[var(--color-ink-secondary)]">No images</span>
            )}
          </div>

          {/* Gallery thumbnails */}
          {product.media.length > 1 && (
            <div className="mt-3 flex gap-2 overflow-x-auto">
              {product.media.map((m, i) => (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => setGalleryIndex(i)}
                  className={[
                    "flex h-14 w-14 shrink-0 items-center justify-center rounded-[var(--radius-md)] border transition-colors",
                    i === galleryIndex
                      ? "border-[var(--color-surface-dark)] bg-[var(--color-canvas-subtle)]"
                      : "border-[var(--color-border)] hover:border-[var(--color-ink-secondary)]",
                  ].join(" ")}
                  aria-label={`View image ${i + 1}`}
                >
                  <svg
                    width="16"
                    height="16"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="var(--color-ink-secondary)"
                    strokeWidth="1.5"
                    aria-hidden="true"
                  >
                    <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
                    <circle cx="8.5" cy="8.5" r="1.5" />
                    <path d="m21 15-5-5L5 21" />
                  </svg>
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Product info */}
        <div className="flex flex-col">
          <h1 className="text-xl font-bold text-[var(--color-ink-primary)] sm:text-2xl">
            {product.title}
          </h1>

          {/* Price */}
          {selectedVariant && (
            <div className="mt-3 flex items-baseline gap-2">
              <span className="text-xl font-bold text-[var(--color-ink-primary)]">
                Rs. {selectedVariant.price.amount.toLocaleString("en-IN")}
              </span>
              {selectedVariant.compareAtPrice && (
                <span className="text-sm text-[var(--color-ink-secondary)] line-through">
                  Rs. {selectedVariant.compareAtPrice.amount.toLocaleString("en-IN")}
                </span>
              )}
            </div>
          )}

          {/* Availability */}
          {selectedInventory && (
            <div className="mt-2">
              {isAvailable ? (
                <Badge variant="success">
                  In stock ({selectedInventory.available} available)
                </Badge>
              ) : (
                <Badge variant="danger">Out of stock</Badge>
              )}
            </div>
          )}

          {/* Description */}
          <p className="mt-4 text-sm text-[var(--color-ink-secondary)] leading-relaxed">
            {product.description}
          </p>

          {/* Variant selector */}
          {publishedVariants.length > 1 && (
            <div className="mt-6">
              <p className="mb-2 text-sm font-medium text-[var(--color-ink-primary)]">
                Options
              </p>
              <div className="flex flex-wrap gap-2">
                {publishedVariants.map((v) => {
                  const vInv = inventoryMap[v.id];
                  const vAvailable = vInv ? vInv.available > 0 : true;
                  return (
                    <button
                      key={v.id}
                      type="button"
                      onClick={() => {
                        setSelectedVariantId(v.id);
                        setQuantity(1);
                      }}
                      disabled={!vAvailable}
                      className={[
                        "min-h-11 rounded-[var(--radius-md)] border px-4 text-sm font-medium transition-colors duration-[var(--duration-hover)]",
                        v.id === selectedVariantId
                          ? "border-[var(--color-surface-dark)] bg-[var(--color-surface-dark)] text-[var(--color-on-dark)]"
                          : "border-[var(--color-border)] text-[var(--color-ink-primary)] hover:border-[var(--color-ink-secondary)]",
                        !vAvailable ? "opacity-40 cursor-not-allowed line-through" : "",
                      ].join(" ")}
                    >
                      {v.name}
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {/* SKU */}
          {selectedVariant && (
            <p className="mt-3 text-xs text-[var(--color-ink-secondary)]">
              SKU: {selectedVariant.sku}
            </p>
          )}

          {/* Quantity + Add to cart */}
          <div className="mt-6 flex flex-wrap items-center gap-3">
            <div className="flex items-center rounded-[var(--radius-md)] border border-[var(--color-border)]">
              <button
                type="button"
                onClick={() => setQuantity(Math.max(1, quantity - 1))}
                className="min-h-11 min-w-11 text-lg text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)] transition-colors"
                aria-label="Decrease quantity"
              >
                −
              </button>
              <span className="min-w-10 text-center text-sm font-medium text-[var(--color-ink-primary)]">
                {quantity}
              </span>
              <button
                type="button"
                onClick={() => setQuantity(quantity + 1)}
                className="min-h-11 min-w-11 text-lg text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)] transition-colors"
                aria-label="Increase quantity"
              >
                +
              </button>
            </div>

            <Button
              onClick={handleAddToCart}
              disabled={!isAvailable || !selectedVariant}
            >
              {addedToCart ? "Added!" : "Add to cart"}
            </Button>
          </div>

          {addedToCart && (
            <Link
              href={`/store/${slug}/cart`}
              className="mt-3 text-sm font-medium text-[var(--color-ink-primary)] hover:underline"
            >
              View cart →
            </Link>
          )}

          {/* Tags */}
          {product.tags.length > 0 && (
            <div className="mt-6 flex flex-wrap gap-2">
              {product.tags.map((tag) => (
                <span
                  key={tag}
                  className="rounded-full bg-[var(--color-canvas-subtle)] px-3 py-1 text-[11px] text-[var(--color-ink-secondary)]"
                >
                  {tag}
                </span>
              ))}
            </div>
          )}
        </div>
      </div>

      <p className="mt-8 text-center text-[10px] text-[var(--color-ink-secondary)]">
        This is a demo storefront. No real transactions occur.
      </p>
    </div>
  );
}
