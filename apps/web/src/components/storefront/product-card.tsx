import Link from "next/link";
import type { Product } from "@/lib/types";

interface ProductCardProps {
  product: Product;
  storeSlug: string;
}

function priceRange(p: Product): string {
  if (p.variants.length === 0) return "—";
  const prices = p.variants.filter((v) => v.isPublished).map((v) => v.price.amount);
  if (prices.length === 0) return "—";
  const min = Math.min(...prices);
  const max = Math.max(...prices);
  if (min === max) return `Rs. ${min.toLocaleString("en-IN")}`;
  return `Rs. ${min.toLocaleString("en-IN")}–${max.toLocaleString("en-IN")}`;
}

export function ProductCard({ product, storeSlug }: ProductCardProps) {
  const firstMedia = product.media[0];

  return (
    <Link
      href={`/store/${storeSlug}/product/${product.id}`}
      className="group flex flex-col overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] transition-colors duration-[var(--duration-hover)] hover:border-[var(--color-ink-secondary)]"
    >
      {/* Image placeholder */}
      <div className="relative flex aspect-square items-center justify-center bg-[var(--color-canvas-subtle)]">
        {firstMedia ? (
          <div className="flex flex-col items-center gap-2 text-[var(--color-ink-secondary)]">
            <svg
              width="32"
              height="32"
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
            <span className="text-[10px]">{firstMedia.altText || "Product image"}</span>
          </div>
        ) : (
          <span className="text-xs text-[var(--color-ink-secondary)]">No image</span>
        )}
      </div>

      {/* Details */}
      <div className="flex flex-1 flex-col p-3">
        <h3 className="text-sm font-semibold text-[var(--color-ink-primary)] group-hover:underline">
          {product.title}
        </h3>
        <p className="mt-1 text-sm text-[var(--color-ink-primary)]">
          {priceRange(product)}
        </p>
        {product.variants.length > 1 && (
          <p className="mt-0.5 text-[11px] text-[var(--color-ink-secondary)]">
            {product.variants.filter((v) => v.isPublished).length} options
          </p>
        )}
      </div>
    </Link>
  );
}
