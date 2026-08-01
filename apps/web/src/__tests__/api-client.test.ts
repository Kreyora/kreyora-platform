import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { apiFetch } from "@/lib/api/api-client";
import { ApiClientError } from "@/lib/api/errors";

describe("apiFetch", () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5001");
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  it("sends GET request with correct headers", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve({ name: "Kreyora" }),
      headers: new Headers(),
    });

    const result = await apiFetch<{ name: string }>("/v1/system/info");

    expect(result).toEqual({ name: "Kreyora" });

    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[0]).toBe("http://localhost:5001/v1/system/info");

    const headers = call[1].headers;
    expect(headers["Accept"]).toBe("application/json");
    expect(headers["X-Correlation-ID"]).toBeTruthy();
  });

  it("sends Content-Type for requests with a body", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve({ id: "1" }),
      headers: new Headers(),
    });

    await apiFetch("/v1/products", {
      method: "POST",
      body: { title: "Test" },
    });

    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[1].headers["Content-Type"]).toBe("application/json");
    expect(call[1].body).toBe(JSON.stringify({ title: "Test" }));
  });

  it("does not send Content-Type when there is no body", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve({}),
      headers: new Headers(),
    });

    await apiFetch("/v1/test");

    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[1].headers["Content-Type"]).toBeUndefined();
  });

  it("returns undefined for 204 No Content", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 204,
      headers: new Headers(),
    });

    const result = await apiFetch("/v1/resource/1");
    expect(result).toBeUndefined();
  });

  it("returns undefined for a successful empty 201 response", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 201,
      headers: new Headers({ "Content-Length": "0" }),
    });

    const result = await apiFetch("/v1/auth/register", {
      method: "POST",
      body: { email: "seller@example.test" },
    });

    expect(result).toBeUndefined();
  });

  it("throws ApiClientError for RFC 7807 error responses", async () => {
    const problemResponse = {
      type: "https://tools.ietf.org/html/rfc9110#section-15.5.5",
      title: "Not Found",
      status: 404,
      detail: "Product not found",
    };

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 404,
      statusText: "Not Found",
      json: () => Promise.resolve(problemResponse),
      headers: new Headers({ "X-Correlation-ID": "test-corr-id" }),
    });

    try {
      await apiFetch("/v1/products/missing");
      expect.fail("Should have thrown");
    } catch (err) {
      expect(err).toBeInstanceOf(ApiClientError);
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(404);
      expect(apiErr.detail).toBe("Product not found");
      expect(apiErr.correlationId).toBe("test-corr-id");
      expect(apiErr.message).toBe("Not Found");
    }
  });

  it("creates a fallback problem when JSON parsing fails", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 500,
      statusText: "Internal Server Error",
      json: () => Promise.reject(new Error("invalid json")),
      headers: new Headers(),
    });

    try {
      await apiFetch("/v1/broken");
      expect.fail("Should have thrown");
    } catch (err) {
      expect(err).toBeInstanceOf(ApiClientError);
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(500);
      expect(apiErr.detail).toBe("HTTP 500 Internal Server Error");
    }
  });

  it("uses empty base URL when NEXT_PUBLIC_API_URL is unset", async () => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "");

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve({ ok: true }),
      headers: new Headers(),
    });

    await apiFetch("/v1/test");

    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(call[0]).toBe("/v1/test");
  });
});

describe("ApiClientError", () => {
  it("stores problem details properties", () => {
    const error = new ApiClientError(
      {
        type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        title: "Validation Error",
        status: 400,
        detail: "Name is required",
        errors: { name: ["Name is required"] },
      },
      "corr-123",
    );

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe("ApiClientError");
    expect(error.message).toBe("Validation Error");
    expect(error.status).toBe(400);
    expect(error.detail).toBe("Name is required");
    expect(error.correlationId).toBe("corr-123");
    expect(error.errors).toEqual({ name: ["Name is required"] });
  });

  it("works without optional fields", () => {
    const error = new ApiClientError({
      type: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
      title: "Internal Server Error",
      status: 500,
      detail: "Something broke",
    });

    expect(error.correlationId).toBeUndefined();
    expect(error.errors).toBeUndefined();
  });
});
