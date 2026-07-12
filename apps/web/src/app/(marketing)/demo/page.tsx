import type { Metadata } from "next";
import Link from "next/link";
import { Section } from "@/components/marketing/section";

export const metadata: Metadata = {
  title: "Demo",
  description:
    "Explore Kreyora as a seller owner, an operator managing conversations, or a customer browsing a storefront.",
};

const PERSONAS = [
  {
    role: "Seller Owner",
    description:
      "You run the business. Set up your catalog, publish your storefront, configure delivery and payments, review analytics, and manage your team.",
    sees: [
      "Dashboard with key metrics",
      "Product catalog and inventory",
      "Storefront configuration",
      "Analytics and billing",
      "Team management",
    ],
    href: "/dashboard",
    cta: "Enter as owner",
  },
  {
    role: "Seller Operator",
    description:
      "You handle day-to-day operations. Respond to customer messages, process orders, manage stock, and take over from the AI assistant when needed.",
    sees: [
      "Unified inbox with all channels",
      "Order management and fulfilment",
      "AI assistant conversation history",
      "Inventory adjustments",
    ],
    href: "/inbox",
    cta: "Enter as operator",
  },
  {
    role: "Customer",
    description:
      "You're shopping at a Nepali seller's store. Browse products, add to cart, choose delivery, and pay with cash on delivery or QR.",
    sees: [
      "Branded storefront",
      "Product collections and search",
      "Cart and checkout",
      "COD and QR payment options",
      "Order confirmation",
    ],
    href: "/store/namaste-crafts",
    cta: "Browse as customer",
  },
];

function ArrowIcon() {
  return (
    <svg
      className="ml-2 h-4 w-4 transition-transform duration-[var(--duration-hover)] ease-[var(--easing-default)] group-hover:translate-x-0.5"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M5 12h14" />
      <path d="m12 5 7 7-7 7" />
    </svg>
  );
}

export default function DemoPage() {
  return (
    <>
      <Section className="px-5 pb-16 pt-20 md:px-8 md:pb-24 md:pt-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="mx-auto max-w-2xl text-center">
            <h1 className="text-display-hero text-[var(--color-ink-primary)]">
              Choose your path
            </h1>
            <p className="mt-6 text-body-marketing text-[var(--color-ink-secondary)]">
              Explore Kreyora from three different perspectives. Each path
              uses demo data — nothing is saved or sent to a live service.
            </p>
          </div>
        </div>
      </Section>

      <Section className="px-5 pb-24 md:px-8 md:pb-32 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="stagger-children grid gap-8 md:grid-cols-3">
            {PERSONAS.map((persona) => (
              <Link
                key={persona.role}
                href={persona.href}
                className="group flex flex-col rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-8 transition-[border-color,box-shadow,transform] duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:-translate-y-0.5 hover:border-[var(--color-ink-secondary)] hover:shadow-[var(--shadow-md)]"
              >
                <h2 className="text-lg font-semibold text-[var(--color-ink-primary)]">
                  {persona.role}
                </h2>
                <p className="mt-3 text-body-app text-[var(--color-ink-secondary)]">
                  {persona.description}
                </p>

                <ul className="mt-6 flex flex-1 flex-col gap-2">
                  {persona.sees.map((item) => (
                    <li
                      key={item}
                      className="flex items-start gap-2 text-body-app text-[var(--color-ink-secondary)]"
                    >
                      <span
                        className="mt-1.5 block h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--color-border)]"
                        aria-hidden="true"
                      />
                      {item}
                    </li>
                  ))}
                </ul>

                <span className="mt-8 inline-flex items-center text-sm font-medium text-[var(--color-ink-primary)]">
                  {persona.cta}
                  <ArrowIcon />
                </span>
              </Link>
            ))}
          </div>
        </div>
      </Section>
    </>
  );
}
