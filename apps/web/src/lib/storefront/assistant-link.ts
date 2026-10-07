/**
 * Remembers an assistant checkout link token for this store's checkout (M09-S05), in session storage only.
 * Checkout sends it so the shop can hand over held items and link the order to the chat; it is cleared after ordering.
 */
const key = (slug: string) => `kreyora:assistant-link:v1:${slug}`;

interface StoredLink { token: string; expiresAt: string }

export function saveAssistantLink(slug: string, token: string, expiresAt: string): void {
  try { sessionStorage.setItem(key(slug), JSON.stringify({ token, expiresAt } satisfies StoredLink)); } catch { /* storage unavailable: checkout still works */ }
}

export function readAssistantLink(slug: string, now: Date = new Date()): string | undefined {
  try {
    const raw = sessionStorage.getItem(key(slug));
    if (!raw) return undefined;
    const value = JSON.parse(raw) as StoredLink;
    return new Date(value.expiresAt).getTime() > now.getTime() ? value.token : undefined;
  } catch {
    return undefined;
  }
}

export function clearAssistantLink(slug: string): void {
  try { sessionStorage.removeItem(key(slug)); } catch { /* ignore */ }
}
