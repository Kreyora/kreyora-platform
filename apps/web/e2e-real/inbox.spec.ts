import { createHmac } from "node:crypto";
import { readFileSync } from "node:fs";
import path from "node:path";
import { expect, test, type BrowserContext, type Page } from "@playwright/test";
import { authFile, type Persona } from "./global-setup";

// M08-S06 real-backend inbox suite: real API + PostgreSQL + Hangfire + Simulator channel, seeded by the
// Development-only E2E seed. Started by scripts/e2e-real.mjs (run-scoped random secrets).
const API = process.env.E2E_API_URL ?? "http://localhost:5030";
const SIMULATOR_SECRET = process.env.E2E_SIMULATOR_SECRET ?? "";
const SHOTS = path.resolve(__dirname, "../../../artifacts/screenshots/M08-S06");

test.describe.configure({ mode: "serial" });

/** Restores the persona's session (signed in once in global-setup) and selects the seeded workspace. */
async function signIn(page: Page, persona: Persona) {
  const state = JSON.parse(readFileSync(authFile(persona), "utf8")) as { cookies: Parameters<BrowserContext["addCookies"]>[0] };
  await page.context().addCookies(state.cookies);
  await page.goto("/workspaces");
  await page.getByRole("link", { name: /Development Store/ }).click();
  await page.waitForURL(/dashboard/);
  await page.goto("/inbox");
  await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
}

async function openConversation(page: Page, label: string) {
  await page.getByRole("link", { name: new RegExp(label) }).first().click();
  await expect(page.getByRole("heading", { name: new RegExp(label) })).toBeVisible();
}

/** Calls the API with the page's own session (cookies + selected workspace), like the app does. */
async function api(page: Page, apiPath: string, method = "GET", body?: unknown) {
  return page.evaluate(async ({ base, apiPath, method, body }) => {
    const tenant = sessionStorage.getItem("kreyora.selected-tenant-id") ?? "";
    const headers: Record<string, string> = { "X-Kreyora-Tenant-Id": tenant, Accept: "application/json" };
    if (method !== "GET") {
      const csrf = await (await fetch(`${base}/v1/auth/csrf`, { credentials: "include" })).json();
      headers["X-CSRF-Token"] = csrf.token;
      headers["Content-Type"] = "application/json";
    }
    const response = await fetch(`${base}${apiPath}`, { method, credentials: "include", headers, body: body === undefined ? undefined : JSON.stringify(body) });
    return { status: response.status, body: await response.json().catch(() => null) };
  }, { base: API, apiPath, method, body });
}

async function sendSimulatorMessage(customerId: string, text: string) {
  const body = JSON.stringify({ message_id: `e2e_live_${Date.now()}`, sender_id: customerId, text, occurred_at: new Date().toISOString() });
  const signature = "sha256=" + createHmac("sha256", SIMULATOR_SECRET).update(body).digest("hex");
  const response = await fetch(`${API}/v1/webhooks/simulator`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-External-Account-Id": "e2e-simulator", "X-Hub-Signature-256": signature },
    body,
  });
  expect(response.status, "signed Simulator webhook accepted").toBeLessThan(300);
}

