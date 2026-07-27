import type { ApiError } from "@/lib/types/common";

export class ApiClientError extends Error {
  readonly status: number;
  readonly detail: string;
  readonly correlationId?: string;
  readonly errors?: Record<string, string[]>;

  constructor(problem: ApiError, correlationId?: string) {
    super(problem.title);
    this.name = "ApiClientError";
    this.status = problem.status;
    this.detail = problem.detail;
    this.correlationId = correlationId;
    this.errors = problem.errors;
  }
}
