# Frontend Rules

Applies to: Next.js, TypeScript, UI components, styles, and frontend test files.

## TypeScript

- Use strict TypeScript (`strict: true`) with no `any` escape hatches unless justified and documented.
- All component props, client ports, API responses, and fixture data must be explicitly typed.

## Accessibility and responsiveness

- Implement accessible, responsive, mobile-first interfaces.
- Meet WCAG 2.2 AA for contrast, keyboard operation, focus visibility, semantics, labels, errors, and target sizing.
- Touch targets must be at least 44x44 CSS pixels.
- Honor `prefers-reduced-motion` globally.
- No horizontal page scrolling at supported viewports.

## Data access boundaries

- Components must not import fixture JSON directly.
- All feature data flows through typed client ports (`CatalogClient`, `OrderClient`, etc.).
- Mock and real data adapters implement the same typed port interface.
- Adapter selection must be testable and swappable.

## Server authority

- Server responses are authoritative for product, price, stock, delivery, fees, payment state, and order totals.
- Browser input cannot set price, stock, payment status, or tenant identity.
- Never calculate trusted commerce facts in presentation components.

## Required UX states

Every major route must handle: loading, empty, error, validation, permission-denied, disconnected/stale, quota-warning, and success states.

## Honesty

- No false live-provider, persistence, or AI claims in the UI.
- Simulated/demo workflows must be visibly marked.
- Do not claim localization is complete unless it is verified.

## Visual direction

- Follow `design_files/project_a_milestones/FRONTEND_DESIGN_DIRECTION.md` for all visual decisions.
- White canvas, bold near-black editorial typography, generous whitespace, deliberate grid, restrained neutral components, product-led color, and smooth minimal motion.

## Tests

- Add tests for critical user interactions, navigation, focus management, reduced motion, and adapter selection.
- Component tests verify rendered behavior, not internal implementation details.
