import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

// Route files only; inbox behaviour is covered by rendered tests in inbox-ui.test.tsx and inbox-data.test.ts
// (M08-S06 replaced the earlier source-string assertions).
const APP_DIR = path.resolve(__dirname, "../app");

describe("Inbox — route file verification", () => {
  const routes = [
    { path: "(seller)/inbox", label: "conversation list" },
    { path: "(seller)/inbox/[id]", label: "conversation detail" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      expect(fs.existsSync(path.join(APP_DIR, route.path, "page.tsx"))).toBe(true);
    });
  }
});
