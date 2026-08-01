import type { ApiError } from "@/lib/types/common";
import { ApiClientError } from "./errors";

const CORRELATION_HEADER = "X-Correlation-ID";

function generateId(): string {
  if (typeof crypto !== "undefined" && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  return `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function getBaseUrl(): string {
  return process.env.NEXT_PUBLIC_API_URL ?? "";
}

export interface ApiFetchOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
}

export async function apiFetch<T>(
  path: string,
  options: ApiFetchOptions = {},
): Promise<T> {
  const { body, headers: extraHeaders, ...rest } = options;
  const correlationId = generateId();

  const headers: Record<string, string> = {
    Accept: "application/json",
    [CORRELATION_HEADER]: correlationId,
    ...(extraHeaders as Record<string, string>),
  };

  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const response = await fetch(`${getBaseUrl()}${path}`, {
    ...rest,
    credentials: "include",
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (!response.ok) {
    const responseCorrelationId =
      response.headers.get(CORRELATION_HEADER) ?? correlationId;

    let problem: ApiError;
    try {
      problem = (await response.json()) as ApiError;
    } catch {
      problem = {
        type: "https://tools.ietf.org/html/rfc9110#section-15",
        title: response.statusText || "Request Failed",
        status: response.status,
        detail: `HTTP ${response.status} ${response.statusText}`,
      };
    }

    throw new ApiClientError(problem, responseCorrelationId);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
