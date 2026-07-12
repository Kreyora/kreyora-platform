# Role Matrix (Provisional)

> **Status: PROVISIONAL** — This matrix is a frontend planning artifact for M01. It does not represent authoritative RBAC. Actual authorization will be enforced server-side in Milestone 03 when the backend Identity module is implemented. Frontend route visibility is not a security boundary.

## Roles

| Role | Description |
|---|---|
| Owner | Full workspace control. Creates workspace, manages team, billing, settings. |
| Admin | Can manage integrations, storefront config, team members. Cannot manage billing. |
| Operator | Handles conversations, orders, catalog updates. Day-to-day commerce work. |
| Viewer | Read-only access to most surfaces. Cannot create, edit, or take actions. |
| PlatformSupport | Audited cross-tenant support access (not exposed in M01 frontend). |

## Route access matrix

| Route | Owner | Admin | Operator | Viewer |
|---|---|---|---|---|
| `/dashboard` | full | full | full | read-only |
| `/catalog` | full | full | full | read-only |
| `/catalog/new` | full | full | full | hidden |
| `/catalog/[id]` | full | full | full | read-only |
| `/catalog/[id]/inventory` | full | full | full | read-only |
| `/inventory/low-stock` | full | full | full | read-only |
| `/orders` | full | full | full | read-only |
| `/orders/[id]` | full | full | full | read-only |
| `/inbox` | full | full | full | read-only |
| `/inbox/[id]` | full | full | full | read-only |
| `/storefront` | full | full | hidden | hidden |
| `/storefront/delivery` | full | full | hidden | hidden |
| `/storefront/payments` | full | full | hidden | hidden |
| `/storefront/preview` | full | full | view | view |
| `/integrations` | full | full | hidden | hidden |
| `/integrations/[id]` | full | full | hidden | hidden |
| `/assistant` | full | full | view | hidden |
| `/assistant/knowledge` | full | full | hidden | hidden |
| `/assistant/console` | full | full | view | hidden |
| `/assistant/history` | full | full | view | read-only |
| `/analytics` | full | view | hidden | read-only |
| `/billing` | full | hidden | hidden | hidden |
| `/team` | full | full | hidden | hidden |
| `/settings` | full | hidden | hidden | hidden |
| `/audit` | full | view | hidden | read-only |

## Viewer behavior in M01

Viewer role is demonstrated in route placeholders as a conceptual indicator. Pages that support Viewer access display a note: "Viewer role: read-only access". This is **not** enforced — it is a visual planning cue for future implementation.

The actual enforcement will happen via:
1. Server-side policy-based authorization (M03)
2. Frontend route guards querying the session's role
3. Conditional rendering of action buttons based on permissions
