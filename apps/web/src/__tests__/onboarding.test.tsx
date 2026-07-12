import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const ONBOARDING_FILE = path.resolve(
  __dirname,
  "../app/(seller)/onboarding/page.tsx",
);

describe("Onboarding wizard", () => {
  const content = fs.readFileSync(ONBOARDING_FILE, "utf-8");

  const stepLabels = [
    "Store profile",
    "Catalog readiness",
    "Delivery rules",
    "Payment setup",
    "Channel connection",
    "Assistant policy",
    "Activation review",
  ];

  for (const label of stepLabels) {
    it(`renders step: "${label}"`, () => {
      expect(content).toContain(label);
    });
  }

  it("shows progress percentage", () => {
    expect(content).toContain("progressPercent");
    expect(content).toContain("steps completed");
  });

  it("handles completed status", () => {
    expect(content).toContain('"completed"');
    expect(content).toContain("Completed");
  });

  it("handles incomplete status with form placeholder", () => {
    expect(content).toContain('"incomplete"');
    expect(content).toContain("Configuration form placeholder");
  });

  it("handles blocked status", () => {
    expect(content).toContain('"blocked"');
    expect(content).toContain("Blocked");
  });

  it("handles permission_denied status", () => {
    expect(content).toContain('"permission_denied"');
    expect(content).toContain("Permission denied");
  });

  it("has previous/next navigation", () => {
    expect(content).toContain("Previous");
    expect(content).toContain("Next");
  });

  it("shows activation status", () => {
    expect(content).toContain("Store activation");
    expect(content).toContain("isActivationReady");
  });

  it("marks saving as simulated", () => {
    expect(content).toContain("simulated");
  });

  it("has skip to dashboard link", () => {
    expect(content).toContain('href="/dashboard"');
    expect(content).toContain("Skip to dashboard");
  });
});
