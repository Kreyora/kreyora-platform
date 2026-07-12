import type { Metadata } from "next";
import Link from "next/link";
import { Section } from "@/components/marketing/section";

export const metadata: Metadata = {
  title: "Kreyora — Social Commerce OS for Nepal",
  description:
    "Turn social DMs into reliable orders. Catalog, storefront, inbox, AI assistant, inventory, and local payments — all in one workspace.",
};

const WORKFLOW_STEPS = [
  {
    number: "01",
    title: "Build your catalog",
    description:
      "Add products, variants, pricing, and stock levels. Your catalog becomes the single source of truth for every channel.",
  },
  {
    number: "02",
    title: "Publish your storefront",
    description:
      "Go live with a branded storefront. Customers browse, add to cart, and check out — no coding required.",
  },
  {
    number: "03",
    title: "Connect social channels",
    description:
      "Link Facebook, Instagram, or WhatsApp. Enquiries flow into one unified inbox alongside storefront orders.",
  },
  {
    number: "04",
    title: "Let AI handle the routine",
    description:
      "The assistant answers product questions, quotes delivery, and drafts orders — always reading real catalog data, never making things up.",
  },
  {
    number: "05",
    title: "Fulfil and grow",
    description:
      "Confirm orders, track payments, manage stock, and use dashboards to understand what sells. Take over from AI anytime.",
  },
];

const CAPABILITIES = [
  {
    title: "Branded storefront",
    description:
      "A clean, mobile-first store under your brand. Product grids, collections, checkout, COD, and QR payments — ready to share.",
  },
  {
    title: "Unified inbox",
    description:
      "Every conversation from every channel in one place. See customer history, assign to team members, and hand off from AI seamlessly.",
  },
  {
    title: "AI assistant with guardrails",
    description:
      "Responds to customers using your real catalog, pricing, and stock. It never fabricates products or prices. You stay in control.",
  },
  {
    title: "Inventory and stock control",
    description:
      "Real-time stock levels, low-stock alerts, reservation tracking, and audit history. No more overselling.",
  },
  {
    title: "Order management",
    description:
      "Confirm, prepare, dispatch, and deliver. Track payment status, fulfilment timeline, and customer communication in one view.",
  },
  {
    title: "Local-first payments",
    description:
      "Cash on delivery and merchant QR verification built for Nepal. No gateway dependency, no surprise fees for your customers.",
  },
];

