"use client";

import Link from "next/link";
import * as DropdownMenuPrimitive from "@radix-ui/react-dropdown-menu";
import { useSession } from "@/hooks/use-session";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { useRouter } from "next/navigation";
import { clearCsrfToken } from "@/lib/adapters/api/auth-client";
import { clearSelectedWorkspace } from "@/lib/session/workspace-selection";
import { useAuthClient } from "@/lib/providers/client-provider";

export function ProfileMenu() {
  const router = useRouter(); const auth = useAuthClient();
  const { session, effectiveRole, demoRoleOverride } = useSession();
  const user = session?.user;

  return (
    <DropdownMenuPrimitive.Root>
      <DropdownMenuPrimitive.Trigger asChild>
        <button
          type="button"
          className="flex min-h-11 items-center gap-2 rounded-[var(--radius-md)] px-2 text-sm transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)] focus-visible:outline-2 focus-visible:outline-[var(--color-focus-ring)] focus-visible:outline-offset-2"
          aria-label="Account menu"
        >
          <Avatar
            name={user?.displayName ?? "User"}
            size="sm"
          />
          <span className="hidden text-sm font-medium text-[var(--color-ink-primary)] lg:inline">
            {user?.displayName ?? "User"}
          </span>
        </button>
      </DropdownMenuPrimitive.Trigger>

      <DropdownMenuPrimitive.Portal>
        <DropdownMenuPrimitive.Content
          align="end"
          sideOffset={8}
          className="z-50 min-w-[220px] rounded-[var(--radius-lg)] border border-[var(--color-border)] bg-[var(--color-canvas)] p-1.5 shadow-[var(--shadow-md)] animate-fade-in"
        >
          {/* User info */}
          <div className="px-3 py-2">
            <p className="text-sm font-medium text-[var(--color-ink-primary)]">
              {user?.displayName ?? "User"}
            </p>
            <p className="text-xs text-[var(--color-ink-secondary)]">
              {user?.email ?? "user@example.com"}
            </p>
            <div className="mt-1.5 flex items-center gap-2">
              <Badge variant="neutral">
                {effectiveRole}
                {demoRoleOverride ? " (demo)" : ""}
              </Badge>
            </div>
          </div>

          <DropdownMenuPrimitive.Separator className="my-1.5 h-px bg-[var(--color-border)]" />

          <DropdownMenuPrimitive.Item asChild>
            <Link
              href="/workspaces"
              className="flex min-h-9 cursor-pointer items-center rounded-[var(--radius-md)] px-3 text-sm text-[var(--color-ink-secondary)] outline-none transition-colors data-[highlighted]:bg-[var(--color-canvas-subtle)] data-[highlighted]:text-[var(--color-ink-primary)]"
            >
              Switch workspace
            </Link>
          </DropdownMenuPrimitive.Item>

          <DropdownMenuPrimitive.Item onSelect={() => { void auth.signOut().finally(() => { clearCsrfToken(); clearSelectedWorkspace(); router.push("/signin"); }); }}>
            <button
              type="button"
              className="flex min-h-9 cursor-pointer items-center rounded-[var(--radius-md)] px-3 text-sm text-[var(--color-ink-secondary)] outline-none transition-colors data-[highlighted]:bg-[var(--color-canvas-subtle)] data-[highlighted]:text-[var(--color-ink-primary)]"
            >
              Sign out
            </button>
          </DropdownMenuPrimitive.Item>
        </DropdownMenuPrimitive.Content>
      </DropdownMenuPrimitive.Portal>
    </DropdownMenuPrimitive.Root>
  );
}
