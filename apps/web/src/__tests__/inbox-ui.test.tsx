import { act, fireEvent, render, renderHook, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ConversationClient } from "@/lib/ports/conversation-client";
import type { Conversation, Message } from "@/lib/types";
import { ApiClientError } from "@/lib/api/errors";
import { MessageComposer } from "@/components/inbox/message-composer";
import { ConversationTimeline, type TimelineMessage } from "@/components/inbox/conversation-timeline";
import { availableStatusActions, windowLikelyClosed } from "@/components/inbox/inbox-display";
import { nextDelay, usePolling } from "@/hooks/use-polling";
import { Wrapper } from "./inbox-test-utils";

vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "c-1" }),
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  usePathname: () => "/inbox/c-1",
}));

import ConversationDetailPage from "@/app/(seller)/inbox/[id]/page";
import InboxPage from "@/app/(seller)/inbox/page";

const conversation = (overrides: Partial<Conversation> = {}): Conversation => ({
  id: "c-1",
  tenantId: "tenant-1",
  channel: "instagram",
  state: "bot_active",
  customerName: "Instagram user ·4821",
  customerIdentifier: "Instagram user ·4821",
  unreadCount: 0,
  labels: [],
  isAutomationActive: true,
  connectionId: "conn-1",
  createdAt: "2026-10-05T07:00:00Z",
  updatedAt: "2026-10-05T07:00:00Z",
  lastCustomerMessageAt: new Date().toISOString(),
  ...overrides,
});

const message = (overrides: Partial<Message> = {}): Message => ({
  id: "m-1",
  conversationId: "c-1",
  direction: "inbound",
  senderName: "Customer",
  senderType: "customer",
  content: "Do you have size M?",
  attachments: [],
  deliveryState: "delivered",
  createdAt: "2026-10-05T07:10:00Z",
  ...overrides,
});

function stubClient(overrides: Partial<ConversationClient> = {}): ConversationClient {
  const current = conversation();
  return {
    listConversations: vi.fn().mockResolvedValue({ items: [current], cursor: null, hasMore: false, totalCount: 1 }),
    getConversation: vi.fn().mockResolvedValue(current),
    getMessages: vi.fn().mockResolvedValue({ items: [message()], cursor: null, hasMore: false }),
    sendReply: vi.fn(),
    markRead: vi.fn().mockResolvedValue(conversation({ unreadCount: 0 })),
    takeOver: vi.fn().mockResolvedValue(conversation({ isAutomationActive: false, state: "human_assigned" })),
    release: vi.fn().mockResolvedValue(conversation()),
    assign: vi.fn(),
    unassign: vi.fn(),
    setLabels: vi.fn(),
    changeStatus: vi.fn(),
    listAssignees: vi.fn().mockResolvedValue([{ userId: "u-me", displayName: "Me", role: "operator" }]),
    ...overrides,
  };
}

const otherClients = {
  integration: { getHealth: vi.fn().mockResolvedValue({ status: "connected", webhookUrl: "", eventsProcessed24h: 0, eventsFailed24h: 0 }) } as never,
  assistant: { listTurns: vi.fn().mockResolvedValue({ items: [], nextCursor: null }) } as never,
};

function renderDetail(role: "owner" | "operator" | "viewer", client: ConversationClient) {
  return render(
    <Wrapper role={role} clients={{ conversation: client, ...otherClients }}>
      <ConversationDetailPage />
    </Wrapper>,
  );
}

