"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useCart } from "@/hooks/use-cart";
import { usePublicCheckoutClient } from "@/lib/providers/client-provider";
import { ApiClientError } from "@/lib/api/errors";
import type { PublicDeliveryQuote } from "@/lib/types/public-storefront";

function idempotencyKey(): string {
  return crypto.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

export default function CheckoutPage() {
  const { slug } = useParams<{ slug: string }>();
  const router = useRouter();
  const checkout = usePublicCheckoutClient();
  const { items, clearCart } = useCart();
  const [name, setName] = useState(""); const [phone, setPhone] = useState(""); const [email, setEmail] = useState("");
  const [line1, setLine1] = useState(""); const [line2, setLine2] = useState(""); const [district, setDistrict] = useState(""); const [municipality, setMunicipality] = useState(""); const [locality, setLocality] = useState("");
  const [privacy, setPrivacy] = useState(false);
  const [quote, setQuote] = useState<PublicDeliveryQuote | null>(null);
  const [isQuoting, setIsQuoting] = useState(false); const [isSubmitting, setIsSubmitting] = useState(false);
  const [message, setMessage] = useState<string>();
  const [sessionKey, setSessionKey] = useState<string>(); const [orderKey, setOrderKey] = useState<string>();

  const canQuote = items.length > 0 && district.trim().length > 0;
  const canSubmit = Boolean(quote?.delivery.codAvailable && name.trim() && phone.trim() && line1.trim() && privacy);
  const lines = useMemo(() => items.map((item) => ({ variantId: item.variantId, quantity: item.quantity })), [items]);
  const destination = () => ({ countryCode: "NP", district: district.trim(), municipality: municipality.trim() || undefined, locality: locality.trim() || undefined });
  const address = () => ({ addressLine1: line1.trim(), addressLine2: line2.trim() || undefined, district: district.trim(), municipality: municipality.trim() || undefined, locality: locality.trim() || undefined });
  const invalidateQuote = () => { setQuote(null); setSessionKey(undefined); setOrderKey(undefined); };

  const explain = (error: unknown) => {
    if (error instanceof ApiClientError) {
      if (error.status === 409) return "Price, stock, or delivery changed. Please calculate delivery again.";
      if (error.status === 429) return error.retryAfterSeconds ? `Please try again in ${error.retryAfterSeconds} seconds.` : "Too many requests. Please wait and try again.";
      return error.detail || "Please check your details and try again.";
    }
    return "We could not reach the store. Your cart is still saved; please try again.";
  };

  async function calculateQuote() {
    if (!canQuote || isQuoting) return;
    setIsQuoting(true); setMessage(undefined);
    try { setQuote(await checkout.createQuote({ slug, lines, destination: destination() })); setSessionKey(undefined); setOrderKey(undefined); }
    catch (error) { setMessage(explain(error)); }
    finally { setIsQuoting(false); }
  }

  async function placeOrder() {
    if (!quote || !canSubmit || isSubmitting) return;
    setIsSubmitting(true); setMessage(undefined);
    const currentSessionKey = sessionKey ?? idempotencyKey();
    const currentOrderKey = orderKey ?? idempotencyKey();
    setSessionKey(currentSessionKey); setOrderKey(currentOrderKey);
    try {
      const session = await checkout.createSession({ slug, quoteToken: quote.quoteToken, idempotencyKey: currentSessionKey, customer: { displayName: name.trim(), phone: phone.trim(), email: email.trim() || undefined, saveContact: false, privacyAcknowledged: privacy }, address: address() });
      const confirmation = await checkout.createCodOrder({ slug, checkoutSessionId: session.id, idempotencyKey: currentOrderKey });
      sessionStorage.setItem(`kreyora:public-confirmation:v1:${slug}:${confirmation.orderNumber}`, JSON.stringify(confirmation));
      clearCart(); router.push(`/store/${slug}/confirmation/${confirmation.orderNumber}`);
    } catch (error) {
      const text = explain(error); setMessage(text);
      if (error instanceof ApiClientError && error.status === 409) invalidateQuote();
      setIsSubmitting(false);
    }
  }

  if (items.length === 0) return <div className="py-16 text-center"><h1 className="text-lg font-semibold">Nothing to check out</h1><p className="mt-1 text-sm text-[var(--color-ink-secondary)]">Add items to your cart first.</p><Link href={`/store/${slug}`} className="mt-4 inline-block text-sm underline">Browse products</Link></div>;

  const total = quote?.totals;
  return <div><h1 className="text-xl font-bold">Checkout</h1><p className="mt-1 text-sm text-[var(--color-ink-secondary)]">Delivery and final totals are confirmed by the store before you place a COD order.</p>
    {message && <div role="alert" className="mt-4 rounded-[var(--radius-md)] border border-[var(--color-danger)] p-3 text-sm text-[var(--color-danger)]">{message}</div>}
    <div className="mt-6 grid gap-8 lg:grid-cols-3"><div className="space-y-6 lg:col-span-2">
      <section><h2 className="mb-3 text-base font-semibold">Contact information</h2><div className="grid gap-4 sm:grid-cols-2"><Input label="Full name" value={name} onChange={(event) => setName(event.target.value)} required /><Input label="Phone" value={phone} onChange={(event) => setPhone(event.target.value)} required /><Input label="Email (optional)" type="email" value={email} onChange={(event) => setEmail(event.target.value)} /></div></section>
      <section><h2 className="mb-3 text-base font-semibold">Delivery address</h2><div className="grid gap-4 sm:grid-cols-2"><Input label="Address line 1" value={line1} onChange={(event) => { setLine1(event.target.value); invalidateQuote(); }} required /><Input label="Address line 2" value={line2} onChange={(event) => { setLine2(event.target.value); invalidateQuote(); }} /><Input label="District" value={district} onChange={(event) => { setDistrict(event.target.value); invalidateQuote(); }} required /><Input label="Municipality" value={municipality} onChange={(event) => { setMunicipality(event.target.value); invalidateQuote(); }} /><Input label="Locality" value={locality} onChange={(event) => { setLocality(event.target.value); invalidateQuote(); }} /></div><Button className="mt-4" variant="outline" onClick={calculateQuote} loading={isQuoting} disabled={!canQuote}>Calculate delivery</Button></section>
      {quote && <section className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4"><h2 className="text-base font-semibold">Server quote</h2><p className="mt-1 text-sm text-[var(--color-ink-secondary)]">{quote.delivery.name}{quote.delivery.estimatedEtaText ? ` · ${quote.delivery.estimatedEtaText}` : ""}</p>{!quote.delivery.codAvailable && <p role="alert" className="mt-3 text-sm text-[var(--color-danger)]">Cash on delivery is not available for this destination. Edit the address and calculate again.</p>}<p className="mt-2 text-xs text-[var(--color-ink-secondary)]">Quote expires {new Date(quote.expiresAt).toLocaleTimeString()}</p></section>}
      <label className="flex items-start gap-3 text-sm"><input type="checkbox" checked={privacy} onChange={(event) => setPrivacy(event.target.checked)} className="mt-1" /><span>I acknowledge the store&apos;s privacy policy and allow these details to be used to process this order.</span></label>
    </div>
    <aside className="h-fit rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5"><h2 className="text-base font-semibold">Order summary</h2><div className="mt-4 space-y-2 text-sm">{items.map((item) => <div key={item.variantId} className="flex justify-between gap-3"><span>{item.productTitle} × {item.quantity}</span><span>Rs. {(item.unitPriceNpr * item.quantity).toLocaleString("en-IN")}</span></div>)}</div><div className="mt-4 space-y-2 border-t pt-4 text-sm"><div className="flex justify-between"><span>Merchandise</span><span>Rs. {(total?.merchandiseSubtotalNpr ?? 0).toLocaleString("en-IN")}</span></div><div className="flex justify-between"><span>Delivery</span><span>{quote ? `Rs. ${total?.deliveryFeeNpr.toLocaleString("en-IN")}` : "Calculate first"}</span></div><div className="flex justify-between font-bold"><span>Total</span><span>{quote ? `Rs. ${total?.totalNpr.toLocaleString("en-IN")}` : "—"}</span></div></div><Button className="mt-5 w-full" onClick={placeOrder} loading={isSubmitting} disabled={!canSubmit || isSubmitting}>Place cash-on-delivery order</Button></aside>
    </div></div>;
}
