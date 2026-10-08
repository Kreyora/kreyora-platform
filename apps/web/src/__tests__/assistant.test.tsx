import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AssistantClient } from "@/lib/ports/assistant-client";
import type { AssistantPolicy, AssistantReadiness, AssistantTurn, AssistantUsage, KnowledgeDocument, Role } from "@/lib/types";
import { ApiClientError } from "@/lib/api/errors";
import { AiActivity } from "@/components/inbox/ai-activity";
import { waitingFor } from "@/components/inbox/inbox-display";
import { Wrapper } from "./inbox-test-utils";

// M09-S08: behaviour of the assistant screens with a stubbed client (the same port the API and demo adapters implement).

const search = { value: new URLSearchParams() };
vi.mock("next/navigation", () => ({
  usePathname: () => "/assistant",
  useSearchParams: () => search.value,
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  useParams: () => ({}),
}));

import AssistantOverviewPage from "@/app/(seller)/assistant/page";
import KnowledgePage from "@/app/(seller)/assistant/knowledge/page";
import ConsolePage from "@/app/(seller)/assistant/console/page";
import HistoryPage from "@/app/(seller)/assistant/history/page";

const policy = (overrides: Partial<AssistantPolicy> = {}): AssistantPolicy => ({
  enabled: true,
  replyStyle: "matchCustomer",
  supportedLanguages: ["ne", "ne-Latn", "en"],
  tone: "friendly",
  brandNote: null,
  businessHours: [{ day: "monday", closed: false, opens: "09:00", closes: "19:00" }],
  timeZone: "Asia/Kathmandu",
  outsideHoursBehavior: "answerAndPromiseFollowUp",
  unrecognizedMediaBehavior: "askForDetails",
  escalationKeywords: ["wholesale"],
  allowedTools: ["SearchProducts"],
  maxToolSteps: 4,
  maxRepliesPerConversationPerHour: 20,
  maxOutputTokens: 600,
  reviewedAt: null,
  version: "v-1",
  fixedEscalationCategories: [],
  availableTools: ["SearchProducts", "GetPrice"],
  platformCaps: { maxToolSteps: 6, maxRepliesPerConversationPerHour: 60, maxOutputTokens: 800 },
  ...overrides,
});

const readiness = (overrides: Partial<AssistantReadiness> = {}): AssistantReadiness => ({
  isActive: false,
  sellerEnabled: true,
  platformEnabled: true,
  checks: [
    { code: "store_ready", passed: true, required: true, detail: "ok", blockers: [] },
    { code: "policy_reviewed", passed: false, required: true, detail: "Review and save the assistant settings.", blockers: [] },
  ],
  ...overrides,
});

const usage = (turnsToday = 3): AssistantUsage => ({
  days: 2,
  daily: [
    { date: "2026-10-07", turns: 5, replied: 4, handedOff: 1, skipped: 0, blocked: 0, modelCalls: 9, inputTokens: 100, outputTokens: 10, estimatedCostUsd: 0, playgroundTurns: 0 },
    { date: "2026-10-08", turns: turnsToday, replied: turnsToday, handedOff: 0, skipped: 0, blocked: 0, modelCalls: 6, inputTokens: 50, outputTokens: 5, estimatedCostUsd: 0, playgroundTurns: 1 },
  ],
  turnsToday,
  dailyTurnCap: 10,
  platformEnabled: true,
  nextResetAt: "2026-10-09T00:00:00Z",
});

const doc = (overrides: Partial<KnowledgeDocument> = {}): KnowledgeDocument => ({
  id: "d-1",
  title: "Returns",
  category: "returns",
  source: "text",
  activeVersion: null,
  pendingVersions: [{ id: "v-1", versionNumber: 1, state: "pendingReview", characterCount: 40, originalFileName: null, submittedAt: "2026-10-08T00:00:00Z", reviewedAt: null, reviewNote: null, hasSuspiciousInstructions: false }],
  latestVersionNumber: 1,
  createdAt: "2026-10-08T00:00:00Z",
  modifiedAt: "2026-10-08T00:00:00Z",
  indexStatus: null,
  ...overrides,
});

