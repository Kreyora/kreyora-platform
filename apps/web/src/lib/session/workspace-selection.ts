const KEY = "kreyora.selected-tenant-id";

export function selectedWorkspaceId(): string | null {
  return typeof window === "undefined" ? null : window.sessionStorage.getItem(KEY);
}

export function selectWorkspace(id: string): void { window.sessionStorage.setItem(KEY, id); }
export function clearSelectedWorkspace(): void { if (typeof window !== "undefined") window.sessionStorage.removeItem(KEY); }
