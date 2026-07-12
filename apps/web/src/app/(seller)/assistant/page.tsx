"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { useSession } from "@/hooks/use-session";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { ViewerBadge } from "@/components/viewer-badge";
import type { AssistantConfig } from "@/lib/types";

const DEMO_TENANT_ID = "tenant-namaste-crafts";

const NAV_ITEMS = [
  { label: "Overview", href: "/assistant" },
  { label: "Knowledge", href: "/assistant/knowledge" },
  { label: "Console", href: "/assistant/console" },
  { label: "History", href: "/assistant/history" },
];

export default function AssistantPolicyPage() {
  const { ai } = useClients();
  const { effectiveRole } = useSession();
  const [config, setConfig] = useState<AssistantConfig | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    ai.getAssistantConfig(DEMO_TENANT_ID).then((c) => {
      setConfig(c);
      setIsLoading(false);
    });
  }, [ai]);

  if (isLoading || !config) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-40" />
        <Skeleton className="mt-6 h-48 w-full rounded-[var(--radius-lg)]" />
      </div>
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">AI Assistant</h1>
        {effectiveRole === "viewer" && <ViewerBadge />}
      </div>

      {/* Sub-navigation */}
      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Assistant navigation">
        {NAV_ITEMS.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${
              item.href === "/assistant"
                ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]"
                : "border-transparent text-[var(--color-ink-secondary)]"
            }`}
          >
            {item.label}
          </Link>
        ))}
      </nav>

      {/* Config display */}
      <div className="mt-8 space-y-6">
        <section>
          <h2 className="mb-4 text-base font-semibold text-[var(--color-ink-primary)]">Configuration</h2>
          <div className="grid gap-4 sm:grid-cols-2">
            <ConfigCard label="Status" value={config.isEnabled ? "Enabled" : "Disabled"}>
              <Badge variant={config.isEnabled ? "success" : "neutral"}>
                {config.isEnabled ? "Active" : "Inactive"}
              </Badge>
            </ConfigCard>
            <ConfigCard label="Language" value={config.language} />
            <ConfigCard label="Tone" value={config.tone} />
            <ConfigCard label="Max tool iterations" value={String(config.maxToolIterations)} />
            <ConfigCard
              label="Cost budget / conversation"
              value={config.costBudgetPerConversation ? `$${config.costBudgetPerConversation.toFixed(2)}` : "No limit"}
            />
            <ConfigCard label="Auto-escalate on low confidence" value={config.autoEscalateOnLowConfidence ? "Yes" : "No"}>
              <Badge variant={config.autoEscalateOnLowConfidence ? "info" : "neutral"}>
                {config.autoEscalateOnLowConfidence ? "Enabled" : "Disabled"}
              </Badge>
            </ConfigCard>
          </div>
        </section>

        <p className="text-xs text-[var(--color-ink-secondary)]">
          Last updated: {new Date(config.updatedAt).toLocaleDateString()}
        </p>
        <p className="text-[10px] text-[var(--color-ink-secondary)]">
          Configuration changes are simulated. No real AI settings are modified.
        </p>
      </div>
    </div>
  );
}

function ConfigCard({ label, value, children }: { label: string; value: string; children?: React.ReactNode }) {
  return (
    <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] px-5 py-4">
      <p className="text-xs text-[var(--color-ink-secondary)]">{label}</p>
      <div className="mt-1 flex items-center gap-2">
        <span className="text-sm font-medium text-[var(--color-ink-primary)]">{value}</span>
        {children}
      </div>
    </div>
  );
}
