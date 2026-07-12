export default function SellerLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-full">
      <aside className="hidden md:flex w-60 flex-col border-r border-(--color-border) bg-(--color-canvas) px-4 py-6">
        <span className="text-sm font-bold text-(--color-ink-primary) mb-6">Kreyora</span>
        <nav className="flex flex-col gap-1 text-sm text-(--color-ink-secondary)">
          <span className="px-2 py-1.5 rounded-md">Dashboard</span>
          <span className="px-2 py-1.5 rounded-md">Catalog</span>
          <span className="px-2 py-1.5 rounded-md">Orders</span>
          <span className="px-2 py-1.5 rounded-md">Inbox</span>
          <span className="px-2 py-1.5 rounded-md">Storefront</span>
          <span className="px-2 py-1.5 rounded-md">Integrations</span>
          <span className="px-2 py-1.5 rounded-md">Assistant</span>
          <span className="px-2 py-1.5 rounded-md">Analytics</span>
          <span className="px-2 py-1.5 rounded-md">Billing</span>
          <span className="px-2 py-1.5 rounded-md">Team</span>
          <span className="px-2 py-1.5 rounded-md">Settings</span>
          <span className="px-2 py-1.5 rounded-md">Audit</span>
        </nav>
      </aside>
      <div className="flex-1 flex flex-col">
        <header className="border-b border-(--color-border) px-6 py-3 flex items-center justify-between">
          <span className="text-sm font-semibold text-(--color-ink-primary)">Seller Workspace</span>
          <span className="text-xs text-(--color-ink-secondary)">Simulated session</span>
        </header>
        <main className="flex-1 px-6 py-6">{children}</main>
      </div>
    </div>
  );
}
