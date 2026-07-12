import type { StorefrontClient } from "@/lib/ports/storefront-client";
import { store, storeReadiness, deliveryRules, paymentMethods } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

export const mockStorefrontClient: StorefrontClient = {
  async getStore(_tenantId: string) {
    await delay();
    return store;
  },

  async getReadiness(_tenantId: string) {
    await delay();
    return storeReadiness;
  },

  async getDeliveryRules(_tenantId: string) {
    await delay();
    return deliveryRules;
  },

  async getPaymentMethods(_tenantId: string) {
    await delay();
    return paymentMethods;
  },
};
