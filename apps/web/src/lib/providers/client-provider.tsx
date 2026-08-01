"use client";

import { createContext, useContext, type ReactNode } from "react";
import type { IdentityClient } from "@/lib/ports/identity-client";
import type { AuthClient } from "@/lib/ports/auth-client";
import type { CatalogClient } from "@/lib/ports/catalog-client";
import type { InventoryClient } from "@/lib/ports/inventory-client";
import type { StorefrontClient } from "@/lib/ports/storefront-client";
import type { CheckoutClient } from "@/lib/ports/checkout-client";
import type { OrderClient } from "@/lib/ports/order-client";
import type { PaymentClient } from "@/lib/ports/payment-client";
import type { ConversationClient } from "@/lib/ports/conversation-client";
import type { IntegrationClient } from "@/lib/ports/integration-client";
import type { AIClient } from "@/lib/ports/ai-client";
import type { BillingClient } from "@/lib/ports/billing-client";
import type { ReportingClient } from "@/lib/ports/reporting-client";
import type { AuditClient } from "@/lib/ports/audit-client";
import {
  mockIdentityClient,
  mockAuthClient,
  mockCatalogClient,
  mockInventoryClient,
  mockStorefrontClient,
  mockCheckoutClient,
  mockOrderClient,
  mockPaymentClient,
  mockConversationClient,
  mockIntegrationClient,
  mockAIClient,
  mockBillingClient,
  mockReportingClient,
  mockAuditClient,
} from "@/lib/adapters/mock";
import { apiAuthClient, apiAuditClient, apiIdentityClient } from "@/lib/adapters/api";

/**
 * Determined at build time from the NEXT_PUBLIC_API_URL env var.
 * When the var is set the app uses real API adapters; otherwise mock/fixture
 * adapters power the demo mode.
 */
export const USING_FIXTURE_ADAPTERS = !process.env.NEXT_PUBLIC_API_URL;

if (
  USING_FIXTURE_ADAPTERS &&
  typeof window !== "undefined" &&
  process.env.NODE_ENV === "production"
) {
  console.warn(
    "[Kreyora] Fixture adapters are active in a production build. " +
      "Set the NEXT_PUBLIC_API_URL environment variable to connect to the " +
      "real API before deploying.",
  );
}

export interface ClientSet {
  auth: AuthClient;
  identity: IdentityClient;
  catalog: CatalogClient;
  inventory: InventoryClient;
  storefront: StorefrontClient;
  checkout: CheckoutClient;
  order: OrderClient;
  payment: PaymentClient;
  conversation: ConversationClient;
  integration: IntegrationClient;
  ai: AIClient;
  billing: BillingClient;
  reporting: ReportingClient;
  audit: AuditClient;
}

const defaultClients: ClientSet = {
  auth: USING_FIXTURE_ADAPTERS ? mockAuthClient : apiAuthClient,
  identity: USING_FIXTURE_ADAPTERS ? mockIdentityClient : apiIdentityClient,
  catalog: mockCatalogClient,
  inventory: mockInventoryClient,
  storefront: mockStorefrontClient,
  checkout: mockCheckoutClient,
  order: mockOrderClient,
  payment: mockPaymentClient,
  conversation: mockConversationClient,
  integration: mockIntegrationClient,
  ai: mockAIClient,
  billing: mockBillingClient,
  reporting: mockReportingClient,
  audit: USING_FIXTURE_ADAPTERS ? mockAuditClient : apiAuditClient,
};

const ClientContext = createContext<ClientSet>(defaultClients);

export function ClientProvider({
  children,
  clients,
}: {
  children: ReactNode;
  clients?: Partial<ClientSet>;
}) {
  const merged = { ...defaultClients, ...clients };
  return <ClientContext.Provider value={merged}>{children}</ClientContext.Provider>;
}

export function useClients(): ClientSet {
  return useContext(ClientContext);
}

export function useIdentityClient(): IdentityClient {
  return useContext(ClientContext).identity;
}

export function useAuthClient(): AuthClient {
  return useContext(ClientContext).auth;
}

export function useCatalogClient(): CatalogClient {
  return useContext(ClientContext).catalog;
}

export function useInventoryClient(): InventoryClient {
  return useContext(ClientContext).inventory;
}

export function useStorefrontClient(): StorefrontClient {
  return useContext(ClientContext).storefront;
}

export function useCheckoutClient(): CheckoutClient {
  return useContext(ClientContext).checkout;
}

export function useOrderClient(): OrderClient {
  return useContext(ClientContext).order;
}

export function usePaymentClient(): PaymentClient {
  return useContext(ClientContext).payment;
}

export function useConversationClient(): ConversationClient {
  return useContext(ClientContext).conversation;
}

export function useIntegrationClient(): IntegrationClient {
  return useContext(ClientContext).integration;
}

export function useAIClient(): AIClient {
  return useContext(ClientContext).ai;
}

export function useBillingClient(): BillingClient {
  return useContext(ClientContext).billing;
}

export function useReportingClient(): ReportingClient {
  return useContext(ClientContext).reporting;
}

export function useAuditClient(): AuditClient {
  return useContext(ClientContext).audit;
}
