"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useClients } from "@/lib/providers/client-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import type { AIActionTrace } from "@/lib/types";
import type { BadgeVariant } from "@/components/ui/badge";

const FIXTURE_CONV_ID = "conv-facebook-001";

const ESCALATION_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  none: { label: "None", variant: "neutral" },
  pending: { label: "Pending", variant: "warning" },
  escalated: { label: "Escalated", variant: "danger" },
  resolved: { label: "Resolved", variant: "success" },
};

const COST_MAP: Record<string, { label: string; variant: BadgeVariant }> = {
  low: { label: "Low", variant: "success" },
  medium: { label: "Medium", variant: "warning" },
  high: { label: "High", variant: "danger" },
};

const NAV_ITEMS = [
  { label: "Overview", href: "/assistant" },
  { label: "Knowledge", href: "/assistant/knowledge" },
  { label: "Console", href: "/assistant/console" },
  { label: "History", href: "/assistant/history" },
];

interface ChatMessage {
  role: "user" | "assistant";
  content: string;
}

export default function TestConsolePage() {
  const { ai } = useClients();
  const [traces, setTraces] = useState<AIActionTrace[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [inputText, setInputText] = useState("");
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([]);
  const [showTrace, setShowTrace] = useState(false);
  const [responding, setResponding] = useState(false);

  useEffect(() => {
    ai.getActionTraces(FIXTURE_CONV_ID).then((t) => {
      setTraces(t);
      setIsLoading(false);
    });
  }, [ai]);

  const handleSend = () => {
    if (!inputText.trim() || responding) return;
    const userMsg: ChatMessage = { role: "user", content: inputText.trim() };
    setChatMessages((prev) => [...prev, userMsg]);
    setInputText("");
    setResponding(true);

    setTimeout(() => {
      const trace = traces[0];
      const botReply: ChatMessage = {
        role: "assistant",
        content: trace
          ? trace.responseGenerated
          : "I can help you with product information, pricing, and orders. This is a simulated response using fixture data.",
      };
      setChatMessages((prev) => [...prev, botReply]);
      setResponding(false);
      setShowTrace(true);
    }, 800);
  };

  if (isLoading) {
    return (
      <div>
        <Skeleton className="mb-2 h-8 w-40" />
        <Skeleton className="mt-6 h-96 w-full rounded-[var(--radius-lg)]" />
      </div>
    );
  }

  const activeTrace = traces[0] ?? null;

  return (
    <div>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-heading-page text-[var(--color-ink-primary)]">AI Assistant</h1>
      </div>

      <nav className="mt-4 flex gap-1 border-b border-[var(--color-border)]" aria-label="Assistant navigation">
        {NAV_ITEMS.map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={`inline-flex min-h-11 items-center border-b-2 px-[var(--space-4)] text-sm font-medium transition-colors hover:text-[var(--color-ink-primary)] ${
              item.href === "/assistant/console"
                ? "border-[var(--color-ink-primary)] text-[var(--color-ink-primary)]"
                : "border-transparent text-[var(--color-ink-secondary)]"
            }`}
          >
            {item.label}
          </Link>
        ))}
      </nav>

      <h2 className="mt-6 text-base font-semibold text-[var(--color-ink-primary)]">Test Console</h2>

      <div className="mt-4 grid gap-6 lg:grid-cols-3">
        {/* Chat area */}
        <div className="lg:col-span-2">
          <div className="min-h-[300px] rounded-[var(--radius-lg)] border border-[var(--color-border)] p-4">
            {chatMessages.length === 0 && !responding && (
              <p className="text-center text-sm text-[var(--color-ink-secondary)]">
                Send a message to test the AI assistant. Responses use fixture data.
              </p>
            )}
            <div className="flex flex-col gap-3">
              {chatMessages.map((msg, i) => (
                <div
                  key={i}
                  className={`max-w-[80%] rounded-[var(--radius-lg)] px-4 py-3 text-sm ${
                    msg.role === "user"
                      ? "ml-auto bg-[var(--color-surface-dark)] text-[var(--color-on-dark)]"
                      : "mr-auto border border-[var(--color-border)] bg-[var(--color-canvas)] text-[var(--color-ink-primary)]"
                  }`}
                >
                  {msg.content}
                </div>
              ))}
              {responding && (
                <div className="mr-auto rounded-[var(--radius-lg)] border border-[var(--color-border)] px-4 py-3 text-sm text-[var(--color-ink-secondary)]">
                  Thinking...
                </div>
              )}
            </div>
          </div>

          <div className="mt-3 flex gap-2">
            <Input
              placeholder="Type a test message..."
              value={inputText}
              onChange={(e) => setInputText(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); handleSend(); } }}
              aria-label="Test console input"
              className="flex-1"
            />
            <Button onClick={handleSend} disabled={!inputText.trim() || responding}>
              Send
            </Button>
          </div>
        </div>

        {/* Tool trace sidebar */}
        <div className="lg:self-start">
          {showTrace && activeTrace && (
            <div className="rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5">
              <h3 className="text-sm font-semibold text-[var(--color-ink-primary)]">Tool Trace</h3>

              <div className="mt-3 space-y-2 text-sm">
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Intent</span>
                  <span className="text-[var(--color-ink-primary)]">{activeTrace.intent}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Confidence</span>
                  <span className="text-[var(--color-ink-primary)]">{(activeTrace.confidenceScore * 100).toFixed(0)}%</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Tokens</span>
                  <span className="text-[var(--color-ink-primary)]">{activeTrace.tokenCount}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Cost</span>
                  <Badge variant={COST_MAP[activeTrace.costBand]?.variant ?? "neutral"}>
                    {COST_MAP[activeTrace.costBand]?.label ?? activeTrace.costBand}
                  </Badge>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Latency</span>
                  <span className="text-[var(--color-ink-primary)]">{activeTrace.latencyMs}ms</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-[var(--color-ink-secondary)]">Escalation</span>
                  <Badge variant={ESCALATION_MAP[activeTrace.escalationState]?.variant ?? "neutral"}>
                    {ESCALATION_MAP[activeTrace.escalationState]?.label ?? activeTrace.escalationState}
                  </Badge>
                </div>
              </div>

              {/* Tool calls */}
              <h4 className="mt-4 text-xs font-semibold text-[var(--color-ink-primary)]">
                Tool Calls ({activeTrace.toolCalls.length})
              </h4>
              <div className="mt-2 space-y-2">
                {activeTrace.toolCalls.map((tc, i) => (
                  <div key={i} className="rounded-[var(--radius-md)] border border-[var(--color-border)] p-3">
                    <p className="text-xs font-medium text-[var(--color-ink-primary)]">{tc.tool}</p>
                    <p className="mt-1 text-[10px] text-[var(--color-ink-secondary)]">{tc.durationMs}ms</p>
                    <div className="mt-1 rounded bg-[var(--color-canvas-subtle)] p-2 text-[10px]">
                      <p className="text-[var(--color-ink-secondary)]">
                        In: {JSON.stringify(tc.input)}
                      </p>
                      <p className="text-[var(--color-ink-secondary)]">
                        Out: {JSON.stringify(tc.output)}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {!showTrace && (
            <div className="rounded-[var(--radius-lg)] border border-dashed border-[var(--color-border)] p-5 text-center">
              <p className="text-xs text-[var(--color-ink-secondary)]">
                Send a message to see tool traces here.
              </p>
            </div>
          )}
        </div>
      </div>

      <p className="mt-6 text-[10px] text-[var(--color-ink-secondary)]">
        This console uses fixture data. No real AI model is invoked.
      </p>
    </div>
  );
}
