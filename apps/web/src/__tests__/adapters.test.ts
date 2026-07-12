import { describe, it, expect } from "vitest";
import {
  mockIdentityClient,
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
import { TENANT_SLUG } from "@/lib/adapters/fixtures/data";

describe("Mock adapters implement port interfaces", () => {
  it("IdentityClient returns a session with user, tenant, and membership", async () => {
    const session = await mockIdentityClient.getCurrentSession();
    expect(session.user).toBeDefined();
    expect(session.user.id).toBeTruthy();
    expect(session.tenant).toBeDefined();
    expect(session.tenant.slug).toBeTruthy();
    expect(session.membership).toBeDefined();
    expect(session.membership.role).toBeTruthy();
  });

  it("IdentityClient returns workspaces", async () => {
    const workspaces = await mockIdentityClient.getWorkspaces();
    expect(Array.isArray(workspaces)).toBe(true);
    expect(workspaces.length).toBeGreaterThan(0);
  });

  it("IdentityClient returns onboarding state", async () => {
    const state = await mockIdentityClient.getOnboardingState("test");
    expect(state.steps).toBeDefined();
    expect(Array.isArray(state.steps)).toBe(true);
    expect(typeof state.isActivationReady).toBe("boolean");
  });

  it("IdentityClient returns team members as paginated result", async () => {
    const result = await mockIdentityClient.getTeamMembers("test");
    expect(result.items).toBeDefined();
    expect(Array.isArray(result.items)).toBe(true);
    expect(typeof result.hasMore).toBe("boolean");
  });

  it("CatalogClient returns products as paginated result", async () => {
    const result = await mockCatalogClient.listProducts();
    expect(result.items).toBeDefined();
    expect(Array.isArray(result.items)).toBe(true);
    expect(result.items.length).toBeGreaterThan(0);
    expect(result.items[0].title).toBeTruthy();
    expect(result.items[0].variants).toBeDefined();
  });

  it("CatalogClient returns a single product by id", async () => {
    const list = await mockCatalogClient.listProducts();
    const product = await mockCatalogClient.getProduct(list.items[0].id);
    expect(product.id).toBe(list.items[0].id);
    expect(product.publishState).toBeTruthy();
  });

  it("CatalogClient returns collections", async () => {
    const collections = await mockCatalogClient.getCollections();
    expect(Array.isArray(collections)).toBe(true);
  });

  it("InventoryClient returns inventory for a variant", async () => {
    const products = await mockCatalogClient.listProducts();
    const variantId = products.items[0].variants[0].id;
    const inventory = await mockInventoryClient.getInventory(variantId);
    expect(inventory.variantId).toBe(variantId);
    expect(typeof inventory.onHand).toBe("number");
    expect(typeof inventory.available).toBe("number");
  });

  it("InventoryClient returns low stock items", async () => {
    const lowStock = await mockInventoryClient.getLowStock();
    expect(Array.isArray(lowStock)).toBe(true);
    for (const item of lowStock) {
      expect(item.isLowStock).toBe(true);
    }
  });

  it("StorefrontClient returns store with readiness", async () => {
    const store = await mockStorefrontClient.getStore("test");
    expect(store.slug).toBeTruthy();
    expect(store.profile).toBeDefined();
    expect(store.readiness).toBeDefined();
    expect(typeof store.readiness.isReady).toBe("boolean");
  });

  it("StorefrontClient returns delivery rules", async () => {
    const rules = await mockStorefrontClient.getDeliveryRules("test");
    expect(Array.isArray(rules)).toBe(true);
    expect(rules.length).toBeGreaterThan(0);
  });

  it("CheckoutClient returns a cart", async () => {
    const cart = await mockCheckoutClient.getCart(TENANT_SLUG);
    expect(cart.items).toBeDefined();
    expect(cart.subtotal).toBeDefined();
    expect(typeof cart.itemCount).toBe("number");
  });

  it("OrderClient returns orders as paginated result", async () => {
    const result = await mockOrderClient.listOrders();
    expect(result.items).toBeDefined();
    expect(result.items.length).toBeGreaterThan(0);
    const order = result.items[0];
    expect(order.status).toBeTruthy();
    expect(order.paymentStatus).toBeTruthy();
    expect(order.fulfilmentStatus).toBeTruthy();
  });

  it("PaymentClient returns payment methods", async () => {
    const methods = await mockPaymentClient.getPaymentMethods("test");
    expect(Array.isArray(methods)).toBe(true);
    expect(methods.length).toBeGreaterThan(0);
  });

  it("ConversationClient returns conversations", async () => {
    const result = await mockConversationClient.listConversations();
    expect(result.items).toBeDefined();
    expect(result.items.length).toBeGreaterThan(0);
    expect(result.items[0].channel).toBeTruthy();
    expect(result.items[0].state).toBeTruthy();
  });

  it("IntegrationClient returns connections", async () => {
    const connections = await mockIntegrationClient.listConnections();
    expect(Array.isArray(connections)).toBe(true);
    expect(connections.length).toBeGreaterThan(0);
    expect(connections[0].provider).toBeTruthy();
  });

  it("AIClient returns assistant config", async () => {
    const config = await mockAIClient.getAssistantConfig("test");
    expect(typeof config.isEnabled).toBe("boolean");
    expect(config.language).toBeTruthy();
  });

  it("BillingClient returns plan and subscription", async () => {
    const result = await mockBillingClient.getPlan("test");
    expect(result.plan).toBeDefined();
    expect(result.plan.name).toBeTruthy();
    expect(result.subscription).toBeDefined();
  });

  it("BillingClient returns quota status", async () => {
    const quotas = await mockBillingClient.getQuotaStatus("test");
    expect(Array.isArray(quotas)).toBe(true);
    for (const q of quotas) {
      expect(q.level).toBeTruthy();
      expect(typeof q.percentUsed).toBe("number");
    }
  });

  it("ReportingClient returns dashboard metrics", async () => {
    const metrics = await mockReportingClient.getDashboardMetrics("test");
    expect(typeof metrics.totalOrders).toBe("number");
    expect(metrics.totalRevenue).toBeDefined();
  });

  it("AuditClient returns audit events", async () => {
    const result = await mockAuditClient.listAuditEvents();
    expect(result.items).toBeDefined();
    expect(result.items.length).toBeGreaterThan(0);
    expect(result.items[0].actor).toBeDefined();
    expect(result.items[0].action).toBeTruthy();
  });
});
