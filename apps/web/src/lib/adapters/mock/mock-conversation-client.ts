import type { ConversationClient } from "@/lib/ports/conversation-client";
import type {
  Conversation,
  ConversationAssignee,
  ConversationStatusAction,
  Message,
  PaginatedResult,
} from "@/lib/types";
import { conversations, messagesByConversationId, teamMembers } from "../fixtures/data";

/**
 * Demo-mode inbox (no NEXT_PUBLIC_API_URL). Works on an in-memory copy of the fixtures and never calls the
 * network. Simulated replies move from pending to sent after a short delay so the UI flow can be explored.
 */
const MOCK_DELAY_MS = 50;
const SIMULATED_SEND_MS = 1200;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

let state = clone();

function clone() {
  return {
    conversations: conversations.map((c) => ({ ...c, labels: [...c.labels] })),
    messages: Object.fromEntries(
      Object.entries(messagesByConversationId).map(([id, items]) => [id, items.map((m) => ({ ...m }))]),
    ) as Record<string, Message[]>,
    sentKeys: new Map<string, Message>(),
  };
}

/** Test seam: restore pristine demo data. */
export function resetMockConversations(): void {
  state = clone();
}

function find(id: string): Conversation {
  const conversation = state.conversations.find((c) => c.id === id);
  if (!conversation) {
    throw new Error(`Conversation not found: ${id}`);
  }
  return conversation;
}

function update(id: string, change: (c: Conversation) => Partial<Conversation>): Conversation {
  const current = find(id);
  const next = { ...current, ...change(current), updatedAt: new Date().toISOString() };
  state.conversations = state.conversations.map((c) => (c.id === id ? next : c));
  return next;
}

const STATUS_TARGET: Record<ConversationStatusAction, Conversation["state"]> = {
  resolve: "resolved",
  reopen: "new",
  close: "closed",
  mark_spam: "spam",
  unmark_spam: "new",
};

export const mockConversationClient: ConversationClient = {
  async listConversations(params): Promise<PaginatedResult<Conversation>> {
    await delay();
    let filtered = [...state.conversations];
    if (params?.state) filtered = filtered.filter((c) => c.state === params.state);
    if (params?.channel) filtered = filtered.filter((c) => c.channel === params.channel);
    if (params?.unreadOnly) filtered = filtered.filter((c) => c.unreadCount > 0);
    if (params?.assignedTo) filtered = filtered.filter((c) => c.assignment?.assigneeId === params.assignedTo);
    if (params?.needsPerson) {
      // Same rule as the server: a person owns it, it is open, and the customer is waiting; longest wait first.
      filtered = filtered
        .filter((c) => !c.isAutomationActive && c.waitingSince && !["resolved", "closed", "spam"].includes(c.state))
        .sort((a, b) => (a.waitingSince ?? "").localeCompare(b.waitingSince ?? ""));
    }
    if (params?.search) {
      const query = params.search.toLowerCase();
      filtered = filtered.filter(
        (c) =>
          c.customerName.toLowerCase().includes(query) ||
          c.lastMessage?.toLowerCase().includes(query) ||
          c.labels.some((l) => l.toLowerCase().includes(query)),
      );
    }
    return { items: filtered, cursor: null, hasMore: false, totalCount: filtered.length };
  },

  async getConversation(id) {
    await delay();
    return find(id);
  },

  async getMessages(conversationId) {
    await delay();
    const items = state.messages[conversationId] ?? [];
    return { items: [...items], cursor: null, hasMore: false, totalCount: items.length };
  },

  async sendReply(conversationId, text, idempotencyKey) {
    await delay();
    const existing = state.sentKeys.get(idempotencyKey);
    if (existing) return existing;

    const conversation = find(conversationId);
    const message: Message = {
      id: `demo-msg-${Date.now()}`,
      conversationId,
      direction: "outbound",
      senderName: "You (demo)",
      senderType: "staff",
      content: text,
      attachments: [],
      deliveryState: "pending",
      createdAt: new Date().toISOString(),
    };
    state.messages[conversationId] = [...(state.messages[conversationId] ?? []), message];
    state.sentKeys.set(idempotencyKey, message);
    update(conversation.id, () => ({ isAutomationActive: false, state: "human_assigned", lastMessage: text, lastMessageAt: message.createdAt }));
    setTimeout(() => {
      state.messages[conversationId] = (state.messages[conversationId] ?? []).map((m) =>
        m.id === message.id ? { ...m, deliveryState: "sent" } : m,
      );
    }, SIMULATED_SEND_MS);
    return message;
  },

  async markRead(id) {
    await delay();
    return update(id, () => ({ unreadCount: 0 }));
  },

  async takeOver(id) {
    await delay();
    return update(id, (c) => ({ isAutomationActive: false, state: ["resolved", "closed", "spam"].includes(c.state) ? c.state : "human_assigned" }));
  },

  async release(id) {
    await delay();
    // The hand-back ends the escalation (as on the server); the reason stays in the audit log.
    return update(id, (c) => ({ isAutomationActive: true, escalationCategory: undefined, escalatedAt: undefined, waitingSince: undefined, state: ["resolved", "closed", "spam"].includes(c.state) ? c.state : "bot_active" }));
  },

  async assign(id, userId) {
    await delay();
    const member = teamMembers.find((m) => m.user.id === userId);
    return update(id, () => ({
      assignment: { assigneeId: userId, assigneeName: member?.user.displayName ?? "Team member", assignedAt: new Date().toISOString() },
    }));
  },

  async unassign(id) {
    await delay();
    return update(id, () => ({ assignment: undefined }));
  },

  async setLabels(id, labels) {
    await delay();
    return update(id, () => ({ labels: [...new Set(labels.map((l) => l.trim()).filter(Boolean))] }));
  },

  async changeStatus(id, action) {
    await delay();
    return update(id, () => ({ state: STATUS_TARGET[action] }));
  },

  async listAssignees(): Promise<ConversationAssignee[]> {
    await delay();
    return teamMembers.map((m) => ({ userId: m.user.id, displayName: m.user.displayName, role: m.membership.role }));
  },
};
