import { mkdirSync } from "node:fs";
import path from "node:path";
import { request } from "@playwright/test";

// The API allows 5 sign-ins per 15 minutes in total (global "auth-sign-in" limiter), so each persona signs in
// exactly once here and the suite reuses the saved session cookies. E2E_AUTH_DIR is a run-scoped temp
// directory created and deleted by scripts/e2e-real.mjs.
export const PERSONAS = ["owner", "admin", "operator", "viewer"] as const;
export type Persona = (typeof PERSONAS)[number];

export function authFile(persona: Persona): string {
  return path.join(process.env.E2E_AUTH_DIR ?? "test-results/.auth", `${persona}.json`);
}

export default async function globalSetup() {
  const api = process.env.E2E_API_URL ?? "http://localhost:5030";
  mkdirSync(path.dirname(authFile("owner")), { recursive: true });
  for (const persona of PERSONAS) {
    const context = await request.newContext({ baseURL: api });
    const csrf = (await (await context.get("/v1/auth/csrf")).json()) as { token: string };
    const response = await context.post("/v1/auth/sign-in", {
      headers: { "X-CSRF-Token": csrf.token },
      data: { email: `${persona}@kreyora.test`, password: process.env.E2E_PASSWORD ?? "" },
    });
    if (!response.ok()) throw new Error(`sign-in for ${persona} failed with ${response.status()}`);
    await context.storageState({ path: authFile(persona) });
    await context.dispose();
  }
}
