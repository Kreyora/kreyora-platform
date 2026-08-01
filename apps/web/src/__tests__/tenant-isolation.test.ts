import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  clearSelectedWorkspace,
  selectedWorkspaceId,
  selectWorkspace,
} from "@/lib/session/workspace-selection";

describe("real-mode workspace selection", () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.resetModules();
    vi.unstubAllEnvs();
  });

  it("persists only the selected workspace and clears it on sign-out/session recovery", () => {
    expect(selectedWorkspaceId()).toBeNull();
    selectWorkspace("01J00000000000000000000001");
    expect(selectedWorkspaceId()).toBe("01J00000000000000000000001");
    selectWorkspace("01J00000000000000000000002");
    expect(selectedWorkspaceId()).toBe("01J00000000000000000000002");
    clearSelectedWorkspace();
    expect(selectedWorkspaceId()).toBeNull();
  });

  it("attaches the selected workspace header to each tenant-scoped real API request", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030");
    selectWorkspace("01J00000000000000000000001");
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: () => Promise.resolve({ permissions: ["catalog.read"] }),
    });
    globalThis.fetch = fetchMock;

    const { apiIdentityClient } = await import("@/lib/adapters/api/identity-client");
    await expect(apiIdentityClient.getPermissions()).resolves.toEqual(["catalog.read"]);

    expect(fetchMock.mock.calls[0][0]).toBe("http://localhost:5030/v1/permissions");
    expect(fetchMock.mock.calls[0][1].headers["X-Kreyora-Tenant-Id"]).toBe("01J00000000000000000000001");
  });
});
