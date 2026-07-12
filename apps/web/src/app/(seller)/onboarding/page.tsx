"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useIdentityClient } from "@/lib/providers/client-provider";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { OnboardingState, OnboardingStep, OnboardingStepStatus } from "@/lib/types";

const STEP_META: Record<
  OnboardingStep,
  { label: string; description: string }
> = {
  store_profile: {
    label: "Store profile",
    description:
      "Set up your store name, tagline, description, logo, and contact information. This is the first thing customers see.",
  },
  catalog_readiness: {
    label: "Catalog readiness",
    description:
      "Add at least one published product with a title, description, price, and variant. Your catalog powers your storefront and AI assistant.",
  },
  delivery_rules: {
    label: "Delivery rules",
    description:
      "Define delivery zones, fees, and estimated delivery times. Specify whether COD is available per zone.",
  },
  payment_setup: {
    label: "Payment setup",
    description:
      "Enable cash on delivery and/or merchant QR payment. Upload your QR code and set payment instructions.",
  },
  channel_connection: {
    label: "Channel connection",
    description:
      "Connect at least one social channel (Facebook, Instagram, or WhatsApp) to start receiving customer messages in your inbox.",
  },
  assistant_policy: {
    label: "Assistant policy",
    description:
      "Configure the AI assistant's language, tone, escalation rules, and upload your knowledge base documents.",
  },
  activation_review: {
    label: "Activation review",
    description:
      "Review your setup checklist and activate your store. All readiness checks must pass before your storefront goes live.",
  },
};

const STEP_ORDER: OnboardingStep[] = [
  "store_profile",
  "catalog_readiness",
  "delivery_rules",
  "payment_setup",
  "channel_connection",
  "assistant_policy",
  "activation_review",
];

function StatusIcon({ status }: { status: OnboardingStepStatus }) {
  if (status === "completed") {
    return (
      <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--color-success-subtle)]">
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="var(--color-success)"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M20 6 9 17l-5-5" />
        </svg>
      </span>
    );
  }
  if (status === "blocked") {
    return (
      <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--color-warning-subtle)]">
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="var(--color-warning)"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <path d="M12 9v4M12 17h.01" />
        </svg>
      </span>
    );
  }
  if (status === "permission_denied") {
    return (
      <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--color-danger-subtle)]">
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="var(--color-danger)"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
          <path d="M7 11V7a5 5 0 0 1 10 0v4" />
        </svg>
      </span>
    );
  }
  return (
    <span className="flex h-6 w-6 items-center justify-center rounded-full border-2 border-[var(--color-border)] bg-[var(--color-canvas)]">
      <span className="h-2 w-2 rounded-full bg-[var(--color-border)]" />
    </span>
  );
}

function StepContent({
  step,
  status,
  completedAt,
}: {
  step: OnboardingStep;
  status: OnboardingStepStatus;
  completedAt?: string;
}) {
  const meta = STEP_META[step];

  if (status === "completed") {
    return (
      <div className="rounded-[var(--radius-md)] border border-[var(--color-success-subtle)] bg-[var(--color-success-subtle)] p-4">
        <p className="text-sm font-medium text-[var(--color-success)]">
          Completed
          {completedAt && (
            <span className="ml-1 font-normal text-[var(--color-ink-secondary)]">
              on {new Date(completedAt).toLocaleDateString()}
            </span>
          )}
        </p>
        <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
          {meta.description}
        </p>
        <Button variant="outline" size="sm" className="mt-3">
          Edit (simulated)
        </Button>
      </div>
    );
  }

  if (status === "blocked") {
    return (
      <div className="rounded-[var(--radius-md)] border border-[var(--color-warning-subtle)] bg-[var(--color-warning-subtle)] p-4">
        <p className="text-sm font-medium text-[var(--color-warning)]">
          Blocked
        </p>
        <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
          This step requires a prerequisite to be completed first. Check the
          steps above for incomplete items.
        </p>
      </div>
    );
  }

  if (status === "permission_denied") {
    return (
      <div className="rounded-[var(--radius-md)] border border-[var(--color-danger-subtle)] bg-[var(--color-danger-subtle)] p-4">
        <p className="text-sm font-medium text-[var(--color-danger)]">
          Permission denied
        </p>
        <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
          Your current role does not have permission to configure this step.
          Contact the workspace owner.
        </p>
      </div>
    );
  }

  return (
    <div className="rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-4">
      <p className="text-sm text-[var(--color-ink-secondary)]">
        {meta.description}
      </p>
      <div className="mt-4 rounded-[var(--radius-md)] border border-dashed border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-6 text-center text-xs text-[var(--color-ink-secondary)]">
        Configuration form placeholder — will be built in a future step.
      </div>
      <Button className="mt-3">Save (simulated)</Button>
    </div>
  );
}

