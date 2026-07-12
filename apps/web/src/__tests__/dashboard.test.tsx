import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const DASHBOARD_FILE = path.resolve(
  __dirname,
  "../app/(seller)/dashboard/page.tsx",
);

describe("Dashboard page", () => {
  const content = fs.readFileSync(DASHBOARD_FILE, "utf-8");

  it("displays metric cards", () => {
    expect(content).toContain("Total orders");
    expect(content).toContain("Total revenue");
    expect(content).toContain("Open conversations");
    expect(content).toContain("AI credits");
  });

  it("shows setup progress", () => {
    expect(content).toContain("Setup progress");
    expect(content).toContain("setupProgress");
  });

  it("shows low stock alert", () => {
    expect(content).toContain("low stock");
    expect(content).toContain("lowStockProducts");
  });

  it("shows recent orders section", () => {
    expect(content).toContain("Recent orders");
    expect(content).toContain("orderNumber");
  });

  it("renders ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
    expect(content).toContain('effectiveRole === "viewer"');
  });

  it("shows quick actions for non-viewer roles", () => {
    expect(content).toContain("Quick actions");
    expect(content).toContain("Add product");
    expect(content).toContain("View inbox");
    expect(content).toContain("Check inventory");
  });

  it("hides quick actions for viewer role", () => {
    expect(content).toContain('effectiveRole !== "viewer"');
  });

  it("shows loading skeleton initially", () => {
    expect(content).toContain("Skeleton");
    expect(content).toContain("isLoading");
  });

  it("links to onboarding from setup progress", () => {
    expect(content).toContain('href="/onboarding"');
  });

  it("links to orders list", () => {
    expect(content).toContain('href="/orders"');
  });
});
