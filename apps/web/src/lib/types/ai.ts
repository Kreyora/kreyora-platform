import type { TenantId, Timestamp } from "./common";

export type EscalationState = "none" | "pending" | "escalated" | "resolved";

export type KnowledgeDocumentStatus = "pending_review" | "approved" | "rejected" | "archived";

export interface AssistantConfig {
  tenantId: TenantId;
  isEnabled: boolean;
  language: string;
  tone: string;
  maxToolIterations: number;
  costBudgetPerConversation?: number;
  autoEscalateOnLowConfidence: boolean;
  updatedAt: Timestamp;
}

export interface KnowledgeDocument {
  id: string;
  tenantId: TenantId;
  title: string;
  fileName: string;
  fileType: string;
  status: KnowledgeDocumentStatus;
  chunkCount: number;
  uploadedBy: string;
  approvedBy?: string;
  createdAt: Timestamp;
  updatedAt: Timestamp;
}

export type AIToolName =
  | "SearchProducts"
  | "CheckInventory"
  | "GetPrice"
  | "GetShippingInfo"
  | "GetOrderStatus"
  | "QuoteCart"
  | "CreateOrderDraft"
  | "ReserveInventory"
  | "ReleaseReservation"
  | "CreateCheckoutLink"
  | "EscalateToHuman";

export interface AIToolCall {
  tool: AIToolName;
  input: Record<string, unknown>;
  output: Record<string, unknown>;
  durationMs: number;
}

export interface AIActionTrace {
  id: string;
  tenantId: TenantId;
  conversationId: string;
  intent: string;
  toolCalls: AIToolCall[];
  responseGenerated: string;
  confidenceScore: number;
  escalationState: EscalationState;
  tokenCount: number;
  costBand: "low" | "medium" | "high";
  latencyMs: number;
  createdAt: Timestamp;
}
