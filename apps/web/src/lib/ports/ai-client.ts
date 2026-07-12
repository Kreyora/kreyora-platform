import type { AssistantConfig, KnowledgeDocument, AIActionTrace, PaginatedResult } from "@/lib/types";

export interface AIClient {
  getAssistantConfig(tenantId: string): Promise<AssistantConfig>;
  listKnowledge(tenantId: string): Promise<PaginatedResult<KnowledgeDocument>>;
  getActionTraces(conversationId: string): Promise<AIActionTrace[]>;
}
