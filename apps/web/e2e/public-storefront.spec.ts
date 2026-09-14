import { expect, test } from "@playwright/test";

test("customer completes the mobile fixture-mode COD journey", async ({ page }) => {
  await page.goto("/store/namaste-crafts");
  await expect(page.getByRole("heading", { name: "All Products" })).toBeVisible();
  await page.getByRole("link", { name: /Dhaka Topi/ }).click();
  await page.getByRole("button", { name: "Add to cart" }).click();
  await page.getByRole("link", { name: "View cart →" }).click();
  await page.getByRole("link", { name: "Proceed to checkout" }).click();

  await page.getByLabel("Full name").fill("Sita Shrestha");
  await page.getByLabel("Phone").fill("9800000000");
  await page.getByLabel("Address line 1").fill("Ward 1");
  await page.getByLabel("District").fill("Kathmandu");
  await page.getByRole("button", { name: "Calculate delivery" }).click();
  await expect(page.getByRole("heading", { name: "Server quote" })).toBeVisible();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Place cash-on-delivery order" }).click();
  await expect(page.getByRole("heading", { name: "Order submitted" })).toBeVisible();
  await expect(page.getByText("NC-2025-0099")).toBeVisible();
});