const turn = (overrides: Partial<AssistantTurn> = {}): AssistantTurn => ({
  id: "t-1",
  conversationId: "c-1",
  isPlayground: false,
  outcome: "replied",
  reasonCode: "replied",
  startedAt: "2026-10-08T05:00:00Z",
  finishedAt: "2026-10-08T05:00:04Z",
  promptVersion: "assistant-system-v1+abc",
  registryVersion: "kreyora-tools.v2",
  policyVersion: "v-1",
  models: ["gemini"],
  modelCalls: 2,
  providerLatencyMs: 1800,
  tools: [{ tool: "GetPrice", outcome: "ok", durationMs: 20, dryRun: false, replayed: false }],
  citations: 0,
  validationCodes: [],
  inputTokens: 100,
  outputTokens: 10,
  estimatedCostUsd: 0,
  ...overrides,
});

function stub(overrides: Partial<AssistantClient> = {}): AssistantClient {
  return {
    getPolicy: vi.fn().mockResolvedValue(policy()),
    updatePolicy: vi.fn().mockImplementation(async (u) => ({ ...policy(), ...u, reviewedAt: "2026-10-08T06:00:00Z", version: "v-2" })),
    getReadiness: vi.fn().mockResolvedValue(readiness()),
    listKnowledge: vi.fn().mockResolvedValue([doc()]),
    createKnowledge: vi.fn().mockResolvedValue(doc()),
    uploadKnowledge: vi.fn().mockResolvedValue(doc()),
    addKnowledgeVersion: vi.fn(),
    getKnowledgeVersion: vi.fn().mockResolvedValue({ documentId: "d-1", documentTitle: "Returns", version: doc().pendingVersions[0], text: "Exchange within 7 days." }),
    approveKnowledgeVersion: vi.fn().mockResolvedValue(doc()),
    rejectKnowledgeVersion: vi.fn().mockResolvedValue(doc()),
    deleteKnowledge: vi.fn(),
    importStorePolicies: vi.fn().mockResolvedValue([]),
    searchKnowledge: vi.fn().mockResolvedValue({ confidence: "none", mode: "hybrid", passages: [] }),
    runPlayground: vi.fn().mockResolvedValue({ turnId: "p-1", outcome: "replied", reasonCode: "playground", reply: "Kurta ko price NPR 2,450 ho.", tools: [{ tool: "GetPrice", outcome: "ok", dryRun: false }, { tool: "CreateCheckoutLink", outcome: "ok", dryRun: true }], citations: 0, modelCalls: 2, inputTokens: 100, outputTokens: 20, estimatedCostUsd: 0, validationCodes: [] }),
    listTurns: vi.fn().mockResolvedValue({ items: [turn()], nextCursor: null }),
    getUsage: vi.fn().mockResolvedValue(usage()),
    ...overrides,
  };
}

const show = (ui: React.ReactNode, role: Role, client: AssistantClient) => render(<Wrapper role={role} clients={{ assistant: client }}>{ui}</Wrapper>);

beforeEach(() => { search.value = new URLSearchParams(); });
afterEach(() => vi.restoreAllMocks());

