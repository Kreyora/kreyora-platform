"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useSession } from "@/hooks/use-session";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Product, ProductVariant, MediaAsset, Collection } from "@/lib/types";

interface ProductFormProps {
  product?: Product;
  collections: Collection[];
  isEdit?: boolean;
  onSave?: () => void;
  onDelete?: () => void;
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

function MediaSection({ media }: { media: MediaAsset[] }) {
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
}: ProductFormProps) {
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";

  const [title, setTitle] = useState(product?.title ?? "");
  const [description, setDescription] = useState(product?.description ?? "");
  const [slug, setSlug] = useState(product?.slug ?? "");
  const [publishState, setPublishState] = useState<"draft" | "published" | "unpublished" | "archived">(product?.publishState ?? "draft");
  const [tags, setTags] = useState(product?.tags.join(", ") ?? "");
  const [selectedCollections, setSelectedCollections] = useState<string[]>(
    product?.collections ?? [],
  );
  const [saving, setSaving] = useState(false);

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

  function handleSave() {
    setSaving(true);
    setTimeout(() => {
      setSaving(false);
      onSave?.();
    }, 600);
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

      {/* Variants (edit mode) */}
      {isEdit && product && (
        <div>
          <div className="mb-3 flex items-center justify-between">
            <p className="text-sm font-medium text-[var(--color-ink-primary)]">
              Variants ({product.variants.length})
            </p>
            {!isViewer && (
              <Button variant="outline" size="sm" disabled>
                Add variant (simulated)
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
              <Button variant="outline" size="sm" disabled>
                Upload media (simulated)
              </Button>
            )}
          </div>
          <MediaSection media={product.media} />
        </div>
      )}

      {/* Actions */}
      {!isViewer && (
        <div className="flex flex-wrap items-center gap-3 border-t border-[var(--color-border)] pt-6">
          <Button onClick={handleSave} loading={saving}>
            {isEdit ? "Save changes" : "Create product"} (simulated)
          </Button>
          {isEdit && onDelete && (
            <Button variant="ghost" onClick={onDelete}>
              Delete (simulated)
            </Button>
          )}
        </div>
      )}

      <p className="text-xs text-[var(--color-ink-secondary)]">
        Changes are simulated and will not be persisted.
      </p>
    </div>
  );
}
