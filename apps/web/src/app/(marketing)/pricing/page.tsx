import type { Metadata } from "next";
import Link from "next/link";
import { Section } from "@/components/marketing/section";

export const metadata: Metadata = {
  title: "Pricing",
  description:
    "Simple, transparent pricing for Nepali sellers. Start free and scale as you grow.",
};

const PLANS = [
  {
    name: "Free",
    price: "रू 0",
    period: "/month",
    description: "Get started with the basics. Perfect for testing.",
    features: [
      "Up to 25 products",
      "1 team member",
      "Basic storefront",
      "COD payments",
      "Community support",
    ],
    cta: "Start free",
    highlighted: false,
  },
  {
    name: "Growth",
    price: "TBD",
    period: "/month",
    description: "For active sellers scaling their social commerce.",
    features: [
      "Up to 500 products",
      "5 team members",
      "Custom storefront branding",
      "COD + QR payments",
      "AI assistant",
      "Unified inbox",
      "Analytics dashboard",
      "Priority support",
    ],
    cta: "Join waitlist",
    highlighted: true,
  },
  {
    name: "Pro",
    price: "TBD",
    period: "/month",
    description: "For high-volume sellers who need full control.",
    features: [
      "Unlimited products",
      "Unlimited team members",
      "Advanced storefront customization",
      "All payment methods",
      "AI assistant with custom knowledge",
      "Multi-channel inbox",
      "Advanced analytics",
      "Dedicated support",
      "API access",
    ],
    cta: "Join waitlist",
    highlighted: false,
  },
];

export default function PricingPage() {
  return (
    <>
      <Section className="px-5 pb-16 pt-20 md:px-8 md:pb-24 md:pt-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="mx-auto max-w-2xl text-center">
            <h1 className="text-display-hero text-[var(--color-ink-primary)]">
              Pricing
            </h1>
            <p className="mt-6 text-body-marketing text-[var(--color-ink-secondary)]">
              Simple plans that grow with your business. Start free, upgrade
              when you need more.
            </p>
          </div>
        </div>
      </Section>

      <Section className="px-5 pb-20 md:px-8 md:pb-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          {/* Disclaimer */}
          <div className="mx-auto mb-12 max-w-xl rounded-[var(--radius-md)] border border-[var(--color-warning-subtle)] bg-[var(--color-warning-subtle)] p-4 text-center text-sm text-[var(--color-warning)]">
            Pricing has not been finalized. The tiers below are indicative
            and subject to change. &quot;TBD&quot; prices will be announced
            before launch.
          </div>

          <div className="stagger-children grid gap-8 md:grid-cols-3">
            {PLANS.map((plan) => (
              <div
                key={plan.name}
                className={[
                  "flex flex-col rounded-[var(--radius-lg)] border p-8",
                  plan.highlighted
                    ? "border-[var(--color-surface-dark)] shadow-[var(--shadow-md)]"
                    : "border-[var(--color-border)]",
                ].join(" ")}
              >
                <h2 className="text-lg font-semibold text-[var(--color-ink-primary)]">
                  {plan.name}
                </h2>
                <div className="mt-4 flex items-baseline gap-1">
                  <span className="text-heading-page font-bold text-[var(--color-ink-primary)]">
                    {plan.price}
                  </span>
                  <span className="text-sm text-[var(--color-ink-secondary)]">
                    {plan.period}
                  </span>
                </div>
                <p className="mt-2 text-body-app text-[var(--color-ink-secondary)]">
                  {plan.description}
                </p>

                <ul className="mt-6 flex flex-1 flex-col gap-3">
                  {plan.features.map((feature) => (
                    <li
                      key={feature}
                      className="flex items-start gap-2 text-body-app text-[var(--color-ink-secondary)]"
                    >
                      <svg
                        className="mt-0.5 h-4 w-4 shrink-0 text-[var(--color-success)]"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        aria-hidden="true"
                      >
                        <path d="M20 6 9 17l-5-5" />
                      </svg>
                      {feature}
                    </li>
                  ))}
                </ul>

                <Link
                  href={plan.name === "Free" ? "/demo" : "/contact"}
                  className={[
                    "mt-8 inline-flex min-h-11 items-center justify-center rounded-[var(--radius-md)] border px-[var(--space-4)] text-sm font-medium transition-[opacity,background-color] duration-[var(--duration-hover)] ease-[var(--easing-default)]",
                    plan.highlighted
                      ? "border-transparent bg-[var(--color-surface-dark)] text-[var(--color-on-dark)] hover:opacity-90"
                      : "border-[var(--color-border)] bg-[var(--color-canvas)] text-[var(--color-ink-primary)] hover:bg-[var(--color-canvas-subtle)]",
                  ].join(" ")}
                >
                  {plan.cta}
                </Link>
              </div>
            ))}
          </div>
        </div>
      </Section>
    </>
  );
}
