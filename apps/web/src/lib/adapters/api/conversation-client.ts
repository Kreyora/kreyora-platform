import { apiFetch } from "@/lib/api";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { ConversationClient, ConversationListParams } from "@/lib/ports/conversation-client";
import type { Channel, Conversation, ConversationAssignee, PaginatedResult } from "@/lib/types";
import {
  toApiStatus,
  toApiStatusAction,
  toAssignee,
  toChannel,
  toConversationFromDetail,
  toConversationFromSummary,
  toMessage,
  type WireAssignee,
  type WireConversationDetail,
  type WireConversationSummary,
  type WireMessage,
} from "./conversation-mapping";

const tenantHeaders = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });

interface WirePage<T> {
  items: T[];
  page: number | string;
  pageSize: number | string;
  totalCount: number | string;
  hasNextPage?: boolean;
}

interface WireMessagePage {
  items: WireMessage[];
  nextBeforeMessageId: string | null;
}

const ASSIGNEE_TTL_MS = 60_000;
let assigneeCache: { tenantId: string; at: number; items: ConversationAssignee[] } | null = null;

async function assignees(): Promise<ConversationAssignee[]> {
  const tenantId = selectedWorkspaceId() ?? "";
  if (assigneeCache && assigneeCache.tenantId === tenantId && Date.now() - assigneeCache.at < ASSIGNEE_TTL_MS) {
    return assigneeCache.items;
  }

  const items = (await apiFetch<WireAssignee[]>("/v1/conversations/assignees", { headers: tenantHeaders() })).map(toAssignee);
  assigneeCache = { tenantId, at: Date.now(), items };
  return items;
}

/** Test seam: forget cached assignee names. */
export function resetConversationAssigneeCache(): void {
  assigneeCache = null;
}

async function write<T>(path: string, method: "POST" | "PUT", body?: unknown, extra?: Record<string, string>): Promise<T> {
  return apiFetch<T>(path, {
    method,
    body,
    headers: { ...tenantHeaders(), "X-CSRF-Token": await getCsrfToken(), ...extra },
  });
}

async function detail(id: string): Promise<Conversation> {
  const [item, people] = await Promise.all([
    apiFetch<WireConversationDetail>(`/v1/conversations/${encodeURIComponent(id)}`, { headers: tenantHeaders() }),
    assignees(),
  ]);
  return toConversationFromDetail(item, people);
}

async function mutate(id: string, path: string, method: "POST" | "PUT" = "POST", body?: unknown): Promise<Conversation> {
  const [item, people] = await Promise.all([
    write<WireConversationDetail>(`/v1/conversations/${encodeURIComponent(id)}/${path}`, method, body),
    assignees(),
  ]);
  return toConversationFromDetail(item, people);
}

const channelByConversation = new Map<string, Channel>();

export const apiConversationClient: ConversationClient = {
  async listConversations(params?: ConversationListParams): Promise<PaginatedResult<Conversation>> {
    const query = new URLSearchParams();
    query.set("page", String(params?.page ?? 1));
    query.set("pageSize", String(params?.pageSize ?? 20));
    if (params?.state) query.set("status", toApiStatus(params.state));
    if (params?.unreadOnly) query.set("unreadOnly", "true");
    if (params?.assignedTo) query.set("assignedTo", params.assignedTo);

    const [page, people] = await Promise.all([
      apiFetch<WirePage<WireConversationSummary>>(`/v1/conversations?${query}`, { headers: tenantHeaders() }),
      assignees(),
    ]);
    const current = Number(page.page) || 1;
    const size = Number(page.pageSize) || 20;
    const total = Number(page.totalCount) || 0;
    const hasMore = page.hasNextPage ?? current * size < total;
    const items = page.items.map((item) => toConversationFromSummary(item, people));
    items.forEach((c) => channelByConversation.set(c.id, c.channel));
    return { items, cursor: hasMore ? String(current + 1) : null, hasMore, totalCount: total };
  },

  async getConversation(id) {
    const conversation = await detail(id);
    channelByConversation.set(id, conversation.channel);
    return conversation;
  },

  async getMessages(conversationId, options) {
    const query = new URLSearchParams({ pageSize: "50" });
    if (options?.before) query.set("before", options.before);
    const [page, people] = await Promise.all([
      apiFetch<WireMessagePage>(`/v1/conversations/${encodeURIComponent(conversationId)}/messages?${query}`, { headers: tenantHeaders() }),
      assignees(),
    ]);
    const channel = channelByConversation.get(conversationId) ?? toChannel("instagram");
    return {
      items: page.items.map((m) => toMessage(m, channel, people)),
      cursor: page.nextBeforeMessageId,
      hasMore: page.nextBeforeMessageId !== null,
    };
  },

  async sendReply(conversationId, text, idempotencyKey) {
    const [item, people] = await Promise.all([
      write<WireMessage>(`/v1/conversations/${encodeURIComponent(conversationId)}/replies`, "POST", { text }, { "Idempotency-Key": idempotencyKey }),
      assignees(),
    ]);
    return toMessage(item, channelByConversation.get(conversationId) ?? "instagram", people);
  },

  markRead: (id) => mutate(id, "read"),
  takeOver: (id) => mutate(id, "takeover"),
  release: (id) => mutate(id, "release"),
  assign: (id, userId) => mutate(id, "assign", "POST", { userId }),
  unassign: (id) => mutate(id, "unassign"),
  setLabels: (id, labels) => mutate(id, "labels", "PUT", { labels }),
  changeStatus: (id, action) => mutate(id, "status", "POST", { action: toApiStatusAction(action) }),
  listAssignees: () => assignees(),
};
