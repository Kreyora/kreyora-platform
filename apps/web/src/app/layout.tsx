import type { Metadata } from "next";
import { Inter, Noto_Sans_Devanagari } from "next/font/google";
import "./globals.css";
import { DemoIndicator } from "@/components/demo-indicator";
import { ClientProvider } from "@/lib/providers/client-provider";

const inter = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-inter",
});

const notoDevanagari = Noto_Sans_Devanagari({
  subsets: ["devanagari"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-noto-devanagari",
});

export const metadata: Metadata = {
  title: "Kreyora — Social Commerce OS for Nepal",
  description:
    "The reliable commerce layer behind a seller's social DMs. Manage catalog, orders, inbox, and AI assistant from one workspace.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${inter.variable} ${notoDevanagari.variable} h-full`}
    >
      <body className="min-h-full flex flex-col font-[family-name:var(--font-inter),family-name:var(--font-noto-devanagari)]">
        <ClientProvider>
          <DemoIndicator />
          {children}
        </ClientProvider>
      </body>
    </html>
  );
}
