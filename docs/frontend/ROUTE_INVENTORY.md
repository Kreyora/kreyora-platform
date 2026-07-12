# Route Inventory

> Complete mapping of every route, its owning persona, expected layout, and the milestone step that will build it out.

## Route groups

| Group | Layout | Purpose |
|---|---|---|
| `(marketing)` | Marketing shell (header + footer, no auth) | Public-facing pages for lead generation |
| `(auth)` | Centered card layout (no sidebar, no auth) | Authentication pages |
| `(seller)` | Seller workspace shell (sidebar nav, auth required) | Authenticated seller dashboard and tools |
| `(storefront)` | Storefront shell (store-branded header, no auth) | Public buyer-facing storefront per tenant |

## Marketing routes

| Path | Persona | Step | Notes |
|---|---|---|---|
| `/` | Visitor | M01-S02 | Landing / hero page |
| `/features` | Visitor | M01-S02 | Product feature breakdown |
| `/pricing` | Visitor | M01-S02 | Plan comparison |
| `/demo` | Visitor | M01-S02 | Demo request / interactive preview |
| `/contact` | Visitor | M01-S02 | Contact form |

## Auth routes

| Path | Persona | Step | Notes |
|---|---|---|---|
| `/signin` | Seller (unauthenticated) | M01-S03 | Email + password, social OAuth |
| `/recover` | Seller (unauthenticated) | M01-S03 | Password recovery |

## Seller routes

| Path | Persona | Step | Notes |
|---|---|---|---|
| `/workspaces` | Seller (authenticated) | M01-S03 | Workspace picker (multi-tenant) |
| `/onboarding` | Seller owner | M01-S03 | Guided setup wizard |
| `/dashboard` | Seller owner / operator | M01-S03 | Overview metrics, recent orders, alerts |
| `/catalog` | Seller owner / operator | M01-S04 | Product list with search and filters |
| `/catalog/new` | Seller owner / operator | M01-S04 | Create product form |
| `/catalog/[id]` | Seller owner / operator | M01-S04 | Edit product detail |
| `/catalog/[id]/inventory` | Seller owner / operator | M01-S04 | Per-product inventory adjustments |
| `/inventory/low-stock` | Seller owner / operator | M01-S04 | Cross-product low-stock alerts |
| `/orders` | Seller owner / operator | M01-S06 | Order list with filters |
| `/orders/[id]` | Seller owner / operator | M01-S06 | Order detail + fulfillment actions |
| `/inbox` | Seller operator | M01-S06 | Conversation list (all channels) |
| `/inbox/[id]` | Seller operator | M01-S06 | Conversation thread + AI handoff |
| `/storefront` | Seller owner | M01-S07 | Storefront config (profile, SEO) |
| `/storefront/delivery` | Seller owner | M01-S07 | Delivery rules and zones |
| `/storefront/payments` | Seller owner | M01-S07 | Payment method configuration |
| `/storefront/preview` | Seller owner | M01-S07 | Live preview of published storefront |
| `/integrations` | Seller owner / admin | M01-S08 | Channel connection list |
| `/integrations/[id]` | Seller owner / admin | M01-S08 | Connection detail + health |
| `/assistant` | Seller owner / admin | M01-S09 | AI assistant overview |
| `/assistant/knowledge` | Seller owner / admin | M01-S09 | Knowledge base management |
| `/assistant/console` | Seller owner / admin | M01-S09 | Live assistant test console |
| `/assistant/history` | Seller owner / admin | M01-S09 | AI action log |
| `/analytics` | Seller owner | M01-S10 | Revenue, conversion, channel dashboards |
| `/billing` | Seller owner | M01-S10 | Plan, subscription, quota, usage |
| `/team` | Seller owner / admin | M01-S10 | Team member management |
| `/settings` | Seller owner | M01-S10 | Workspace-level settings |
| `/audit` | Seller owner | M01-S10 | Audit event log |

## Storefront routes

| Path | Persona | Step | Notes |
|---|---|---|---|
| `/store/[slug]` | Customer | M01-S05 | Store home (collections, featured products) |
| `/store/[slug]/collection/[id]` | Customer | M01-S05 | Collection product grid |
| `/store/[slug]/product/[id]` | Customer | M01-S05 | Product detail page |
| `/store/[slug]/cart` | Customer | M01-S05 | Shopping cart |
| `/store/[slug]/checkout` | Customer | M01-S05 | Checkout flow |
| `/store/[slug]/confirmation/[orderId]` | Customer | M01-S05 | Order confirmation |
| `/store/[slug]/order-lookup` | Customer | M01-S05 | Track order by number |

## Implementation notes

- 41 routes have placeholder `page.tsx` files across 4 route groups (marketing, auth, seller, storefront).
- Each route group has its own `layout.tsx`: marketing header/footer, auth centered card, seller sidebar workspace, storefront branded header.
- Auth pages (`signin`, `recover`) do **not** inherit the seller workspace sidebar.
- Route placeholders are minimal: an `<h1>` with the page title and a `<p>` describing the future content.
- Viewer-applicable pages include a provisional `ViewerBadge` component indicating read-only access.
- Role matrix is **provisional** (see `ROLE_MATRIX.md`). Frontend route visibility is not an authorization boundary.
- No actual feature logic exists yet; each step will replace the placeholder with the real implementation.
