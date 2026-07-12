import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

function getFilesRecursive(dir: string, ext: string): string[] {
  const results: string[] = [];
  if (!fs.existsSync(dir)) return results;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...getFilesRecursive(fullPath, ext));
    } else if (entry.name.endsWith(ext)) {
      results.push(fullPath);
    }
  }
  return results;
}

describe("Fixture import boundary", () => {
  it("no component file imports directly from fixtures/", () => {
    const componentsDir = path.resolve(__dirname, "../components");
    const appDir = path.resolve(__dirname, "../app");
    const violations: string[] = [];

    for (const dir of [componentsDir, appDir]) {
      const files = [
        ...getFilesRecursive(dir, ".tsx"),
        ...getFilesRecursive(dir, ".ts"),
      ];

      for (const file of files) {
        const content = fs.readFileSync(file, "utf-8");
        if (
          content.includes("fixtures/data") ||
          content.includes("adapters/fixtures") ||
          content.includes("from \"@/lib/adapters/fixtures")
        ) {
          violations.push(path.relative(process.cwd(), file));
        }
      }
    }

    expect(violations).toEqual([]);
  });

  it("no component file imports directly from mock adapters", () => {
    const componentsDir = path.resolve(__dirname, "../components");
    const appDir = path.resolve(__dirname, "../app");
    const violations: string[] = [];

    for (const dir of [componentsDir, appDir]) {
      const files = [
        ...getFilesRecursive(dir, ".tsx"),
        ...getFilesRecursive(dir, ".ts"),
      ];

      for (const file of files) {
        const filePath = path.relative(process.cwd(), file);
        if (filePath.includes("provider")) continue;
        const content = fs.readFileSync(file, "utf-8");
        if (
          content.includes("from \"@/lib/adapters/mock") &&
          !filePath.includes("provider")
        ) {
          violations.push(filePath);
        }
      }
    }

    expect(violations).toEqual([]);
  });
});