export default function LandingPage() {
  return (
    <>
      {/* Hero */}
      <Section className="px-5 pb-24 pt-20 md:px-8 md:pb-32 md:pt-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="max-w-3xl">
            <h1 className="text-display-hero text-[var(--color-ink-primary)]">
              The reliable commerce layer behind your social DMs
            </h1>
            <p className="mt-6 max-w-xl text-body-marketing text-[var(--color-ink-secondary)]">
              Nepali sellers lose orders in scattered DMs. Kreyora turns social
              conversations into a professional storefront, organized inbox, and
              trusted order system — powered by AI that reads your real catalog,
              not its imagination.
            </p>
            <div className="mt-10 flex flex-wrap gap-4">
              <Link
                href="/demo"
                className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-transparent bg-[var(--color-surface-dark)] px-[var(--space-6)] text-sm font-medium text-[var(--color-on-dark)] transition-opacity duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:opacity-90 active:opacity-80"
              >
                Try the demo
              </Link>
              <Link
                href="/features"
                className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-6)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]"
              >
                See features
              </Link>
            </div>
          </div>
        </div>
      </Section>

      {/* Thin divider */}
      <div className="mx-auto max-w-[90rem] px-5 md:px-8 lg:px-12">
        <hr className="border-[var(--color-border)]" />
      </div>

      {/* Problem statement */}
      <Section className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="grid gap-12 md:grid-cols-2 md:items-center md:gap-16">
            <div>
              <p className="text-meta font-semibold uppercase tracking-wider text-[var(--color-ink-secondary)]">
                The problem
              </p>
              <h2 className="mt-3 text-display-section text-[var(--color-ink-primary)]">
                Sellers lose orders in DMs
              </h2>
              <p className="mt-4 text-body-marketing text-[var(--color-ink-secondary)]">
                Thousands of Nepali sellers run their businesses through Facebook
                and Instagram DMs. Orders get missed, prices are quoted from
                memory, stock is tracked in notebooks, and customers wait hours
                for a reply. There&apos;s no storefront, no order history, no
                accountability.
              </p>
            </div>
            <div className="flex items-center justify-center rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-12">
              <span className="text-meta text-[var(--color-ink-secondary)]">
                Interface preview — M01-S05
              </span>
            </div>
          </div>
        </div>
      </Section>

      {/* Workflow */}
      <Section className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <p className="text-meta font-semibold uppercase tracking-wider text-[var(--color-ink-secondary)]">
            How it works
          </p>
          <h2 className="mt-3 max-w-lg text-display-section text-[var(--color-ink-primary)]">
            From catalog to fulfilment in five steps
          </h2>
          <div className="stagger-children mt-12 grid gap-8 md:grid-cols-2 lg:grid-cols-3">
            {WORKFLOW_STEPS.map((step) => (
              <div key={step.number} className="flex flex-col gap-3">
                <span className="text-display-section font-bold text-[var(--color-border)]">
                  {step.number}
                </span>
                <h3 className="text-heading-page text-[var(--color-ink-primary)]">
                  {step.title}
                </h3>
                <p className="text-body-app text-[var(--color-ink-secondary)]">
                  {step.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </Section>

      {/* Thin divider */}
      <div className="mx-auto max-w-[90rem] px-5 md:px-8 lg:px-12">
        <hr className="border-[var(--color-border)]" />
      </div>

      {/* Capabilities */}
      <Section className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <p className="text-meta font-semibold uppercase tracking-wider text-[var(--color-ink-secondary)]">
            Everything you need
          </p>
          <h2 className="mt-3 max-w-lg text-display-section text-[var(--color-ink-primary)]">
            One workspace, every commerce tool
          </h2>
          <div className="stagger-children mt-12 grid gap-x-12 gap-y-10 md:grid-cols-2 lg:grid-cols-3">
            {CAPABILITIES.map((cap) => (
              <div key={cap.title}>
                <h3 className="text-lg font-semibold text-[var(--color-ink-primary)]">
                  {cap.title}
                </h3>
                <p className="mt-2 text-body-app text-[var(--color-ink-secondary)]">
                  {cap.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </Section>

      {/* AI trust principle */}
      <Section className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="mx-auto max-w-2xl text-center">
            <p className="text-meta font-semibold uppercase tracking-wider text-[var(--color-ink-secondary)]">
              Our safety principle
            </p>
            <h2 className="mt-3 text-display-section text-[var(--color-ink-primary)]">
              AI is not the source of truth
            </h2>
            <p className="mt-4 text-body-marketing text-[var(--color-ink-secondary)]">
              Products, prices, stock levels, delivery fees, and payment
              status are application facts — stored in your catalog and order
              system. The AI assistant reads these facts through controlled
              tools. It never invents a product, fabricates a price, or
              confirms a payment on its own. You can take over from AI at any
              moment, and every action is logged.
            </p>
          </div>
        </div>
      </Section>

      {/* Dark contrast section — local-first payments */}
      <Section dark className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="grid gap-12 md:grid-cols-2 md:items-center md:gap-16">
            <div>
              <p className="text-meta font-semibold uppercase tracking-wider opacity-70">
                Built for Nepal
              </p>
              <h2 className="mt-3 text-display-section">
                Cash on delivery and QR payments, no gateway required
              </h2>
              <p className="mt-4 text-body-marketing opacity-80">
                Your customers pay with COD or scan a merchant QR code — the
                payment methods they already trust. No international gateway
                fees, no KYC delays, no surprise charges. Kreyora verifies
                payments manually or through merchant QR confirmation,
                keeping you in control.
              </p>
            </div>
            <div className="flex items-center justify-center rounded-[var(--radius-lg)] border border-white/10 bg-white/5 p-12">
              <span className="text-meta opacity-60">
                Payment flow preview — M01-S05
              </span>
            </div>
          </div>
        </div>
      </Section>

      {/* CTA banner */}
      <Section className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="mx-auto max-w-2xl text-center">
            <h2 className="text-display-section text-[var(--color-ink-primary)]">
              See it in action
            </h2>
            <p className="mt-4 text-body-marketing text-[var(--color-ink-secondary)]">
              Explore the demo as a seller owner, an operator managing
              conversations, or a customer browsing a store.
            </p>
            <div className="mt-8 flex flex-wrap justify-center gap-4">
              <Link
                href="/demo"
                className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-transparent bg-[var(--color-surface-dark)] px-[var(--space-6)] text-sm font-medium text-[var(--color-on-dark)] transition-opacity duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:opacity-90 active:opacity-80"
              >
                Try the demo
              </Link>
              <Link
                href="/contact"
                className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-[var(--space-6)] text-sm font-medium text-[var(--color-ink-primary)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]"
              >
                Join the waitlist
              </Link>
            </div>
          </div>
        </div>
      </Section>
    </>
  );
}
