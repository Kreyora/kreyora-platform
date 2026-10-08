import type { AssistantClient } from "@/lib/ports/assistant-client";
import type { AssistantPolicy, AssistantReadiness, AssistantUsage, KnowledgeDocument, KnowledgeVersionSummary } from "@/lib/types/assistant";
import { demoAssistantPolicy, demoAssistantTurns, demoKnowledge, demoKnowledgeText } from "../fixtures/data";

// Demo mode (M09-S08): in-memory only, nothing leaves the browser, and every reply is labelled as a demo.

const MOCK_DELAY_MS = 50;
const delay = () => new Promise<void>((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T;
export const DEMO_REPLY = "[Demo AI] This is a sample reply. Connect the API to try the real assistant — nothing here is sent to anyone.";

let policy: AssistantPolicy = clone(demoAssistantPolicy);
let knowledge: KnowledgeDocument[] = clone(demoKnowledge);
const texts: Record<string, string> = { ...demoKnowledgeText };
let sequence = 0;

/** Test seam: restore the demo data. */
export function resetMockAssistant(): void {
  policy = clone(demoAssistantPolicy);
  knowledge = clone(demoKnowledge);
  sequence = 0;
}

function readiness(): AssistantReadiness {
  const approved = knowledge.some((d) => d.activeVersion !== null);
  const checks = [
    { code: "store_ready", passed: true, required: true, detail: "Your store can take orders.", blockers: [] },
    { code: "channel_connected", passed: true, required: true, detail: "Instagram is connected (demo).", blockers: [] },
    { code: "policy_reviewed", passed: policy.reviewedAt !== null, required: true, detail: "Review and save the assistant settings.", blockers: [] },
    { code: "knowledge_approved", passed: approved, required: false, detail: "Approve at least one FAQ for better answers.", blockers: [] },
    { code: "ai_entitled", passed: true, required: true, detail: "The assistant is available for this shop (demo).", blockers: [] },
  ];
  const isActive = policy.enabled && checks.every((c) => !c.required || c.passed);
  return { isActive, sellerEnabled: policy.enabled, platformEnabled: true, checks };
}

function newVersion(text: string, number: number, fileName: string | null = null): KnowledgeVersionSummary {
  const id = `kv-demo-${++sequence}`;
  texts[id] = text;
  return { id, versionNumber: number, state: "pendingReview", characterCount: text.length, originalFileName: fileName, submittedAt: new Date().toISOString(), reviewedAt: null, reviewNote: null, hasSuspiciousInstructions: false };
}

function find(documentId: string): KnowledgeDocument {
  const document = knowledge.find((d) => d.id === documentId);
  if (!document) throw new Error("Document not found");
  return document;
}

export const mockAssistantClient: AssistantClient = {
  async getPolicy() { await delay(); return clone(policy); },
  async updatePolicy(update) {
    await delay();
    policy = { ...policy, ...clone(update), reviewedAt: new Date().toISOString(), version: `demo-${++sequence}` };
    return clone(policy);
  },
  async getReadiness() { await delay(); return readiness(); },

  async listKnowledge() { await delay(); return clone(knowledge); },
  async createKnowledge({ title, category, text }) {
    await delay();
    const document: KnowledgeDocument = { id: `kd-demo-${++sequence}`, title, category, source: "text", activeVersion: null, pendingVersions: [newVersion(text, 1)], latestVersionNumber: 1, createdAt: new Date().toISOString(), modifiedAt: new Date().toISOString(), indexStatus: null };
    knowledge = [...knowledge, document];
    return clone(document);
  },
  async uploadKnowledge({ title, category, file }) {
    await delay();
    const document: KnowledgeDocument = { id: `kd-demo-${++sequence}`, title, category, source: "upload", activeVersion: null, pendingVersions: [newVersion(`(demo) ${file.name}`, 1, file.name)], latestVersionNumber: 1, createdAt: new Date().toISOString(), modifiedAt: new Date().toISOString(), indexStatus: null };
    knowledge = [...knowledge, document];
    return clone(document);
  },
  async addKnowledgeVersion(documentId, text) {
    await delay();
    const document = find(documentId);
    document.latestVersionNumber += 1;
    document.pendingVersions = [...document.pendingVersions, newVersion(text, document.latestVersionNumber)];
    return clone(document);
  },
  async getKnowledgeVersion(documentId, versionId) {
    await delay();
    const document = find(documentId);
    const version = [document.activeVersion, ...document.pendingVersions].find((v) => v?.id === versionId);
    if (!version) throw new Error("Version not found");
    return { documentId, documentTitle: document.title, version: clone(version), text: texts[versionId] ?? null };
  },
  async approveKnowledgeVersion(documentId, versionId) {
    await delay();
    const document = find(documentId);
    const version = document.pendingVersions.find((v) => v.id === versionId);
    if (!version) throw new Error("Version not found");
    document.activeVersion = { ...version, state: "active", reviewedAt: new Date().toISOString() };
    document.pendingVersions = document.pendingVersions.filter((v) => v.id !== versionId);
    document.indexStatus = { chunks: 1, indexed: 1 };
    return clone(document);
  },
  async rejectKnowledgeVersion(documentId, versionId, note) {
    await delay();
    const document = find(documentId);
    document.pendingVersions = document.pendingVersions.filter((v) => v.id !== versionId);
    void note;
    return clone(document);
  },
  async deleteKnowledge(documentId) {
    await delay();
    knowledge = knowledge.filter((d) => d.id !== documentId);
  },
  async importStorePolicies() { await delay(); return []; },
  async searchKnowledge(query) {
    await delay();
    const words = query.toLowerCase().split(/\s+/).filter(Boolean);
    const passages = knowledge.filter((d) => d.activeVersion).map((d) => ({ d, text: texts[d.activeVersion!.id] ?? "" }))
      .filter(({ text }) => words.some((w) => text.toLowerCase().includes(w)))
      .map(({ d, text }) => ({ text, score: 0.7, documentId: d.id, documentTitle: d.title }));
    return { confidence: passages.length ? "high" : "none", mode: "lexicalFallback", passages };
  },

  async runPlayground() {
    await delay();
    return { turnId: `turn-demo-play-${++sequence}`, outcome: "replied", reasonCode: "playground", reply: DEMO_REPLY, tools: [], citations: 0, modelCalls: 0, inputTokens: 0, outputTokens: 0, estimatedCostUsd: 0, validationCodes: [] };
  },
  async listTurns(params) {
    await delay();
    const items = demoAssistantTurns.filter((t) => !params?.conversationId || t.conversationId === params.conversationId);
    return { items: clone(items), nextCursor: null };
  },
  async getUsage(days = 7) {
    await delay();
    const today = new Date();
    const daily = Array.from({ length: days }, (_, i) => {
      const date = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() - (days - 1 - i)));
      const turns = (i * 7) % 11 + 3;
      return { date: date.toISOString().slice(0, 10), turns, replied: turns - 2, handedOff: 1, skipped: 1, blocked: 0, modelCalls: turns * 2, inputTokens: turns * 4000, outputTokens: turns * 120, estimatedCostUsd: 0, playgroundTurns: i % 3 };
    });
    const usage: AssistantUsage = { days, daily, turnsToday: daily[daily.length - 1].turns, dailyTurnCap: 150, platformEnabled: true, nextResetAt: new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), today.getUTCDate() + 1)).toISOString() };
    return usage;
  },
};
