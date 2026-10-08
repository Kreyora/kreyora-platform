"use client";

import { useCallback, useEffect, useState } from "react";
import { useClients, USING_FIXTURE_ADAPTERS } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import { AssistantHeader, ErrorNote, canEditAssistant, describeAssistantError, type ScreenError } from "@/components/assistant/assistant-display";
import { ReadinessCard } from "@/components/assistant/readiness-card";
import { UsageCard } from "@/components/assistant/usage-card";
import { PolicyForm } from "@/components/assistant/policy-form";
import type { AssistantPolicy, AssistantReadiness, AssistantUsage } from "@/lib/types";

export default function AssistantOverviewPage() {
  const { assistant } = useClients();
  const { effectiveRole } = useSession();
  const [policy, setPolicy] = useState<AssistantPolicy | null>(null);
  const [readiness, setReadiness] = useState<AssistantReadiness | null>(null);
  const [usage, setUsage] = useState<AssistantUsage | null>(null);
  const [error, setError] = useState<ScreenError | null>(null);
  const [loading, setLoading] = useState(true);
  const [formKey, setFormKey] = useState(0);

  const fetchAll = useCallback(() => Promise.all([assistant.getPolicy(), assistant.getReadiness(), assistant.getUsage(7)]), [assistant]);

  const apply = useCallback(([p, r, u]: [AssistantPolicy, AssistantReadiness, AssistantUsage]) => {
    setPolicy(p);
    setReadiness(r);
    setUsage(u);
    setError(null);
    setFormKey((k) => k + 1);
  }, []);

  useEffect(() => {
    let cancelled = false;
    fetchAll()
      .then((result) => { if (!cancelled) apply(result); })
      .catch((failure: unknown) => { if (!cancelled) setError(describeAssistantError(failure)); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [apply, fetchAll]);

  const load = async () => {
    setLoading(true);
    try {
      apply(await fetchAll());
    } catch (failure) {
      setError(describeAssistantError(failure));
    } finally {
      setLoading(false);
    }
  };

  const save = async (update: Parameters<typeof assistant.updatePolicy>[0]) => {
    const next = await assistant.updatePolicy(update);
    setPolicy(next);
    setReadiness(await assistant.getReadiness());
    return next;
  };

  return (
    <div>
      <AssistantHeader
        title="Assistant"
        description="Answers customer messages from your shop's real catalog, stock, prices and delivery rules — and hands chats to your team when needed."
        actions={effectiveRole === "viewer" ? <ViewerBadge /> : undefined}
      />
      {USING_FIXTURE_ADAPTERS && <p role="note" className="mt-3 text-xs text-[var(--color-ink-secondary)]">Demo data — changes stay in this browser and nothing is sent.</p>}

      {loading && !policy ? (
        <div className="mt-6 space-y-4" aria-busy="true" aria-label="Loading assistant">
          <Skeleton className="h-32 w-full rounded-[var(--radius-lg)]" />
          <Skeleton className="h-48 w-full rounded-[var(--radius-lg)]" />
        </div>
      ) : error && !policy ? (
        <ErrorNote error={error} onRetry={() => void load()} />
      ) : policy && readiness && usage ? (
        <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
          <div className="min-w-0 space-y-6">
            <ReadinessCard readiness={readiness} />
            <UsageCard usage={usage} />
          </div>
          <PolicyForm key={formKey} policy={policy} canEdit={canEditAssistant(effectiveRole)} onSave={save} onRefresh={() => void load()} />
        </div>
      ) : null}
    </div>
  );
}
