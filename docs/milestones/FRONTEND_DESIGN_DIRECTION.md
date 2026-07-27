# Kreyora — Frontend Design Direction

## Status

**Approved direction:** minimal, modern, white-background interface with bold editorial typography, disciplined spacing, clear hierarchy, and smooth restrained motion.

**Visual reference:** [Minimalism in web-design. Landing page](https://www.behance.net/gallery/206331919/Minimalism-in-web-design-Landing-page) by hanney. (Anna), published August 25, 2024.

The reference is inspiration for visual principles only. Do not copy its brand, wording, images, exact layouts, device compositions, or proprietary assets. Kreyora must have an original design system shaped around social-commerce workflows.

## Design character

Kreyora should feel:

- Confident rather than decorative.
- Modern and premium without looking exclusive or difficult.
- Spacious on marketing/storefront pages and efficiently structured inside the seller workspace.
- Primarily monochrome, using color only for product imagery, status, data meaning, and one future brand accent.
- Calm in motion, with no animation that competes with content or slows task completion.
- Credible for small sellers while polished enough to scale globally.

## Core visual principles

### 1. White canvas and strong contrast

- Default canvas: clean white.
- Primary text: near-black, never low-contrast gray for important content.
- Secondary surfaces: warm or neutral off-white/light gray.
- Borders: subtle neutral hairlines.
- Dark sections may be used sparingly for contrast, demonstrations, or the footer—not as the default application background.
- Use a single approved accent color only when branding is finalized. Until then, use monochrome tokens and semantic status colors.

Suggested starting tokens, subject to accessibility verification:

| Role | Starting value |
|---|---|
| Canvas | `#FFFFFF` |
| Subtle canvas | `#F5F5F2` |
| Primary ink | `#111111` |
| Secondary text | `#626262` |
| Border/divider | `#D9D9D5` |
| Dark contrast surface | `#1E2021` |
| Dark-surface text | `#F5F5F2` |

Semantic success, warning, danger, and information colors must be designed for meaning and WCAG-compliant contrast; they are not decorative accents.

### 2. Bold editorial typography

- Use one strong sans-serif display family and one highly readable sans-serif text family, or a variable family that performs both roles.
- Prefer locally hosted/openly licensed fonts with documented loading and fallback behavior.
- Marketing headlines should be short, oversized, bold, and tightly composed.
- Use fluid type with `clamp()` rather than many device-specific sizes.
- Body copy stays compact, readable, and limited in line length.
- Use weight, size, spacing, and alignment before adding color or decoration.
- Avoid excessive all-caps text, outlined text, gradients inside text, or novelty display fonts.

Suggested hierarchy:

| Role | Direction |
|---|---|
| Display hero | Fluid `clamp()` scale, approximately 64–112 px on large screens, 42–64 px on mobile, bold/black weight |
| Section title | Approximately 40–72 px desktop, 32–48 px mobile |
| Workspace page title | Approximately 28–40 px, compact and task-oriented |
| Body | 16–18 px marketing, 14–16 px application UI |
| Metadata/label | 12–14 px with strong contrast and deliberate tracking |

Exact values must be resolved into tokens and tested across Nepali Devanagari, English, and Romanized Nepali content.

### 3. Editorial grid and whitespace

- Use a deliberate grid: 12 columns on desktop, 8 on tablet, and 4 on mobile.
- Marketing sections may use asymmetry, offset text, large image fields, and varied vertical rhythm.
- Seller workspace pages use the same visual language with denser, predictable alignment for operational speed.
- Use generous section spacing instead of unnecessary cards or colored containers.
- Prefer thin rules, alignment, and whitespace to heavy shadows.
- Keep content width controlled; do not stretch paragraphs, forms, or tables across the entire viewport.
- Use edge-to-edge imagery only when it materially strengthens storytelling.

Suggested layout ranges:

| Token | Direction |
|---|---|
| Maximum marketing width | 1280–1440 px |
| Maximum application width | Responsive; wide data views may use available space with controlled gutters |
| Desktop gutter | 48–72 px |
| Tablet gutter | 32–48 px |
| Mobile gutter | 20–24 px |
| Marketing section spacing | 96–176 px |
| Application section spacing | 32–64 px |

### 4. Minimal component language

- Buttons: simple solid or thin-outline treatments, concise labels, confident type, optional pill shape only where appropriate.
- Inputs: white or subtle-surface fields, clear labels, visible focus, restrained borders, and generous touch targets.
- Cards: use only when grouping is meaningful; favor whitespace and dividers over a page full of floating cards.
- Navigation: low visual noise, clear active state, strong information hierarchy.
- Tables: clean rules, intentional density, sticky headers where useful, responsive fallback, and no decorative gradients.
- Dialogs/drawers: focused, quiet surfaces with one obvious primary action.
- Icons: one consistent minimal icon set; icons support labels rather than replacing unclear language.
- Corners: modest radii; avoid mixing many radius sizes.
- Shadows: subtle and rare, primarily for overlays or layered navigation.
- Glassmorphism, neon effects, large gradient fields, excessive badges, and ornamental illustrations are outside the default direction.

## Surface-specific application

### Marketing site

- Short, oversized hero statement with one primary and one secondary action at most.
- Editorial split-grid sections with product/interface imagery.
- Large whitespace and alternating text/image rhythm.
- Thin dividers and numbered workflow sections.
- One dark contrast section may demonstrate the product or emphasize a core principle.
- Claims must remain specific, verifiable, and easy to scan.

### Seller workspace

- Preserve the white canvas and strong typography, but prioritize task clarity over dramatic composition.
- Use compact page headers, contextual actions, clean filters, and data views.
- Keep dashboards restrained: a small number of meaningful metrics, not a wall of cards.
- Distinguish states using text, icons, position, and semantic color—not color alone.
- Maintain consistent empty, loading, error, denied, stale, conflict, and success states.

### Public storefront

- Let seller product photography provide most of the color.
- Use clean product grids, bold product titles, readable price/availability, and an obvious purchase path.
- Theme controls may adjust approved brand tokens but may not introduce arbitrary seller HTML, CSS, or JavaScript.
- Checkout should become more utilitarian and calm as the customer approaches payment and confirmation.

## Motion system

Motion communicates hierarchy, state, and continuity. It must never be required to understand or complete an action.

### Motion principles

- Animate opacity and transforms where possible; avoid layout-thrashing properties.
- Use one restrained easing family across the product.
- Entrances are subtle: short fade plus approximately 12–24 px movement.
- Stagger only closely related items and keep the total sequence brief.
- Hover movement should be nearly imperceptible—usually 1–3 px or an underline/border transition.
- Page transitions should preserve orientation without delaying navigation.
- Data updates should emphasize only the changed region.
- Avoid bounce, elastic effects, dramatic zooms, continuous floating elements, cursor-following effects, and novelty motion.
- Avoid scroll hijacking and heavy parallax.

Suggested starting motion tokens:

| Motion | Duration | Direction |
|---|---:|---|
| Press/focus feedback | 100–160 ms | Immediate and subtle |
| Hover/state transition | 160–220 ms | Color, border, opacity, 1–3 px transform |
| Menu/dialog/drawer | 200–300 ms | Fade with short translate/scale |
| Page/section entrance | 320–520 ms | Fade with 12–24 px translate |
| Related-item stagger | 40–70 ms | Maximum 5–7 visible items |

Recommended default easing: a smooth ease-out similar to `cubic-bezier(0.22, 1, 0.36, 1)`, verified in the implementation rather than copied blindly.

### Reduced motion and accessibility

- Honor `prefers-reduced-motion` globally.
- Reduced-motion mode removes nonessential translation, parallax, smooth scrolling, and stagger; essential state changes may use instant updates or short opacity changes.
- Focus must never be delayed or hidden by animation.
- Do not autoplay distracting video or animation near forms, inboxes, or checkout.
- Pause controls are required for any nonessential movement lasting more than a brief transition.

### Performance constraints

- Prefer CSS transitions for simple states; use a motion library only when coordinated interaction genuinely requires it.
- Lazy-load noncritical media and below-the-fold motion code.
- Prevent layout shift by reserving image/media dimensions.
- Do not animate large blurred layers or expensive filters on low-powered mobile devices.
- Verify smoothness on a mid-range mobile profile, not only a developer laptop.
- Motion must not block interaction, hydration, route navigation, or first content visibility.

## Responsive behavior

- Design mobile and desktop compositions deliberately; do not merely shrink desktop.
- Large display headings must wrap intentionally and avoid isolated words.
- Asymmetric desktop layouts stack into a logical reading order on mobile.
- Navigation, tables, filters, and actions require explicit mobile patterns.
- Touch targets should be at least 44 by 44 CSS pixels unless a stricter project rule supersedes this.
- No horizontal page scrolling at supported viewports.

## Accessibility requirements

- Meet WCAG 2.2 AA expectations for contrast, keyboard operation, focus visibility, semantics, labels, errors, and target sizing.
- Text must remain text; do not bake important copy into images.
- All visual hierarchy must survive browser zoom and user font scaling.
- Motion, color, hover, and position cannot be the only communication channels.
- Product/UI screenshots need useful alt text or must be marked decorative.
- Verify Devanagari glyph quality, line height, truncation, and input behavior before claiming Nepali-language support.

## Required Milestone 01 design deliverables

- Approved token map covering color, type, spacing, grid, radius, borders, elevation, and motion.
- Desktop and mobile shell for marketing, seller workspace, and public storefront.
- Original hero and product-story composition inspired by the reference principles.
- Reusable motion primitives with reduced-motion behavior.
- Component-state matrix for default, hover, focus, active, disabled, loading, error, denied, empty, stale, and success.
- Screenshot set for agreed breakpoints.
- Accessibility and keyboard review.
- Performance and layout-shift observations.
- A short design rationale showing how the result follows Kreyora’s direction without copying the Behance work.

## Design review checklist

- White canvas remains dominant.
- Typography creates the primary visual hierarchy.
- Whitespace and grid replace unnecessary decoration.
- Product imagery and data meaning—not random gradients—provide color.
- Pages have one obvious primary action.
- Marketing pages feel editorial; workspace pages remain efficient.
- Motion is restrained, consistent, interruptible, and reduced-motion safe.
- Mobile layouts are designed, not compressed.
- Contrast, keyboard navigation, focus, labels, and target sizes pass review.
- No reference assets, wording, or exact compositions were copied.

