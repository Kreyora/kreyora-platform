import type { ConversationClient } from "@/lib/ports/conversation-client";
import type { PaginatedResult, Conversation, Message } from "@/lib/types";
import { conversations, messagesByConversationId } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockConversationClient: ConversationClient = {
  async listConversations(params) {
    await delay();

    let filtered = [...conversations];

    if (params?.state) {
      filtered = filtered.filter((c) => c.state === params.state);
    }

    if (params?.channel) {
      filtered = filtered.filter((c) => c.channel === params.channel);
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

    return toPaginated<Conversation>(filtered);
  },

  async getConversation(id: string) {
    await delay();
    const conversation = conversations.find((c) => c.id === id);
    if (!conversation) {
      throw new Error(`Conversation not found: ${id}`);
    }
    return conversation;
  },

  async getMessages(conversationId: string) {
    await delay();
    const messages = messagesByConversationId[conversationId] ?? [];
    return toPaginated<Message>(messages);
  },
};
