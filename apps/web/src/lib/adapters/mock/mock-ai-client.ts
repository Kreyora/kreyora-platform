import type { AIClient } from "@/lib/ports/ai-client";
import type { PaginatedResult, KnowledgeDocument } from "@/lib/types";
import { assistantConfig, knowledgeDocuments, aiActionTraces } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockAIClient: AIClient = {
  async getAssistantConfig(_tenantId: string) {
    await delay();
    return assistantConfig;
  },

  async listKnowledge(_tenantId: string) {
    await delay();
    return toPaginated<KnowledgeDocument>(knowledgeDocuments);
  },

  async getActionTraces(conversationId: string) {
    await delay();
    return aiActionTraces.filter((t) => t.conversationId === conversationId);
  },
};
