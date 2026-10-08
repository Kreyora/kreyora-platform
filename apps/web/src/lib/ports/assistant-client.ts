import type {
  AssistantPolicy,
  AssistantPolicyUpdate,
  AssistantReadiness,
  AssistantTurnPage,
  AssistantUsage,
  KnowledgeCategory,
  KnowledgeDocument,
  KnowledgeSearchResult,
  KnowledgeVersionDetail,
  PlaygroundMessage,
  PlaygroundResult,
} from "@/lib/types/assistant";

/**
 * Seller assistant port (M09-S08). The API adapter talks to `/v1/assistant/*`; the demo adapter keeps state in memory.
 * The server authorizes every call and is the source of truth for readiness, usage and turn outcomes.
 */
export interface AssistantClient {
  getPolicy(): Promise<AssistantPolicy>;
  /** Saving also marks the policy reviewed. A stale `version` is refused with 409. */
  updatePolicy(update: AssistantPolicyUpdate): Promise<AssistantPolicy>;
  getReadiness(): Promise<AssistantReadiness>;

  listKnowledge(): Promise<KnowledgeDocument[]>;
  createKnowledge(input: { title: string; category: KnowledgeCategory; text: string }): Promise<KnowledgeDocument>;
  uploadKnowledge(input: { title: string; category: KnowledgeCategory; file: File }): Promise<KnowledgeDocument>;
  addKnowledgeVersion(documentId: string, text: string): Promise<KnowledgeDocument>;
  getKnowledgeVersion(documentId: string, versionId: string): Promise<KnowledgeVersionDetail>;
  approveKnowledgeVersion(documentId: string, versionId: string): Promise<KnowledgeDocument>;
  rejectKnowledgeVersion(documentId: string, versionId: string, note: string | null): Promise<KnowledgeDocument>;
  deleteKnowledge(documentId: string): Promise<void>;
  importStorePolicies(): Promise<KnowledgeDocument[]>;
  searchKnowledge(query: string): Promise<KnowledgeSearchResult>;

  /** Owner playground: made-up messages through the real assistant; writes are dry-run and nothing is sent. */
  runPlayground(messages: PlaygroundMessage[]): Promise<PlaygroundResult>;
  listTurns(params?: { conversationId?: string; cursor?: string; pageSize?: number }): Promise<AssistantTurnPage>;
  getUsage(days?: number): Promise<AssistantUsage>;
}
