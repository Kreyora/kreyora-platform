import type { PaymentMethod, PaymentAttempt } from "@/lib/types/payments";

export interface PaymentClient {
  getPaymentMethods(tenantId: string): Promise<PaymentMethod[]>;
  getPaymentAttempts(orderId: string): Promise<PaymentAttempt[]>;
}