describe("conversation page", () => {
  it("viewer sees the timeline but no composer or write controls", async () => {
    renderDetail("viewer", stubClient());

    expect(await screen.findByText("Do you have size M?")).toBeInTheDocument();
    expect(screen.getByText(/View-only access/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Reply")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Take over" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Resolve" })).not.toBeInTheDocument();
  });

  it("optimistic reply shows Sending… then is replaced by the server message", async () => {
    let resolve!: (m: Message) => void;
    const client = stubClient({ sendReply: vi.fn(() => new Promise<Message>((r) => { resolve = r; })) });
    renderDetail("operator", client);

    const field = await screen.findByLabelText("Reply");
    fireEvent.change(field, { target: { value: "Yes, size M is available" } });
    fireEvent.keyDown(field, { key: "Enter" });

    expect(await screen.findByText("Sending…", { selector: "span" })).toBeInTheDocument();
    await act(async () => {
      resolve(message({ id: "srv-1", direction: "outbound", senderType: "staff", senderName: "Me", content: "Yes, size M is available", deliveryState: "pending" }));
    });

    await waitFor(() => expect(screen.getAllByText("Yes, size M is available")).toHaveLength(1));
    expect(client.sendReply).toHaveBeenCalledWith("c-1", "Yes, size M is available", expect.any(String));
  });

  it("a definitive denial keeps the draft in the composer with an inline error and no retry bubble", async () => {
    const denial = new ApiClientError({ type: "urn:kreyora:problem:window_closed", title: "window_closed", status: 422, detail: "x" });
    const sendReply = vi.fn().mockRejectedValue(denial);
    renderDetail("operator", stubClient({ sendReply }));

    const field = await screen.findByLabelText("Reply");
    fireEvent.change(field, { target: { value: "late reply" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/Reply window closed/);
    expect(screen.getByLabelText("Reply")).toHaveValue("late reply");
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument();
    expect(screen.getAllByText("late reply")).toHaveLength(1);
  });

  it("an ambiguous failure keeps a retry bubble, and Try again reuses the same idempotency key", async () => {
    const sendReply = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
    renderDetail("operator", stubClient({ sendReply }));

    const field = await screen.findByLabelText("Reply");
    fireEvent.change(field, { target: { value: "maybe sent" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText(/Connection problem/)).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText("Reply")).toHaveValue(""));
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    await waitFor(() => expect(sendReply).toHaveBeenCalledTimes(2));
    expect(sendReply.mock.calls[0][2]).toBe(sendReply.mock.calls[1][2]);
  });

  it("marks an unread conversation read once when an operator opens it", async () => {
    const client = stubClient({ getConversation: vi.fn().mockResolvedValue(conversation({ unreadCount: 3 })) });
    renderDetail("operator", client);

    await screen.findByText("Do you have size M?");
    await waitFor(() => expect(client.markRead).toHaveBeenCalledTimes(1));
  });

  it("viewers never trigger mark-read", async () => {
    const client = stubClient({ getConversation: vi.fn().mockResolvedValue(conversation({ unreadCount: 3 })) });
    renderDetail("viewer", client);

    await screen.findByText("Do you have size M?");
    expect(client.markRead).not.toHaveBeenCalled();
  });

  it("take over updates ownership; a concurrent change shows a refresh prompt", async () => {
    const conflict = new ApiClientError({ type: "urn:kreyora:problem:conversation_changed", title: "x", status: 409, detail: "x" });
    const client = stubClient({ release: vi.fn().mockRejectedValue(conflict) });
    renderDetail("owner", client);

    fireEvent.click(await screen.findByRole("button", { name: "Take over" }));
    expect(await screen.findByText("Automation paused")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Hand back to automation" }));
    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Updated by someone else")).toBeInTheDocument();
    expect(within(alert).getByRole("button", { name: "Refresh" })).toBeInTheDocument();
  });

  it("not-found shows a safe message", async () => {
    const notFound = new ApiClientError({ type: "about:blank", title: "Not Found", status: 404, detail: "x" });
    renderDetail("operator", stubClient({ getConversation: vi.fn().mockRejectedValue(notFound) }));

    expect(await screen.findByText(/doesn't exist or isn't in this workspace/)).toBeInTheDocument();
  });

  it("spam conversations block the composer with a reason", async () => {
    renderDetail("operator", stubClient({ getConversation: vi.fn().mockResolvedValue(conversation({ state: "spam" })) }));

    expect(await screen.findByLabelText("Reply")).toBeDisabled();
    expect(screen.getByText(/blocked while this conversation is marked as spam/)).toBeInTheDocument();
  });

  it("links to redacted connection diagnostics", async () => {
    renderDetail("operator", stubClient());

    const link = await screen.findByRole("link", { name: "View connection diagnostics" });
    expect(link).toHaveAttribute("href", "/integrations/conn-1");
  });
});

describe("inbox list", () => {
  it("shows the demo notice in demo mode and filters through the client", async () => {
    const client = stubClient();
    render(<Wrapper role="operator" clients={{ conversation: client }}><InboxPage /></Wrapper>);

    expect(await screen.findByText(/Demo data — nothing is sent/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "Unread" }));
    await waitFor(() => expect(client.listConversations).toHaveBeenLastCalledWith(expect.objectContaining({ unreadOnly: true })));
    fireEvent.click(screen.getByRole("tab", { name: "Assigned to me" }));
    await waitFor(() => expect(client.listConversations).toHaveBeenLastCalledWith(expect.objectContaining({ assignedTo: "u-me" })));
  });

  it("the needs-a-person view asks the server for the queue and shows the reason and waiting time", async () => {
    const waiting = conversation({ isAutomationActive: false, state: "human_assigned", escalationCategory: "complaint", waitingSince: new Date(Date.now() - 12 * 60_000).toISOString() });
    const client = stubClient({ listConversations: vi.fn().mockResolvedValue({ items: [waiting], cursor: null, hasMore: false, totalCount: 1 }) });
    render(<Wrapper role="viewer" clients={{ conversation: client }}><InboxPage /></Wrapper>);

    fireEvent.click(await screen.findByRole("tab", { name: "Needs a person" }));

    await waitFor(() => expect(client.listConversations).toHaveBeenLastCalledWith(expect.objectContaining({ needsPerson: true })));
    expect(await screen.findByText("Complaint")).toBeInTheDocument();
    expect(screen.getByText(/Waiting 1[12] min/)).toBeInTheDocument();
  });

  it("the queue's empty state explains what appears there", async () => {
    const client = stubClient({ listConversations: vi.fn().mockResolvedValue({ items: [], cursor: null, hasMore: false }) });
    render(<Wrapper role="operator" clients={{ conversation: client }}><InboxPage /></Wrapper>);
    fireEvent.click(await screen.findByRole("tab", { name: "Needs a person" }));
    expect(await screen.findByText("No one is waiting for a person")).toBeInTheDocument();
  });

  it("renders empty and error states", async () => {
    const empty = stubClient({ listConversations: vi.fn().mockResolvedValue({ items: [], cursor: null, hasMore: false }) });
    const { unmount } = render(<Wrapper role="operator" clients={{ conversation: empty }}><InboxPage /></Wrapper>);
    expect(await screen.findByText("No conversations yet")).toBeInTheDocument();
    unmount();

    const failing = stubClient({ listConversations: vi.fn().mockRejectedValue(new TypeError("offline")) });
    render(<Wrapper role="operator" clients={{ conversation: failing }}><InboxPage /></Wrapper>);
    expect(await screen.findByText("Connection problem")).toBeInTheDocument();
  });
});

describe("composer", () => {
  it("Enter sends, Shift+Enter does not, counter and focus return", async () => {
    const onSend = vi.fn().mockResolvedValue(true);
    render(<MessageComposer onSend={onSend} />);
    const field = screen.getByLabelText("Reply");

    fireEvent.change(field, { target: { value: "hi" } });
    expect(screen.getByText("2/1000")).toBeInTheDocument();
    fireEvent.keyDown(field, { key: "Enter", shiftKey: true });
    expect(onSend).not.toHaveBeenCalled();
    fireEvent.keyDown(field, { key: "Enter" });

    await waitFor(() => expect(onSend).toHaveBeenCalledWith("hi"));
    await waitFor(() => expect(field).toHaveFocus());
    expect(field).toHaveValue("");
  });

  it("keeps the text when the server refuses, and blocks over-long text", async () => {
    const onSend = vi.fn().mockResolvedValue(false);
    render(<MessageComposer onSend={onSend} />);
    const field = screen.getByLabelText("Reply");

    fireEvent.change(field, { target: { value: "keep me" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(onSend).toHaveBeenCalled());
    expect(field).toHaveValue("keep me");

    fireEvent.change(field, { target: { value: "x".repeat(1001) } });
    expect(screen.getByRole("button", { name: "Send" })).toBeDisabled();
    expect(field).toHaveAttribute("aria-invalid", "true");
  });
});

describe("timeline failed-message actions", () => {
  const failed = (failureCode?: string): TimelineMessage =>
    message({ id: "f-1", direction: "outbound", senderType: "staff", senderName: "Me", content: "did it arrive?", deliveryState: "failed", failureCode });

  const renderTimeline = (messages: TimelineMessage[], onSendAgain = vi.fn()) => {
    render(
      <ConversationTimeline messages={messages} customerName="Customer" canWrite hasOlder={false} isLoadingOlder={false}
        onLoadOlder={vi.fn()} onSendAgain={onSendAgain} onRetryLocal={vi.fn()} />,
    );
    return onSendAgain;
  };

  it("unconfirmed delivery asks for confirmation before sending again", () => {
    const onSendAgain = renderTimeline([failed("delivery_unconfirmed")]);

    fireEvent.click(screen.getByRole("button", { name: "Send again" }));
    expect(onSendAgain).not.toHaveBeenCalled();
    expect(screen.getByText(/may already have this message. Send again anyway/)).toBeInTheDocument();
    fireEvent.click(within(screen.getByRole("group", { name: "Confirm resend" })).getByRole("button", { name: "Send again" }));
    expect(onSendAgain).toHaveBeenCalledWith("did it arrive?");
  });

  it("a definite failure sends again immediately", () => {
    const onSendAgain = renderTimeline([failed("551")]);

    fireEvent.click(screen.getByRole("button", { name: "Send again" }));
    expect(onSendAgain).toHaveBeenCalledWith("did it arrive?");
  });
});

describe("display rules", () => {
  it("offers status actions per state and hints at closed windows", () => {
    expect(availableStatusActions("spam")).toEqual(["unmark_spam"]);
    expect(availableStatusActions("resolved")).toContain("reopen");
    expect(availableStatusActions("human_assigned")).toEqual(["resolve", "close", "mark_spam"]);
    expect(windowLikelyClosed(new Date(Date.now() - 25 * 3600_000).toISOString())).toBe(true);
    expect(windowLikelyClosed(new Date().toISOString())).toBe(false);
  });
});

describe("polling", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("refreshes on the interval, backs off after failures, recovers, and pauses while hidden", async () => {
    let fail = false;
    const refresh = vi.fn(async () => { if (fail) throw new Error("down"); });
    const { result } = renderHook(() => usePolling(refresh, 5_000));

    await act(async () => { await vi.advanceTimersByTimeAsync(5_000); });
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(result.current.isStale).toBe(false);

    fail = true;
    await act(async () => { await vi.advanceTimersByTimeAsync(5_000); });
    expect(result.current.isStale).toBe(true);
    expect(nextDelay(5_000, 1)).toBe(10_000);
    await act(async () => { await vi.advanceTimersByTimeAsync(9_000); });
    expect(refresh).toHaveBeenCalledTimes(2);
    fail = false;
    await act(async () => { await vi.advanceTimersByTimeAsync(1_000); });
    expect(refresh).toHaveBeenCalledTimes(3);
    expect(result.current.isStale).toBe(false);

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    act(() => { document.dispatchEvent(new Event("visibilitychange")); });
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000); });
    expect(refresh).toHaveBeenCalledTimes(3);

    Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
    await act(async () => { document.dispatchEvent(new Event("visibilitychange")); });
    expect(refresh).toHaveBeenCalledTimes(4);
  });

  it("caps backoff at 60 seconds", () => {
    expect(nextDelay(15_000, 10)).toBe(60_000);
  });
});
