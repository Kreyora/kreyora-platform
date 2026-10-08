import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { apiAssistantClient, toPolicy, toTurn } from "@/lib/adapters/api/assistant-client";
import { apiConversationClient, resetConversationAssigneeCache } from "@/lib/adapters/api/conversation-client";
import { mockAssistantClient, DEMO_REPLY, resetMockAssistant } from "@/lib/adapters/mock/mock-assistant-client";
import { mockConversationClient } from "@/lib/adapters/mock/mock-conversation-client";
import { clearSelectedWorkspace, selectWorkspace } from "@/lib/session/workspace-selection";

// M09-S08: the API adapter normalizes the wire format (enums as names or numbers, numbers as strings) and the demo
// adapter implements the same port; the conversation adapters pass the needs-a-person filter.

function respond(body: unknown) {
  return { ok: true, status: 200, headers: new Headers(), json: () => Promise.resolve(body) };
}

describe("assistant API adapter", () => {
  const originalFetch = globalThis.fetch;
  let calls: { url: string; init: RequestInit }[];

  beforeEach(() => {
    vi.stubEnv("NEXT_PUBLIC_API_URL", "http://localhost:5030");
    selectWorkspace("tenant-1");
    calls = [];
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    clearSelectedWorkspace();
    resetConversationAssigneeCache();
    vi.unstubAllEnvs();
  });

  it("maps policy enums from names or numbers, .NET day numbers and string numbers", () => {
    const policy = toPolicy({
      enabled: true, replyStyle: 2, supportedLanguages: ["ne"], tone: "formal", brandNote: null,
      businessHours: [{ day: 0, closed: true }, { day: "Monday", closed: false, opens: "10:00", closes: "18:00" }],
      timeZone: "Asia/Kathmandu", outsideHoursBehavior: "doNotAnswer", unrecognizedMediaBehavior: "2",
      escalationKeywords: [], allowedTools: [], maxToolSteps: "4", maxRepliesPerConversationPerHour: 20, maxOutputTokens: "600",
      reviewedAt: null, version: "v1", fixedEscalationCategories: [], availableTools: [],
      platformCaps: { maxToolSteps: 6, maxRepliesPerConversationPerHour: "60", maxOutputTokens: 800 },
    } as never);

    expect(policy).toMatchObject({
      replyStyle: "alwaysRomanized", tone: "formal", outsideHoursBehavior: "doNotAnswer", unrecognizedMediaBehavior: "handToPerson",
      maxToolSteps: 4, maxOutputTokens: 600, platformCaps: { maxRepliesPerConversationPerHour: 60 },
    });
    expect(policy.businessHours.map((h) => h.day)).toEqual(["sunday", "monday"]);
  });

  it("summarizes a turn: distinct models, summed provider time, no text", () => {
    const item = toTurn({
      id: "t1", conversationId: null, isPlayground: true, outcome: "escalated", reasonCode: "model_escalation", startedAt: "s", finishedAt: null,
      policyVersion: null, promptVersion: "p", registryVersion: "r",
      modelCalls: [{ profile: "Primary", provider: "g", model: "m1", latencyMs: "900", inputTokens: 1, outputTokens: 1, outcome: "stop" }, { profile: "Fallback", provider: "g", model: "m1", latencyMs: 600, inputTokens: 1, outputTokens: 1, outcome: "stop" }],
      toolSteps: [], citations: [], validationCodes: ["ungrounded_number"], inputTokens: "10", outputTokens: 2, estimatedCostUsd: "0.0001", outboundMessageId: null,
    } as never);

    expect(item).toMatchObject({ outcome: "escalated", models: ["m1"], modelCalls: 2, providerLatencyMs: 1500, inputTokens: 10, estimatedCostUsd: 0.0001 });
  });

  it("sends the tenant, CSRF and idempotency headers on writes and builds the turn query", async () => {
    globalThis.fetch = vi.fn().mockImplementation(async (url: string, init: RequestInit) => {
      calls.push({ url, init });
      if (url.endsWith("/v1/auth/csrf")) return respond({ token: "csrf-1" });
      if (url.includes("/v1/assistant/turns")) return respond({ items: [], nextCursor: null });
      return respond({ turnId: "p", outcome: "replied", reasonCode: "playground", reply: "hi", tools: [], citations: [], modelCalls: 1, inputTokens: 1, outputTokens: 1, estimatedCostUsd: 0, validationCodes: [] });
    }) as never;

    const result = await apiAssistantClient.runPlayground([{ from: "customer", text: "hello" }]);
    await apiAssistantClient.listTurns({ conversationId: "c 1", cursor: "abc" });

    expect(result.outcome).toBe("replied");
    const play = calls.find((c) => c.url.endsWith("/v1/assistant/playground"))!;
    const headers = play.init.headers as Record<string, string>;
    expect(headers["X-Kreyora-Tenant-Id"]).toBe("tenant-1");
    expect(headers["X-CSRF-Token"]).toBe("csrf-1");
    expect(headers["Idempotency-Key"]).toBeTruthy();
    expect(JSON.parse(play.init.body as string)).toEqual({ messages: [{ from: "customer", text: "hello" }] });
    expect(calls.some((c) => c.url.endsWith("/v1/assistant/turns?pageSize=25&conversationId=c+1&cursor=abc"))).toBe(true);
  });

  it("asks the server for the needs-a-person queue", async () => {
    globalThis.fetch = vi.fn().mockImplementation(async (url: string) => {
      calls.push({ url, init: {} });
      if (url.includes("/assignees")) return respond([]);
      return respond({
        items: [{ id: "c1", connectionId: "k", channel: "instagram", status: "humanAssigned", customerLabel: "Test Customer", customerUsername: "test.customer", lastMessagePreview: null, lastMessageAt: null, unreadCount: 0, labels: [], assignedUserId: null, assignedAt: null, isAutomationActive: false, createdAt: "c", modifiedAt: "m", escalationCategory: "complaint", waitingSince: "2026-10-08T00:00:00Z" }],
        page: 1, pageSize: 20, totalCount: 1,
      });
    }) as never;

    const page = await apiConversationClient.listConversations({ needsPerson: true });

    expect(calls[0].url).toContain("needsPerson=true");
    expect(page.items[0]).toMatchObject({ escalationCategory: "complaint", waitingSince: "2026-10-08T00:00:00Z", customerUsername: "test.customer", customerIdentifier: "@test.customer" });
  });
});

