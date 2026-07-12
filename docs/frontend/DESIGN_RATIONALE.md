# Design Rationale

> Explains **why** the design system makes each choice, not just **what** it contains.

## Visual direction

Kreyora targets independent Nepali sellers who need to project credibility and professionalism to their social-media customers. The visual language is:

- **Warm neutrals**, not sterile whites or stark blacks. The canvas (`#ffffff`) and canvas-subtle (`#f5f5f2`) provide a clean base with warmth. Borders use `#d9d9d5` instead of pure grey.
- **High-contrast ink** (`#111111` on white) ensures readability across all screen qualities common in the Nepal market.
- **Restrained color** — accent and semantic colors appear only where they carry meaning (success, warning, danger, info). The primary UI chrome is monochromatic, letting seller product imagery stand out.
- **Accessible by default** — every text/background pair meets WCAG 2.1 AA contrast ratios. Automated tests verify this.

## Typography

Two font families provide complete script coverage:

- **Inter** — Latin display and body text. Variable font with weights 100–900.
- **Noto Sans Devanagari** — Devanagari script for Nepali text. Weights 400, 500, 600, 700.

Both are loaded via `next/font/google` with `display: "swap"` for fast first paint and zero layout shift. The font stack falls through Inter → Noto Sans Devanagari → system sans-serif, ensuring mixed-language lines (English + Nepali) render without fallback flicker.

Fluid type scales (`text-display-hero` through `text-meta`) use CSS `clamp()` for responsive sizing without breakpoint jumps. This avoids jarring layout shifts on mobile.

## Spacing

An 8-point spatial system (`--space-1` through `--space-16`) provides consistency. The 4px unit (`--space-1`) exists only for fine adjustments (borders, icon gaps). All component padding and margins snap to the 8pt grid.

## Motion

Motion serves orientation, not decoration:

- **Fade** transitions indicate element appearance/disappearance.
- **Slide** transitions show spatial navigation (drawers, panels).
- **Hover lift** and **press feedback** confirm interactivity.
- **Stagger children** creates reading-order flow in lists.

All motion respects `prefers-reduced-motion: reduce` by setting `animation-duration` and `transition-duration` to near-zero. The `useReducedMotion` hook provides programmatic access for JS-driven animations.

## Component philosophy

- **Composition over configuration** — components expose semantic props (`variant`, `size`, `loading`) rather than accepting raw className overrides for critical states.
- **Headless accessibility primitives** — complex interactive controls (Dialog, Drawer, Select, DropdownMenu, Tabs, Toast) are built on [Radix UI](https://www.radix-ui.com/primitives) primitives. Radix provides battle-tested focus trapping, keyboard navigation, portal rendering, and dismissal behavior. All visual styling remains original and follows `FRONTEND_DESIGN_DIRECTION.md`. No pre-styled design system controls the project's visual identity.
- **44px minimum touch target** — `min-h-11` on all clickable elements respects mobile ergonomics (WCAG 2.5.8).
- **Consistent state matrix** — every component documents its states (default, hover, active, focus, disabled, loading, error, empty) so that no state is accidentally un-styled.

### Radix UI primitive dependency rationale

| Decision | Rationale |
|---|---|
| **Selected** | Radix UI Primitives (`@radix-ui/react-*`) |
| **Why** | Complex ARIA patterns (dialog focus trapping, dropdown keyboard nav, select typeahead, toast auto-dismiss) require significant implementation effort and testing. Radix provides unstyled, composable primitives that handle these correctly without imposing visual opinions. |
| **Alternatives considered** | Hand-written implementations (original M01-S01 approach) — rejected due to incomplete focus trapping, keyboard navigation bugs, and type safety issues discovered during type checking. |
| **Packages used** | `@radix-ui/react-dialog`, `@radix-ui/react-dropdown-menu`, `@radix-ui/react-select`, `@radix-ui/react-tabs`, `@radix-ui/react-toast`, `@radix-ui/react-visually-hidden` |
| **Boundary** | Only **unstyled** Radix primitives are used. No Radix Themes, no pre-styled component library. All visual styling is original Tailwind CSS + CSS custom property tokens. |

## Data access architecture

The port/adapter pattern isolates UI components from data source details:

1. **Types** (`lib/types/`) define domain shapes without import dependencies on any API client.
2. **Ports** (`lib/ports/`) declare async interfaces the UI calls.
3. **Adapters** (`lib/adapters/mock/`) implement ports with deterministic fixture data.
4. **Provider** (`lib/providers/client-provider.tsx`) injects adapters via React Context.

This lets the entire UI work with demo data today and swap to real API adapters later without touching any component code. The fixture boundary is enforced by automated tests: no component or page file may import directly from `adapters/mock/` or `adapters/fixtures/`.
