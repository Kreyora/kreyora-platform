import type { Conversation, Message, PaginatedResult } from "@/lib/types";

export interface ConversationClient {
  listConversations(params?: {
    state?: string;
    channel?: string;
    search?: string;
    cursor?: string;
  }): Promise<PaginatedResult<Conversation>>;
  getConversation(id: string): Promise<Conversation>;
  getMessages(conversationId: string): Promise<PaginatedResult<Message>>;
}
