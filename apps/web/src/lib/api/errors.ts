import type { ApiError } from "@/lib/types/common";

export class ApiClientError extends Error {
  readonly status: number;
  /** RFC 7807 problem type; carries stable reason codes such as `urn:kreyora:problem:window_closed`. */
  readonly type?: string;
  readonly detail: string;
  readonly correlationId?: string;
  readonly errors?: Record<string, string[]>;
  readonly retryAfterSeconds?: number;

  constructor(problem: ApiError, correlationId?: string, retryAfterSeconds?: number) {
    super(problem.title);
    this.name = "ApiClientError";
    this.status = problem.status;
    this.type = problem.type;
    this.detail = problem.detail;
    this.correlationId = correlationId;
    this.errors = problem.errors;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}
