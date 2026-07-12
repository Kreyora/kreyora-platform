"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { Role } from "@/lib/types";

interface NavItem {
  href: string;
  label: string;
  icon: React.ReactNode;
  /** Roles that can access. If undefined, all roles can access. */
  roles?: Role[];
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

function icon(d: string) {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d={d} />
    </svg>
  );
}

const NAV_GROUPS: NavGroup[] = [
  {
    label: "Core",
    items: [
      {
        href: "/dashboard",
        label: "Dashboard",
        icon: icon("M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"),
      },
      {
        href: "/catalog",
        label: "Catalog",
        icon: icon("M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4"),
      },
      {
        href: "/orders",
        label: "Orders",
        icon: icon("M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"),
      },
      {
        href: "/inbox",
        label: "Inbox",
        icon: icon("M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"),
      },
    ],
  },
  {
    label: "Configure",
    items: [
      {
        href: "/storefront",
        label: "Storefront",
        icon: icon("M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"),
        roles: ["owner", "admin"],
      },
      {
        href: "/integrations",
        label: "Integrations",
        icon: icon("M13 2L3 14h9l-1 8 10-12h-9l1-8z"),
        roles: ["owner", "admin"],
      },
      {
        href: "/assistant",
        label: "Assistant",
        icon: icon("M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm0 14a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm1-5h-2V7h2z"),
        roles: ["owner", "admin"],
      },
    ],
  },
  {
    label: "Business",
    items: [
      {
        href: "/analytics",
        label: "Analytics",
        icon: icon("M18 20V10M12 20V4M6 20v-6"),
        roles: ["owner", "admin", "viewer"],
      },
      {
        href: "/billing",
        label: "Billing",
        icon: icon("M2 5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5zm0 4h20"),
        roles: ["owner"],
      },
      {
        href: "/team",
        label: "Team",
        icon: icon("M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm14 10v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"),
        roles: ["owner", "admin"],
      },
      {
        href: "/settings",
        label: "Settings",
        icon: icon("M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"),
        roles: ["owner"],
      },
      {
        href: "/audit",
        label: "Audit",
        icon: icon("M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"),
        roles: ["owner", "admin", "viewer"],
      },
    ],
  },
];

function isItemVisible(item: NavItem, role: Role): boolean {
  if (!item.roles) return true;
  return item.roles.includes(role);
}

interface SidebarNavProps {
  role: Role;
  onLinkClick?: () => void;
}

export function SidebarNav({ role, onLinkClick }: SidebarNavProps) {
  const pathname = usePathname();

  return (
    <nav className="flex flex-col gap-6" aria-label="Seller navigation">
      {NAV_GROUPS.map((group) => {
        const visibleItems = group.items.filter((item) =>
          isItemVisible(item, role),
        );
        if (visibleItems.length === 0) return null;

        return (
          <div key={group.label}>
            <p className="mb-2 px-2 text-[11px] font-semibold uppercase tracking-wider text-[var(--color-ink-secondary)]">
              {group.label}
            </p>
            <ul className="flex flex-col gap-0.5">
              {visibleItems.map((item) => {
                const active =
                  pathname === item.href ||
                  (item.href !== "/dashboard" &&
                    pathname.startsWith(item.href + "/"));

                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      onClick={onLinkClick}
                      className={[
                        "flex min-h-9 items-center gap-3 rounded-[var(--radius-md)] px-2 text-sm transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)]",
                        active
                          ? "bg-[var(--color-canvas-subtle)] font-medium text-[var(--color-ink-primary)]"
                          : "text-[var(--color-ink-secondary)] hover:bg-[var(--color-canvas-subtle)] hover:text-[var(--color-ink-primary)]",
                      ].join(" ")}
                      aria-current={active ? "page" : undefined}
                    >
                      {item.icon}
                      {item.label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        );
      })}
    </nav>
  );
}

export { NAV_GROUPS, isItemVisible };
export type { NavItem, NavGroup };
