# Milestone 01 — Complete Frontend Showcase

## Objective

Build a polished, fully navigable frontend demonstration of Kreyora before implementing business backends. It must show the complete seller and customer experience with typed fixtures and simulated workflows, while keeping all data access behind replaceable interfaces.

This milestone produces demonstration software, not production commerce. No screen may imply that a provider, payment gateway, AI model, or backend operation is live.

All frontend work must follow [`FRONTEND_DESIGN_DIRECTION.md`](FRONTEND_DESIGN_DIRECTION.md): a dominant white canvas, bold near-black editorial typography, generous whitespace, deliberate grid, restrained neutral components, product-led color, and smooth minimal motion. The Behance project referenced there is inspiration only; do not copy its assets, copy, brand, or exact compositions.

## Dependencies

- Approved Kreyora product scope and route map from `plan.md`.
- No backend dependency.
- Product name, logo, and visual identity may use explicit placeholders until approved.

## Implementation design

### Frontend boundaries

Use a Next.js TypeScript application with three visible surfaces:

1. Public marketing/demo pages.
2. Authenticated seller workspace prototype.
3. Public demo storefront and checkout prototype.

All feature data passes through typed ports such as `CatalogClient`, `OrderClient`, `ConversationClient`, `StorefrontClient`, `IntegrationClient`, and `BillingClient`. The initial adapters read deterministic fixtures or a mock service worker. Components must not import fixture JSON directly.

### Approved visual language

- Clean white background as the default across marketing, seller, and storefront surfaces.
- Oversized bold display typography on marketing/storytelling pages; compact bold hierarchy inside the workspace.
- Editorial 12/8/4-column responsive grid with purposeful asymmetry on marketing pages and predictable alignment in operational views.
- Near-monochrome palette, thin dividers, minimal shadows, modest radii, and restrained outline/solid controls.
- Generous whitespace instead of unnecessary cards, gradients, glass effects, or decorative containers.
- Seller product imagery and semantic status colors provide most non-neutral color.
- Original Kreyora compositions; no reproduction of reference assets or exact layouts.

### Motion language

- Use shared duration/easing tokens and reusable primitives.
- Prefer opacity and short transforms, approximately 12–24 px for entrances and 1–3 px for hover feedback.
- Keep micro-interactions fast, overlays under roughly 300 ms, and section entrances restrained.
- Avoid bounce, scroll hijacking, heavy parallax, continuous floating elements, and decorative cursor effects.
- Honor `prefers-reduced-motion` globally and verify that animation never blocks navigation, input, checkout, or inbox work.
- Test on a mid-range mobile performance profile and prevent animation-related layout shift.

### Required seller routes

- Sign-in, workspace selection, onboarding/readiness.
- Dashboard.
- Catalog, product editor, variants, media, inventory, low-stock view.
- Orders list/detail, payment verification, and fulfilment timeline.
- Unified inbox, conversation detail, AI/human ownership, assignment.
- Storefront editor, delivery rules, payment methods, preview.
- Integrations and connection-health views.
- Assistant policy, knowledge, test console, and action history.
- Analytics, usage/plan, team/RBAC preview, settings, audit activity.

### Required customer routes

- Store home, collection/category, product detail, variant selection.
- Cart, address/contact, delivery quote, COD/QR selection.
- Order confirmation and safe order-lookup/support entry point.

### UX states

Every major route needs realistic loading, empty, error, validation, permission-denied, disconnected, quota-warning, and success states. Demonstrate Nepali Devanagari, English, and Romanized Nepali content without claiming full localization is complete.

## Step status

| Step | Description | Status |
|---:|---|---|
| 01 | Route inventory, design system, and mock architecture | `NOT STARTED` |
| 02 | Marketing site and guided product demo | `NOT STARTED` |
| 03 | Seller shell, authentication screens, and onboarding | `NOT STARTED` |
| 04 | Catalog, inventory, and storefront-management UI | `NOT STARTED` |
| 05 | Public storefront, cart, and checkout UI | `NOT STARTED` |
| 06 | Orders, payments, fulfilment, and notifications UI | `NOT STARTED` |
| 07 | Inbox, integrations, AI assistant, and takeover UI | `NOT STARTED` |
| 08 | Dashboard, billing, settings, responsive QA, and demo script | `NOT STARTED` |

## Prompt 01 — Route inventory, design system, and mock architecture

