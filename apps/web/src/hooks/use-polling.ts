"use client";

import { useCallback, useEffect, useRef, useState } from "react";

export const MAX_BACKOFF_MS = 60_000;

export interface PollingState {
  /** Time of the last successful refresh. */
  lastSuccessAt: Date | null;
  /** Consecutive failures since the last success. */
  failureCount: number;
  /** True while refreshes are failing (data may be stale). */
  isStale: boolean;
  /** Refresh now and restart the interval. */
  refreshNow: () => Promise<void>;
}

/** Next delay after `failures` consecutive failures: doubles from the base interval, capped at 60 s. */
export function nextDelay(intervalMs: number, failures: number): number {
  return failures === 0 ? intervalMs : Math.min(MAX_BACKOFF_MS, intervalMs * 2 ** failures);
}

/**
 * Live refresh by polling (M08-S06 decision Q2): runs `refresh` every `intervalMs`, pauses while the tab is
 * hidden, refreshes immediately when it becomes visible again, and backs off after failures.
 */
export function usePolling(refresh: () => Promise<void>, intervalMs: number, enabled = true): PollingState {
  const [lastSuccessAt, setLastSuccessAt] = useState<Date | null>(null);
  const [failureCount, setFailureCount] = useState(0);
  const refreshRef = useRef(refresh);
  const runNowRef = useRef<() => Promise<void>>(async () => {
    await refreshRef.current();
  });

  useEffect(() => {
    refreshRef.current = refresh;
  }, [refresh]);

  useEffect(() => {
    if (!enabled) return undefined;

    let timer: ReturnType<typeof setTimeout> | null = null;
    let failures = 0;
    let running = false;
    let disposed = false;

    const clear = () => {
      if (timer) {
        clearTimeout(timer);
        timer = null;
      }
    };

    const schedule = () => {
      if (disposed || (typeof document !== "undefined" && document.visibilityState === "hidden")) return;
      timer = setTimeout(() => void run(), nextDelay(intervalMs, failures));
    };

    async function run(): Promise<void> {
      clear();
      if (running || disposed) return;
      running = true;
      try {
        await refreshRef.current();
        failures = 0;
        if (!disposed) {
          setFailureCount(0);
          setLastSuccessAt(new Date());
        }
      } catch {
        failures += 1;
        if (!disposed) setFailureCount(failures);
      } finally {
        running = false;
      }
      schedule();
    }

    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        clear();
      } else {
        void run();
      }
    };

    runNowRef.current = run;
    schedule();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      disposed = true;
      clear();
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [enabled, intervalMs]);

  const refreshNow = useCallback(() => runNowRef.current(), []);

  return { lastSuccessAt, failureCount, isStale: failureCount > 0, refreshNow };
}
