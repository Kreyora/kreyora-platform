import type { Cart, CheckoutQuote, Order, Address } from "@/lib/types";
import type { PaymentMethodType } from "@/lib/types/checkout";

export interface CheckoutClient {
  getCart(storeSlug: string): Promise<Cart>;
  createQuote(storeSlug: string, params: {
    items: Array<{ variantId: string; quantity: number }>;
    deliveryAddress: Address;
    deliveryRuleId: string;
  }): Promise<CheckoutQuote>;
  submitOrder(storeSlug: string, params: {
    quoteReservationId: string;
    paymentMethod: PaymentMethodType;
    customerName: string;
    customerPhone: string;
    customerEmail?: string;
    deliveryAddress: Address;
  }): Promise<Order>;
}
