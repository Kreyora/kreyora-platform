import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");
const COMPONENTS_DIR = path.resolve(__dirname, "../components");
const HOOKS_DIR = path.resolve(__dirname, "../hooks");

describe("Storefront — route file verification", () => {
  const routes = [
    { path: "(storefront)/store/[slug]", label: "store home" },
    { path: "(storefront)/store/[slug]/collection/[id]", label: "collection" },
    { path: "(storefront)/store/[slug]/product/[id]", label: "product detail" },
    { path: "(storefront)/store/[slug]/cart", label: "cart" },
    { path: "(storefront)/store/[slug]/checkout", label: "checkout" },
    { path: "(storefront)/store/[slug]/confirmation/[orderId]", label: "confirmation" },
    { path: "(storefront)/store/[slug]/order-lookup", label: "order lookup" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Storefront — layout", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/layout.tsx"),
    "utf-8",
  );

  it("uses StorefrontClient to load store data", () => {
    expect(content).toContain("getStore");
  });

  it("wraps children in CartProvider", () => {
    expect(content).toContain("CartProvider");
  });

  it("uses StoreHeader and StoreFooter components", () => {
    expect(content).toContain("StoreHeader");
    expect(content).toContain("StoreFooter");
  });
});

describe("Storefront — store home page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/page.tsx"),
    "utf-8",
  );

  it("uses getStore and listProducts", () => {
    expect(content).toContain("getStore");
    expect(content).toContain("listProducts");
  });

  it("fetches collections", () => {
    expect(content).toContain("getCollections");
  });

  it("has search input", () => {
    expect(content).toContain("Search products");
  });

  it("renders ProductCard components", () => {
    expect(content).toContain("ProductCard");
  });

  it("has demo disclaimer", () => {
    expect(content).toContain("demo storefront");
  });
});

describe("Storefront — collection page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/collection/[id]/page.tsx"),
    "utf-8",
  );

  it("filters products by collection", () => {
    expect(content).toContain("collection: id");
  });

  it("has breadcrumb navigation", () => {
    expect(content).toContain("Breadcrumb");
  });

  it("renders ProductCard", () => {
    expect(content).toContain("ProductCard");
  });
});

describe("Storefront — product detail page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/product/[id]/page.tsx"),
    "utf-8",
  );

  it("uses getProduct from catalog client", () => {
    expect(content).toContain("getProduct");
  });

  it("uses getInventory for availability", () => {
    expect(content).toContain("getInventory");
  });

  it("has variant selector", () => {
    expect(content).toContain("selectedVariantId");
    expect(content).toContain("publishedVariants");
  });

  it("has Add to cart button", () => {
    expect(content).toContain("Add to cart");
  });

  it("shows out-of-stock state", () => {
    expect(content).toContain("Out of stock");
  });

  it("has quantity controls", () => {
    expect(content).toContain("Decrease quantity");
    expect(content).toContain("Increase quantity");
  });

  it("shows compare-at price", () => {
    expect(content).toContain("compareAtPrice");
    expect(content).toContain("line-through");
  });

  it("has image gallery", () => {
    expect(content).toContain("galleryIndex");
  });
});

describe("Storefront — cart page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/cart/page.tsx"),
    "utf-8",
  );

  it("uses useCart hook", () => {
    expect(content).toContain("useCart");
  });

  it("has quantity controls", () => {
    expect(content).toContain("updateQuantity");
    expect(content).toContain("removeItem");
  });

  it("shows order summary with subtotal", () => {
    expect(content).toContain("Order Summary");
    expect(content).toContain("subtotal");
  });

  it("has proceed to checkout button", () => {
    expect(content).toContain("Proceed to checkout");
  });

  it("has empty cart state", () => {
    expect(content).toContain("Your cart is empty");
  });

  it("has demo disclaimer", () => {
    expect(content).toContain("demo cart");
  });
});

describe("Storefront — checkout page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/checkout/page.tsx"),
    "utf-8",
  );

  it("uses createQuote and submitOrder", () => {
    expect(content).toContain("createQuote");
    expect(content).toContain("submitOrder");
  });

  it("has contact info fields", () => {
    expect(content).toContain("Full name");
    expect(content).toContain("Phone");
  });

  it("has delivery address fields", () => {
    expect(content).toContain("Address line 1");
    expect(content).toContain("City");
    expect(content).toContain("District");
  });

  it("has delivery rule selection", () => {
    expect(content).toContain("getDeliveryRules");
    expect(content).toContain("Delivery Method");
  });

  it("has payment method selection", () => {
    expect(content).toContain("Payment Method");
    expect(content).toContain("getPaymentMethods");
  });

  it("has duplicate-submit protection", () => {
    expect(content).toContain("submitted");
    expect(content).toContain("submitting");
    expect(content).toContain("disabled={!isFormValid || submitted}");
  });

  it("shows order summary", () => {
    expect(content).toContain("Order Summary");
    expect(content).toContain("Total");
  });

  it("has simulated disclaimer", () => {
    expect(content).toContain("demo checkout");
  });
});

describe("Storefront — confirmation page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/confirmation/[orderId]/page.tsx"),
    "utf-8",
  );

  it("shows order number", () => {
    expect(content).toContain("orderNumber");
    expect(content).toContain("Order confirmed");
  });

  it("shows order items and total", () => {
    expect(content).toContain("order.items");
    expect(content).toContain("order.total");
  });

  it("links to order lookup", () => {
    expect(content).toContain("order-lookup");
    expect(content).toContain("Track your order");
  });

  it("has simulated disclaimer", () => {
    expect(content).toContain("simulated order confirmation");
  });
});

describe("Storefront — order lookup page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(storefront)/store/[slug]/order-lookup/page.tsx"),
    "utf-8",
  );

  it("has search input for order number", () => {
    expect(content).toContain("Order number");
    expect(content).toContain("searchQuery");
  });

  it("uses listOrders for search", () => {
    expect(content).toContain("listOrders");
  });

  it("shows order not found state", () => {
    expect(content).toContain("Order not found");
  });

  it("shows order timeline", () => {
    expect(content).toContain("Timeline");
    expect(content).toContain("activity");
  });

  it("has demo disclaimer", () => {
    expect(content).toContain("demo order lookup");
  });
});

describe("Storefront — components exist", () => {
  it("store-header.tsx exists", () => {
    expect(
      fs.existsSync(path.join(COMPONENTS_DIR, "storefront/store-header.tsx")),
    ).toBe(true);
  });

  it("store-footer.tsx exists", () => {
    expect(
      fs.existsSync(path.join(COMPONENTS_DIR, "storefront/store-footer.tsx")),
    ).toBe(true);
  });

  it("product-card.tsx exists", () => {
    expect(
      fs.existsSync(path.join(COMPONENTS_DIR, "storefront/product-card.tsx")),
    ).toBe(true);
  });

  it("use-cart.ts hook exists", () => {
    expect(fs.existsSync(path.join(HOOKS_DIR, "use-cart.ts"))).toBe(true);
  });
});
