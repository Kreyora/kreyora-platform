import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

function routeFileExists(routeGroupDir: string, routePath: string): boolean {
  const fullPath = path.join(APP_DIR, routeGroupDir, routePath, "page.tsx");
  return fs.existsSync(fullPath);
}

describe("Route inventory — all routes have placeholder pages", () => {
  const marketingRoutes = [
    "",
    "features",
    "pricing",
    "demo",
    "contact",
  ];

  for (const route of marketingRoutes) {
    it(`(marketing)/${route} exists`, () => {
      expect(routeFileExists("(marketing)", route)).toBe(true);
    });
  }

  const authRoutes = ["signin", "recover"];

  for (const route of authRoutes) {
    it(`(auth)/${route} exists`, () => {
      expect(routeFileExists("(auth)", route)).toBe(true);
    });
  }

  const sellerRoutes = [
    "workspaces",
    "onboarding",
    "dashboard",
    "catalog",
    "catalog/new",
    "catalog/[id]",
    "catalog/[id]/inventory",
    "inventory/low-stock",
    "orders",
    "orders/[id]",
    "inbox",
    "inbox/[id]",
    "storefront",
    "storefront/delivery",
    "storefront/payments",
    "storefront/preview",
    "integrations",
    "integrations/[id]",
    "assistant",
    "assistant/knowledge",
    "assistant/console",
    "assistant/history",
    "analytics",
    "billing",
    "team",
    "settings",
    "audit",
  ];

  for (const route of sellerRoutes) {
    it(`(seller)/${route} exists`, () => {
      expect(routeFileExists("(seller)", route)).toBe(true);
    });
  }

  const storefrontRoutes = [
    "store/[slug]",
    "store/[slug]/collection/[id]",
    "store/[slug]/product/[id]",
    "store/[slug]/cart",
    "store/[slug]/checkout",
    "store/[slug]/confirmation/[orderId]",
    "store/[slug]/order-lookup",
  ];

  for (const route of storefrontRoutes) {
    it(`(storefront)/${route} exists`, () => {
      expect(routeFileExists("(storefront)", route)).toBe(true);
    });
  }

  it("layout files exist for all route groups", () => {
    expect(fs.existsSync(path.join(APP_DIR, "(marketing)/layout.tsx"))).toBe(true);
    expect(fs.existsSync(path.join(APP_DIR, "(auth)/layout.tsx"))).toBe(true);
    expect(fs.existsSync(path.join(APP_DIR, "(seller)/layout.tsx"))).toBe(true);
    expect(fs.existsSync(path.join(APP_DIR, "(storefront)/layout.tsx"))).toBe(true);
    expect(fs.existsSync(path.join(APP_DIR, "layout.tsx"))).toBe(true);
  });
});
