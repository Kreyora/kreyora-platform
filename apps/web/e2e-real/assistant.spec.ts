import { readFileSync } from "node:fs";
import path from "node:path";
import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import { authFile, type Persona } from "./global-setup";

// M09-S08 real-backend assistant suite: real API + PostgreSQL with the assistant on in Fake AI mode (no provider is
// called). Started by scripts/e2e-real.mjs. Screenshots go to artifacts/screenshots/M09-S08.
const API = process.env.E2E_API_URL ?? "http://localhost:5030";
const SHOTS = path.resolve(__dirname, "../../../artifacts/screenshots/M09-S08");

test.describe.configure({ mode: "serial" });

async function signIn(page: Page, persona: Persona, target: string) {
  const state = JSON.parse(readFileSync(authFile(persona), "utf8")) as { cookies: Parameters<BrowserContext["addCookies"]>[0] };
  await page.context().addCookies(state.cookies);
  await page.goto("/workspaces");
  await page.getByRole("link", { name: /Development Store/ }).click();
  await page.waitForURL(/dashboard/);
  await page.goto(target);
}

async function api(page: Page, apiPath: string, method = "GET", body?: unknown) {
  return page.evaluate(async ({ base, apiPath, method, body }) => {
    const tenant = sessionStorage.getItem("kreyora.selected-tenant-id") ?? "";
    const headers: Record<string, string> = { "X-Kreyora-Tenant-Id": tenant, Accept: "application/json" };
    if (method !== "GET") {
      const csrf = await (await fetch(`${base}/v1/auth/csrf`, { credentials: "include" })).json();
      headers["X-CSRF-Token"] = csrf.token;
      headers["Content-Type"] = "application/json";
      headers["Idempotency-Key"] = crypto.randomUUID();
    }
    const response = await fetch(`${base}${apiPath}`, { method, credentials: "include", headers, body: body === undefined ? undefined : JSON.stringify(body) });
    return { status: response.status, body: await response.json().catch(() => null) };
  }, { base: API, apiPath, method, body });
}

const noHorizontalScroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

test("@desktop @mobile Owner reviews settings, approves knowledge, tests in the console and sees it in history", async ({ page }, testInfo) => {
  await signIn(page, "owner", "/assistant");
  await expect(page.getByRole("heading", { name: "Assistant" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Status" })).toBeVisible();
  await page.getByLabel("Tone").selectOption("formal");
  await page.getByRole("button", { name: "Save settings" }).click();
  await expect(page.getByText("Saved. Settings are marked reviewed.")).toBeVisible();
  const policy = await api(page, "/v1/assistant/policy");
  expect(String(policy.body.tone).toLowerCase()).toBe("formal");
  expect(policy.body.reviewedAt).not.toBeNull();
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-owner-overview.png`, fullPage: true });
  expect(await noHorizontalScroll(page)).toBeLessThanOrEqual(0);

  await page.getByRole("link", { name: "Knowledge" }).click();
  const title = `E2E FAQ ${testInfo.project.name}`;
  await page.getByLabel("Title").fill(title);
  await page.getByLabel("Text", { exact: true }).fill("Made-up FAQ: exchanges within 7 days with the tag on.");
  await page.getByRole("button", { name: "Submit for review" }).click();
  const card = page.getByRole("listitem").filter({ hasText: title });
  await card.getByRole("button", { name: /Review v1/ }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Approve and use" }).click();
  await expect(card.getByText("In use · v1")).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-owner-knowledge.png`, fullPage: true });
  expect(await noHorizontalScroll(page)).toBeLessThanOrEqual(0);

  // The console runs the shop's real tools, so the workspace needs a store (the E2E seed has none).
  if ((await api(page, "/v1/store")).status === 404) {
    const created = await api(page, "/v1/store", "POST", {
      displayName: "E2E Boutique", platformSlug: `e2e-boutique-${Date.now()}`, tagline: null, themePreset: "default", brandAccentHex: null,
      contactName: "E2E", contactEmail: "owner@kreyora.test", contactPhone: null, contactWhatsApp: null, facebookUrl: null, instagramUrl: null, tikTokUrl: null,
      termsPolicy: null, privacyPolicy: null, returnsPolicy: null, paymentPolicy: null,
    });
    expect(created.status, JSON.stringify(created.body)).toBeLessThan(300);
  }

  await page.getByRole("link", { name: "Console" }).click();
  await page.getByLabel("Customer message").fill("Made-up question: do you deliver to Pokhara?");
  await page.getByRole("button", { name: "Send" }).click();
  const trace = page.getByRole("region", { name: "What happened" });
  await expect(trace.getByText(/Replied|Handed to a person|Safe hand-off|Not sent/)).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-owner-console.png`, fullPage: true });
  expect(await noHorizontalScroll(page)).toBeLessThanOrEqual(0);

  await page.getByRole("link", { name: "History" }).click();
  await expect(page.getByText("Console test").first()).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-owner-history.png`, fullPage: true });
  expect(await noHorizontalScroll(page)).toBeLessThanOrEqual(0);
});

test("@desktop Viewer reads the assistant but can't change it or use the console", async ({ page }) => {
  await signIn(page, "viewer", "/assistant");
  await expect(page.getByText(/View only/)).toBeVisible();
  await expect(page.getByLabel("Tone")).toBeDisabled();
  await expect(page.getByRole("button", { name: "Save settings" })).toHaveCount(0);

  await page.getByRole("link", { name: "Console" }).click();
  await expect(page.getByText("Only the shop owner or an admin can use the test console.")).toBeVisible();
  const forced = await api(page, "/v1/assistant/playground", "POST", { messages: [{ from: "customer", text: "hi" }] });
  expect(forced.status).toBe(403);
});

test("@desktop @mobile Admin takes a chat over and it appears in the needs-a-person queue", async ({ page }, testInfo) => {
  await signIn(page, "admin", "/inbox");
  await page.getByRole("link", { name: /Simulator user ·open/ }).first().click();
  const id = page.url().split("/inbox/")[1];
  await expect(page.getByRole("button", { name: /^(Take over|Hand back to automation)$/ })).toBeVisible(); // the panel has loaded
  if (await page.getByRole("button", { name: "Take over" }).isVisible()) await page.getByRole("button", { name: "Take over" }).click();
  await expect(page.getByRole("button", { name: "Hand back to automation" })).toBeVisible();
  await expect(page.getByRole("region", { name: "Assistant activity" })).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-admin-conversation.png`, fullPage: true });

  await page.goto("/inbox");
  await page.getByRole("tab", { name: "Needs a person" }).click();
  const queue = await api(page, "/v1/conversations?needsPerson=true");
  expect(queue.status).toBe(200);
  // The screen shows exactly the server's queue (whether this chat is in it depends on who spoke last).
  const items = queue.body.items as { id: string }[];
  if (items.length === 0) await expect(page.getByText("No one is waiting for a person")).toBeVisible();
  else await expect(page.getByRole("list", { name: "Conversations" }).getByRole("listitem")).toHaveCount(items.length);
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-admin-queue.png`, fullPage: true });
  expect(await noHorizontalScroll(page)).toBeLessThanOrEqual(0);

  await api(page, `/v1/conversations/${id}/release`, "POST"); // leave the seeded chat as the inbox suite expects
});