export default function OnboardingPage() {
  const identityClient = useIdentityClient();
  const [state, setState] = useState<OnboardingState | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [activeStep, setActiveStep] = useState<OnboardingStep>(STEP_ORDER[0]);

  useEffect(() => {
    let cancelled = false;
    identityClient.getOnboardingState("").then((s) => {
      if (!cancelled) {
        setState(s);
        setIsLoading(false);
        const firstIncomplete = s.steps.find(
          (st) => st.status !== "completed",
        );
        if (firstIncomplete) {
          setActiveStep(firstIncomplete.step);
        }
      }
    });
    return () => {
      cancelled = true;
    };
  }, [identityClient]);

  if (isLoading || !state) {
    return (
      <div className="mx-auto max-w-3xl">
        <Skeleton className="mb-4 h-8 w-48" />
        <Skeleton className="mb-8 h-4 w-96" />
        <Skeleton className="h-2 w-full rounded-full" />
        <div className="mt-8 space-y-4">
          {Array.from({ length: 7 }).map((_, i) => (
            <Skeleton key={i} className="h-16 w-full rounded-[var(--radius-md)]" />
          ))}
        </div>
      </div>
    );
  }

  const completedCount = state.steps.filter(
    (s) => s.status === "completed",
  ).length;
  const progressPercent = Math.round((completedCount / state.steps.length) * 100);
  const activeIndex = STEP_ORDER.indexOf(activeStep);

  const activeStepData = state.steps.find((s) => s.step === activeStep);

  return (
    <div className="mx-auto max-w-3xl">
      <h1 className="text-heading-page text-[var(--color-ink-primary)]">
        Set up your workspace
      </h1>
      <p className="mt-2 text-sm text-[var(--color-ink-secondary)]">
        Complete each step to get your store ready. You can return to any step
        at any time.
      </p>

      {/* Progress bar */}
      <div className="mt-6">
        <div className="flex items-center justify-between text-xs text-[var(--color-ink-secondary)]">
          <span>
            {completedCount} of {state.steps.length} steps completed
          </span>
          <span>{progressPercent}%</span>
        </div>
        <div className="mt-2 h-2 overflow-hidden rounded-full bg-[var(--color-canvas-subtle)]">
          <div
            className="h-full rounded-full bg-[var(--color-success)] transition-[width] duration-[var(--duration-entrance)] ease-[var(--easing-default)]"
            style={{ width: `${progressPercent}%` }}
          />
        </div>
      </div>

      {/* Steps list */}
      <div className="mt-8 flex flex-col gap-2">
        {STEP_ORDER.map((step, i) => {
          const stepData = state.steps.find((s) => s.step === step);
          const status = stepData?.status ?? "incomplete";
          const isActive = step === activeStep;
          const meta = STEP_META[step];

          return (
            <div key={step}>
              <button
                type="button"
                onClick={() => setActiveStep(step)}
                className={[
                  "flex w-full items-center gap-3 rounded-[var(--radius-md)] px-4 py-3 text-left transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)]",
                  isActive
                    ? "bg-[var(--color-canvas-subtle)] ring-1 ring-[var(--color-border)]"
                    : "hover:bg-[var(--color-canvas-subtle)]",
                ].join(" ")}
                aria-expanded={isActive}
              >
                <StatusIcon status={status} />
                <div className="flex-1">
                  <span
                    className={[
                      "text-sm",
                      status === "completed"
                        ? "text-[var(--color-ink-secondary)]"
                        : "font-medium text-[var(--color-ink-primary)]",
                    ].join(" ")}
                  >
                    {i + 1}. {meta.label}
                  </span>
                </div>
                <svg
                  width="16"
                  height="16"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  className={[
                    "shrink-0 text-[var(--color-ink-secondary)] transition-transform duration-[var(--duration-hover)]",
                    isActive ? "rotate-90" : "",
                  ].join(" ")}
                  aria-hidden="true"
                >
                  <path d="m9 18 6-6-6-6" />
                </svg>
              </button>

              {isActive && activeStepData && (
                <div className="ml-5 mt-2 border-l-2 border-[var(--color-border)] pl-6 pb-2">
                  <StepContent
                    step={step}
                    status={activeStepData.status}
                    completedAt={activeStepData.completedAt}
                  />
                  <div className="mt-3 flex gap-2">
                    {activeIndex > 0 && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setActiveStep(STEP_ORDER[activeIndex - 1])}
                      >
                        Previous
                      </Button>
                    )}
                    {activeIndex < STEP_ORDER.length - 1 && (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setActiveStep(STEP_ORDER[activeIndex + 1])}
                      >
                        Next
                      </Button>
                    )}
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Activation status */}
      <div className="mt-8 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-4">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-[var(--color-ink-primary)]">
              Store activation
            </p>
            <p className="mt-0.5 text-xs text-[var(--color-ink-secondary)]">
              {state.isActivationReady
                ? "All checks passed. Your store is ready to go live."
                : `Complete all steps to activate your store. ${completedCount}/${state.steps.length} done.`}
            </p>
          </div>
          <Button
            disabled={!state.isActivationReady}
          >
            {state.isActivationReady ? "Activate store" : "Not ready"}
          </Button>
        </div>
      </div>

      <p className="mt-4 text-center text-xs text-[var(--color-ink-secondary)]">
        All saving and activation is simulated — no data is persisted.
      </p>

      <div className="mt-6">
        <Link
          href="/dashboard"
          className="text-sm text-[var(--color-ink-secondary)] underline underline-offset-2 hover:text-[var(--color-ink-primary)]"
        >
          Skip to dashboard
        </Link>
      </div>
    </div>
  );
}
