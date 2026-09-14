import Link from "next/link";
import type { PublicCatalogProduct } from "@/lib/types/public-storefront";
import { usePublicStorefrontClient } from "@/lib/providers/client-provider";

interface ProductCardProps {
  product: PublicCatalogProduct;
  storeSlug: string;
}

function priceRange(p: PublicCatalogProduct): string {
  if (p.variants.length === 0) return "—";
  const prices = p.variants.map((v) => v.priceNpr);
  if (prices.length === 0) return "—";
  const min = Math.min(...prices);
  const max = Math.max(...prices);
  if (min === max) return `Rs. ${min.toLocaleString("en-IN")}`;
  return `Rs. ${min.toLocaleString("en-IN")}–${max.toLocaleString("en-IN")}`;
}

export function ProductCard({ product, storeSlug }: ProductCardProps) {
  const firstMedia = product.media[0];
  const storefront = usePublicStorefrontClient();

  return (
    <Link
      href={`/store/${storeSlug}/product/${product.slug}`}
      className="group flex flex-col overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] transition-colors duration-[var(--duration-hover)] hover:border-[var(--color-ink-secondary)]"
    >
      {/* Image placeholder */}
      <div className="relative flex aspect-square items-center justify-center bg-[var(--color-canvas-subtle)]">
        {firstMedia ? (
          <img src={storefront.getMediaUrl(storeSlug, firstMedia.id)} alt={firstMedia.altText ?? product.title} className="h-full w-full object-cover" />
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
            {product.variants.length} options
          </p>
        )}
      </div>
    </Link>
  );
}
