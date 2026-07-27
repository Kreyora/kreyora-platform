import { describe, it, expect, vi, afterEach } from "vitest";

describe("Adapter switching logic", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("uses fixture adapters when NEXT_PUBLIC_API_URL is unset", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "");

    const mod = await import("@/lib/providers/client-provider");
    expect(mod.USING_FIXTURE_ADAPTERS).toBe(true);
  });

  it("uses real adapters when NEXT_PUBLIC_API_URL is set", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5001");

    const mod = await import("@/lib/providers/client-provider");
    expect(mod.USING_FIXTURE_ADAPTERS).toBe(false);
  });
});
