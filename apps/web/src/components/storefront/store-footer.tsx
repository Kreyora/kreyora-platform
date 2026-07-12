import type { Store } from "@/lib/types";

interface StoreFooterProps {
  store: Store;
}

export function StoreFooter({ store }: StoreFooterProps) {
  const { profile } = store;

  return (
    <footer className="border-t border-[var(--color-border)] bg-[var(--color-canvas)]">
      <div className="mx-auto max-w-5xl px-4 py-8">
        <div className="flex flex-col gap-6 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-sm font-bold text-[var(--color-ink-primary)]">
              {profile.name}
            </p>
            {profile.tagline && (
              <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
                {profile.tagline}
              </p>
            )}
          </div>

          <div className="flex flex-col gap-1 text-xs text-[var(--color-ink-secondary)]">
            {profile.contactEmail && <span>{profile.contactEmail}</span>}
            {profile.contactPhone && <span>{profile.contactPhone}</span>}
            {Object.entries(profile.socialLinks).length > 0 && (
              <div className="mt-1 flex gap-3">
                {Object.entries(profile.socialLinks).map(([name, url]) => (
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
          Powered by Kreyora — This is a demo storefront. No real transactions occur.
        </div>
      </div>
    </footer>
  );
}