describe("assistant overview", () => {
  it("shows readiness with a fix link, usage, and saves settings with the version (reviewed on save)", async () => {
    const client = stub();
    show(<AssistantOverviewPage />, "owner", client);

    expect(await screen.findByText("Not active yet")).toBeInTheDocument();
    expect(screen.getByText("Settings reviewed").parentElement).toHaveTextContent("not done");
    expect(screen.getByRole("link", { name: "Review settings below" })).toBeInTheDocument();
    expect(screen.getByText("Usage")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Tone"), { target: { value: "formal" } });
    fireEvent.click(screen.getByRole("button", { name: "Save settings" }));

    await screen.findByText("Saved. Settings are marked reviewed.");
    expect(client.updatePolicy).toHaveBeenCalledWith(expect.objectContaining({ tone: "formal", version: "v-1", escalationKeywords: ["wholesale"] }));
    expect(client.getReadiness).toHaveBeenCalledTimes(2); // refreshed after the save
  });

  it("viewers see the settings read-only, with no save button", async () => {
    show(<AssistantOverviewPage />, "viewer", stub());

    expect(await screen.findByText(/View only/)).toBeInTheDocument();
    expect(screen.getByLabelText("Tone")).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Save settings" })).not.toBeInTheDocument();
  });

  it("a stale save asks for a refresh, which reloads the server's settings", async () => {
    const client = stub({ updatePolicy: vi.fn().mockRejectedValue(new ApiClientError({ type: "conflict", title: "Conflict", status: 409, detail: "" })) });
    show(<AssistantOverviewPage />, "admin", client);

    fireEvent.click(await screen.findByRole("button", { name: "Save settings" }));
    expect(await screen.findByText(/Someone else changed this/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await waitFor(() => expect(client.getPolicy).toHaveBeenCalledTimes(2));
  });

  it("a refused load shows permission-denied without a retry", async () => {
    show(<AssistantOverviewPage />, "owner", stub({ getPolicy: vi.fn().mockRejectedValue(new ApiClientError({ type: "forbidden", title: "Forbidden", status: 403, detail: "" })) }));

    expect(await screen.findByRole("alert")).toHaveTextContent("You don't have permission");
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument();
  });

  it("warns near the daily limit and explains when Kreyora has the assistant off", async () => {
    show(<AssistantOverviewPage />, "owner", stub({ getUsage: vi.fn().mockResolvedValue(usage(9)), getReadiness: vi.fn().mockResolvedValue(readiness({ platformEnabled: false })) }));

    expect(await screen.findByText(/used 90% of today's limit/)).toBeInTheDocument();
    expect(screen.getByText("Off for all shops")).toBeInTheDocument();
  });
});

describe("knowledge", () => {
  it("reviews a pending version in a dialog and approves it", async () => {
    const client = stub();
    show(<KnowledgePage />, "owner", client);

    expect(await screen.findByText("1 waiting for review")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Review v1" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Exchange within 7 days.")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Approve and use" }));

    await waitFor(() => expect(client.approveKnowledgeVersion).toHaveBeenCalledWith("d-1", "v-1"));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("validates new text, submits it for review, and viewers can't add or delete", async () => {
    const client = stub();
    const { unmount } = show(<KnowledgePage />, "owner", client);
    await screen.findByText("1 waiting for review");

    fireEvent.click(screen.getByRole("button", { name: "Submit for review" }));
    expect(await screen.findByText(/Give it a title/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Opening hours" } });
    fireEvent.change(screen.getByLabelText("Text"), { target: { value: "10 to 7, closed Saturday." } });
    fireEvent.click(screen.getByRole("button", { name: "Submit for review" }));
    await waitFor(() => expect(client.createKnowledge).toHaveBeenCalledWith({ title: "Opening hours", category: "faq", text: "10 to 7, closed Saturday.", file: null }));
    unmount();

    show(<KnowledgePage />, "viewer", stub());
    await screen.findByText("1 waiting for review");
    expect(screen.queryByText("Add knowledge")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });

  it("the search test says when nothing is relevant, and shows passages when found", async () => {
    const client = stub();
    show(<KnowledgePage />, "operator", client);
    fireEvent.change(await screen.findByLabelText("Question"), { target: { value: "return?" } });
    fireEvent.click(screen.getByRole("button", { name: "Search" }));
    expect(await screen.findByText(/Nothing relevant/)).toBeInTheDocument();

    vi.mocked(client.searchKnowledge).mockResolvedValue({ confidence: "high", mode: "hybrid", passages: [{ text: "Exchange within 7 days.", score: 0.81, documentId: "d-1", documentTitle: "Returns" }] });
    fireEvent.click(screen.getByRole("button", { name: "Search" }));
    expect(await screen.findByText("Exchange within 7 days.")).toBeInTheDocument();
  });

  it("shows the empty state", async () => {
    show(<KnowledgePage />, "owner", stub({ listKnowledge: vi.fn().mockResolvedValue([]) }));
    expect(await screen.findByText("No knowledge yet")).toBeInTheDocument();
  });
});

describe("test console", () => {
  it("runs the real turn, keeps the conversation, and marks test-only tools", async () => {
    const client = stub();
    show(<ConsolePage />, "owner", client);
    expect(screen.getByRole("note")).toHaveTextContent("Test only: nothing is sent");

    fireEvent.change(screen.getByLabelText("Customer message"), { target: { value: "red kurta kati ho?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("Kurta ko price NPR 2,450 ho.")).toBeInTheDocument();
    expect(screen.getByText("Replied")).toBeInTheDocument();
    expect(screen.getByText("test only")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Customer message"), { target: { value: "M size?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(client.runPlayground).toHaveBeenLastCalledWith([
      { from: "customer", text: "red kurta kati ho?" },
      { from: "shop", text: "Kurta ko price NPR 2,450 ho." },
      { from: "customer", text: "M size?" },
    ]));
  });

  it("keeps the message in the box when the run fails, shows the server's reason, and operators can't use it", async () => {
    const { unmount } = show(<ConsolePage />, "admin", stub({ runPlayground: vi.fn().mockRejectedValue(new ApiClientError({ type: "store", title: "Conflict", status: 409, detail: "Create the store before using the assistant tools." })) }));
    fireEvent.change(screen.getByLabelText("Customer message"), { target: { value: "hello" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Create the store before using the assistant tools.");
    expect(screen.getByLabelText("Customer message")).toHaveValue("hello");
    unmount();

    show(<ConsolePage />, "operator", stub());
    expect(screen.getByRole("alert")).toHaveTextContent("Only the shop owner or an admin");
    expect(screen.queryByLabelText("Customer message")).not.toBeInTheDocument();
  });
});

describe("history", () => {
  it("lists decisions with reasons and links, filters by outcome, and pages older ones", async () => {
    const client = stub({
      listTurns: vi.fn()
        .mockResolvedValueOnce({ items: [turn(), turn({ id: "t-2", outcome: "escalated", reasonCode: "person_requested", tools: [] })], nextCursor: "next" })
        .mockResolvedValueOnce({ items: [turn({ id: "t-3", isPlayground: true, conversationId: null })], nextCursor: null }),
    });
    show(<HistoryPage />, "viewer", client);

    expect(await screen.findByText("Customer asked for a person")).toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: "Open conversation" })[0]).toHaveAttribute("href", "/inbox/c-1");
    fireEvent.change(screen.getByLabelText("Filter by outcome"), { target: { value: "escalated" } });
    expect(screen.queryByText("GetPrice · ok")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Filter by outcome"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Load older" }));
    expect(await screen.findByText("Console test")).toBeInTheDocument();
    expect(client.listTurns).toHaveBeenLastCalledWith({ conversationId: undefined, cursor: "next", pageSize: 25 });
  });

  it("filters to one conversation from the inbox link and shows the empty state", async () => {
    search.value = new URLSearchParams("conversationId=c-9");
    const client = stub({ listTurns: vi.fn().mockResolvedValue({ items: [], nextCursor: null }) });
    show(<HistoryPage />, "owner", client);

    expect(await screen.findByText("No assistant activity yet")).toBeInTheDocument();
    expect(client.listTurns).toHaveBeenCalledWith({ conversationId: "c-9", pageSize: 25 });
    expect(screen.getByRole("link", { name: "Show all" })).toHaveAttribute("href", "/assistant/history");
  });
});

describe("inbox assistant strip and queue helpers", () => {
  it("shows the latest decisions without text and links to the full history", () => {
    render(<AiActivity conversationId="c-1" turns={[turn({ outcome: "blocked", reasonCode: "automation_paused", tools: [] })]} />);

    expect(screen.getByText("A person owns the chat")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Full history" })).toHaveAttribute("href", "/assistant/history?conversationId=c-1");
  });

  it("formats waiting time", () => {
    const now = Date.parse("2026-10-08T12:00:00Z");
    expect(waitingFor("2026-10-08T11:48:00Z", now)).toBe("12 min");
    expect(waitingFor("2026-10-08T09:00:00Z", now)).toBe("3 h");
    expect(waitingFor("2026-10-05T12:00:00Z", now)).toBe("3 days");
  });
});
