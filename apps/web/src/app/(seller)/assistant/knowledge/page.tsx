"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { ViewerBadge } from "@/components/viewer-badge";
import type { KnowledgeDocument } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const DOC_STATUS: Record<string, { label: string; variant: BadgeVariant }> = {
  pending_review: { label: "Pending Review", variant: "warning" },
  approved: { label: "Approved", variant: "success" },
  rejected: { label: "Rejected", variant: "danger" },
  archived: { label: "Archived", variant: "neutral" },
};

const NAV_ITEMS = [
  { label: "Overview", href: "/assistant" },
  { label: "Knowledge", href: "/assistant/knowledge" },
  { label: "Console", href: "/assistant/console" },
  { label: "History", href: "/assistant/history" },
];

export default function KnowledgeDocumentsPage() {
  const { ai } = useClients();
  const { effectiveRole } = useSession();
  const isViewer = effectiveRole === "viewer";
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [localStatuses, setLocalStatuses] = useState<Record<string, string>>({});

  useEffect(() => {
    ai.listKnowledge(DEMO_TENANT_ID).then((result) => {
      setDocuments(result.items);
      setIsLoading(false);
    });
  }, [ai]);

  const handleAction = (docId: string, newStatus: string) => {
    setLocalStatuses((prev) => ({ ...prev, [docId]: newStatus }));
  };

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-40" />
        <div className="mt-6 space-y-3">
          {Array.from({ length: 2 }).map((_, i) => (
            <Skeleton key={i} className="h-20 w-full rounded-[var(--radius-lg)]" />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">AI Assistant</h1>
        {isViewer && <ViewerBadge />}
      </div>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Assistant navigation">
        {NAV_ITEMS.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${
              item.href === "/assistant/knowledge"
                ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]"
                : "border-transparent text-[var(--color-ink-secondary)]"
            }`}
          >
            {item.label}
          </Link>
        ))}
      </nav>

      <div className="mt-6 flex items-center justify-between">
        <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">Knowledge Documents</h2>
        {!isViewer && (
          <Button variant="outline" disabled>
            Upload Document
          </Button>
        )}
      </div>

      {documents.length === 0 ? (
        <div className="mt-6">
          <EmptyState title="No documents" description="Upload knowledge documents for the AI assistant." />
        </div>
      ) : (
        <div className="mt-4 flex flex-col gap-3">
          {documents.map((doc) => {
            const effectiveStatus = localStatuses[doc.id] ?? doc.status;
            const ds = DOC_STATUS[effectiveStatus] ?? { label: effectiveStatus, variant: "neutral" as const };
            return (
              <div key={doc.id} className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium text-[var(--color-ink-primary)]">{doc.title}</p>
                    <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
                      {doc.fileName} · {doc.fileType} · {doc.chunkCount} chunks
                    </p>
                  </div>
                  <Badge variant={ds.variant}>{ds.label}</Badge>
                </div>
                <div className="mt-3 flex items-center justify-between text-xs text-[var(--color-ink-secondary)]">
                  <span>Uploaded by {doc.uploadedBy}{doc.approvedBy ? ` · Approved by ${doc.approvedBy}` : ""}</span>
                  <span>{new Date(doc.updatedAt).toLocaleDateString()}</span>
                </div>
                {!isViewer && effectiveStatus !== "archived" && (
                  <div className="mt-3 flex gap-2">
                    {effectiveStatus === "pending_review" && (
                      <>
                        <Button size="sm" variant="outline" onClick={() => handleAction(doc.id, "approved")}>
                          Approve
                        </Button>
                        <Button size="sm" variant="ghost" onClick={() => handleAction(doc.id, "rejected")}>
                          Reject
                        </Button>
                      </>
                    )}
                    {(effectiveStatus === "approved" || effectiveStatus === "rejected") && (
                      <Button size="sm" variant="ghost" onClick={() => handleAction(doc.id, "archived")}>
                        Archive
                      </Button>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        Document lifecycle actions are simulated. No real changes are persisted.
      </p>
    </div>
  );
}