> Create the frontend foundation for Kreyora. First read `FRONTEND_DESIGN_DIRECTION.md`, then inventory all required routes and produce a route-to-persona matrix before coding. Scaffold or normalize a strict TypeScript Next.js app. Establish an original accessible token system for the approved white canvas, near-black type, neutral surfaces/dividers, semantic colors, fluid bold typography, 12/8/4-column grid, spacing, radii, elevation, and motion. Define reusable motion primitives and global reduced-motion behavior before feature animations. Establish navigation, feedback components, tables, forms, dialogs, mobile patterns, and a component-state matrix. Define domain-facing TypeScript types and client ports for identity, catalog, inventory, storefront, checkout, orders, payments, conversations, integrations, AI, billing, reporting, and audit. Implement deterministic fixture adapters behind those ports; components must never read fixture files directly. Add a visible global “Demo data” indicator. Provide a route manifest, design rationale, token map, component inventory, fixture scenarios, and tests for adapter selection, navigation, focus, and reduced motion. Do not implement feature pages beyond minimal route placeholders.

**Review checkpoint:** approve information architecture, originality, visual/motion tokens, responsive grid, accessibility, client-port boundaries, and fixture scenarios.

## Prompt 02 — Marketing site and guided product demo

> Implement the public Kreyora marketing surface and guided product-demo entry using the approved minimal editorial direction. Use a dominant white canvas, an original oversized short hero statement, bold black type, large whitespace, thin rules, asymmetric text/interface-image compositions, restrained outline/solid calls to action, numbered workflow sections, and at most one purposeful dark contrast section. Present the Nepal social-commerce problem, product workflow, storefront/inbox/AI/inventory capabilities, local-first COD/QR positioning, safety principle that AI is not the commerce source of truth, and clear demo calls to action. Use honest placeholder copy for pricing or provider availability. Build responsive header/footer, feature sections, workflow explanation, FAQ, contact/waitlist placeholders, metadata, social preview placeholders, and smooth minimal entrance/hover/page motion with global reduced-motion fallbacks. Add a guided demo selector for Seller Owner, Seller Operator, and Customer paths. Test navigation, keyboard access, focus visibility, metadata, mobile composition, reduced motion, layout shift, and mid-range mobile smoothness. Do not copy the Behance reference’s assets, text, or exact layouts, and do not add real lead submission or analytics without an approved service.

**Review checkpoint:** approve positioning, claims, navigation, original editorial composition, typography/spacing, motion restraint, mobile behavior, and demo entry paths.

## Prompt 03 — Seller shell, authentication screens, and onboarding

> Implement the seller workspace shell using mock identity and the approved white-canvas design system. Translate the bold editorial language into a quieter operational interface: compact strong page titles, clean hierarchy, thin dividers, minimal elevation, clear focus, and efficient spacing without turning every metric or setting into a floating card. Build sign-in, account recovery placeholder, workspace selection, global navigation, mobile navigation, profile menu, notification center, and role-aware menu states for Owner, Admin, Operator, and Viewer. Implement a resumable onboarding experience covering store profile, catalog readiness, delivery rules, COD/QR setup, channel connection, assistant policy, and activation review. Simulate completed, incomplete, blocked, and permission-denied states through typed fixtures. Clearly mark authentication and saving as simulated. Add component, motion/reduced-motion, accessibility, and end-to-end navigation tests.

**Review checkpoint:** approve seller navigation, role visibility, onboarding order, responsive behavior, and simulated-state clarity.

## Prompt 04 — Catalog, inventory, and storefront-management UI

> Implement complete seller-facing catalog and inventory showcase routes using the approved minimal component language. Include product list/search/filter, product create/edit, variants, media manager, publishing readiness, stock ledger, stock adjustment, reservation visibility, low-stock warnings, bulk-action placeholders, and audit details. Implement storefront administration for brand/profile, controlled theme settings, homepage section ordering, product publication, delivery rules, COD and merchant-QR configuration, policy content, public URL, readiness checks, and preview. Use typed client ports and deterministic fixtures only. Prefer alignment, whitespace, thin rules, and purposeful groups over card-heavy dashboards; use product imagery and semantic states for color. Include validation, unsaved-change protection, optimistic-UI simulation with rollback, empty/error/denied/stale/conflict states, responsive tables/cards where grouping is truly useful, restrained state transitions, and tests. Never imply that mock stock or payment settings are persisted.

**Review checkpoint:** approve domain terminology, form structure, inventory visualization, readiness rules, and store-preview workflow.

## Prompt 05 — Public storefront, cart, and checkout UI

