import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  toApiStatus,
  toApiStatusAction,
  toConversationFromDetail,
  toConversationFromSummary,
  toConversationState,
  toDeliveryState,
  toMessage,
  type WireMessage,
} from "@/lib/adapters/api/conversation-mapping";
import { apiConversationClient, resetConversationAssigneeCache } from "@/lib/adapters/api/conversation-client";
import { clearCsrfToken } from "@/lib/adapters/api/auth-client";
import { clearSelectedWorkspace, selectWorkspace } from "@/lib/session/workspace-selection";
import { ApiClientError } from "@/lib/api/errors";
import { describeFailureCode, describeInboxError, reasonCode } from "@/lib/utils/conversation-errors";
import { defaultClientsForTest } from "./inbox-test-utils";
import { mockConversationClient } from "@/lib/adapters/mock";

const assignees = [{ userId: "u-1", displayName: "Asha", role: "operator" }];

function wireMessage(overrides: Partial<WireMessage> = {}): WireMessage {
  return {
    id: "m-1",
    conversationId: "c-1",
    direction: "outbound",
    origin: "staff",
    kind: "text",
    text: "hello",
    mediaUrl: null,
    mediaContentType: null,
    deliveryStatus: "sent",
    occurredAt: "2026-10-05T08:00:00Z",
    isRedacted: false,
    reactions: [],
    isPending: false,
    actorUserId: "u-1",
    deliveryFailureCode: null,
    ...overrides,
  };
}

describe("conversation mapping (API wire → UI)", () => {
  it.each([
    ["new", "new"], ["botActive", "bot_active"], ["humanAssigned", "human_assigned"], ["HumanAssigned", "human_assigned"],
    ["awaitingCustomer", "awaiting_customer"], ["checkoutInProgress", "checkout_in_progress"], ["orderCreated", "order_created"],
    ["resolved", "resolved"], ["closed", "closed"], ["spam", "spam"], [3, "human_assigned"],
  ] as const)("maps status %s → %s", (wire, ui) => {
    expect(toConversationState(wire)).toBe(ui);
  });

  it("maps UI state and actions to API values", () => {
    expect(toApiStatus("human_assigned")).toBe("humanAssigned");
    expect(toApiStatusAction("mark_spam")).toBe("markSpam");
    expect(toApiStatusAction("unmark_spam")).toBe("unmarkSpam");
  });

  it("derives delivery state from pending/status/direction", () => {
    expect(toDeliveryState({ direction: "outbound", isPending: true, deliveryStatus: null })).toBe("pending");
    expect(toDeliveryState({ direction: "outbound", isPending: false, deliveryStatus: "read" })).toBe("read");
    expect(toDeliveryState({ direction: "outbound", isPending: false, deliveryStatus: "failed" })).toBe("failed");
    expect(toDeliveryState({ direction: "inbound", isPending: false, deliveryStatus: null })).toBe("delivered");
  });

  it("maps senders: staff name, automation, provider-app echo, customer", () => {
    expect(toMessage(wireMessage(), "instagram", assignees).senderName).toBe("Asha");
    expect(toMessage(wireMessage({ origin: "automation", actorUserId: null }), "instagram", assignees).senderType).toBe("bot");
    const echo = toMessage(wireMessage({ origin: "providerNative", actorUserId: null }), "instagram", assignees);
    expect(echo.senderType).toBe("external");
    expect(echo.senderName).toBe("Sent from the Instagram app");
    expect(toMessage(wireMessage({ origin: "customer", direction: "inbound" }), "instagram", assignees).senderType).toBe("customer");
  });

  it("hides redacted content and exposes failure codes and media safely", () => {
    expect(toMessage(wireMessage({ isRedacted: true, text: null }), "instagram", assignees).content).toBe("Message removed");
    expect(toMessage(wireMessage({ deliveryStatus: "failed", deliveryFailureCode: "delivery_unconfirmed" }), "instagram", assignees).failureCode)
      .toBe("delivery_unconfirmed");
    const media = toMessage(wireMessage({ kind: "media", mediaUrl: "https://cdn.example/a.jpg", mediaContentType: "image", text: null }), "instagram", assignees);
    expect(media.attachments).toEqual([{ url: "https://cdn.example/a.jpg", type: "image", name: "image attachment" }]);
  });

  it("maps summaries and details including assignee names and erased customers", () => {
    const base = {
      id: "c-1", connectionId: "conn-1", channel: "instagram", status: "humanAssigned", unreadCount: "2", labels: ["vip"],
      assignedUserId: "u-1", assignedAt: "2026-10-05T08:00:00Z", isAutomationActive: false, lastMessageAt: null,
      createdAt: "2026-10-05T07:00:00Z", modifiedAt: "2026-10-05T08:00:00Z",
    };
    const summary = toConversationFromSummary({ ...base, customerLabel: "Instagram user ·4821", lastMessagePreview: "hi" }, assignees);
    expect(summary).toMatchObject({ state: "human_assigned", unreadCount: 2, customerName: "Instagram user ·4821", channel: "instagram" });
    expect(summary.assignment?.assigneeName).toBe("Asha");

    const detail = toConversationFromDetail({
      ...base, storeId: null, automationMode: "humanTakeover", lastCustomerMessageAt: "2026-10-05T07:30:00Z", customerLastReadAt: null,
      customer: { id: "i-1", channel: "instagram", customerLabel: "Instagram user ·4821", firstSeenAt: "", lastSeenAt: "", customerId: null, isErased: true },
    }, []);
    expect(detail.customerIdentifier).toBe("Customer data erased");
    expect(detail.assignment?.assigneeName).toBe("Team member");
    expect(detail.lastCustomerMessageAt).toBe("2026-10-05T07:30:00Z");
  });
});