describe("demo adapters", () => {
  beforeEach(() => resetMockAssistant());

  it("the demo assistant labels its replies and approves knowledge in memory", async () => {
    const reply = await mockAssistantClient.runPlayground([{ from: "customer", text: "hi" }]);
    expect(reply.reply).toBe(DEMO_REPLY);

    const pending = (await mockAssistantClient.listKnowledge()).find((d) => d.pendingVersions.length > 0)!;
    const approved = await mockAssistantClient.approveKnowledgeVersion(pending.id, pending.pendingVersions[0].id);
    expect(approved.activeVersion?.state).toBe("active");
    expect((await mockAssistantClient.getReadiness()).checks.find((c) => c.code === "knowledge_approved")?.passed).toBe(true);
  });

  it("the demo queue lists only person-owned waiting chats, and a hand-back removes them", async () => {
    const queue = await mockConversationClient.listConversations({ needsPerson: true });
    expect(queue.items.length).toBeGreaterThan(0);
    expect(queue.items.every((c) => !c.isAutomationActive && c.waitingSince)).toBe(true);

    await mockConversationClient.release(queue.items[0].id);
    const after = await mockConversationClient.listConversations({ needsPerson: true });
    expect(after.items.map((c) => c.id)).not.toContain(queue.items[0].id);
  });
});
