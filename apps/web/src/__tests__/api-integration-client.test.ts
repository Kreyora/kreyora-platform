import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { apiIntegrationClient } from "@/lib/adapters/api/integration-client";

describe("apiIntegrationClient", () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030");
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.unstubAllEnvs();
  });

  it("lists channel connections with mapped status and capabilities", async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () =>
        Promise.resolve([
          {
            id: "conn_1",
            tenantId: "tenant_1",
            channel: "Simulator",
            displayName: "Test Simulator",
            externalAccountId: "sim_account_1",
            status: "Active",
            hasCredentials: true,
            capabilities: {
              canReceiveText: true,
              canSendText: true,
              canSendMedia: false,
              canReceiveMedia: false,
              requiresTemplatesOutsideWindow: false,
              supportsDeliveryReceipts: true,
            },
            createdAt: "2026-09-20T12:00:00Z",
          },
        ]),
      headers: new Headers(),
    });

    const connections = await apiIntegrationClient.listConnections();

    expect(connections).toHaveLength(1);
    expect(connections[0].id).toBe("conn_1");
    expect(connections[0].provider).toBe("simulator");
    expect(connections[0].status).toBe("connected");
    expect(connections[0].capabilities.canReceiveMessages).toBe(true);
    expect(connections[0].capabilities.canSendMedia).toBe(false);
  });

  it("fetches connection with enriched health diagnostics", async () => {
    globalThis.fetch = vi
      .fn()
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            id: "conn_1",
            tenantId: "tenant_1",
            channel: "WhatsApp",
            displayName: "Official WhatsApp",
            externalAccountId: "+9779800000001",
            status: "Active",
            hasCredentials: true,
            capabilities: {
              canReceiveText: true,
              canSendText: true,
            },
            createdAt: "2026-09-20T12:00:00Z",
          }),
        headers: new Headers(),
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            connectionId: "conn_1",
            channel: "WhatsApp",
            displayName: "Official WhatsApp",
            status: "Active",
            isHealthy: true,
            webhookUrl: "/v1/webhooks/whatsapp/conn_1",
            eventsProcessed24h: 42,
            eventsFailed24h: 1,
            deadLetterCount: 0,
            lastEventAt: "2026-09-21T10:00:00Z",
          }),
        headers: new Headers(),
      });

    const connection = await apiIntegrationClient.getConnection("conn_1");

    expect(connection.id).toBe("conn_1");
    expect(connection.provider).toBe("whatsapp");
    expect(connection.health.eventsProcessed24h).toBe(42);
    expect(connection.health.eventsFailed24h).toBe(1);
  });

  it("replays webhook event with idempotency key", async () => {
    globalThis.fetch = vi
      .fn()
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () => Promise.resolve({ csrfToken: "test_csrf_token" }),
        headers: new Headers(),
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            succeeded: true,
            resumedAt: "2026-09-21T12:00:00Z",
          }),
        headers: new Headers(),
      });

    const result = await apiIntegrationClient.replayWebhook("evt_123", "idemp_test_key");

    expect(result.success).toBe(true);

    const call = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[1];
    expect(call[0]).toBe("http://localhost:5030/v1/integrations/webhooks/evt_123/replay");
    expect(call[1].headers["Idempotency-Key"]).toBe("idemp_test_key");
  });

  it("reconnects connection and fetches updated health", async () => {
    globalThis.fetch = vi
      .fn()
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () => Promise.resolve({ csrfToken: "test_csrf_token" }),
        headers: new Headers(),
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () => Promise.resolve({ isHealthy: true, status: "Active" }),
        headers: new Headers(),
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            connectionId: "conn_1",
            channel: "Simulator",
            displayName: "Simulator",
            status: "Active",
            isHealthy: true,
            webhookUrl: "/v1/webhooks/simulator/conn_1",
            eventsProcessed24h: 10,
            eventsFailed24h: 0,
            deadLetterCount: 0,
          }),
        headers: new Headers(),
      });

    const health = await apiIntegrationClient.reconnect("conn_1");

    expect(health.status).toBe("connected");
    expect(health.eventsProcessed24h).toBe(10);
  });
});

