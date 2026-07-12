import type { Store, StoreReadiness, DeliveryRule } from "@/lib/types";
import type { PaymentMethod } from "@/lib/types/payments";

export interface StorefrontClient {
  getStore(tenantId: string): Promise<Store>;
  getReadiness(tenantId: string): Promise<StoreReadiness>;
  getDeliveryRules(tenantId: string): Promise<DeliveryRule[]>;
  getPaymentMethods(tenantId: string): Promise<PaymentMethod[]>;
}
