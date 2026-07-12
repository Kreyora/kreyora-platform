import type { PaymentClient } from "@/lib/ports/payment-client";
import { paymentMethods, paymentAttempts } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

export const mockPaymentClient: PaymentClient = {
  async getPaymentMethods(_tenantId: string) {
    await delay();
    return paymentMethods;
  },

  async getPaymentAttempts(orderId: string) {
    await delay();
    return paymentAttempts.filter((pa) => pa.orderId === orderId);
  },
};
