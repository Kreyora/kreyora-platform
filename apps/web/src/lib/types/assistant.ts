// M09-S08: seller assistant screens. Shapes follow the API (`/v1/assistant/*`), with enums normalized to camelCase strings.

export type AssistantReplyStyle = "matchCustomer" | "alwaysRomanized" | "alwaysDevanagari" | "alwaysEnglish";
export type AssistantTone = "friendly" | "formal";
export type OutsideHoursBehavior = "answerNormally" | "answerAndPromiseFollowUp" | "doNotAnswer";
export type UnrecognizedMediaBehavior = "askForDetails" | "handToPerson";
export type KnowledgeCategory = "faq" | "delivery" | "returns" | "payment" | "brand" | "other";
export type KnowledgeVersionState = "pendingReview" | "active" | "superseded" | "rejected" | "deleted";
export type AssistantTurnOutcome = "running" | "replied" | "escalated" | "fallback" | "skipped" | "superseded" | "blocked" | "abandoned";
export type DayOfWeek = "monday" | "tuesday" | "wednesday" | "thursday" | "friday" | "saturday" | "sunday";

export interface DailyHours {
  day: DayOfWeek;
  closed: boolean;
  /** "HH:mm" local shop time. */
  opens: string | null;
  closes: string | null;
}

export interface AssistantPolicy {
  enabled: boolean;
  replyStyle: AssistantReplyStyle;
  supportedLanguages: string[];
  tone: AssistantTone;
  brandNote: string | null;
  businessHours: DailyHours[];
  timeZone: string;
  outsideHoursBehavior: OutsideHoursBehavior;
  unrecognizedMediaBehavior: UnrecognizedMediaBehavior;
  escalationKeywords: string[];
  allowedTools: string[];
  maxToolSteps: number;
  maxRepliesPerConversationPerHour: number;
  maxOutputTokens: number;
  reviewedAt: string | null;
  /** Concurrency token: send it back on save; a stale one is refused (409). */
  version: string;
  fixedEscalationCategories: string[];
  availableTools: string[];
  platformCaps: { maxToolSteps: number; maxRepliesPerConversationPerHour: number; maxOutputTokens: number };
}

export type AssistantPolicyUpdate = Omit<AssistantPolicy, "timeZone" | "reviewedAt" | "fixedEscalationCategories" | "availableTools" | "platformCaps">;

export interface AssistantReadinessCheck {
  code: string;
  passed: boolean;
  required: boolean;
  detail: string;
  blockers: string[];
}

export interface AssistantReadiness {
  isActive: boolean;
  sellerEnabled: boolean;
  platformEnabled: boolean;
  checks: AssistantReadinessCheck[];
}

export interface KnowledgeVersionSummary {
  id: string;
  versionNumber: number;
  state: KnowledgeVersionState;
  characterCount: number;
  originalFileName: string | null;
  submittedAt: string;
  reviewedAt: string | null;
  reviewNote: string | null;
  hasSuspiciousInstructions: boolean;
}

export interface KnowledgeDocument {
  id: string;
  title: string;
  category: KnowledgeCategory;
  source: string;
  activeVersion: KnowledgeVersionSummary | null;
  pendingVersions: KnowledgeVersionSummary[];
  latestVersionNumber: number;
  createdAt: string;
  modifiedAt: string;
  indexStatus: { chunks: number; indexed: number } | null;
}

export interface KnowledgeVersionDetail {
  documentId: string;
  documentTitle: string;
  version: KnowledgeVersionSummary;
  text: string | null;
}

export interface KnowledgePassage {
  text: string;
  score: number;
  documentId: string;
  documentTitle: string;
}

export interface KnowledgeSearchResult {
  confidence: "none" | "low" | "high";
  mode: "hybrid" | "lexicalFallback";
  passages: KnowledgePassage[];
}

export interface PlaygroundMessage {
  from: "customer" | "shop";
  text: string;
}

export interface PlaygroundResult {
  turnId: string;
  outcome: AssistantTurnOutcome;
  reasonCode: string;
  reply: string | null;
  tools: { tool: string; outcome: string; dryRun: boolean }[];
  citations: number;
  modelCalls: number;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
  validationCodes: string[];
}

export interface AssistantTurn {
  id: string;
  conversationId: string | null;
  isPlayground: boolean;
  outcome: AssistantTurnOutcome;
  reasonCode: string;
  startedAt: string;
  finishedAt: string | null;
  promptVersion: string | null;
  registryVersion: string | null;
  policyVersion: string | null;
  models: string[];
  modelCalls: number;
  providerLatencyMs: number;
  tools: { tool: string; outcome: string; durationMs: number; dryRun: boolean; replayed: boolean }[];
  citations: number;
  validationCodes: string[];
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
}

export interface AssistantTurnPage {
  items: AssistantTurn[];
  nextCursor: string | null;
}

export interface AssistantUsageDay {
  date: string;
  turns: number;
  replied: number;
  handedOff: number;
  skipped: number;
  blocked: number;
  modelCalls: number;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
  playgroundTurns: number;
}

export interface AssistantUsage {
  days: number;
  daily: AssistantUsageDay[];
  turnsToday: number;
  dailyTurnCap: number;
  platformEnabled: boolean;
  nextResetAt: string;
}