test("@desktop Operator daily workflow, and the inbox equals the backend", async ({ page }) => {
  await signIn(page, "operator");
  await page.screenshot({ path: `${SHOTS}/desktop-inbox-list.png`, fullPage: true });
  await openConversation(page, "Simulator user ·open");

  await page.getByLabel("Reply").fill("Yes — the red kurta is in stock in size M.");
  await page.getByRole("button", { name: "Send" }).click();
  const bubble = page.getByRole("region", { name: "Messages" }).locator("ol > li").filter({ hasText: "the red kurta is in stock" });
  await expect(bubble).toBeVisible();
  // Delivered by the scheduled outbound job through the Simulator provider.
  await expect(bubble.getByText("Sent")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByText("Automation paused").first()).toBeVisible();

  await page.getByRole("button", { name: "Assign to me" }).click();
  await expect(page.getByText("Assigned to E2E Operator").first()).toBeVisible();
  await page.getByLabel("New label").fill("vip");
  await page.getByRole("button", { name: "Add" }).click();
  await expect(page.getByRole("list", { name: "Labels" }).getByText("vip")).toBeVisible();
  await page.getByRole("button", { name: "Resolve" }).click();
  await expect(page.getByText("Resolved").first()).toBeVisible();
  await page.getByRole("button", { name: "Reopen" }).click();
  await expect(page.getByText("New", { exact: true }).first()).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/desktop-conversation-after-workflow.png`, fullPage: true });

  // Inbox truth matches durable backend state.
  const id = page.url().split("/inbox/")[1];
  const detail = await api(page, `/v1/conversations/${id}`);
  const messages = await api(page, `/v1/conversations/${id}/messages`);
  const me = await api(page, "/v1/auth/me");
  expect(detail.status).toBe(200);
  expect(detail.body.status).toBe("new");
  expect(detail.body.isAutomationActive).toBe(false);
  expect(detail.body.labels).toEqual(["vip"]);
  expect(detail.body.assignedUserId).toBe(me.body.id);
  expect(detail.body.unreadCount).toBe(0); // opening it as an Operator marked it read
  const outbound = messages.body.items.filter((m: { direction: string }) => m.direction === "outbound");
  expect(outbound).toHaveLength(1);
  expect(outbound[0].deliveryStatus).toBe("sent");
  await expect(page.getByRole("region", { name: "Messages" }).locator("ol > li")).toHaveCount(messages.body.items.length);
});

test("@desktop New customer messages appear live without reloading", async ({ page }) => {
  await signIn(page, "operator");
  await openConversation(page, "Simulator user ·cond");

  await sendSimulatorMessage("e2e-customer-second", "Also, do you have it in blue?");

  // Immediate processing job + 5 s conversation polling (Hangfire queue poll ≤ 15 s).
  await expect(page.getByText("Also, do you have it in blue?")).toBeVisible({ timeout: 60_000 });
  // The provider-supplied sender name replaces the masked label (ADR-016 identity refresh).
  await expect(page.getByRole("heading", { name: "Simulator User" })).toBeVisible();

  // Back in the list it shows as unread (mark-read happens once per view, on open).
  await page.getByRole("link", { name: "Inbox" }).first().click();
  await expect(page.getByRole("link", { name: /Also, do you have it in blue\?/ }).getByLabel("1 unread")).toBeVisible();
});

test("@desktop Admin takes over and hands back to automation", async ({ page }) => {
  await signIn(page, "admin");
  // ·open was taken over by the operator's reply in the first test.
  await openConversation(page, "Simulator user ·open");

  await page.getByRole("button", { name: "Hand back to automation" }).click();
  await expect(page.getByText("Automation on").first()).toBeVisible();
  await page.getByRole("button", { name: "Take over" }).click();
  await expect(page.getByText("Automation paused").first()).toBeVisible();
});

test("@desktop Owner sees a clear denial outside the reply window", async ({ page }) => {
  await signIn(page, "owner");
  await openConversation(page, "Simulator user ·tale");

  await expect(page.getByText(/more than 24 hours ago/)).toBeVisible();
  await page.getByLabel("Reply").fill("Sorry for the late reply!");
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Reply window closed" })).toBeVisible();
  // Nothing was created, so the draft stays in the composer and there is no retry bubble.
  await expect(page.getByLabel("Reply")).toHaveValue("Sorry for the late reply!");
  await expect(page.getByRole("button", { name: "Try again" })).toHaveCount(0);
  await page.screenshot({ path: `${SHOTS}/desktop-window-denial.png`, fullPage: true });

  const id = page.url().split("/inbox/")[1];
  const messages = await api(page, `/v1/conversations/${id}/messages`);
  expect(messages.body.items.filter((m: { direction: string }) => m.direction === "outbound")).toHaveLength(0);
});

test("@desktop @mobile Viewer can read but not change anything", async ({ page }, testInfo) => {
  await signIn(page, "viewer");
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-viewer-inbox.png`, fullPage: true });
  await openConversation(page, "Simulator user ·open");

  await expect(page.getByText(/View-only access/)).toBeVisible();
  await expect(page.getByLabel("Reply")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Take over" })).toHaveCount(0);
  await page.screenshot({ path: `${SHOTS}/${testInfo.project.name}-viewer-conversation.png`, fullPage: true });

  const id = page.url().split("/inbox/")[1];
  const forced = await api(page, `/v1/conversations/${id}/takeover`, "POST");
  expect(forced.status).toBe(403);
});

test("@mobile Inbox fits a 360 px phone screen without horizontal scrolling", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 780 });
  await signIn(page, "operator");
  const listOverflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  await openConversation(page, "Simulator user ·open");
  const conversationOverflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  await page.screenshot({ path: `${SHOTS}/mobile-chromium-operator-conversation.png`, fullPage: true });

  expect(listOverflow).toBeLessThanOrEqual(0);
  expect(conversationOverflow).toBeLessThanOrEqual(0);
});
