import { describe, expect, it } from "vitest";
import * as fs from "fs";
import * as path from "path";

const appDirectory = path.resolve(__dirname, "../app/(auth)");
const read = (...parts: string[]) => fs.readFileSync(path.join(appDirectory, ...parts), "utf-8");

describe("authentication routes", () => {
  it("uses real credential submission and has no social login controls", () => {
    const content = read("signin/page.tsx");
    expect(content).toContain("auth.signIn");
    expect(content).toContain('router.push("/workspaces")');
    expect(content).not.toContain("Google");
    expect(content).not.toContain("Facebook");
  });

  it("provides owner registration and email-based reset completion routes", () => {
    expect(read("signup/page.tsx")).toContain("auth.register");
    expect(read("recover/page.tsx")).toContain("auth.requestPasswordReset");
    expect(read("recover/reset/page.tsx")).toContain("auth.resetPassword");
    expect(read("recover/page.tsx")).not.toContain("developmentToken");
    expect(read("recover/page.tsx")).not.toContain("Continue development reset");
  });
});