describe("api conversation client", () => {
  const originalFetch = globalThis.fetch;
  let calls: Array<{ url: string; init: RequestInit }>;

  function respond(routes: Record<string, unknown>) {
    calls = [];
    globalThis.fetch = vi.fn(async (url: string | URL | Request, init?: RequestInit) => {
      const href = String(url);
      calls.push({ url: href, init: init ?? {} });
      const path = href.replace("http://localhost:5030", "").split("?")[0];
      const key = `${init?.method ?? "GET"} ${path}`;
      const body = routes[key] ?? routes[path];
      if (body instanceof ApiClientErrorBody) {
        return { ok: false, status: body.status, statusText: "Error", headers: new Headers(), json: () => Promise.resolve(body.problem) } as unknown as Response;
      }
      return { ok: true, status: 200, headers: new Headers(), json: () => Promise.resolve(body ?? {}) } as unknown as Response;
    }) as typeof fetch;
  }

  beforeEach(() => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030");
    selectWorkspace("tenant-1");
    clearCsrfToken();
    resetConversationAssigneeCache();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    clearSelectedWorkspace();
    vi.unstubAllEnvs();
  });

  it("sends a reply with tenant, CSRF and idempotency headers and maps the pending message", async () => {
    respond({
      "/v1/auth/csrf": { token: "csrf-1" },
      "/v1/conversations/assignees": [{ userId: "u-1", displayName: "Asha", role: "operator" }],
      "POST /v1/conversations/c-1/replies": wireMessage({ isPending: true, deliveryStatus: null }),
    });

    const message = await apiConversationClient.sendReply("c-1", "hello", "key-123");

    const post = calls.find((c) => c.url.endsWith("/v1/conversations/c-1/replies"))!;
    const headers = post.init.headers as Record<string, string>;
    expect(post.init.method).toBe("POST");
    expect(headers["X-Kreyora-Tenant-Id"]).toBe("tenant-1");
    expect(headers["X-CSRF-Token"]).toBe("csrf-1");
    expect(headers["Idempotency-Key"]).toBe("key-123");
    expect(JSON.parse(String(post.init.body))).toEqual({ text: "hello" });
    expect(message.deliveryState).toBe("pending");
    expect(message.senderName).toBe("Asha");
  });

  it("lists with server filters and computes the next page", async () => {
    respond({
      "/v1/conversations/assignees": [],
      "/v1/conversations": { items: [], page: 1, pageSize: 20, totalCount: 45, hasNextPage: true },
    });

    const page = await apiConversationClient.listConversations({ state: "human_assigned", unreadOnly: true, assignedTo: "u-1" });

    const list = calls.find((c) => c.url.includes("/v1/conversations?"))!;
    expect(list.url).toContain("status=humanAssigned");
    expect(list.url).toContain("unreadOnly=true");
    expect(list.url).toContain("assignedTo=u-1");
    expect(page).toMatchObject({ hasMore: true, cursor: "2", totalCount: 45 });
  });

  it("sends status actions in API casing", async () => {
    respond({
      "/v1/auth/csrf": { token: "csrf-1" },
      "/v1/conversations/assignees": [],
      "POST /v1/conversations/c-1/status": {
        id: "c-1", connectionId: "conn-1", storeId: null, channel: "instagram", status: "spam", automationMode: "automated",
        isAutomationActive: true, unreadCount: 0, labels: [], assignedUserId: null, assignedAt: null, lastMessageAt: null,
        lastCustomerMessageAt: null, customerLastReadAt: null, createdAt: "", modifiedAt: "",
        customer: { id: "i", channel: "instagram", customerLabel: "x", firstSeenAt: "", lastSeenAt: "", customerId: null, isErased: false },
      },
    });

    const result = await apiConversationClient.changeStatus("c-1", "mark_spam");

    const post = calls.find((c) => c.url.endsWith("/status"))!;
    expect(JSON.parse(String(post.init.body))).toEqual({ action: "markSpam" });
    expect(result.state).toBe("spam");
  });

  it("surfaces server denial codes as plain-language copy", async () => {
    respond({
      "/v1/auth/csrf": { token: "csrf-1" },
      "/v1/conversations/assignees": [],
      "POST /v1/conversations/c-1/replies": new ApiClientErrorBody(422, {
        type: "urn:kreyora:problem:window_closed_human_agent_unavailable", title: "window_closed_human_agent_unavailable", status: 422, detail: "x",
      }),
    });

    const error = await apiConversationClient.sendReply("c-1", "late", "k").catch((e: unknown) => e);

    expect(reasonCode(error)).toBe("window_closed_human_agent_unavailable");
    expect(describeInboxError(error).title).toBe("Reply window closed");
  });
});

class ApiClientErrorBody {
  constructor(public status: number, public problem: { type: string; title: string; status: number; detail: string }) {}
}

describe("inbox error copy", () => {
  it("maps conflicts to a refresh prompt and unknown failures safely", () => {
    const conflict = new ApiClientError({ type: "urn:kreyora:problem:conversation_changed", title: "x", status: 409, detail: "x" });
    expect(describeInboxError(conflict)).toMatchObject({ refresh: true, title: "Updated by someone else" });
    expect(describeInboxError(new TypeError("fetch failed")).title).toBe("Connection problem");
    expect(describeInboxError(new ApiClientError({ type: "about:blank", title: "x", status: 404, detail: "x" })).title).toBe("Not found");
    expect(describeFailureCode("delivery_unconfirmed")).toContain("may already have it");
    expect(describeFailureCode(undefined)).toBe("This message wasn't delivered.");
  });
});

describe("adapter selection", () => {
  it("uses the demo conversation client when no API URL is configured", () => {
    expect(defaultClientsForTest().conversation).toBe(mockConversationClient);
  });
});
