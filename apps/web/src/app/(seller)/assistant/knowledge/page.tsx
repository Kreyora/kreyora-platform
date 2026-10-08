"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useClients, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { ViewerBadge } from "@/components/viewer-badge";
import {
  AssistantHeader,
  CATEGORY_LABEL,
  ErrorNote,
  canEditAssistant,
  describeAssistantError,
  formatWhen,
  type ScreenError,
} from "@/components/assistant/assistant-display";
import type { KnowledgeCategory, KnowledgeDocument, KnowledgeSearchResult, KnowledgeVersionDetail } from "@/lib/types";

const field = "min-h-11 w-full rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm";
const CATEGORIES = Object.keys(CATEGORY_LABEL) as KnowledgeCategory[];

export default function KnowledgePage() {
  const { assistant } = useClients();
  const { effectiveRole } = useSession();
  const canEdit = canEditAssistant(effectiveRole);
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ScreenError | null>(null);
  const [review, setReview] = useState<KnowledgeVersionDetail | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      setDocuments(await assistant.listKnowledge());
    } catch (failure) {
      setError(describeAssistantError(failure));
    } finally {
      setLoading(false);
    }
  }, [assistant]);

  useEffect(() => {
    let cancelled = false;
    assistant.listKnowledge()
      .then((items) => { if (!cancelled) setDocuments(items); })
      .catch((failure: unknown) => { if (!cancelled) setError(describeAssistantError(failure)); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [assistant]);

  async function act(work: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await work();
      await load();
    } catch (failure) {
      setError(describeAssistantError(failure));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <AssistantHeader
        title="Knowledge"
        description="Your FAQ, delivery, returns and brand notes. The assistant only uses approved versions, and never for prices or stock — those always come from your shop data."
        actions={effectiveRole === "viewer" ? <ViewerBadge /> : undefined}
      />
      {USING_FIXTURE_ADAPTERS && <p role="note" className="mt-3 text-xs text-[var(--color-ink-secondary)]">Demo data — changes stay in this browser.</p>}
      {error && <ErrorNote error={error} onRetry={() => void load()} />}

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <section aria-labelledby="documents-title" className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 id="documents-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Documents</h2>
            {canEdit && (
              <Button size="sm" variant="ghost" className="ml-auto" disabled={busy} onClick={() => void act(() => assistant.importStorePolicies())}>
                Import store policies
              </Button>
            )}
          </div>
          {loading ? (
            <div className="mt-3 space-y-3" aria-busy="true" aria-label="Loading documents">
              {Array.from({ length: 2 }).map((_, i) => <Skeleton key={i} className="h-20 w-full rounded-[var(--radius-lg)]" />)}
            </div>
          ) : documents.length === 0 ? (
            <EmptyState title="No knowledge yet" description="Add your FAQ or import your store policies. Approve a version to let the assistant use it." />
          ) : (
            <ul className="mt-3 space-y-3" aria-label="Knowledge documents">
              {documents.map((document) => (
                <li key={document.id} className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-medium text-[var(--color-ink-primary)]">{document.title}</span>
                    <Badge variant="neutral">{CATEGORY_LABEL[document.category]}</Badge>
                    {document.activeVersion ? <Badge variant="success">In use · v{document.activeVersion.versionNumber}</Badge> : <Badge variant="neutral">Not in use</Badge>}
                    {document.pendingVersions.length > 0 && <Badge variant="warning">{document.pendingVersions.length} waiting for review</Badge>}
                  </div>
                  <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
                    Updated {formatWhen(document.modifiedAt)}
                    {document.indexStatus && ` · search ready ${document.indexStatus.indexed}/${document.indexStatus.chunks}`}
                  </p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {document.pendingVersions.map((version) => (
                      <Button key={version.id} size="sm" variant="outline" disabled={busy} onClick={() => void act(async () => setReview(await assistant.getKnowledgeVersion(document.id, version.id)))}>
                        Review v{version.versionNumber}{version.hasSuspiciousInstructions ? " (check wording)" : ""}
                      </Button>
                    ))}
                    {document.activeVersion && (
                      <Button size="sm" variant="ghost" disabled={busy} onClick={() => void act(async () => setReview(await assistant.getKnowledgeVersion(document.id, document.activeVersion!.id)))}>
                        View v{document.activeVersion.versionNumber}
                      </Button>
                    )}
                    {canEdit && (
                      <Button
                        size="sm"
                        variant="ghost"
                        disabled={busy}
                        onClick={() => { if (window.confirm(`Delete "${document.title}"? The assistant stops using it at once.`)) void act(() => assistant.deleteKnowledge(document.id)); }}
                      >
                        Delete
                      </Button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>

        <div className="min-w-0 space-y-6">
          {canEdit && <AddKnowledge busy={busy} onAdd={(input) => act(() => (input.file ? assistant.uploadKnowledge({ ...input, file: input.file }) : assistant.createKnowledge(input)))} />}
          <SearchTest onSearch={(query) => assistant.searchKnowledge(query)} />
        </div>
      </div>

      {review && (
        <ReviewDialog
          detail={review}
          canEdit={canEdit && review.version.state === "pendingReview"}
          busy={busy}
          onClose={() => setReview(null)}
          onApprove={() => void act(async () => { await assistant.approveKnowledgeVersion(review.documentId, review.version.id); setReview(null); })}
          onReject={(note) => void act(async () => { await assistant.rejectKnowledgeVersion(review.documentId, review.version.id, note); setReview(null); })}
        />
      )}
    </div>
  );
}

function AddKnowledge({ busy, onAdd }: { busy: boolean; onAdd: (input: { title: string; category: KnowledgeCategory; text: string; file: File | null }) => Promise<void> }) {
  const [title, setTitle] = useState("");
  const [category, setCategory] = useState<KnowledgeCategory>("faq");
  const [text, setText] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [invalid, setInvalid] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!title.trim() || (!text.trim() && !file)) {
      setInvalid("Give it a title and either paste text or choose a file.");
      return;
    }

    setInvalid(null);
    await onAdd({ title: title.trim(), category, text: text.trim(), file });
    setTitle("");
    setText("");
    setFile(null);
  }

  return (
    <form onSubmit={submit} className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-labelledby="add-title">
      <h2 id="add-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Add knowledge</h2>
      <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">New text waits for your approval before the assistant uses it.</p>
      <label className="mt-3 block text-sm">Title<input className={`${field} mt-1`} value={title} maxLength={120} onChange={(e) => setTitle(e.target.value)} /></label>
      <label className="mt-3 block text-sm">
        Category
        <select className={`${field} mt-1`} value={category} onChange={(e) => setCategory(e.target.value as KnowledgeCategory)}>
          {CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_LABEL[c]}</option>)}
        </select>
      </label>
      <label className="mt-3 block text-sm">Text<textarea className={`${field} mt-1 min-h-28 py-2`} value={text} onChange={(e) => setText(e.target.value)} disabled={!!file} /></label>
      <label className="mt-3 block text-sm">
        Or upload a text, Markdown or PDF file
        <input type="file" accept=".txt,.md,.pdf,text/plain,text/markdown,application/pdf" className="mt-1 block min-h-11 text-sm" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
      </label>
      {invalid && <p role="alert" className="mt-2 text-sm text-[var(--color-danger)]">{invalid}</p>}
      <Button type="submit" className="mt-3" disabled={busy}>Submit for review</Button>
    </form>
  );
}

function SearchTest({ onSearch }: { onSearch: (query: string) => Promise<KnowledgeSearchResult> }) {
  const [query, setQuery] = useState("");
  const [result, setResult] = useState<KnowledgeSearchResult | null>(null);
  const [error, setError] = useState<ScreenError | null>(null);
  const [searching, setSearching] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!query.trim()) return;
    setSearching(true);
    setError(null);
    try {
      setResult(await onSearch(query.trim()));
    } catch (failure) {
      setError(describeAssistantError(failure));
    } finally {
      setSearching(false);
    }
  }

  return (
    <form onSubmit={submit} className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-labelledby="search-title">
      <h2 id="search-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Try a question</h2>
      <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">See which approved passages the assistant would read. Use made-up questions.</p>
      <div className="mt-3 flex gap-2">
        <label htmlFor="knowledge-query" className="sr-only">Question</label>
        <input id="knowledge-query" className={field} value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Return garna milcha?" />
        <Button type="submit" variant="outline" disabled={searching}>Search</Button>
      </div>
      {error && <ErrorNote error={error} />}
      {result && (
        <div className="mt-3" aria-live="polite">
          <p className="text-xs text-[var(--color-ink-secondary)]">
            {result.passages.length === 0 ? "Nothing relevant — the assistant would say a team member will confirm." : `Confidence: ${result.confidence}`}
            {result.mode === "lexicalFallback" && " · keyword match only"}
          </p>
          <ul className="mt-2 space-y-2">
            {result.passages.map((p, i) => (
              <li key={`${p.documentId}-${i}`} className="rounded-[var(--radius-md)] bg-[var(--color-canvas-subtle)] p-3 text-sm">
                <p className="text-xs font-medium text-[var(--color-ink-secondary)]">{p.documentTitle} · {p.score.toFixed(2)}</p>
                <p className="mt-1 whitespace-pre-wrap text-[var(--color-ink-primary)]">{p.text}</p>
              </li>
            ))}
          </ul>
        </div>
      )}
    </form>
  );
}

function ReviewDialog({ detail, canEdit, busy, onClose, onApprove, onReject }: {
  detail: KnowledgeVersionDetail;
  canEdit: boolean;
  busy: boolean;
  onClose: () => void;
  onApprove: () => void;
  onReject: (note: string | null) => void;
}) {
  const [note, setNote] = useState("");
  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent className="max-h-[90vh] w-[calc(100vw-2rem)] max-w-2xl overflow-y-auto" closeLabel="Close review">
        <DialogTitle>{detail.documentTitle} · v{detail.version.versionNumber}</DialogTitle>
        <DialogDescription>{canEdit ? "Approve to let the assistant use this text, or reject it." : "Read-only view of this version."}</DialogDescription>
        {detail.version.hasSuspiciousInstructions && (
          <p role="status" className="mt-2 rounded-[var(--radius-md)] border border-[var(--color-warning)] p-2 text-sm">This text contains wording that looks like instructions to the assistant. It is always treated as plain information, but check it before approving.</p>
        )}
        <pre className="mt-4 whitespace-pre-wrap rounded-[var(--radius-md)] bg-[var(--color-canvas-subtle)] p-3 text-sm text-[var(--color-ink-primary)]">{detail.text ?? "(text not available)"}</pre>
        {canEdit && (
          <label className="mt-4 block text-sm">Note if rejecting (optional)<input className={`${field} mt-1`} value={note} maxLength={300} onChange={(e) => setNote(e.target.value)} /></label>
        )}
        <div className="mt-4 flex flex-wrap gap-2">
          {canEdit && <Button disabled={busy} onClick={onApprove}>Approve and use</Button>}
          {canEdit && <Button variant="outline" disabled={busy} onClick={() => onReject(note.trim() || null)}>Reject</Button>}
        </div>
      </DialogContent>
    </Dialog>
  );
}
