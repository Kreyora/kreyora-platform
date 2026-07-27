import type { Metadata } from "next";
import Link from "next/link";
import { Section } from "@/components/marketing/section";

export const metadata: Metadata = {
  title: "Features",
  description:
    "Storefront, unified inbox, AI assistant, inventory, order management, and local payments — everything a Nepali seller needs in one workspace.",
};

const FEATURES = [
  {
    title: "Branded storefront",
    description:
      "Launch a professional online store without building a website. Add products, organize collections, customize your brand profile, and share a link your customers can trust.",
    details: [
      "Mobile-first responsive design",
      "Product collections and search",
      "COD and QR checkout built in",
      "Delivery zone rules and fee calculation",
      "Store readiness checks before going live",
    ],
  },
  {
    title: "Unified inbox",
    description:
      "Every customer conversation — from Facebook, Instagram, WhatsApp, and your storefront — arrives in one timeline. No more switching between apps.",
    details: [
      "All channels in a single view",
      "Customer order and conversation history",
      "Team assignment and handoff",
      "AI and human ownership indicators",
      "Message delivery status tracking",
    ],
  },
  {
    title: "AI assistant with guardrails",
    description:
      "The assistant answers routine product questions, quotes delivery fees, and drafts orders — reading your real catalog, not making things up. You stay in control.",
    details: [
      "Reads real catalog, pricing, and stock data",
      "Never fabricates products or prices",
      "Human takeover stops AI instantly",
      "Every AI action is logged and auditable",
      "Configurable assistant policy and knowledge base",
    ],
  },
  {
    title: "Inventory and stock control",
    description:
      "Track stock levels in real time across your catalog. Get low-stock alerts, see reservation holds, and review a complete audit trail of every change.",
    details: [
      "Real-time on-hand and available counts",
      "Low-stock threshold alerts",
      "Reservation tracking for pending orders",
      "Stock adjustment history",
      "Prevents overselling automatically",
    ],
  },
  {
    title: "Order management",
    description:
      "From confirmation to delivery, manage the complete order lifecycle. Track payment status, fulfilment progress, and customer communication in one view.",
    details: [
      "Order list with search and filters",
      "Payment verification workflow",
      "Fulfilment timeline and status updates",
      "Activity history and audit trail",
      "Role-based action permissions",
    ],
  },
  {
    title: "Local-first payments",
    description:
      "Cash on delivery and merchant QR verification — the payment methods Nepali customers already trust. No international gateway, no surprise fees.",
    details: [
      "Cash on delivery support",
      "Merchant QR code payment verification",
      "No gateway dependency or KYC delays",
      "Payment status tracking per order",
      "Ready for future gateway integration",
    ],
  },
];

export default function FeaturesPage() {
  return (
    <>
      <Section className="px-5 pb-16 pt-20 md:px-8 md:pb-24 md:pt-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="max-w-2xl">
            <h1 className="text-display-hero text-[var(--color-ink-primary)]">
              Features
            </h1>
            <p className="mt-6 text-body-marketing text-[var(--color-ink-secondary)]">
              Everything a Nepali seller needs to turn scattered DMs into a
              professional commerce operation — in one workspace.
            </p>
          </div>
        </div>
      </Section>

      {FEATURES.map((feature, i) => (
        <Section
          key={feature.title}
          className="px-5 py-16 md:px-8 md:py-24 lg:px-12"
        >
          <div className="mx-auto max-w-[90rem]">
            <div
              className={[
                "grid gap-12 md:items-start md:gap-16",
                i % 2 === 0
                  ? "md:grid-cols-[1fr_1fr]"
                  : "md:grid-cols-[1fr_1fr]",
              ].join(" ")}
            >
              <div className={i % 2 !== 0 ? "md:order-2" : ""}>
                <p className="text-meta font-semibold uppercase tracking-wider text-[var(--color-ink-secondary)]">
                  {String(i + 1).padStart(2, "0")}
                </p>
                <h2 className="mt-2 text-heading-page text-[var(--color-ink-primary)]">
                  {feature.title}
                </h2>
                <p className="mt-3 text-body-marketing text-[var(--color-ink-secondary)]">
                  {feature.description}
                </p>
                <ul className="mt-6 flex flex-col gap-2">
                  {feature.details.map((detail) => (
                    <li
                      key={detail}
                      className="flex items-start gap-2 text-body-app text-[var(--color-ink-secondary)]"
                    >
                      <span
                        className="mt-1.5 block h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--color-ink-secondary)]"
                        aria-hidden="true"
                      />
                      {detail}
                    </li>
                  ))}
                </ul>
              </div>
              <div className={`flex items-center justify-center rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas-subtle)] p-12${i % 2 !== 0 ? " md:order-1" : ""}`}>
                <span className="text-meta text-[var(--color-ink-secondary)]">
                  Interface preview — upcoming step
                </span>
              </div>
            </div>
          </div>
          {i < FEATURES.length - 1 && (
            <div className="mx-auto mt-16 max-w-[90rem] md:mt-24">
              <hr className="border-[var(--color-border)]" />
            </div>
          )}
        </Section>
      ))}

      <Section className="px-5 py-20 md:px-8 md:py-28 lg:px-12">
        <div className="mx-auto max-w-[90rem] text-center">
          <h2 className="text-display-section text-[var(--color-ink-primary)]">
            Ready to explore?
          </h2>
          <p className="mt-4 text-body-marketing text-[var(--color-ink-secondary)]">
            See how it all works together in the interactive demo.
          </p>
          <div className="mt-8 flex flex-wrap justify-center gap-4">
            <Link
              href="/demo"
              className="inline-flex min-h-11 items-center rounded-[var(--radius-md)] border border-transparent bg-[var(--color-surface-dark)] px-[var(--space-6)] text-sm font-medium text-[var(--color-on-dark)] transition-opacity duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:opacity-90 active:opacity-80"
            >
              Try the demo
            </Link>
          </div>
        </div>
      </Section>
    </>
  );
}
