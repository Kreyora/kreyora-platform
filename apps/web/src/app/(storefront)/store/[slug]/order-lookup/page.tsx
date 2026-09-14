"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { USING_PUBLIC_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";

export default function OrderLookupPage() {
  const { slug } = useParams<{ slug: string }>();
  return <div className="mx-auto max-w-lg py-12 text-center"><h1 className="text-xl font-bold">Order updates</h1><p className="mt-2 text-sm text-[var(--color-ink-secondary)]">{USING_PUBLIC_FIXTURE_ADAPTERS ? "Order tracking is available only in the demo fixture data." : "Online order tracking is not available yet. Please contact the store with your order number."}</p><Link href={`/store/${slug}`} className="mt-5 inline-block text-sm underline">Back to store</Link></div>;
}
