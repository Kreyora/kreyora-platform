export default function StorefrontLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex flex-col min-h-full">
      <header className="border-b border-(--color-border) px-4 py-3">
        <div className="mx-auto max-w-5xl flex items-center justify-between">
          <span className="text-sm font-bold text-(--color-ink-primary)">Store Name</span>
          <span className="text-xs text-(--color-ink-secondary)">Demo Storefront</span>
        </div>
      </header>
      <main className="flex-1 mx-auto max-w-5xl w-full px-4 py-6">{children}</main>
      <footer className="border-t border-(--color-border) px-4 py-6 text-center text-xs text-(--color-ink-secondary)">
        Powered by Kreyora — Demo storefront
      </footer>
    </div>
  );
}
