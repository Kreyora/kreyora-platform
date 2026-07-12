"use client";

import { useState, type FormEvent } from "react";
import type { Metadata } from "next";
import { Section } from "@/components/marketing/section";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";

export default function ContactPage() {
  const [submitted, setSubmitted] = useState(false);
  const [loading, setLoading] = useState(false);

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setLoading(true);
    setTimeout(() => {
      setLoading(false);
      setSubmitted(true);
    }, 800);
  }

  return (
    <>
      <Section className="px-5 pb-16 pt-20 md:px-8 md:pb-24 md:pt-28 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="max-w-2xl">
            <h1 className="text-display-hero text-[var(--color-ink-primary)]">
              Get in touch
            </h1>
            <p className="mt-6 text-body-marketing text-[var(--color-ink-secondary)]">
              Interested in Kreyora? Join the waitlist or send us a message.
              We&apos;ll get back to you as soon as we can.
            </p>
          </div>
        </div>
      </Section>

      <Section className="px-5 pb-24 md:px-8 md:pb-32 lg:px-12">
        <div className="mx-auto max-w-[90rem]">
          <div className="grid gap-16 md:grid-cols-2">
            <div>
              {submitted ? (
                <div className="rounded-[var(--radius-lg)] border border-[var(--color-success-subtle)] bg-[var(--color-success-subtle)] p-8">
                  <h2 className="text-lg font-semibold text-[var(--color-success)]">
                    Thank you!
                  </h2>
                  <p className="mt-2 text-body-app text-[var(--color-ink-secondary)]">
                    Your message has been received. This is a demo — no data
                    was actually submitted. In a live system, we would follow
                    up via email.
                  </p>
                  <button
                    type="button"
                    onClick={() => setSubmitted(false)}
                    className="mt-4 text-sm font-medium text-[var(--color-success)] underline underline-offset-2"
                  >
                    Send another message
                  </button>
                </div>
              ) : (
                <form
                  onSubmit={handleSubmit}
                  className="flex flex-col gap-[var(--space-5)]"
                >
                  <Input
                    label="Name"
                    name="name"
                    placeholder="Your name"
                    required
                    autoComplete="name"
                  />
                  <Input
                    label="Email"
                    name="email"
                    type="email"
                    placeholder="you@example.com"
                    required
                    autoComplete="email"
                  />
                  <Textarea
                    label="Message"
                    name="message"
                    placeholder="Tell us about your business or what you'd like to know..."
                    rows={5}
                    required
                  />
                  <div className="text-xs text-[var(--color-ink-secondary)]">
                    This form is simulated. No data is submitted or stored.
                  </div>
                  <Button type="submit" loading={loading} className="self-start">
                    Send message
                  </Button>
                </form>
              )}
            </div>

            <div>
              <h2 className="text-lg font-semibold text-[var(--color-ink-primary)]">
                Other ways to reach us
              </h2>
              <dl className="mt-6 flex flex-col gap-6">
                <div>
                  <dt className="text-sm font-medium text-[var(--color-ink-primary)]">
                    Email
                  </dt>
                  <dd className="mt-1 text-body-app text-[var(--color-ink-secondary)]">
                    hello@kreyora.com (placeholder)
                  </dd>
                </div>
                <div>
                  <dt className="text-sm font-medium text-[var(--color-ink-primary)]">
                    Location
                  </dt>
                  <dd className="mt-1 text-body-app text-[var(--color-ink-secondary)]">
                    Kathmandu, Nepal
                  </dd>
                </div>
              </dl>
            </div>
          </div>
        </div>
      </Section>
    </>
  );
}
