import { apiFetch } from "@/lib/api";
import type { AuthClient, AuthenticatedUser, RegisterOwnerInput } from "@/lib/ports/auth-client";

let csrfToken: string | null = null;
export function clearCsrfToken(): void { csrfToken = null; }

export async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken;
  const response = await apiFetch<{ token: string }>("/v1/auth/csrf");
  csrfToken = response.token;
  return csrfToken;
}

async function post<T>(path: string, body?: unknown): Promise<T> {
  return apiFetch<T>(path, {
    method: "POST",
    body,
    headers: { "X-CSRF-Token": await getCsrfToken() },
  });
}

export const apiAuthClient: AuthClient = {
  async register(input: RegisterOwnerInput) {
    await post<void>("/v1/auth/register", input);
  },
  async signIn(email, password) {
    await post<void>("/v1/auth/sign-in", { email, password });
  },
  async signOut() {
    await post<void>("/v1/auth/sign-out");
  },
  async getCurrentUser() {
    return apiFetch<AuthenticatedUser>("/v1/auth/me");
  },
  async requestPasswordReset(email) {
    await post<void>("/v1/auth/password-reset/request", { email });
  },
  async resetPassword(email, token, newPassword) {
    await post<void>("/v1/auth/password-reset/confirm", { email, token, newPassword });
  },
};
