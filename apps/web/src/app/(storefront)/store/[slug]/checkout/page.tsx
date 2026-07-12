"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useClients } from "@/lib/providers/client-provider";
import { useCart } from "@/hooks/use-cart";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { DeliveryRule, Address, PaymentMethodType } from "@/lib/types";
import type { PaymentMethod } from "@/lib/types/payments";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

export default function CheckoutPage() {
  const { slug } = useParams<{ slug: string }>();
  const router = useRouter();
  const { storefront, checkout } = useClients();
  const { items, subtotal, clearCart } = useCart();

  const [deliveryRules, setDeliveryRules] = useState<DeliveryRule[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [line1, setLine1] = useState("");
  const [line2, setLine2] = useState("");
  const [city, setCity] = useState("");
  const [district, setDistrict] = useState("");
  const [selectedRuleId, setSelectedRuleId] = useState("");
  const [selectedPayment, setSelectedPayment] = useState<PaymentMethodType>("cod");
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      storefront.getDeliveryRules(DEMO_TENANT_ID),
      storefront.getPaymentMethods(DEMO_TENANT_ID),
    ]).then(([rules, methods]) => {
      if (!cancelled) {
        setDeliveryRules(rules.filter((r) => r.isActive));
        setPaymentMethods(methods.filter((m) => m.isEnabled));
        if (rules.length > 0) setSelectedRuleId(rules[0].id);
        setIsLoading(false);
      }
    });
    return () => { cancelled = true; };
  }, [storefront]);

  const selectedRule = deliveryRules.find((r) => r.id === selectedRuleId);
  const deliveryFee =
    selectedRule?.feeType === "threshold" &&
    selectedRule.freeAbove &&
    subtotal >= selectedRule.freeAbove.amount
      ? 0
      : selectedRule?.flatFee?.amount ?? 0;
  const total = subtotal + deliveryFee;

  const isFormValid = name && phone && line1 && city && district && selectedRuleId;

  const handleSubmit = useCallback(async () => {
    if (!isFormValid || submitting || submitted) return;
    setSubmitting(true);

    const address: Address = {
      line1,
      line2: line2 || undefined,
      city,
      district,
      country: "NP",
      contactName: name,
      contactPhone: phone,
    };

    try {
      const quote = await checkout.createQuote(slug, {
        items: items.map((i) => ({ variantId: i.variantId, quantity: i.quantity })),
        deliveryAddress: address,
        deliveryRuleId: selectedRuleId,
      });

      const order = await checkout.submitOrder(slug, {
        quoteReservationId: quote.reservationId,
        paymentMethod: selectedPayment,
        customerName: name,
        customerPhone: phone,
        customerEmail: email || undefined,
        deliveryAddress: address,
      });

      setSubmitted(true);
      clearCart();
      router.push(`/store/${slug}/confirmation/${order.id}`);
    } catch {
      setSubmitting(false);
    }
  }, [
    isFormValid, submitting, submitted, name, phone, email,
    line1, line2, city, district, selectedRuleId, selectedPayment,
    items, slug, checkout, clearCart, router,
  ]);

  if (items.length === 0 && !submitted) {
    return (
      <div className="flex flex-col items-center py-16 text-center">
        <h1 className="text-lg font-semibold text-[var(--color-ink-primary)]">
          Nothing to check out
        </h1>
        <p className="mt-1 text-sm text-[var(--color-ink-secondary)]">
          Add items to your cart first.
        </p>
        <Link
          href={`/store/${slug}`}
          className="mt-4 inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] px-[var(--space-4)] text-sm font-medium text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)]"
        >
          Browse products
        </Link>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-6 h-8 w-40" />
        <div className="space-y-4">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-11 w-full" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-xl font-bold text-[var(--color-ink-primary)]">Checkout</h1>

      <div className="mt-6 grid gap-8 lg:grid-cols-3">
        {/* Form */}
        <div className="space-y-6 lg:col-span-2">
          {/* Contact */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
              Contact Information
            </h2>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input
                label="Full name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                placeholder="e.g. Sita Shrestha"
              />
              <Input
                label="Phone"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                required
                placeholder="+977-98..."
              />
              <Input
                label="Email (optional)"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="your@email.com"
              />
            </div>
          </section>

          {/* Address */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
              Delivery Address
            </h2>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input
                label="Address line 1"
                value={line1}
                onChange={(e) => setLine1(e.target.value)}
                required
                placeholder="Street address, ward"
              />
              <Input
                label="Address line 2"
                value={line2}
                onChange={(e) => setLine2(e.target.value)}
                placeholder="Landmark, area"
              />
              <Input
                label="City"
                value={city}
                onChange={(e) => setCity(e.target.value)}
                required
                placeholder="e.g. Kathmandu"
              />
              <Input
                label="District"
                value={district}
                onChange={(e) => setDistrict(e.target.value)}
                required
                placeholder="e.g. Kathmandu"
              />
            </div>
          </section>

          {/* Delivery rule */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
              Delivery Method
            </h2>
            <div className="flex flex-col gap-2">
              {deliveryRules.map((rule) => {
                const fee =
                  rule.feeType === "threshold" &&
                  rule.freeAbove &&
                  subtotal >= rule.freeAbove.amount
                    ? 0
                    : rule.flatFee?.amount ?? 0;
                return (
                  <label
                    key={rule.id}
                    className={[
                      "flex cursor-pointer items-start gap-3 rounded-[var(--radius-md)] border p-4 transition-colors",
                      rule.id === selectedRuleId
                        ? "border-[var(--color-surface-dark)] bg-[var(--color-canvas-subtle)]"
                        : "border-[var(--color-border)] hover:bg-[var(--color-canvas-subtle)]",
                    ].join(" ")}
                  >
                    <input
                      type="radio"
                      name="delivery"
                      checked={rule.id === selectedRuleId}
                      onChange={() => setSelectedRuleId(rule.id)}
                      className="mt-1"
                    />
                    <div className="flex-1">
                      <p className="text-sm font-medium text-[var(--color-ink-primary)]">
                        {rule.name}
                      </p>
                      <p className="text-xs text-[var(--color-ink-secondary)]">
                        {rule.zones.join(", ")}
                        {rule.estimatedDays && ` · ${rule.estimatedDays}`}
                      </p>
                      <p className="mt-1 text-sm font-medium text-[var(--color-ink-primary)]">
                        {fee === 0 ? "Free" : `Rs. ${fee.toLocaleString("en-IN")}`}
                      </p>
                      {rule.freeAbove && fee > 0 && (
                        <p className="text-[11px] text-[var(--color-ink-secondary)]">
                          Free above Rs. {rule.freeAbove.amount.toLocaleString("en-IN")}
                        </p>
                      )}
                    </div>
                  </label>
                );
              })}
            </div>
          </section>

          {/* Payment */}
          <section>
            <h2 className="mb-3 text-base font-semibold text-[var(--color-ink-primary)]">
              Payment Method
            </h2>
            <div className="flex flex-col gap-2">
              {paymentMethods.map((pm) => (
                <label
                  key={pm.id}
                  className={[
                    "flex cursor-pointer items-start gap-3 rounded-[var(--radius-md)] border p-4 transition-colors",
                    pm.type === selectedPayment
                      ? "border-[var(--color-surface-dark)] bg-[var(--color-canvas-subtle)]"
                      : "border-[var(--color-border)] hover:bg-[var(--color-canvas-subtle)]",
                  ].join(" ")}
                >
                  <input
                    type="radio"
                    name="payment"
                    checked={pm.type === selectedPayment}
                    onChange={() => setSelectedPayment(pm.type)}
                    className="mt-1"
                  />
                  <div>
                    <p className="text-sm font-medium text-[var(--color-ink-primary)]">
                      {pm.label}
                    </p>
                    {pm.instructions && (
                      <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
                        {pm.instructions}
                      </p>
                    )}
                    {pm.qrImageUrl && pm.type === selectedPayment && (
                      <div className="mt-3 flex h-32 w-32 items-center justify-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)]">
                        <span className="text-[10px] text-[var(--color-ink-secondary)]">
                          QR placeholder
                        </span>
                      </div>
                    )}
                  </div>
                </label>
              ))}
            </div>
          </section>
        </div>

        {/* Order summary sidebar */}
        <div className="lg:self-start">
          <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">
              Order Summary
            </h2>
            <div className="mt-4 flex flex-col divide-y divide-[var(--color-border)]">
              {items.map((item) => (
                <div key={item.variantId} className="flex justify-between py-2 text-sm">
                  <span className="text-[var(--color-ink-secondary)]">
                    {item.productTitle} × {item.quantity}
                  </span>
                  <span className="text-[var(--color-ink-primary)]">
                    Rs. {(item.unitPrice.amount * item.quantity).toLocaleString("en-IN")}
                  </span>
                </div>
              ))}
            </div>
            <div className="mt-3 space-y-1 border-t border-[var(--color-border)] pt-3 text-sm">
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Subtotal</span>
                <span className="text-[var(--color-ink-primary)]">
                  Rs. {subtotal.toLocaleString("en-IN")}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-[var(--color-ink-secondary)]">Delivery</span>
                <span className="text-[var(--color-ink-primary)]">
                  {deliveryFee === 0 ? "Free" : `Rs. ${deliveryFee.toLocaleString("en-IN")}`}
                </span>
              </div>
            </div>
            <div className="mt-3 border-t border-[var(--color-border)] pt-3">
              <div className="flex justify-between">
                <span className="text-sm font-semibold text-[var(--color-ink-primary)]">
                  Total
                </span>
                <span className="text-lg font-bold text-[var(--color-ink-primary)]">
                  Rs. {total.toLocaleString("en-IN")}
                </span>
              </div>
            </div>

            <Button
              className="mt-4 w-full"
              onClick={handleSubmit}
              loading={submitting}
              disabled={!isFormValid || submitted}
            >
              {submitted ? "Order placed" : "Place order (simulated)"}
            </Button>

            <p className="mt-3 text-center text-[10px] text-[var(--color-ink-secondary)]">
              This is a demo checkout. No real payment or order is processed.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
