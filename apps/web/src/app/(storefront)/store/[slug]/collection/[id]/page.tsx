"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { USING_PUBLIC_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";

export default function CollectionPage() {
  const { slug } = useParams<{ slug: string }>();
  return <div className="py-16 text-center"><h1 className="text-xl font-bold">Collections are unavailable</h1><p className="mt-2 text-sm text-[var(--color-ink-secondary)]">{USING_PUBLIC_FIXTURE_ADAPTERS ? "Collection browsing is not included in this demo path." : "This storefront currently lists products directly."}</p><Link href={`/store/${slug}`} className="mt-4 inline-block text-sm underline">Back to store</Link></div>;
}
