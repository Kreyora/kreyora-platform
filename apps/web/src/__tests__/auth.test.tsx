import { describe, it, expect, vi } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Auth — sign-in page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(auth)/signin/page.tsx"),
    "utf-8",
  );

  it("renders an email input", () => {
    expect(content).toContain('type="email"');
  });

  it("renders a password input", () => {
    expect(content).toContain('type={showPassword ? "text" : "password"}');
  });

  it("has a submit button", () => {
    expect(content).toContain('type="submit"');
    expect(content).toContain("Sign in");
  });

  it("has social login buttons for Google and Facebook", () => {
    expect(content).toContain("Google");
    expect(content).toContain("Facebook");
  });

  it("has a forgot password link to /recover", () => {
    expect(content).toContain('href="/recover"');
    expect(content).toContain("Forgot password");
  });

  it("navigates to /workspaces on simulated submit", () => {
    expect(content).toContain('router.push("/workspaces")');
  });
});

describe("Auth — recovery page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(auth)/recover/page.tsx"),
    "utf-8",
  );

  it("renders an email input", () => {
    expect(content).toContain('type="email"');
  });

  it("has a submit button", () => {
    expect(content).toContain("Send recovery link");
  });

  it("shows success state after submission", () => {
    expect(content).toContain("Check your email");
  });

  it("has a back to sign in link", () => {
    expect(content).toContain('href="/signin"');
    expect(content).toContain("Back to sign in");
  });

  it("contains simulated disclaimer", () => {
    expect(content).toContain("simulated");
  });
});

describe("Auth — layout", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(auth)/layout.tsx"),
    "utf-8",
  );

  it("has back to home link", () => {
    expect(content).toContain('href="/"');
    expect(content).toContain("Back to home");
  });

  it("displays Kreyora branding", () => {
    expect(content).toContain("Kreyora");
  });

  it("shows simulated authentication disclaimer", () => {
    expect(content).toContain("Simulated authentication");
  });

  it("uses centered card layout with border and shadow", () => {
    expect(content).toContain("max-w-md");
    expect(content).toContain("rounded-[var(--radius-lg)]");
    expect(content).toContain("shadow-[var(--shadow-sm)]");
  });
});
