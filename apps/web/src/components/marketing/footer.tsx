import Link from "next/link";

const PRODUCT_LINKS = [
  { href: "/features", label: "Features" },
  { href: "/pricing", label: "Pricing" },
  { href: "/demo", label: "Demo" },
] as const;

const COMPANY_LINKS = [
  { href: "/contact", label: "Contact" },
] as const;

export function MarketingFooter() {
  return (
    <footer className="border-t border-[var(--color-border)] bg-[var(--color-canvas)]">
      <div className="mx-auto max-w-[90rem] px-5 py-12 md:px-8 lg:px-12">
        <div className="grid grid-cols-2 gap-8 md:grid-cols-4">
          {/* Brand */}
          <div className="col-span-2 md:col-span-1">
            <span className="text-lg font-bold text-[var(--color-ink-primary)]">
              Kreyora
            </span>
            <p className="mt-3 max-w-xs text-sm leading-relaxed text-[var(--color-ink-secondary)]">
              The reliable commerce layer behind a seller&apos;s social DMs.
              Built for Nepal.
            </p>
          </div>

          {/* Product */}
          <div>
            <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--color-ink-primary)]">
              Product
            </h3>
            <ul className="mt-3 flex flex-col gap-2">
              {PRODUCT_LINKS.map((link) => (
                <li key={link.href}>
                  <Link
                    href={link.href}
                    className="text-sm text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:text-[var(--color-ink-primary)]"
                  >
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          {/* Company */}
          <div>
            <h3 className="text-xs font-semibold uppercase tracking-wider text-[var(--color-ink-primary)]">
              Company
            </h3>
            <ul className="mt-3 flex flex-col gap-2">
              {COMPANY_LINKS.map((link) => (
                <li key={link.href}>
                  <Link
                    href={link.href}
                    className="text-sm text-[var(--color-ink-secondary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:text-[var(--color-ink-primary)]"
                  >
                    {link.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        </div>

        {/* Bottom bar */}
        <div className="mt-12 flex flex-col items-center justify-between gap-4 border-t border-[var(--color-border)] pt-6 text-xs text-[var(--color-ink-secondary)] md:flex-row">
          <span>© {new Date().getFullYear()} Kreyora. All rights reserved.</span>
          <span>Demonstration only — not connected to a live service.</span>
        </div>
      </div>
    </footer>
  );
}
