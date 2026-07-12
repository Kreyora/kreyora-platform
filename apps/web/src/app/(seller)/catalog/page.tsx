"use client";

import { useEffect, useState, useMemo } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { Product, Collection } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const STATUS_BADGE: Record<string, { label: string; variant: BadgeVariant }> = {
  draft: { label: "Draft", variant: "neutral" },
  published: { label: "Published", variant: "success" },
  unpublished: { label: "Unpublished", variant: "warning" },
  archived: { label: "Archived", variant: "neutral" },
};

function priceRange(p: Product): string {
  if (p.variants.length === 0) return "—";
  const prices = p.variants.map((v) => v.price.amount);
  const min = Math.min(...prices);
  const max = Math.max(...prices);
  if (min === max) return `Rs. ${min.toLocaleString("en-IN")}`;
  return `Rs. ${min.toLocaleString("en-IN")}–${max.toLocaleString("en-IN")}`;
}

export default function CatalogPage() {
  const { catalog } = useClients();
  const { effectiveRole } = useSession();
  const [products, setProducts] = useState<Product[]>([]);
  const [collections, setCollections] = useState<Collection[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [collectionFilter, setCollectionFilter] = useState("");

  useEffect(() => {
    let cancelled = false;
    Promise.all([catalog.listProducts(), catalog.getCollections()]).then(
      ([p, c]) => {
        if (!cancelled) {
          setProducts(p.items);
          setCollections(c);
          setIsLoading(false);
        }
      },
    );
    return () => {
      cancelled = true;
    };
  }, [catalog]);

  const filtered = useMemo(() => {
    let result = products;
    if (search) {
      const q = search.toLowerCase();
      result = result.filter(
        (p) =>
          p.title.toLowerCase().includes(q) ||
          p.description.toLowerCase().includes(q) ||
          p.tags.some((t) => t.toLowerCase().includes(q)),
      );
    }
    if (statusFilter) {
      result = result.filter((p) => p.publishState === statusFilter);
    }
    if (collectionFilter) {
      result = result.filter((p) => p.collections.includes(collectionFilter));
    }
    return result;
  }, [products, search, statusFilter, collectionFilter]);

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-32" />
        <Skeleton className="mb-6 h-4 w-64" />
        <Skeleton className="mb-4 h-11 w-full" />
        <div className="space-y-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-md)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <h1 className="text-heading-page text-[var(--color-ink-primary)]">
            Catalog
          </h1>
          {effectiveRole === "viewer" && <ViewerBadge />}
        </div>
        {effectiveRole !== "viewer" && (
          <Link
            href="/catalog/new"
            className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-transparent bg-[var(--color-surface-dark)] px-[var(--space-4)] text-sm font-medium text-[var(--color-on-dark)] transition-opacity duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:opacity-90"
          >
            Add product
          </Link>
        )}
      </div>

      {/* Search and filters */}
      <div className="mt-6 flex flex-wrap items-end gap-3">
        <div className="min-w-[200px] flex-1">
          <Input
            placeholder="Search products..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search products"
          />
        </div>
        <select
          value={collectionFilter}
          onChange={(e) => setCollectionFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by collection"
        >
          <option value="">All collections</option>
          {collections.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-3)] text-sm text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Filter by status"
        >
          <option value="">All statuses</option>
          <option value="draft">Draft</option>
          <option value="published">Published</option>
          <option value="unpublished">Unpublished</option>
          <option value="archived">Archived</option>
        </select>
      </div>

      {/* Product table */}
      {filtered.length === 0 ? (
        <div className="mt-8">
          <EmptyState
            title="No products found"
            description={
              search || statusFilter || collectionFilter
                ? "Try adjusting your search or filters."
                : "Get started by adding your first product."
            }
          />
        </div>
      ) : (
        <>
          {/* Desktop table */}
          <div className="mt-6 hidden overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-border)] md:block">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-[var(--color-border)] bg-[var(--color-canvas-subtle)]">
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Product
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Status
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Variants
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Price
                  </th>
                  <th className="px-4 py-2.5 text-xs font-medium text-[var(--color-ink-secondary)]">
                    Updated
                  </th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((p) => {
                  const status = STATUS_BADGE[p.publishState] ?? STATUS_BADGE.draft;
                  return (
                    <tr
                      key={p.id}
                      className="border-b border-[var(--color-border)] last:border-b-0 transition-colors hover:bg-[var(--color-canvas-subtle)]"
                    >
                      <td className="px-4 py-3">
                        <Link
                          href={`/catalog/${p.id}`}
                          className="font-medium text-[var(--color-ink-primary)] hover:underline"
                        >
                          {p.title}
                        </Link>
                      </td>
                      <td className="px-4 py-3">
                        <Badge variant={status.variant}>{status.label}</Badge>
                      </td>
                      <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                        {p.variants.length}
                      </td>
                      <td className="px-4 py-3 text-[var(--color-ink-primary)]">
                        {priceRange(p)}
                      </td>
                      <td className="px-4 py-3 text-[var(--color-ink-secondary)]">
                        {new Date(p.updatedAt).toLocaleDateString()}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Mobile cards */}
          <div className="mt-6 flex flex-col gap-3 md:hidden">
            {filtered.map((p) => {
              const status = STATUS_BADGE[p.publishState] ?? STATUS_BADGE.draft;
              return (
                <Link
                  key={p.id}
                  href={`/catalog/${p.id}`}
                  className="rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-4 transition-colors duration-[var(--duration-hover)] hover:bg-[var(--color-canvas-subtle)]"
                >
                  <div className="flex items-start justify-between gap-3">
                    <h3 className="text-sm font-medium text-[var(--color-ink-primary)]">
                      {p.title}
                    </h3>
                    <Badge variant={status.variant}>{status.label}</Badge>
                  </div>
                  <div className="mt-2 flex gap-4 text-xs text-[var(--color-ink-secondary)]">
                    <span>{p.variants.length} variant{p.variants.length !== 1 ? "s" : ""}</span>
                    <span>{priceRange(p)}</span>
                  </div>
                </Link>
              );
            })}
          </div>
        </>
      )}

      <p className="mt-6 text-xs text-[var(--color-ink-secondary)]">
        {products.length} product{products.length !== 1 ? "s" : ""} total
        {filtered.length !== products.length && `, ${filtered.length} shown`}
      </p>
    </div>
  );
}
