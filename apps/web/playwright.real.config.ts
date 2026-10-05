import { defineConfig, devices } from "@playwright/test";

// Real-backend suite (M08-S06). Started by scripts/e2e-real.mjs, which provides the API, web server and
// run-scoped credentials; this config never starts servers itself.
export default defineConfig({
  testDir: "./e2e-real",
  globalSetup: "./e2e-real/global-setup.ts",
  timeout: 120_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  use: { baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3100", trace: "retain-on-failure" },
  projects: [
    { name: "desktop-chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1280, height: 900 } }, grep: /@desktop/ },
    { name: "mobile-chromium", use: { ...devices["Pixel 5"], browserName: "chromium" }, grep: /@mobile/ },
  ],
});
