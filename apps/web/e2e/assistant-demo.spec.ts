import { expect, test } from "@playwright/test";

// M09-S08: the assistant screens in demo mode are labelled, never call an API, and fit a phone screen.
test("demo assistant screens are labelled and never reach an API", async ({ page }) => {
  const apiCalls: string[] = [];
  page.on("request", (request) => {
    if (request.url().includes("/v1/")) apiCalls.push(request.url());
  });

  await page.goto("/assistant");
  await expect(page.getByRole("heading", { name: "Assistant" })).toBeVisible();
  await expect(page.getByText(/Demo data — changes stay in this browser/)).toBeVisible();
  await expect(page.getByRole("heading", { name: "Status" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  await page.getByLabel("Tone").selectOption("formal");
  await page.getByRole("button", { name: "Save settings" }).click();
  await expect(page.getByText("Saved. Settings are marked reviewed.")).toBeVisible();

  await page.getByRole("link", { name: "Console" }).click();
  await expect(page.getByText(/Demo mode: replies are samples/)).toBeVisible();
  await page.getByLabel("Customer message").fill("Red kurta kati ho?");
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.getByText(/\[Demo AI\]/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);

  await page.getByRole("link", { name: "History" }).click();
  await expect(page.getByRole("list", { name: "Assistant turns" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);

  await page.getByRole("link", { name: "Knowledge" }).click();
  await expect(page.getByRole("list", { name: "Knowledge documents" })).toBeVisible();

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  expect(apiCalls).toEqual([]);
});

test("the demo inbox queue shows who is waiting for a person", async ({ page }) => {
  await page.goto("/inbox");
  await page.getByRole("tab", { name: "Needs a person" }).click();
  await expect(page.getByText("Payment problem")).toBeVisible();
  await expect(page.getByText(/Waiting/)).toBeVisible();
});
