import { describe, expect, it } from "vitest";
import * as fs from "fs";
import * as path from "path";

const app = path.resolve(__dirname, "../app/(storefront)/store/[slug]");
const read = (...segments: string[]) => fs.readFileSync(path.join(app, ...segments), "utf-8");

describe("M05-S06 public storefront boundary", () => {
  it("keeps the public shell below the slug route and scopes the cart to it", () => {
    const layout = read("layout.tsx");
    expect(layout).toContain("usePublicStorefrontClient");
    expect(layout).toContain("storefront.getStore(slug)");
    expect(layout).toContain("CartProvider");
    expect(layout).toContain("storeSlug={slug}");
  });

  it("uses the public catalog port and server search rather than seller collections", () => {
    const page = read("page.tsx");
    expect(page).toContain("usePublicStorefrontClient");
    expect(page).toContain("listProducts(slug");
    expect(page).not.toContain("getCollections");
    expect(page).not.toContain("DEMO_TENANT_ID");
  });

  it("uses public product/media data and never calls seller inventory", () => {
    const page = read("product", "[id]", "page.tsx");
    expect(page).toContain("storefront.getProduct(slug, productSlug)");
    expect(page).toContain("storefront.getMediaUrl");
    expect(page).not.toContain("getInventory");
    expect(page).not.toContain("SKU:");
  });

  it("uses public cart item prices as estimates before checkout", () => {
    const page = read("cart", "page.tsx");
    expect(page).toContain("unitPriceNpr");
    expect(page).toContain("Calculated at checkout");
  });

  it("uses quote, session, and COD order APIs without delivery-rule or payment selection", () => {
    const page = read("checkout", "page.tsx");
    expect(page).toContain("checkout.createQuote");
    expect(page).toContain("checkout.createSession");
    expect(page).toContain("checkout.createCodOrder");
    expect(page).toContain("idempotencyKey");
    expect(page).not.toContain("getDeliveryRules");
    expect(page).not.toContain("getPaymentMethods");
    expect(page).not.toContain("QR placeholder");
  });

  it("does not query seller orders from confirmation or live order lookup", () => {
    expect(read("confirmation", "[orderId]", "page.tsx")).not.toContain("useOrderClient");
    expect(read("confirmation", "[orderId]", "page.tsx")).toContain("public-confirmation");
    expect(read("order-lookup", "page.tsx")).not.toContain("listOrders");
  });
});

describe("M05-S06 public adapter layer", () => {
  const adapter = fs.readFileSync(path.resolve(__dirname, "../lib/adapters/api/public-storefront-client.ts"), "utf-8");
  const provider = fs.readFileSync(path.resolve(__dirname, "../lib/providers/client-provider.tsx"), "utf-8");

  it("uses development slug routes locally and host-bound routes otherwise", () => {
    expect(adapter).toContain("/public/v1/dev/stores/");
    expect(adapter).toContain("/public/v1/store");
  });

  it("uses generated contracts and omits browser credentials", () => {
    expect(adapter).toContain("generated/v1");
    expect(adapter).toContain('credentials: "omit"');
  });

  it("selects fixtures explicitly instead of falling back after a live failure", () => {
    expect(provider).toContain("USING_PUBLIC_FIXTURE_ADAPTERS");
    expect(provider).toContain("apiPublicStorefrontClient");
    expect(provider).toContain("mockPublicStorefrontClient");
  });
});
