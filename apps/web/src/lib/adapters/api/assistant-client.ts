import { apiFetch } from "@/lib/api";
import type { components } from "@/lib/api/generated/v1";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { AssistantClient } from "@/lib/ports/assistant-client";
import type {
  AssistantPolicy,
  AssistantPolicyUpdate,
  AssistantReadiness,
  AssistantTurn,
  AssistantTurnOutcome,
  AssistantUsage,
  DailyHours,
  DayOfWeek,
  KnowledgeCategory,
  KnowledgeDocument,
  KnowledgeSearchResult,
  KnowledgeVersionDetail,
  KnowledgeVersionState,
  KnowledgeVersionSummary,
  PlaygroundResult,
} from "@/lib/types/assistant";

type Schemas = components["schemas"];
type WireEnum = string | number;
type WireNumber = number | string;

const tenantHeaders = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });

function newKey(): string {
  return typeof crypto !== "undefined" && crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

async function send<T>(path: string, method: "POST" | "PUT" | "DELETE", body?: unknown): Promise<T> {
  return apiFetch<T>(path, { method, body, headers: { ...tenantHeaders(), "X-CSRF-Token": await getCsrfToken(), "Idempotency-Key": newKey() } });
}

const get = <T,>(path: string) => apiFetch<T>(path, { headers: tenantHeaders() });

const num = (value: WireNumber | null | undefined): number => Number(value ?? 0) || 0;

/** The API writes enums as camelCase names; older or numeric forms are mapped through the table. */
function enumOf<T extends string>(value: WireEnum | null | undefined, byNumber: readonly T[], fallback: T): T {
  if (typeof value === "number") return byNumber[value - 1] ?? fallback;
  if (typeof value !== "string" || value.length === 0) return fallback;
  if (/^\d+$/.test(value)) return byNumber[Number(value) - 1] ?? fallback;
  const camel = (value.charAt(0).toLowerCase() + value.slice(1)) as T;
  return byNumber.includes(camel) ? camel : fallback;
}

const REPLY_STYLES = ["matchCustomer", "alwaysRomanized", "alwaysDevanagari", "alwaysEnglish"] as const;
const TONES = ["friendly", "formal"] as const;
const OUTSIDE_HOURS = ["answerNormally", "answerAndPromiseFollowUp", "doNotAnswer"] as const;
const MEDIA = ["askForDetails", "handToPerson"] as const;
const CATEGORIES: readonly KnowledgeCategory[] = ["faq", "delivery", "returns", "payment", "brand", "other"];
const VERSION_STATES: readonly KnowledgeVersionState[] = ["pendingReview", "active", "superseded", "rejected", "deleted"];
const OUTCOMES: readonly AssistantTurnOutcome[] = ["running", "replied", "escalated", "fallback", "skipped", "superseded", "blocked", "abandoned"];
// .NET DayOfWeek: Sunday = 0.
const DAYS: readonly DayOfWeek[] = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

function day(value: WireEnum): DayOfWeek {
  if (typeof value === "number") return DAYS[value] ?? "sunday";
  if (/^\d+$/.test(value)) return DAYS[Number(value)] ?? "sunday";
  const lower = value.toLowerCase() as DayOfWeek;
  return DAYS.includes(lower) ? lower : "sunday";
}

type WirePolicy = Omit<Schemas["AssistantPolicyItem"], "replyStyle" | "tone" | "outsideHoursBehavior" | "unrecognizedMediaBehavior" | "businessHours"> & {
  replyStyle: WireEnum;
  tone: WireEnum;
  outsideHoursBehavior: WireEnum;
  unrecognizedMediaBehavior: WireEnum;
  businessHours: { day: WireEnum; closed: boolean; opens?: string | null; closes?: string | null }[];
};

export function toPolicy(item: WirePolicy): AssistantPolicy {
  return {
    enabled: item.enabled,
    replyStyle: enumOf(item.replyStyle, REPLY_STYLES, "matchCustomer"),
    supportedLanguages: [...item.supportedLanguages],
    tone: enumOf(item.tone, TONES, "friendly"),
    brandNote: item.brandNote ?? null,
    businessHours: item.businessHours.map((h): DailyHours => ({ day: day(h.day), closed: h.closed, opens: h.opens ?? null, closes: h.closes ?? null })),
    timeZone: item.timeZone,
    outsideHoursBehavior: enumOf(item.outsideHoursBehavior, OUTSIDE_HOURS, "answerAndPromiseFollowUp"),
    unrecognizedMediaBehavior: enumOf(item.unrecognizedMediaBehavior, MEDIA, "askForDetails"),
    escalationKeywords: [...item.escalationKeywords],
    allowedTools: [...item.allowedTools],
    maxToolSteps: num(item.maxToolSteps),
    maxRepliesPerConversationPerHour: num(item.maxRepliesPerConversationPerHour),
    maxOutputTokens: num(item.maxOutputTokens),
    reviewedAt: item.reviewedAt ?? null,
    version: item.version,
    fixedEscalationCategories: [...item.fixedEscalationCategories],
    availableTools: [...item.availableTools],
    platformCaps: {
      maxToolSteps: num(item.platformCaps.maxToolSteps),
      maxRepliesPerConversationPerHour: num(item.platformCaps.maxRepliesPerConversationPerHour),
      maxOutputTokens: num(item.platformCaps.maxOutputTokens),
    },
  };
}

type WireVersion = Omit<Schemas["KnowledgeVersionSummary"], "state"> & { state: WireEnum };
type WireDocument = Omit<Schemas["KnowledgeDocumentItem"], "category" | "source" | "activeVersion" | "pendingVersions" | "storePolicyKind"> & {
  category: WireEnum;
  source: WireEnum;
  activeVersion?: WireVersion | null;
  pendingVersions: WireVersion[];
};

function toVersion(v: WireVersion): KnowledgeVersionSummary {
  return {
    id: v.id,
    versionNumber: num(v.versionNumber),
    state: enumOf(v.state, VERSION_STATES, "pendingReview"),
    characterCount: num(v.characterCount),
    originalFileName: v.originalFileName ?? null,
    submittedAt: v.submittedAt,
    reviewedAt: v.reviewedAt ?? null,
    reviewNote: v.reviewNote ?? null,
    hasSuspiciousInstructions: v.hasSuspiciousInstructions,
  };
}

export function toDocument(d: WireDocument): KnowledgeDocument {
  return {
    id: d.id,
    title: d.title,
    category: enumOf(d.category, CATEGORIES, "other"),
    source: enumOf(d.source, ["text", "upload", "storePolicy"] as const, "text"),
    activeVersion: d.activeVersion ? toVersion(d.activeVersion) : null,
    pendingVersions: d.pendingVersions.map(toVersion),
    latestVersionNumber: num(d.latestVersionNumber),
    createdAt: d.createdAt,
    modifiedAt: d.modifiedAt,
    indexStatus: d.indexStatus ? { chunks: num(d.indexStatus.chunks), indexed: num(d.indexStatus.indexed) } : null,
  };
}

type WireTurn = Omit<Schemas["AssistantTurnItem"], "outcome"> & { outcome: WireEnum };

export function toTurn(t: WireTurn): AssistantTurn {
  return {
    id: t.id,
    conversationId: t.conversationId ?? null,
    isPlayground: t.isPlayground,
    outcome: enumOf(t.outcome, OUTCOMES, "blocked"),
    reasonCode: t.reasonCode,
    startedAt: t.startedAt,
    finishedAt: t.finishedAt ?? null,
    promptVersion: t.promptVersion ?? null,
    registryVersion: t.registryVersion ?? null,
    policyVersion: t.policyVersion ?? null,
    models: [...new Set(t.modelCalls.map((c) => c.model).filter((m): m is string => !!m))],
    modelCalls: t.modelCalls.length,
    providerLatencyMs: t.modelCalls.reduce((sum, c) => sum + num(c.latencyMs), 0),
    tools: t.toolSteps.map((s) => ({ tool: s.tool, outcome: s.outcome, durationMs: num(s.durationMs), dryRun: s.dryRun, replayed: s.replayed })),
    citations: t.citations.length,
    validationCodes: [...t.validationCodes],
    inputTokens: num(t.inputTokens),
    outputTokens: num(t.outputTokens),
    estimatedCostUsd: num(t.estimatedCostUsd),
  };
}

type WirePlayground = Omit<Schemas["AssistantPlaygroundResult"], "outcome"> & { outcome: WireEnum };

export function toPlayground(r: WirePlayground): PlaygroundResult {
  return {
    turnId: r.turnId,
    outcome: enumOf(r.outcome, OUTCOMES, "blocked"),
    reasonCode: r.reasonCode,
    reply: r.reply ?? null,
    tools: r.tools.map((t) => ({ tool: t.tool, outcome: t.outcome, dryRun: t.dryRun })),
    citations: r.citations.length,
    modelCalls: num(r.modelCalls),
    inputTokens: num(r.inputTokens),
    outputTokens: num(r.outputTokens),
    estimatedCostUsd: num(r.estimatedCostUsd),
    validationCodes: [...r.validationCodes],
  };
}

export function toUsage(u: Schemas["AssistantUsageItem"]): AssistantUsage {
  return {
    days: num(u.days),
    daily: u.daily.map((d) => ({
      date: d.date,
      turns: num(d.turns),
      replied: num(d.replied),
      handedOff: num(d.handedOff),
      skipped: num(d.skipped),
      blocked: num(d.blocked),
      modelCalls: num(d.modelCalls),
      inputTokens: num(d.inputTokens),
      outputTokens: num(d.outputTokens),
      estimatedCostUsd: num(d.estimatedCostUsd),
      playgroundTurns: num(d.playgroundTurns),
    })),
    turnsToday: num(u.turnsToday),
    dailyTurnCap: num(u.dailyTurnCap),
    platformEnabled: u.platformEnabled,
    nextResetAt: u.nextResetAt,
  };
}

interface WireSearch {
  confidence: WireEnum;
  mode: WireEnum;
  passages: { text: string; score: WireNumber; citation: { documentId: string; documentTitle: string } }[];
}

function toSearch(r: WireSearch): KnowledgeSearchResult {
  return {
    confidence: enumOf(r.confidence, ["none", "low", "high"] as const, "none"),
    mode: enumOf(r.mode, ["hybrid", "lexicalFallback"] as const, "lexicalFallback"),
    passages: r.passages.map((p) => ({ text: p.text, score: num(p.score), documentId: p.citation.documentId, documentTitle: p.citation.documentTitle })),
  };
}

function toUpdateBody(u: AssistantPolicyUpdate) {
  return {
    ...u,
    businessHours: u.businessHours.map((h) => ({ ...h, opens: h.closed ? null : h.opens, closes: h.closed ? null : h.closes })),
  };
}

const doc = (documentId: string) => `/v1/assistant/knowledge/${encodeURIComponent(documentId)}`;

export const apiAssistantClient: AssistantClient = {
  getPolicy: async () => toPolicy(await get<WirePolicy>("/v1/assistant/policy")),
  updatePolicy: async (update) => toPolicy(await send<WirePolicy>("/v1/assistant/policy", "PUT", toUpdateBody(update))),
  getReadiness: () => get<AssistantReadiness>("/v1/assistant/readiness"),

  listKnowledge: async () => (await get<WireDocument[]>("/v1/assistant/knowledge")).map(toDocument),
  createKnowledge: async (input) => toDocument(await send<WireDocument>("/v1/assistant/knowledge", "POST", input)),
  async uploadKnowledge({ title, category, file }) {
    const form = new FormData();
    form.set("file", file);
    form.set("title", title);
    form.set("category", category);
    return toDocument(await send<WireDocument>("/v1/assistant/knowledge/upload", "POST", form));
  },
  addKnowledgeVersion: async (documentId, text) => toDocument(await send<WireDocument>(`${doc(documentId)}/versions`, "POST", { text })),
  async getKnowledgeVersion(documentId, versionId) {
    const detail = await get<Omit<KnowledgeVersionDetail, "version"> & { version: WireVersion }>(`${doc(documentId)}/versions/${encodeURIComponent(versionId)}`);
    return { ...detail, version: toVersion(detail.version), text: detail.text ?? null };
  },
  approveKnowledgeVersion: async (documentId, versionId) => toDocument(await send<WireDocument>(`${doc(documentId)}/versions/${encodeURIComponent(versionId)}/approve`, "POST")),
  rejectKnowledgeVersion: async (documentId, versionId, note) => toDocument(await send<WireDocument>(`${doc(documentId)}/versions/${encodeURIComponent(versionId)}/reject`, "POST", { note })),
  async deleteKnowledge(documentId) {
    await send<unknown>(doc(documentId), "DELETE");
  },
  importStorePolicies: async () => (await send<WireDocument[]>("/v1/assistant/knowledge/import-store-policies", "POST")).map(toDocument),
  searchKnowledge: async (query) => toSearch(await send<WireSearch>("/v1/assistant/knowledge/search", "POST", { query })),

  runPlayground: async (messages) => toPlayground(await send<WirePlayground>("/v1/assistant/playground", "POST", { messages })),
  async listTurns(params) {
    const query = new URLSearchParams({ pageSize: String(params?.pageSize ?? 25) });
    if (params?.conversationId) query.set("conversationId", params.conversationId);
    if (params?.cursor) query.set("cursor", params.cursor);
    const page = await get<{ items: WireTurn[]; nextCursor?: string | null }>(`/v1/assistant/turns?${query}`);
    return { items: page.items.map(toTurn), nextCursor: page.nextCursor ?? null };
  },
  getUsage: async (days = 7) => toUsage(await get<Schemas["AssistantUsageItem"]>(`/v1/assistant/usage?days=${days}`)),
};
