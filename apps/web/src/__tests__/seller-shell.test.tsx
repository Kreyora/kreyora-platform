import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import * as fs from "fs";
import * as path from "path";

vi.mock("next/navigation", () => ({
  usePathname: () => "/dashboard",
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

import { SidebarNav, NAV_GROUPS, isItemVisible } from "@/components/seller/sidebar-nav";

describe("SidebarNav — role-aware visibility", () => {
  it("renders all nav links for owner role", () => {
    render(<SidebarNav role="owner" />);
    const links = screen.getAllByRole("link");
    const hrefs = links.map((l) => l.getAttribute("href"));
    expect(hrefs).toContain("/dashboard");
    expect(hrefs).toContain("/catalog");
    expect(hrefs).toContain("/orders");
    expect(hrefs).toContain("/inbox");
    expect(hrefs).toContain("/storefront");
    expect(hrefs).toContain("/integrations");
    expect(hrefs).toContain("/billing");
    expect(hrefs).toContain("/settings");
    expect(hrefs).toContain("/team");
    expect(hrefs).toContain("/audit");
  });

  it("hides billing, settings, and team for operator role", () => {
    render(<SidebarNav role="operator" />);
    const links = screen.getAllByRole("link");
    const hrefs = links.map((l) => l.getAttribute("href"));
    expect(hrefs).toContain("/dashboard");
    expect(hrefs).toContain("/catalog");
    expect(hrefs).toContain("/orders");
    expect(hrefs).toContain("/inbox");
    expect(hrefs).not.toContain("/billing");
    expect(hrefs).not.toContain("/settings");
    expect(hrefs).not.toContain("/team");
    expect(hrefs).not.toContain("/storefront");
  });

  it("hides configure and most business links for viewer role", () => {
    render(<SidebarNav role="viewer" />);
    const links = screen.getAllByRole("link");
    const hrefs = links.map((l) => l.getAttribute("href"));
    expect(hrefs).toContain("/dashboard");
    expect(hrefs).toContain("/analytics");
    expect(hrefs).toContain("/audit");
    expect(hrefs).not.toContain("/billing");
    expect(hrefs).not.toContain("/settings");
    expect(hrefs).not.toContain("/storefront");
    expect(hrefs).not.toContain("/integrations");
  });

  it("marks the active link with aria-current=page", () => {
    render(<SidebarNav role="owner" />);
    const dashboardLink = screen.getByRole("link", { name: "Dashboard" });
    expect(dashboardLink).toHaveAttribute("aria-current", "page");
  });
});

describe("SidebarNav — isItemVisible", () => {
  it("allows unrestricted items for any role", () => {
    const item = { href: "/dashboard", label: "Dashboard", icon: <span /> };
    expect(isItemVisible(item, "viewer")).toBe(true);
    expect(isItemVisible(item, "operator")).toBe(true);
    expect(isItemVisible(item, "owner")).toBe(true);
  });

  it("restricts items to specified roles", () => {
    const item = {
      href: "/billing",
      label: "Billing",
      icon: <span />,
      roles: ["owner" as const],
    };
    expect(isItemVisible(item, "owner")).toBe(true);
    expect(isItemVisible(item, "admin")).toBe(false);
    expect(isItemVisible(item, "operator")).toBe(false);
    expect(isItemVisible(item, "viewer")).toBe(false);
  });
});

describe("Seller shell layout", () => {
  const layoutContent = fs.readFileSync(
    path.resolve(__dirname, "../app/(seller)/layout.tsx"),
    "utf-8",
  );

  it("uses SessionProvider", () => {
    expect(layoutContent).toContain("SessionProvider");
  });

  it("includes SidebarNav component", () => {
    expect(layoutContent).toContain("SidebarNav");
  });

  it("includes ProfileMenu component", () => {
    expect(layoutContent).toContain("ProfileMenu");
  });

  it("includes MobileNav component", () => {
    expect(layoutContent).toContain("MobileNav");
  });

  it("includes RoleSwitcher component", () => {
    expect(layoutContent).toContain("RoleSwitcher");
  });

  it("shows workspace name from session", () => {
    expect(layoutContent).toContain("session?.tenant.name");
  });
});
