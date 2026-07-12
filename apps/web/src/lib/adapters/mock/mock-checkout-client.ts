import type { CheckoutClient } from "@/lib/ports/checkout-client";
import type { Order } from "@/lib/types";
import {
  TENANT_ID,
  TENANT_SLUG,
  demoCart,
  demoCheckoutQuote,
  allVariants,
  products,
  deliveryRules,
  paymentMethods,
} from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

export const mockCheckoutClient: CheckoutClient = {
  async getCart(storeSlug: string) {
    await delay();
    if (storeSlug !== TENANT_SLUG) {
      throw new Error(`Store not found: ${storeSlug}`);
    }
    return demoCart;
  },

  async createQuote(storeSlug, params) {
    await delay();
    if (storeSlug !== TENANT_SLUG) {
      throw new Error(`Store not found: ${storeSlug}`);
    }

    const items = params.items.map((item) => {
      const variant = allVariants.find((v) => v.id === item.variantId);
      if (!variant) {
        throw new Error(`Variant not found: ${item.variantId}`);
      }
      const product = products.find((p) => p.id === variant.productId);

      return {
        variantId: item.variantId,
        productTitle: product?.title ?? "Product",
        variantName: variant.name,
        unitPrice: variant.price,
        quantity: item.quantity,
        available: true,
      };
    });

    const subtotalAmount = items.reduce(
      (sum, i) => sum + i.unitPrice.amount * i.quantity,
      0,
    );

    const rule = deliveryRules.find((r) => r.id === params.deliveryRuleId);
    const deliveryFee =
      rule?.feeType === "threshold" &&
      rule.freeAbove &&
      subtotalAmount >= rule.freeAbove.amount
        ? 0
        : (rule?.flatFee?.amount ?? 150);

    const total = subtotalAmount + deliveryFee;

    return {
      ...demoCheckoutQuote,
      subtotal: { amount: subtotalAmount, currency: "NPR" },
      deliveryFee: { amount: deliveryFee, currency: "NPR" },
      total: { amount: total, currency: "NPR" },
      items,
      deliveryQuote: {
        ruleId: params.deliveryRuleId,
        ruleName: rule?.name ?? "Kathmandu Valley Delivery",
        fee: { amount: deliveryFee, currency: "NPR" },
        estimatedDays: rule?.estimatedDays,
        codAvailable: rule?.codAvailable ?? true,
        expiresAt: demoCheckoutQuote.deliveryQuote.expiresAt,
      },
      availablePaymentMethods: paymentMethods
        .filter((pm) => pm.isEnabled)
        .map((pm) => ({
          type: pm.type,
          label: pm.label,
          description: pm.instructions ?? "",
          isAvailable: true,
          qrImageUrl: pm.qrImageUrl,
          instructions: pm.instructions,
        })),
    };
  },

  async submitOrder(storeSlug, params) {
    await delay();
    if (storeSlug !== TENANT_SLUG) {
      throw new Error(`Store not found: ${storeSlug}`);
    }

    const order: Order = {
      id: "order-new-checkout",
      tenantId: TENANT_ID,
      orderNumber: "NC-2025-0099",
      status: "pending_confirmation",
      paymentStatus: params.paymentMethod === "cod" ? "pending" : "awaiting_verification",
      fulfilmentStatus: "unfulfilled",
      source: "storefront",
      items: demoCart.items.map((item, idx) => ({
        id: `oi-new-${idx + 1}`,
        variantId: item.variantId,
        productTitle: item.productTitle,
        variantName: item.variantName,
        sku: "NC-NEW",
        unitPrice: item.unitPrice,
        quantity: item.quantity,
        lineTotal: {
          amount: item.unitPrice.amount * item.quantity,
          currency: "NPR",
        },
      })),
      subtotal: demoCheckoutQuote.subtotal,
      deliveryFee: demoCheckoutQuote.deliveryFee,
      total: demoCheckoutQuote.total,
      currency: "NPR",
      customerName: params.customerName,
      customerPhone: params.customerPhone,
      customerEmail: params.customerEmail,
      deliveryAddress: params.deliveryAddress,
      paymentMethod: params.paymentMethod,
      activity: [
        {
          id: "oa-new-created",
          orderId: "order-new-checkout",
          action: "order.created",
          actorId: "system",
          actorName: "System",
          details: { quoteReservationId: params.quoteReservationId },
          createdAt: new Date().toISOString(),
        },
      ],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };

    return order;
  },
};
