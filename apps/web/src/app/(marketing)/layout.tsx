import type { Metadata } from "next";
import { MarketingHeader } from "@/components/marketing/header";
import { MarketingFooter } from "@/components/marketing/footer";

export const metadata: Metadata = {
  title: {
    template: "%s | Kreyora",
    default: "Kreyora — Social Commerce OS for Nepal",
  },
  description:
    "The reliable commerce layer behind a seller's social DMs. Manage catalog, orders, inbox, and AI assistant from one workspace.",
};

export default function MarketingLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-full flex-col">
      <MarketingHeader />
      <main className="flex-1">{children}</main>
      <MarketingFooter />
    </div>
  );
}
