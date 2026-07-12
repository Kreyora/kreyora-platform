# Token Map

> Maps every CSS custom property to its purpose and value. Source of truth: `web/src/app/globals.css`.

## Color tokens

| Token | Value | Purpose |
|---|---|---|
| `--color-canvas` | `#ffffff` | Default page background |
| `--color-canvas-subtle` | `#f5f5f2` | Secondary background (cards, hover states) |
| `--color-ink-primary` | `#111111` | Primary text, headings |
| `--color-ink-secondary` | `#626262` | Secondary text, captions |
| `--color-border` | `#d9d9d5` | Borders, dividers |
| `--color-surface-dark` | `#1e2021` | Dark backgrounds (buttons, sidebar) |
| `--color-on-dark` | `#f5f5f2` | Text on dark surfaces |
| `--color-accent` | `#d4a574` | Accent highlights (warm gold) |
| `--color-success` | `#1a7a3a` | Positive states (confirmed, in-stock) |
| `--color-warning` | `#9a6700` | Caution states (low-stock, pending) |
| `--color-danger` | `#c93131` | Error and destructive states |
| `--color-info` | `#1a6fb5` | Informational states |
| `--color-focus-ring` | `#1a6fb5` | Focus-visible outline color |
| `--color-success-subtle` | `#e6f4ea` | Background tint for success badges |
| `--color-warning-subtle` | `#fff8e1` | Background tint for warning badges |
| `--color-danger-subtle` | `#fdecea` | Background tint for danger badges |
| `--color-info-subtle` | `#e3f2fd` | Background tint for info badges |

## Spacing tokens

| Token | Value | Tailwind class |
|---|---|---|
| `--space-0` | `0px` | `p-0`, `m-0`, etc. |
| `--space-1` | `4px` | `p-[var(--space-1)]` |
| `--space-2` | `8px` | `gap-2`, etc. |
| `--space-3` | `12px` | `p-[var(--space-3)]` |
| `--space-4` | `16px` | `p-4`, `gap-4` |
| `--space-5` | `20px` | `p-5` |
| `--space-6` | `24px` | `p-6` |
| `--space-8` | `32px` | `p-8` |
| `--space-10` | `40px` | `p-10` |
| `--space-12` | `48px` | `p-12` |
| `--space-16` | `64px` | `p-16` |

## Radius tokens

| Token | Value | Use |
|---|---|---|
| `--radius-sm` | `4px` | Small elements (badges, chips) |
| `--radius-md` | `8px` | Buttons, inputs, cards |
| `--radius-lg` | `12px` | Modals, drawers |
| `--radius-full` | `9999px` | Avatars, pills |

## Elevation tokens

| Token | Value | Use |
|---|---|---|
| `--shadow-sm` | `0 1px 2px rgba(0,0,0,0.06)` | Cards, hoverable surfaces |
| `--shadow-md` | `0 4px 12px rgba(0,0,0,0.08)` | Dropdowns, popovers |
| `--shadow-lg` | `0 8px 24px rgba(0,0,0,0.12)` | Modals, drawers |

## Motion tokens

| Token | Value | Use |
|---|---|---|
| `--duration-instant` | `100ms` | Micro-interactions (hover, press) |
| `--duration-hover` | `150ms` | Hover transitions |
| `--duration-enter` | `200ms` | Element enter animations |
| `--duration-leave` | `150ms` | Element exit animations |
| `--duration-move` | `300ms` | Layout shifts, slides |
| `--easing-default` | `cubic-bezier(0.4, 0, 0.2, 1)` | General transitions |
| `--easing-in` | `cubic-bezier(0.4, 0, 1, 1)` | Exit animations |
| `--easing-out` | `cubic-bezier(0, 0, 0.2, 1)` | Enter animations |

## Grid tokens

| Token | Value | Use |
|---|---|---|
| `--grid-max-width` | `1280px` | Maximum content width |
| `--grid-gutter` | `24px` | Default column gap |
| `--grid-margin` | `16px` | Page edge margin (mobile) |
| `--grid-margin-lg` | `32px` | Page edge margin (desktop) |

## Typography — Font stack

| CSS variable | Family | Use |
|---|---|---|
| `--font-inter` | Inter | Latin display and body text |
| `--font-noto-devanagari` | Noto Sans Devanagari | Devanagari script (Nepali) |

### Resolved font stack

```css
font-family:
  "Inter",
  "Noto Sans Devanagari",
  ui-sans-serif,
  system-ui,
  -apple-system,
  sans-serif;
```

### Language coverage

| Script | Family | Weights loaded |
|---|---|---|
| Latin (English, romanized Nepali) | Inter | 100–900 (variable) |
| Devanagari (Nepali) | Noto Sans Devanagari | 400, 500, 600, 700 |

Noto Sans Devanagari is loaded via `next/font/google` with `subsets: ["devanagari"]` and `display: "swap"` for fast first paint.

### Testing checklist

- [ ] Nepali Devanagari text renders correctly
- [ ] English text renders correctly
- [ ] Romanized Nepali renders correctly
- [ ] Mixed-language lines wrap and align properly
- [ ] Regular and bold weights display for both scripts
- [ ] Forms, tables, badges, navigation, and large headings display correctly
- [ ] Wrapping, truncation, and line height are consistent

## TypeScript access

All tokens are also exported as typed objects from `apps/web/src/design-system/tokens/`:

```typescript
import { colors, spacing, motion } from "@/design-system/tokens";

colors.canvas     // "#ffffff"
spacing.space4    // "16px"
motion.durationEnter // "200ms"
```
