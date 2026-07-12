import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Storefront Admin — route file verification", () => {
  const routes = [
    { path: "(seller)/storefront", label: "store profile" },
    { path: "(seller)/storefront/delivery", label: "delivery rules" },
    { path: "(seller)/storefront/payments", label: "payment methods" },
    { path: "(seller)/storefront/preview", label: "preview" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Storefront Admin — store profile page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/storefront/page.tsx"),
    "utf-8",
  );

  it("uses getStore from storefront client", () => {
    expect(content).toContain("getStore");
  });

  it("displays store name and tagline", () => {
    expect(content).toContain("profile.name");
    expect(content).toContain("profile.tagline");
  });

  it("displays contact info", () => {
    expect(content).toContain("contactEmail");
    expect(content).toContain("contactPhone");
  });

  it("displays social links", () => {
    expect(content).toContain("socialLinks");
  });

  it("shows theme settings with accent color", () => {
    expect(content).toContain("accentColor");
    expect(content).toContain("logoUrl");
    expect(content).toContain("bannerUrl");
  });

  it("shows readiness checklist", () => {
    expect(content).toContain("hasProfile");
    expect(content).toContain("hasPublishedProducts");
    expect(content).toContain("hasDeliveryRules");
    expect(content).toContain("hasPaymentMethods");
    expect(content).toContain("isReady");
  });

  it("shows published status", () => {
    expect(content).toContain("isPublished");
    expect(content).toContain("Live");
    expect(content).toContain("Draft");
  });

  it("has sub-navigation tabs", () => {
    expect(content).toContain("Profile");
    expect(content).toContain("Delivery");
    expect(content).toContain("Payments");
    expect(content).toContain("Preview");
  });

  it("has simulated save button", () => {
    expect(content).toContain("Save Changes");
  });

  it("has disclaimer about simulation", () => {
    expect(content).toContain("simulated");
  });

  it("supports viewer role", () => {
    expect(content).toContain("ViewerBadge");
    expect(content).toContain("isViewer");
  });
});

describe("Storefront Admin — delivery rules page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/storefront/delivery/page.tsx"),
    "utf-8",
  );

  it("uses getDeliveryRules from storefront client", () => {
    expect(content).toContain("getDeliveryRules");
  });

  it("displays rule name and zones", () => {
    expect(content).toContain("rule.name");
    expect(content).toContain("rule.zones");
  });

  it("shows fee type information", () => {
    expect(content).toContain("feeType");
    expect(content).toContain("flatFee");
    expect(content).toContain("freeAbove");
  });

  it("shows estimated delivery days", () => {
    expect(content).toContain("estimatedDays");
  });

  it("shows COD availability badge", () => {
    expect(content).toContain("codAvailable");
    expect(content).toContain("COD");
    expect(content).toContain("No COD");
  });

  it("shows active status badge", () => {
    expect(content).toContain("isActive");
    expect(content).toContain("Active");
    expect(content).toContain("Inactive");
  });

  it("has simulated add rule button", () => {
    expect(content).toContain("Add Rule");
  });

  it("has sub-navigation", () => {
    expect(content).toContain("NAV_ITEMS");
  });
});

describe("Storefront Admin — payment methods page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/storefront/payments/page.tsx"),
    "utf-8",
  );

  it("uses getPaymentMethods from storefront client", () => {
    expect(content).toContain("getPaymentMethods");
  });

  it("shows payment method type badges", () => {
    expect(content).toContain("Cash on Delivery");
    expect(content).toContain("Merchant QR");
  });

  it("displays method label and instructions", () => {
    expect(content).toContain("m.label");
    expect(content).toContain("instructions");
  });

  it("shows enabled status", () => {
    expect(content).toContain("isEnabled");
    expect(content).toContain("Enabled");
    expect(content).toContain("Disabled");
  });

  it("shows QR image URL", () => {
    expect(content).toContain("qrImageUrl");
  });

  it("has disclaimer about simulation", () => {
    expect(content).toContain("simulated");
  });
});

describe("Storefront Admin — preview page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/storefront/preview/page.tsx"),
    "utf-8",
  );

  it("uses getStore from storefront client", () => {
    expect(content).toContain("getStore");
  });

  it("shows public URL with store slug", () => {
    expect(content).toContain("store.slug");
    expect(content).toContain("/store/");
  });

  it("shows published and readiness status", () => {
    expect(content).toContain("isPublished");
    expect(content).toContain("Published");
    expect(content).toContain("Draft");
    expect(content).toContain("isReady");
  });

  it("has readiness checklist", () => {
    expect(content).toContain("hasProfile");
    expect(content).toContain("hasPublishedProducts");
  });

  it("has open storefront button/link", () => {
    expect(content).toContain("Open Storefront");
  });

  it("has sub-navigation", () => {
    expect(content).toContain("NAV_ITEMS");
  });
});
