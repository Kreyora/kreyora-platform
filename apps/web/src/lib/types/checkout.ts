import type { Money, Address } from "./common";

export interface CartItem {
  variantId: string;
  productTitle: string;
  variantName: string;
  imageUrl?: string;
  unitPrice: Money;
  quantity: number;
  available: boolean;
}

export interface Cart {
  items: CartItem[];
  subtotal: Money;
  itemCount: number;
}

export interface DeliveryQuote {
  ruleId: string;
  ruleName: string;
  fee: Money;
  estimatedDays?: string;
  codAvailable: boolean;
  expiresAt: string;
}

export type PaymentMethodType = "cod" | "merchant_qr";

export interface PaymentMethodOption {
  type: PaymentMethodType;
  label: string;
  description: string;
  isAvailable: boolean;
  qrImageUrl?: string;
  instructions?: string;
}

export interface CheckoutQuote {
  subtotal: Money;
  deliveryFee: Money;
  total: Money;
  items: CartItem[];
  deliveryQuote: DeliveryQuote;
  availablePaymentMethods: PaymentMethodOption[];
  reservationId: string;
  expiresAt: string;
}
