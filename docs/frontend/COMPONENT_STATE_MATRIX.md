# Component State Matrix

> Documents every UI component and the states it must render correctly.

## State definitions

| State | Meaning |
|---|---|
| **default** | Normal resting state |
| **hover** | Mouse over (desktop) or long-press preview (mobile) |
| **active** | Being clicked/pressed |
| **focus** | Keyboard focus with `focus-visible` outline |
| **disabled** | Non-interactive, reduced opacity |
| **loading** | Async operation in progress; shows spinner or skeleton |
| **error** | Validation failure or destructive warning |
| **empty** | No content to display; shows placeholder guidance |

## Component matrix

### Button (`components/ui/button.tsx`)

| Prop | Options |
|---|---|
| `variant` | `solid`, `outline`, `ghost` |
| `size` | `sm`, `md`, `lg` |

| State | Behaviour |
|---|---|
| default | Styled per variant |
| hover | `opacity-90` (solid), `bg-canvas-subtle` (outline/ghost) |
| active | `scale-[0.97]`, `opacity-80` (solid) |
| focus | `outline-2 outline-focus-ring outline-offset-2` |
| disabled | `opacity-50`, `pointer-events-none`, `cursor-not-allowed` |
| loading | `aria-busy=true`, spinner icon, children hidden from AT via `aria-hidden` |

### IconButton (`components/ui/icon-button.tsx`)

| Prop | Options |
|---|---|
| `variant` | `solid`, `outline`, `ghost` |
| `size` | `sm`, `md`, `lg` |
| `label` | **Required** — used as `aria-label` |

Same state matrix as Button.

### Input (`components/ui/input.tsx`)

| State | Behaviour |
|---|---|
| default | Bordered, `h-11` min touch target |
| hover | — |
| focus | `ring-2 ring-focus-ring` |
| disabled | `opacity-50`, `cursor-not-allowed` |
| error | `border-danger`, `ring-danger`, `aria-invalid=true` |
| readonly | Visually subdued, not editable |

### Textarea (`components/ui/textarea.tsx`)

Same state matrix as Input but with vertical resize enabled.

### Select (`components/ui/select.tsx`) — Radix: `@radix-ui/react-select`

Compound component: `Select`, `SelectTrigger`, `SelectValue`, `SelectContent`, `SelectItem`, `SelectGroup`, `SelectLabel`, `SelectSeparator`.

| State | Behaviour |
|---|---|
| default | Bordered trigger with chevron, `h-11` min touch target |
| open | Portal-rendered dropdown with scroll, shadow |
| focus | `ring-2 ring-focus-ring` |
| disabled | `opacity-50`, `cursor-not-allowed` |
| error | `data-error` attribute changes border to `var(--color-danger)` |
| highlighted (item) | `bg-canvas-subtle` via `data-[highlighted]` |

Radix handles: keyboard navigation, typeahead search, scroll-into-view, portal rendering.

### Checkbox (`components/ui/checkbox.tsx`)

| State | Behaviour |
|---|---|
| default | Unchecked box |
| checked | Filled box with checkmark |
| indeterminate | Dash indicator |
| hover | Border color change |
| focus | `ring-2 ring-focus-ring` |
| disabled | `opacity-50`, non-interactive |

### Label (`components/ui/label.tsx`)

| State | Behaviour |
|---|---|
| default | `text-sm font-medium` |
| required | Red asterisk indicator |

### Badge (`components/ui/badge.tsx`)

| Prop | Options |
|---|---|
| `variant` | `neutral`, `success`, `warning`, `danger`, `info` |

Single visual state per variant — no interactive states.

### Avatar (`components/ui/avatar.tsx`)

| Prop | Options |
|---|---|
| `size` | `sm` (32px), `md` (40px), `lg` (48px) |

| State | Behaviour |
|---|---|
| default | Shows image or initials fallback |
| loading | Skeleton pulse |
| error (image) | Falls back to initials |

### Skeleton (`components/ui/skeleton.tsx`)

Single state: animated pulse placeholder. Used inside other components during loading.

### EmptyState (`components/ui/empty-state.tsx`)

Single state: centered icon, title, description, optional action button.

