import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import * as fs from "fs";
import * as path from "path";

vi.mock("next/navigation", () => ({
  usePathname: () => "/",
  useRouter: () => ({ push: vi.fn(), back: vi.fn(), forward: vi.fn() }),
}));

vi.mock("next/link", () => ({
  default: ({
    children,
    href,
    ...props
  }: {
    children: React.ReactNode;
    href: string;
    [key: string]: unknown;
  }) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

import { MarketingHeader } from "@/components/marketing/header";
import { MarketingFooter } from "@/components/marketing/footer";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Marketing — route file verification", () => {
  const routes = [
    { group: "(marketing)", path: "", label: "landing" },
    { group: "(marketing)", path: "features", label: "features" },
    { group: "(marketing)", path: "pricing", label: "pricing" },
    { group: "(marketing)", path: "demo", label: "demo" },
    { group: "(marketing)", path: "contact", label: "contact" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.group, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Marketing — auth route references corrected to M01-S03", () => {
  it("signin page is a real implementation (not a placeholder)", () => {
    const content = fs.readFileSync(
      path.join(APP_DIR, "(auth)", "signin/page.tsx"),
      "utf-8",
    );
    expect(content).toContain("Sign in");
    expect(content).not.toContain("M01-S02");
  });

  it("recover page is a real implementation (not a placeholder)", () => {
    const content = fs.readFileSync(
      path.join(APP_DIR, "(auth)", "recover/page.tsx"),
      "utf-8",
    );
    expect(content).toContain("Account recovery");
    expect(content).not.toContain("M01-S02");
  });

  it("ROUTE_INVENTORY.md assigns auth routes to M01-S03", () => {
    const inventory = fs.readFileSync(
      path.resolve(__dirname, "../../../../docs/frontend/ROUTE_INVENTORY.md"),
      "utf-8",
    );
    const signinLine = inventory
      .split("\n")
      .find((l) => l.includes("/signin"));
    const recoverLine = inventory
      .split("\n")
      .find((l) => l.includes("/recover"));
    expect(signinLine).toContain("M01-S03");
    expect(recoverLine).toContain("M01-S03");
  });
});

describe("MarketingHeader", () => {
  it("renders the logo", () => {
    render(<MarketingHeader />);
    expect(screen.getByText("Kreyora")).toBeInTheDocument();
  });

  it("renders desktop nav links", () => {
    render(<MarketingHeader />);
    const links = screen
      .getAllByRole("link")
      .map((a) => a.getAttribute("href"));
    expect(links).toContain("/features");
    expect(links).toContain("/pricing");
    expect(links).toContain("/demo");
    expect(links).toContain("/contact");
  });

  it("renders 'Try demo' CTA linking to /demo", () => {
    render(<MarketingHeader />);
    const ctas = screen.getAllByText("Try demo");
    expect(ctas.length).toBeGreaterThanOrEqual(1);
    const link = ctas[0].closest("a");
    expect(link).toHaveAttribute("href", "/demo");
  });

  it("has a mobile menu button", () => {
    render(<MarketingHeader />);
    expect(screen.getByLabelText("Open menu")).toBeInTheDocument();
  });
});

describe("MarketingFooter", () => {
  it("renders the Kreyora brand", () => {
    render(<MarketingFooter />);
    expect(screen.getByText("Kreyora")).toBeInTheDocument();
  });

  it("renders product and company link sections", () => {
    render(<MarketingFooter />);
    expect(screen.getByText("Product")).toBeInTheDocument();
    expect(screen.getByText("Company")).toBeInTheDocument();
  });

  it("contains footer links to marketing pages", () => {
    render(<MarketingFooter />);
    const links = screen
      .getAllByRole("link")
      .map((a) => a.getAttribute("href"));
    expect(links).toContain("/features");
    expect(links).toContain("/pricing");
    expect(links).toContain("/demo");
    expect(links).toContain("/contact");
  });

  it("shows demo disclaimer", () => {
    render(<MarketingFooter />);
    expect(
      screen.getByText(/not connected to a live service/i),
    ).toBeInTheDocument();
  });
});

describe("Marketing — demo selector links", () => {
  it("demo page file references correct routes for each persona", () => {
    const content = fs.readFileSync(
      path.join(APP_DIR, "(marketing)", "demo", "page.tsx"),
      "utf-8",
    );
    expect(content).toContain("/dashboard");
    expect(content).toContain("/inbox");
    expect(content).toContain("/store/namaste-crafts");
  });
});

describe("Marketing — metadata exports", () => {
  const pagesWithMetadata = [
    { path: "(marketing)/page.tsx", label: "landing" },
    { path: "(marketing)/features/page.tsx", label: "features" },
    { path: "(marketing)/pricing/page.tsx", label: "pricing" },
    { path: "(marketing)/demo/page.tsx", label: "demo" },
    { path: "(marketing)/layout.tsx", label: "layout" },
  ];

  for (const page of pagesWithMetadata) {
    it(`${page.label} exports metadata`, () => {
      const content = fs.readFileSync(
        path.join(APP_DIR, page.path),
        "utf-8",
      );
      expect(content).toMatch(/export\s+(const\s+)?metadata/);
    });
  }
});

describe("Marketing — reduced motion safety", () => {
  it("Section component uses CSS transition (inherits reduced-motion rules)", () => {
    const sectionFile = fs.readFileSync(
      path.resolve(__dirname, "../components/marketing/section.tsx"),
      "utf-8",
    );
    expect(sectionFile).toContain("duration-[var(--duration-entrance)]");
  });
});

describe("Marketing — contact form simulation", () => {
  it("contact page file contains simulated submit disclaimer", () => {
    const content = fs.readFileSync(
      path.join(APP_DIR, "(marketing)", "contact", "page.tsx"),
      "utf-8",
    );
    expect(content).toContain("No data is submitted or stored");
  });
});
