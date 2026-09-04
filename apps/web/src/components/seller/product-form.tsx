"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useSession } from "@/hooks/use-session";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Product, ProductVariant, MediaAsset, Collection } from "@/lib/types";
import type { ProductInput, ProductVariantInput } from "@/lib/ports/catalog-client";

interface ProductFormProps {
  product?: Product;
  collections: Collection[];
  isEdit?: boolean;
  onSave: (input: ProductInput & { initialVariant?: ProductVariantInput }) => Promise<void>;
  onDelete?: () => Promise<void>;
  onUploadMedia?: (file: File, altText?: string) => Promise<void>;
  onDeleteMedia?: (mediaId: string) => Promise<void>;
}

function slugify(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s-]/g, "")
    .replace(/\s+/g, "-")
    .replace(/-+/g, "-")
    .slice(0, 80);
}

function VariantsSection({ variants }: { variants: ProductVariant[] }) {
  if (variants.length === 0) {
    return (
      <div className="rounded-[var(--radius-md)] border border-dashed border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-6 text-center text-sm text-[var(--color-ink-secondary)]">
        No variants yet. Add your first variant to set pricing and SKU.
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
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Price</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Compare</th>
            <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">Published</th>
          </tr>
        </thead>
        <tbody>
          {variants.map((v) => (
            <tr key={v.id} className="border-b border-[var(--color-border)] last:border-b-0">
              <td className="px-4 py-3 font-medium text-[var(--color-ink-primary)]">{v.name}</td>
              <td className="px-4 py-3 text-[var(--color-ink-secondary)]">{v.sku}</td>
              <td className="px-4 py-3 text-[var(--color-ink-primary)]">
                Rs. {v.price.amount.toLocaleString("en-IN")}
              </td>
              <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                {v.compareAtPrice ? `Rs. ${v.compareAtPrice.amount.toLocaleString("en-IN")}` : "—"}
              </td>
              <td className="px-4 py-3">
                <Badge variant={v.isPublished ? "success" : "neutral"}>
                  {v.isPublished ? "Yes" : "No"}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function MediaSection({ media, onDelete }: { media: MediaAsset[]; onDelete?: (id: string) => Promise<void> }) {
  if (media.length === 0) {
    return (
      <div className="rounded-[var(--radius-md)] border border-dashed border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-6 text-center text-sm text-[var(--color-ink-secondary)]">
        No media uploaded. Add product images to attract customers.
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
      {media.map((m) => (
        <div
          key={m.id}
          className="flex aspect-square flex-col items-center justify-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-3"
        >
          <svg
            width="24"
            height="24"
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
          <span className="mt-2 text-center text-[11px] leading-tight text-[var(--color-ink-secondary)]">
            {m.altText || "Product image"}
          </span>
          <span className="mt-1 text-[10px] text-[var(--color-ink-secondary)]">
            #{m.sortOrder}
          </span>
          {onDelete && (
            <button type="button" onClick={() => void onDelete(m.id)} className="mt-2 text-xs text-[var(--color-danger)] underline">
              Remove
            </button>
          )}
        </div>
      ))}
    </div>
  );
}

export function ProductForm({
  product,
  collections,
  isEdit = false,
  onSave,
  onDelete,
  onUploadMedia,
  onDeleteMedia,
}: ProductFormProps) {
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [title, setTitle] = useState(product?.title ?? "");
  const [description, setDescription] = useState(product?.description ?? "");
  const [slug, setSlug] = useState(product?.slug ?? "");
  const [publishState, setPublishState] = useState<"draft" | "published" | "unpublished" | "archived">(product?.publishState ?? "draft");
  const [tags, setTags] = useState(product?.tags.join(", ") ?? "");
  const [variantName, setVariantName] = useState("Default");
  const [variantSku, setVariantSku] = useState("");
  const [variantPrice, setVariantPrice] = useState("");
  const [mediaAltText, setMediaAltText] = useState("");
  const [selectedCollections, setSelectedCollections] = useState<string[]>(
    product?.collections ?? [],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleTitleChange(value: string) {
    setTitle(value);
    if (!isEdit) {
      setSlug(slugify(value));
    }
  }

  function handleCollectionToggle(colId: string) {
    setSelectedCollections((prev) =>
      prev.includes(colId)
        ? prev.filter((c) => c !== colId)
        : [...prev, colId],
    );
  }

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await onSave({
        title,
        description: description || undefined,
        slug,
        initialVariant: isEdit ? undefined : {
          sku: variantSku,
          name: variantName,
          priceNpr: Number(variantPrice),
          isPublished: publishState === "published",
        },
      });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "We could not save this product. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="flex flex-col gap-8">
      {isViewer && (
        <div className="flex items-center gap-2">
          <ViewerBadge />
        </div>
      )}

      {/* Details */}
      <div className="flex flex-col gap-[var(--space-4)]">
        <Input
          label="Product title"
          value={title}
          onChange={(e) => handleTitleChange(e.target.value)}
          required
          disabled={isViewer}
          placeholder="e.g. Hand-knit Pashmina Shawl"
        />
        <Textarea
          label="Description"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          rows={4}
          disabled={isViewer}
          placeholder="Describe your product..."
        />
        <Input
          label="URL slug"
          value={slug}
          onChange={(e) => setSlug(e.target.value)}
          disabled={isViewer}
          hint="Used in the storefront URL"
        />
        <div className="flex flex-col gap-[var(--space-2)]">
          <label className="text-sm font-medium text-[var(--color-ink-primary)]">
            Status
          </label>
          <select
            value={publishState}
            onChange={(e) => setPublishState(e.target.value as "draft" | "published")}
            disabled={isViewer}
            className="min-h-11 w-full rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] disabled:cursor-not-allowed disabled:opacity-60"
          >
            <option value="draft">Draft</option>
            <option value="published">Published</option>
          </select>
        </div>
      </div>

      {/* Collections */}
      <div>
        <p className="mb-3 text-sm font-medium text-[var(--color-ink-primary)]">
          Collections
        </p>
        <div className="flex flex-wrap gap-3">
          {collections.map((c) => (
            <label
              key={c.id}
              className="flex items-center gap-2 text-sm text-[var(--color-ink-secondary)]"
            >
              <input
                type="checkbox"
                checked={selectedCollections.includes(c.id)}
                onChange={() => handleCollectionToggle(c.id)}
                disabled={isViewer}
                className="h-4 w-4 rounded border-[var(--color-border)]"
              />
              {c.name}
            </label>
          ))}
        </div>
      </div>

      {/* Tags */}
      <Input
        label="Tags"
        value={tags}
        onChange={(e) => setTags(e.target.value)}
        disabled={isViewer}
        placeholder="e.g. handmade, winter, textile"
        hint="Comma-separated tags for search and organization"
      />

      {!isEdit && (
        <div className="grid gap-4 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4 sm:grid-cols-3">
          <p className="sm:col-span-3 text-sm font-medium text-[var(--color-ink-primary)]">First variant</p>
          <Input label="Variant name" value={variantName} onChange={(event) => setVariantName(event.target.value)} disabled={isViewer} required />
          <Input label="SKU" value={variantSku} onChange={(event) => setVariantSku(event.target.value)} disabled={isViewer} required />
          <Input label="Price (NPR)" type="number" min="1" step="0.01" value={variantPrice} onChange={(event) => setVariantPrice(event.target.value)} disabled={isViewer} required />
        </div>
      )}

      {/* Variants (edit mode) */}
      {isEdit && product && (
        <div>
          <div className="mb-3 flex items-center justify-between">
            <p className="text-sm font-medium text-[var(--color-ink-primary)]">
              Variants ({product.variants.length})
            </p>
            {!isViewer && (
              <Button variant="outline" size="sm" disabled>
                Add variant (available in the next catalog update)
              </Button>
            )}
          </div>
          <VariantsSection variants={product.variants} />
        </div>
      )}

      {/* Media (edit mode) */}
      {isEdit && product && (
        <div>
          <div className="mb-3 flex items-center justify-between">
            <p className="text-sm font-medium text-[var(--color-ink-primary)]">
              Media ({product.media.length})
            </p>
            {!isViewer && (
              <label className="inline-flex min-h-9 cursor-pointer items-center rounded-[var(--radius-md)] border border-[var(--color-border)] px-3 text-sm font-medium text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)]">
                Upload media
                <input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" onChange={(event) => { const file = event.target.files?.[0]; if (file && onUploadMedia) void onUploadMedia(file, mediaAltText || undefined); event.currentTarget.value = ""; }} />
              </label>
            )}
          </div>
          {!isViewer && onUploadMedia && <Input label="Image alt text" value={mediaAltText} onChange={(event) => setMediaAltText(event.target.value)} placeholder="Describe this image" />}
          <div className="mt-3"><MediaSection media={product.media} onDelete={isViewer ? undefined : onDeleteMedia} /></div>
        </div>
      )}

      {/* Actions */}
      {!isViewer && (
        <div className="flex flex-wrap items-center gap-3 border-t border-[var(--color-border)] pt-6">
          <Button onClick={() => void handleSave()} loading={saving} disabled={!title || !slug || (!isEdit && (!variantSku || !variantPrice))}>
            {isEdit ? "Save changes" : "Create product"}
          </Button>
          {isEdit && onDelete && (
            <Button variant="ghost" onClick={() => void onDelete()}>
              Archive product
            </Button>
          )}
        </div>
      )}

      {error && <p role="alert" className="text-sm text-[var(--color-danger)]">{error}</p>}
    </div>
  );
}
