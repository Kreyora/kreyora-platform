import { expect, test } from "@playwright/test";

test("demo inbox is labelled and simulated actions never reach an API", async ({ page }) => {
  const apiCalls: string[] = [];
  page.on("request", (request) => {
    if (request.url().includes("/v1/")) apiCalls.push(request.url());
  });

  await page.goto("/inbox");
  await expect(page.getByText(/Demo data — nothing is sent/)).toBeVisible();
  await page.getByRole("link", { name: /Priya Karki/ }).click();
  await expect(page.getByText(/Demo data — replies are simulated/)).toBeVisible();
  await page.getByLabel("Reply").fill("Demo reply");
  await page.getByRole("button", { name: "Send" }).click();
  const bubble = page.getByRole("region", { name: "Messages" }).locator("ol > li").filter({ hasText: "Demo reply" });
  await expect(bubble).toBeVisible();
  await expect(bubble.getByText("Sent")).toBeVisible({ timeout: 10_000 });

  expect(apiCalls).toEqual([]);
});
