import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Inventory — route file verification", () => {
  const routes = [
    { path: "(seller)/catalog/[id]/inventory", label: "product inventory" },
    { path: "(seller)/inventory/low-stock", label: "low-stock alerts" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Inventory — product inventory page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/catalog/[id]/inventory/page.tsx"),
    "utf-8",
  );

  it("uses getStockMovements", () => {
    expect(content).toContain("getStockMovements");
  });

  it("uses getReservations", () => {
    expect(content).toContain("getReservations");
  });

  it("uses getInventory", () => {
    expect(content).toContain("getInventory");
  });

  it("has stock ledger section", () => {
    expect(content).toContain("Stock Ledger");
  });

  it("has reservations section", () => {
    expect(content).toContain("Reservations");
  });

  it("has stock adjustment form", () => {
    expect(content).toContain("Stock Adjustment");
    expect(content).toContain("Adjust stock");
  });

  it("shows simulated adjustment disclaimer", () => {
    expect(content).toContain("Stock adjustments are simulated");
  });

  it("shows variant inventory cards with on-hand/committed/available", () => {
    expect(content).toContain("On hand");
    expect(content).toContain("Committed");
    expect(content).toContain("Available");
  });

  it("shows low-stock badge", () => {
    expect(content).toContain("Low stock");
    expect(content).toContain("isLowStock");
  });

  it("has breadcrumb navigation", () => {
    expect(content).toContain("Breadcrumb");
    expect(content).toContain("/catalog");
  });

  it("renders ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("hides adjustment form for viewer", () => {
    expect(content).toContain("isViewer");
  });

  it("shows movement type badges", () => {
    expect(content).toContain("MOVEMENT_BADGE");
  });

  it("shows reservation state badges", () => {
    expect(content).toContain("RESERVATION_BADGE");
  });
});

describe("Inventory — low-stock alerts page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/inventory/low-stock/page.tsx"),
    "utf-8",
  );

  it("uses getLowStock", () => {
    expect(content).toContain("getLowStock");
  });

  it("has product/variant/SKU columns", () => {
    expect(content).toContain("Product");
    expect(content).toContain("Variant");
    expect(content).toContain("SKU");
  });

  it("shows on-hand / threshold with danger badge", () => {
    expect(content).toContain("On Hand / Threshold");
    expect(content).toContain('variant="danger"');
  });

  it("has 'View inventory' links", () => {
    expect(content).toContain("View inventory");
    expect(content).toContain("/inventory");
  });

  it("has empty state for no low-stock items", () => {
    expect(content).toContain("No low-stock items");
  });

  it("renders ViewerBadge", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("has responsive layout", () => {
    expect(content).toContain("md:block");
    expect(content).toContain("md:hidden");
  });
});
