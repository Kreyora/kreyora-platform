"use client";

import { useSession } from "@/hooks/use-session";
import type { Role } from "@/lib/types";

const DEMO_ROLES: Role[] = ["owner", "admin", "operator", "viewer"];

export function RoleSwitcher() {
  const { effectiveRole, demoRoleOverride, setDemoRole } = useSession();

  return (
    <div className="rounded-[var(--radius-md)] border border-dashed border-[var(--color-warning)] bg-[var(--color-warning-subtle)] p-3">
      <p className="mb-2 text-[11px] font-semibold uppercase tracking-wider text-[var(--color-warning)]">
        Demo role switcher
      </p>
      <div className="flex flex-wrap gap-1.5">
        {DEMO_ROLES.map((role) => (
          <button
            key={role}
            type="button"
            onClick={() =>
              setDemoRole(role === demoRoleOverride ? null : role)
            }
            className={[
              "rounded-[var(--radius-sm)] px-2.5 py-1 text-xs font-medium capitalize transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)]",
              effectiveRole === role
                ? "bg-[var(--color-surface-dark)] text-[var(--color-on-dark)]"
                : "bg-[var(--color-canvas)] text-[var(--color-ink-secondary)] hover:bg-[var(--color-canvas-subtle)]",
            ].join(" ")}
            aria-pressed={effectiveRole === role}
          >
            {role}
          </button>
        ))}
      </div>
      {demoRoleOverride && (
        <button
          type="button"
          onClick={() => setDemoRole(null)}
          className="mt-2 text-[11px] text-[var(--color-warning)] underline underline-offset-2"
        >
          Reset to actual role
        </button>
      )}
    </div>
  );
}
