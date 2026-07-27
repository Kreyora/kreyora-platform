import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { apiSystemClient } from "@/lib/adapters/api";

describe("apiSystemClient", () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5001");
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  it("fetches and parses system info", async () => {
    const mockResponse = {
      name: "Kreyora",
      version: "0.1.0",
      environment: "Development",
    };

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve(mockResponse),
      headers: new Headers(),
    });

    const info = await apiSystemClient.getInfo();

    expect(info.name).toBe("Kreyora");
    expect(info.version).toBe("0.1.0");
    expect(info.environment).toBe("Development");

    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[0]).toContain("/v1/system/info");
  });
});
