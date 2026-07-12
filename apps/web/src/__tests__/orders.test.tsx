import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";
import { getAllowedActions } from "@/lib/utils/order-actions";

const APP_DIR = path.resolve(__dirname, "../app");
const UTILS_DIR = path.resolve(__dirname, "../lib/utils");

describe("Orders — route file verification", () => {
  const routes = [
    { path: "(seller)/orders", label: "order list" },
    { path: "(seller)/orders/[id]", label: "order detail" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }

  it("order-actions utility file exists", () => {
    const file = path.join(UTILS_DIR, "order-actions.ts");
    expect(fs.existsSync(file)).toBe(true);
  });
});

describe("Orders — order list page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/orders/page.tsx"),
    "utf-8",
  );

  it("uses listOrders from order client", () => {
    expect(content).toContain("listOrders");
  });

  it("has search input", () => {
    expect(content).toContain("Search");
    expect(content).toContain('aria-label="Search orders"');
  });

  it("has status filter", () => {
    expect(content).toContain("statusFilter");
    expect(content).toContain('aria-label="Filter by status"');
  });

  it("has source filter", () => {
    expect(content).toContain("sourceFilter");
    expect(content).toContain('aria-label="Filter by source"');
  });

  it("has payment status filter", () => {
    expect(content).toContain("paymentFilter");
    expect(content).toContain('aria-label="Filter by payment"');
  });

  it("renders order/payment/fulfilment status badges", () => {
    expect(content).toContain("ORDER_STATUS");
    expect(content).toContain("PAYMENT_STATUS");
    expect(content).toContain("FULFILMENT_STATUS");
    expect(content).toContain("Badge");
  });

  it("has responsive layout (table + mobile cards)", () => {
    expect(content).toContain("hidden");
    expect(content).toContain("md:block");
    expect(content).toContain("md:hidden");
  });

  it("links to order detail page", () => {
    expect(content).toContain("/orders/");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});

describe("Orders — order detail page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/orders/[id]/page.tsx"),
    "utf-8",
  );

  it("uses getOrder to load data", () => {
    expect(content).toContain("getOrder");
  });

  it("uses getOrderActivity to load timeline", () => {
    expect(content).toContain("getOrderActivity");
  });

  it("uses getPaymentAttempts for payment section", () => {
    expect(content).toContain("getPaymentAttempts");
  });

  it("has financial snapshot section with items table", () => {
    expect(content).toContain("Items");
    expect(content).toContain("unitPrice");
    expect(content).toContain("lineTotal");
    expect(content).toContain("Subtotal");
    expect(content).toContain("Delivery");
  });

  it("has customer snapshot section", () => {
    expect(content).toContain("Customer");
    expect(content).toContain("customerName");
    expect(content).toContain("customerPhone");
  });

  it("has delivery snapshot section", () => {
    expect(content).toContain("Delivery");
    expect(content).toContain("deliveryAddress");
  });

  it("has payment section with attempts", () => {
    expect(content).toContain("Payment");
    expect(content).toContain("attempts");
  });

  it("has activity timeline", () => {
    expect(content).toContain("Activity Timeline");
    expect(content).toContain("allActivities");
  });

  it("has notification delivery status section", () => {
    expect(content).toContain("Notification Delivery");
    expect(content).toContain("notifications");
  });

  it("has three independent status badges", () => {
    expect(content).toContain("Pay:");
    expect(content).toContain("Ship:");
  });

  it("uses getAllowedActions for action policy", () => {
    expect(content).toContain("getAllowedActions");
  });

  it("has action confirmation dialog", () => {
    expect(content).toContain("handleExecuteAction");
    expect(content).toContain("activeAction");
  });

  it("has simulated action disclaimer", () => {
    expect(content).toContain("Actions are simulated");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});

describe("Orders — getAllowedActions policy", () => {
  it("returns confirm and cancel for pending_confirmation", () => {
    const actions = getAllowedActions("pending_confirmation", "pending", "unfulfilled", "cod", "owner");
    const labels = actions.map((a) => a.action);
    expect(labels).toContain("confirm");
    expect(labels).toContain("cancel");
  });

  it("returns mark_cod_collected for confirmed + cod pending", () => {
    const actions = getAllowedActions("confirmed", "pending", "unfulfilled", "cod", "owner");
    const labels = actions.map((a) => a.action);
    expect(labels).toContain("mark_cod_collected");
  });

  it("returns verify/reject for confirmed + QR awaiting_verification", () => {
    const actions = getAllowedActions("confirmed", "awaiting_verification", "unfulfilled", "merchant_qr", "owner");
    const labels = actions.map((a) => a.action);
    expect(labels).toContain("verify_payment");
    expect(labels).toContain("reject_payment");
  });

  it("returns prepare for confirmed + unfulfilled", () => {
    const actions = getAllowedActions("confirmed", "paid", "unfulfilled", "cod", "owner");
    const labels = actions.map((a) => a.action);
    expect(labels).toContain("prepare");
  });

  it("returns dispatch for confirmed/processing + ready", () => {
    const actions = getAllowedActions("processing", "paid", "ready", "cod", "owner");
    const labels = actions.map((a) => a.action);
    expect(labels).toContain("dispatch");
  });

  it("returns deliver for processing + dispatched", () => {
    const actions = getAllowedActions("processing", "paid", "dispatched", "cod", "owner");
    const labels = actions.map((a) => a.action);
    expect(labels).toContain("deliver");
  });

  it("returns no actions for fulfilled", () => {
    const actions = getAllowedActions("fulfilled", "paid", "delivered", "cod", "owner");
    expect(actions).toHaveLength(0);
  });

  it("returns no actions for cancelled", () => {
    const actions = getAllowedActions("cancelled", "pending", "cancelled", "cod", "owner");
    expect(actions).toHaveLength(0);
  });

  it("returns no actions for viewer role", () => {
    const actions = getAllowedActions("pending_confirmation", "pending", "unfulfilled", "cod", "viewer");
    expect(actions).toHaveLength(0);
  });

  it("cancel requires reason", () => {
    const actions = getAllowedActions("pending_confirmation", "pending", "unfulfilled", "cod", "owner");
    const cancel = actions.find((a) => a.action === "cancel");
    expect(cancel?.requiresReason).toBe(true);
    expect(cancel?.destructive).toBe(true);
  });

  it("reject_payment requires reason", () => {
    const actions = getAllowedActions("confirmed", "awaiting_verification", "unfulfilled", "merchant_qr", "owner");
    const reject = actions.find((a) => a.action === "reject_payment");
    expect(reject?.requiresReason).toBe(true);
    expect(reject?.destructive).toBe(true);
  });
});
