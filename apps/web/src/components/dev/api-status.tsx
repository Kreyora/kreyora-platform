"use client";

import { useEffect, useState } from "react";
import { apiSystemClient, type SystemInfo } from "@/lib/adapters/api";
import { ApiClientError } from "@/lib/api/errors";

type Status = "idle" | "loading" | "connected" | "error";

export function ApiStatus() {
  const apiUrl = process.env.NEXT_PUBLIC_API_URL;
  const isDev = process.env.NODE_ENV === "development";
  const [status, setStatus] = useState<Status>(() => (apiUrl && isDev ? "loading" : "idle"));
  const [info, setInfo] = useState<SystemInfo | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [correlationId, setCorrelationId] = useState<string | null>(null);

  useEffect(() => {
    if (!apiUrl || !isDev) return;

    apiSystemClient
      .getInfo()
      .then((data) => {
        setInfo(data);
        setStatus("connected");
      })
      .catch((err: unknown) => {
        setStatus("error");
        if (err instanceof ApiClientError) {
          setErrorMessage(err.detail);
          setCorrelationId(err.correlationId ?? null);
        } else if (err instanceof Error) {
          setErrorMessage(err.message);
        } else {
          setErrorMessage("Unknown error");
        }
      });
  }, [apiUrl, isDev]);

  if (!apiUrl || !isDev) return null;

  return (
    <div
      data-testid="api-status"
      className="fixed bottom-4 right-4 z-50 rounded-lg border bg-white p-4 text-sm shadow-lg"
      style={{ maxWidth: 320 }}
    >
      <div className="mb-2 font-semibold">API Connectivity</div>

      {status === "loading" && (
        <div className="text-gray-500">Connecting to {apiUrl}...</div>
      )}

      {status === "connected" && info && (
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="inline-block h-2 w-2 rounded-full bg-green-500" />
            <span className="text-green-700">Connected</span>
          </div>
          <div className="text-gray-600">
            <strong>Name:</strong> {info.name}
          </div>
          <div className="text-gray-600">
            <strong>Version:</strong> {info.version}
          </div>
          <div className="text-gray-600">
            <strong>Env:</strong> {info.environment}
          </div>
        </div>
      )}

      {status === "error" && (
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="inline-block h-2 w-2 rounded-full bg-red-500" />
            <span className="text-red-700">Error</span>
          </div>
          <div className="text-gray-600">{errorMessage}</div>
          {correlationId && (
            <div className="text-xs text-gray-400">
              Correlation: {correlationId}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
