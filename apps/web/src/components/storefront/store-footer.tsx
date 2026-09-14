import type { PublicStorefront } from "@/lib/types/public-storefront";

interface StoreFooterProps {
  store: PublicStorefront;
}

export function StoreFooter({ store }: StoreFooterProps) {
  return (
    <footer className="border-t border-[var(--color-border)] bg-[var(--color-canvas)]">
      <div className="mx-auto max-w-5xl px-4 py-8">
        <div className="flex flex-col gap-6 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-sm font-bold text-[var(--color-ink-primary)]">
              {store.displayName}
            </p>
            {store.tagline && (
              <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
                {store.tagline}
              </p>
            )}
          </div>

          <div className="flex flex-col gap-1 text-xs text-[var(--color-ink-secondary)]">
            {store.contactEmail && <span>{store.contactEmail}</span>}
            {store.contactPhone && <span>{store.contactPhone}</span>}
            {Object.entries(store.socialLinks).length > 0 && (
              <div className="mt-1 flex gap-3">
                {Object.entries(store.socialLinks).map(([name, url]) => (
                  <a
                    key={name}
                    href={url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="capitalize hover:text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)]"
                  >
                    {name}
                  </a>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="mt-6 border-t border-[var(--color-border)] pt-4 text-center text-[10px] text-[var(--color-ink-secondary)]">
          Powered by Kreyora
        </div>
      </div>
    </footer>
  );
}
