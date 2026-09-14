"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useCart } from "@/hooks/use-cart";
import { usePublicStorefrontClient, USING_PUBLIC_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import type { PublicCatalogProduct } from "@/lib/types/public-storefront";

export default function ProductDetailPage() {
  const { slug, id: productSlug } = useParams<{ slug: string; id: string }>();
  const storefront = usePublicStorefrontClient();
  const { addItem } = useCart();
  const [product, setProduct] = useState<PublicCatalogProduct | null>(null);
  const [selectedVariantId, setSelectedVariantId] = useState<string>();
  const [quantity, setQuantity] = useState(1);
  const [galleryIndex, setGalleryIndex] = useState(0);
  const [error, setError] = useState(false);
  const [added, setAdded] = useState(false);

  useEffect(() => {
    let active = true;
    Promise.resolve().then(() => {
      if (active) { setProduct(null); setError(false); }
      return storefront.getProduct(slug, productSlug);
    }).then((value) => {
      if (active) { setProduct(value); setSelectedVariantId(value.variants[0]?.id); }
    }).catch(() => active && setError(true));
    return () => { active = false; };
  }, [slug, productSlug, storefront]);

  const selected = useMemo(() => product?.variants.find((variant) => variant.id === selectedVariantId), [product, selectedVariantId]);
  if (error) return <div className="py-16 text-center"><h1 className="text-xl font-bold">This product is unavailable</h1><Link href={`/store/${slug}`} className="mt-4 inline-block text-sm underline">Back to store</Link></div>;
  if (!product) return <div className="grid gap-8 md:grid-cols-2"><Skeleton className="aspect-square w-full rounded-[var(--radius-lg)]" /><div className="space-y-4"><Skeleton className="h-8 w-3/4" /><Skeleton className="h-24 w-full" /></div></div>;

  const media = product.media[galleryIndex];
  const add = () => {
    if (!selected) return;
    addItem({ variantId: selected.id, productTitle: product.title, productSlug: product.slug, variantName: selected.name, imageId: product.media[0]?.id, imageAlt: product.media[0]?.altText, unitPriceNpr: selected.priceNpr, quantity });
    setAdded(true); window.setTimeout(() => setAdded(false), 2000);
  };

  return <div>
    <nav className="mb-4 text-sm text-[var(--color-ink-secondary)]" aria-label="Breadcrumb"><Link href={`/store/${slug}`} className="hover:underline">Home</Link><span className="mx-2">/</span><span>{product.title}</span></nav>
    <div className="grid gap-8 md:grid-cols-2">
      <div>
        <div className="aspect-square overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
          {media ? <img src={storefront.getMediaUrl(slug, media.id)} alt={media.altText ?? product.title} className="h-full w-full object-cover" /> : <div className="flex h-full items-center justify-center text-sm text-[var(--color-ink-secondary)]">No images</div>}
        </div>
        {product.media.length > 1 && <div className="mt-3 flex gap-2 overflow-x-auto">{product.media.map((item, index) => <button key={item.id} type="button" onClick={() => setGalleryIndex(index)} className={`h-14 w-14 shrink-0 overflow-hidden rounded-[var(--radius-md)] border ${galleryIndex === index ? "border-[var(--color-surface-dark)]" : "border-[var(--color-border)]"}`} aria-label={`View image ${index + 1}`}><img src={storefront.getMediaUrl(slug, item.id)} alt="" className="h-full w-full object-cover" /></button>)}</div>}
      </div>
      <div>
        <h1 className="text-2xl font-bold text-[var(--color-ink-primary)]">{product.title}</h1>
        {selected && <div className="mt-3"><span className="text-xl font-bold">Rs. {selected.priceNpr.toLocaleString("en-IN")}</span>{selected.compareAtPriceNpr && <span className="ml-2 text-sm text-[var(--color-ink-secondary)] line-through">Rs. {selected.compareAtPriceNpr.toLocaleString("en-IN")}</span>}</div>}
        {product.description && <p className="mt-4 text-sm leading-relaxed text-[var(--color-ink-secondary)]">{product.description}</p>}
        {product.variants.length > 1 && <fieldset className="mt-6"><legend className="mb-2 text-sm font-medium">Options</legend><div className="flex flex-wrap gap-2">{product.variants.map((variant) => <button key={variant.id} type="button" onClick={() => { setSelectedVariantId(variant.id); setQuantity(1); }} className={`min-h-11 rounded-[var(--radius-md)] border px-4 text-sm ${variant.id === selectedVariantId ? "border-[var(--color-surface-dark)] bg-[var(--color-surface-dark)] text-[var(--color-on-dark)]" : "border-[var(--color-border)]"}`}>{variant.name}</button>)}</div></fieldset>}
        <div className="mt-6 flex flex-wrap items-center gap-3"><div className="flex items-center rounded-[var(--radius-md)] border border-[var(--color-border)]"><button type="button" onClick={() => setQuantity(Math.max(1, quantity - 1))} className="min-h-11 min-w-11" aria-label="Decrease quantity">−</button><span className="min-w-10 text-center">{quantity}</span><button type="button" onClick={() => setQuantity(quantity + 1)} className="min-h-11 min-w-11" aria-label="Increase quantity">+</button></div><Button onClick={add} disabled={!selected}>{added ? "Added!" : "Add to cart"}</Button></div>
        {added && <Link href={`/store/${slug}/cart`} className="mt-3 inline-block text-sm font-medium underline">View cart →</Link>}
      </div>
    </div>
    {USING_PUBLIC_FIXTURE_ADAPTERS && <p className="mt-8 text-center text-[10px] text-[var(--color-ink-secondary)]">Demo storefront — no real transactions occur.</p>}
  </div>;
}
