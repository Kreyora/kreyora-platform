import type { ApiError } from "@/lib/types/common";

export class ApiClientError extends Error {
  readonly status: number;
  readonly detail: string;
  readonly correlationId?: string;
  readonly errors?: Record<string, string[]>;
  readonly retryAfterSeconds?: number;

  constructor(problem: ApiError, correlationId?: string, retryAfterSeconds?: number) {
    super(problem.title);
    this.name = "ApiClientError";
    this.status = problem.status;
    this.detail = problem.detail;
    this.correlationId = correlationId;
    this.errors = problem.errors;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}