> Build the mobile-first public demo storefront using a clean white editorial product system. Let seller product photography provide most of the color; use bold product names, restrained metadata, clean grids, subtle dividers, and an obvious purchase path. Implement store home, categories/collections, search, product detail, image gallery, variants, availability, cart, contact/address capture, delivery quote display, payment-method selection for COD and merchant QR, order summary, QR proof simulation if enabled, confirmation, and support/order-lookup entry. Use the public storefront client port; do not calculate trusted price, stock, fees, or payment state inside presentation components. Fixture responses must demonstrate price change, unavailable variant, expired quote, delivery-unavailable address, duplicate submit protection, and successful checkout. Motion may support gallery, cart, and route continuity but must become quieter through checkout, respect reduced motion, and never delay purchase actions. Add accessibility, responsive, component, motion, and end-to-end tests.

**Review checkpoint:** approve customer purchase path, mobile usability, error recovery, and the server-authority boundaries represented in the UI.

## Prompt 06 — Orders, payments, fulfilment, and notifications UI

> Implement seller order list/detail routes with search, filters, source, immutable financial snapshot, customer/delivery snapshot, status timelines, inventory allocation, payment state, fulfilment state, activity history, and notification delivery status. Build guarded demonstration actions for confirm, cancel with reason, verify/reject merchant QR, mark COD collected, prepare, dispatch, and deliver. Use mock policy results to show allowed and denied transitions. Add clear confirmation dialogs, actor/reason previews, validation, failure recovery, responsive layouts, and tests for action visibility by role and order state.

**Review checkpoint:** approve operational terminology, independent order/payment/fulfilment states, action safeguards, and audit presentation.

## Prompt 07 — Inbox, integrations, AI assistant, and takeover UI

> Implement the unified inbox showcase: conversation list, channel/customer badges, filters, unread state, assignment, labels, message timeline, delivery state, staff composer, retry display, provider-health context, and human takeover/release controls. Add integration cards and connection-detail views with capability, health, token-expiry, webhook/replay, and reconnect simulations. Add assistant policy, approved knowledge-document lifecycle, test console, tool-trace view, cost/usage indicators, escalation state, and redacted action logs. Demonstrate Nepali, English, and Romanized Nepali conversations. Automation must visibly stop after human takeover. Add tests for role/state actions and the takeover UI invariant.

**Review checkpoint:** approve inbox workflow, provider diagnostics, AI transparency, escalation, and takeover behavior.

## Prompt 08 — Dashboard, billing, settings, responsive QA, and demo script

> Complete the showcase with restrained dashboard projections for setup progress, orders/revenue, open chats, reply time, low stock, integration health, and usage. Show only meaningful metrics and avoid a wall of decorative cards. Add plan/entitlement and 70/90/100 percent quota states using mock data; do not implement subscription collection. Complete team/settings/audit surfaces, global error boundaries, skeletons, empty states, toast behavior, accessibility, responsive polish, shared motion primitives, and reduced-motion fallbacks. Audit the entire product against `FRONTEND_DESIGN_DIRECTION.md`, including white-canvas dominance, typography, grid, whitespace, component restraint, semantic color, animation timing/easing, mobile composition, keyboard/focus, contrast, layout shift, and mid-range mobile smoothness. Run lint, type checking, unit/component tests, and end-to-end tests for Seller Owner, Seller Operator, and Customer journeys. Create a deterministic demo script and desktop/mobile screenshots for key routes, plus a short originality/design-rationale note. Remove dead placeholders unless explicitly labeled as future scope.

**Review checkpoint:** conduct the complete stakeholder walkthrough and approve the frontend contract for backend integration.

## Milestone exit gate

- Every listed route is navigable and visually coherent on mobile and desktop.
- The implementation conforms to `FRONTEND_DESIGN_DIRECTION.md` and is an original design rather than a copy of the reference.
- The white canvas, bold typography, editorial grid, deliberate whitespace, minimal component language, and restrained semantic color are consistently demonstrated.
- Shared motion tokens and primitives are smooth, minimal, `prefers-reduced-motion` safe, and verified on a mid-range mobile profile without interaction delay or avoidable layout shift.
- Three deterministic demo journeys pass end to end.
- Mock data is accessed only through replaceable typed clients.
- No UI makes false live-provider, payment, persistence, or AI claims.
- Accessibility and automated checks pass at the agreed thresholds.
- Route map, token map, component/state inventory, design rationale, desktop/mobile screenshots, demo script, and checkpoint reports are approved.
- The approved mock client interfaces become inputs to Milestone 02 API-contract work.

## Out of scope

- Real authentication, database, uploads, webhooks, AI calls, payments, notifications, or analytics ingestion.
- Production branding or legal/pricing claims without stakeholder approval.
- Arbitrary seller HTML/CSS/JavaScript themes.
