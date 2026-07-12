export default function MarketingLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex flex-col min-h-full">
      <header className="border-b border-(--color-border) px-6 py-4">
        <nav className="mx-auto max-w-[90rem] flex items-center justify-between">
          <span className="text-lg font-bold text-(--color-ink-primary)">Kreyora</span>
          <div className="flex gap-4 text-sm text-(--color-ink-secondary)">
            <span>Features</span>
            <span>Pricing</span>
            <span>Demo</span>
            <span>Contact</span>
          </div>
        </nav>
      </header>
      <main className="flex-1">{children}</main>
      <footer className="border-t border-(--color-border) px-6 py-8 text-center text-xs text-(--color-ink-secondary)">
        © 2026 Kreyora — Placeholder footer. Not connected to a live service.
      </footer>
    </div>
  );
}
