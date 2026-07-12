import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");
const COMPONENTS_DIR = path.resolve(__dirname, "../components");

describe("Catalog — route file verification", () => {
  const routes = [
    { path: "(seller)/catalog", label: "product list" },
    { path: "(seller)/catalog/new", label: "create product" },
    { path: "(seller)/catalog/[id]", label: "edit product" },
    { path: "(seller)/catalog/[id]/inventory", label: "product inventory" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Catalog — product list page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/catalog/page.tsx"),
    "utf-8",
  );

  it("uses listProducts from catalog client", () => {
    expect(content).toContain("listProducts");
  });

  it("has search input", () => {
    expect(content).toContain("Search products");
  });

  it("has collection filter", () => {
    expect(content).toContain("collectionFilter");
    expect(content).toContain("getCollections");
  });

  it("has status filter", () => {
    expect(content).toContain("statusFilter");
    expect(content).toMatch(/Draft|Published|Archived/);
  });

  it("shows 'Add product' button", () => {
    expect(content).toContain("Add product");
  });

  it("links to /catalog/new", () => {
    expect(content).toContain("/catalog/new");
  });

  it("has responsive layout (table + mobile cards)", () => {
    expect(content).toContain("hidden");
    expect(content).toContain("md:block");
    expect(content).toContain("md:hidden");
  });

  it("renders status badges", () => {
    expect(content).toContain("STATUS_BADGE");
    expect(content).toContain("Badge");
  });
});

describe("Catalog — create product page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/catalog/new/page.tsx"),
    "utf-8",
  );

  it("uses ProductForm component", () => {
    expect(content).toContain("ProductForm");
  });

  it("has breadcrumb with 'Catalog > New Product'", () => {
    expect(content).toContain("Catalog");
    expect(content).toContain("New Product");
  });

  it("fetches collections for the form", () => {
    expect(content).toContain("getCollections");
  });
});

describe("Catalog — edit product page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/catalog/[id]/page.tsx"),
    "utf-8",
  );

  it("uses getProduct to load data", () => {
    expect(content).toContain("getProduct");
  });

  it("has tabs for Details, Variants, Media, Inventory", () => {
    expect(content).toContain("Details");
    expect(content).toContain("Variants");
    expect(content).toContain("Media");
    expect(content).toContain("Inventory");
  });

  it("uses Radix Tabs primitive", () => {
    expect(content).toContain("@radix-ui/react-tabs");
  });

  it("links to inventory page", () => {
    expect(content).toContain("/inventory");
  });

  it("has delete/archive action", () => {
    expect(content).toContain("Delete");
  });
});

describe("Catalog — shared ProductForm component", () => {
  const content = fs.readFileSync(
    path.join(COMPONENTS_DIR, "seller/product-form.tsx"),
    "utf-8",
  );

  it("has title, description, slug inputs", () => {
    expect(content).toContain("Product title");
    expect(content).toContain("Description");
    expect(content).toContain("URL slug");
  });

  it("has publish state select", () => {
    expect(content).toContain("publishState");
  });

  it("has collections checkboxes", () => {
    expect(content).toContain("Collections");
    expect(content).toContain("checkbox");
  });

  it("has tags input", () => {
    expect(content).toContain("Tags");
  });

  it("shows simulated save disclaimer", () => {
    expect(content).toContain("Changes are simulated and will not be persisted");
  });

  it("renders ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("disables form for viewer role", () => {
    expect(content).toContain("isViewer");
    expect(content).toContain("disabled={isViewer}");
  });
});