### ErrorBoundary (`components/ui/error-boundary.tsx`)

| State | Behaviour |
|---|---|
| normal | Renders children transparently |
| error | Shows fallback UI with error title, message, and retry button |

### Table (`components/ui/table.tsx`)

Compound component: `Table`, `TableHeader`, `TableBody`, `TableRow`, `TableHead`, `TableCell`.

| State | Behaviour |
|---|---|
| default | Bordered rows, header background |
| hover (row) | `bg-canvas-subtle` |
| empty | Consumer wraps in EmptyState |

### Dialog (`components/ui/dialog.tsx`) — Radix: `@radix-ui/react-dialog`

Compound component: `Dialog`, `DialogTrigger`, `DialogContent`, `DialogHeader`, `DialogFooter`, `DialogTitle`, `DialogDescription`, `DialogClose`.

| State | Behaviour |
|---|---|
| closed | Not rendered (portal removed) |
| open | Centered modal with backdrop overlay, Radix focus trap, `aria-modal=true`, escape key dismissal |

Radix handles: focus trapping, scroll lock, click-outside dismiss, escape key, portal rendering.

### Drawer (`components/ui/drawer.tsx`) — Radix: `@radix-ui/react-dialog`

Built on Radix Dialog primitives. Slides from the right edge. Same accessibility guarantees as Dialog.

Compound component: `Drawer`, `DrawerTrigger`, `DrawerContent`, `DrawerHeader`, `DrawerFooter`, `DrawerTitle`, `DrawerDescription`, `DrawerClose`.

### Toast (`components/ui/toast.tsx`) — Radix: `@radix-ui/react-toast`

Compound component: `ToastProvider`, `ToastViewport`, `Toast`, `ToastTitle`, `ToastDescription`, `ToastAction`, `ToastClose`.

| Prop | Options |
|---|---|
| `variant` | `success`, `warning`, `danger`, `info` |

| State | Behaviour |
|---|---|
| entering | Slide in from right (`data-[state=open]`) |
| visible | Static with Radix auto-dismiss countdown |
| exiting | Slide out right (`data-[state=closed]`) |
| dismissed | Removed from DOM by Radix |

Radix handles: auto-dismiss timer, swipe-to-dismiss, focus management, viewport stacking.

### Tabs (`components/ui/tabs.tsx`) — Radix: `@radix-ui/react-tabs`

Compound component: `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent`.

| State | Behaviour |
|---|---|
| default (trigger) | Inactive styling |
| active (trigger) | Bottom border accent, `font-semibold`, `data-[state=active]` |
| focus (trigger) | `outline-2 outline-focus-ring` |

Keyboard: Radix handles arrow key navigation, `aria-selected`, `role=tablist`/`tab`/`tabpanel`.

### Breadcrumb (`components/ui/breadcrumb.tsx`)

| State | Behaviour |
|---|---|
| default (link) | Secondary ink color |
| current | Bold, `aria-current=page` |
| hover (link) | Underline |

### Divider (`components/ui/divider.tsx`)

Single state: `1px` border in `--color-border`. Supports horizontal and vertical orientation.

### DropdownMenu (`components/ui/dropdown-menu.tsx`) — Radix: `@radix-ui/react-dropdown-menu`

Compound component: `DropdownMenu`, `DropdownMenuTrigger`, `DropdownMenuContent`, `DropdownMenuItem`, `DropdownMenuLabel`, `DropdownMenuSeparator`.

| State | Behaviour |
|---|---|
| closed | Only trigger visible |
| open | Portal-rendered content below trigger with shadow |
| highlighted (item) | `bg-canvas-subtle` via `data-[highlighted]` |
| disabled (item) | `opacity-50`, skipped in keyboard nav via `data-[disabled]` |
| destructive (item) | `text-danger`, `bg-danger-subtle` on highlight |

Radix handles: keyboard navigation (arrow keys, Home/End, typeahead), click-outside dismiss, focus management, portal rendering.

## Accessibility invariants (all components)

- Minimum 44px touch target height (`min-h-11`).
- `focus-visible` outline on keyboard navigation.
- Appropriate ARIA roles and attributes.
- Reduced-motion support via CSS media query.
- Semantic HTML elements where applicable.
