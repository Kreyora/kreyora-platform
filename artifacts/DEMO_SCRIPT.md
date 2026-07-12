# Kreyora – Milestone 01 Demo Script

> Deterministic walkthrough for demonstrating the complete frontend showcase.
> All data is from mock fixtures — no backend required.

## Prerequisites

```bash
cd apps/web
npm run dev
```

Open `http://localhost:3000` in a modern browser (Chrome/Edge recommended, 1280px+).

---

## Persona 1: Customer (Public Storefront)

### 1.1 Landing Page
1. Navigate to `http://localhost:3000`
2. Observe the marketing landing page with hero section, feature highlights, pricing tiers
3. Scroll to see scroll-triggered animations (respects `prefers-reduced-motion`)
4. Click "Get Started" — verify navigation to sign-in page

### 1.2 Browse Storefront
1. Navigate to `http://localhost:3000/store/namaste-crafts`
2. Observe store header with name, tagline, and published status
3. Browse the product grid — verify product cards with images, titles, prices
4. Click on a product to view the product detail page
5. Verify product detail shows: title, description, price, variants, images, stock status

### 1.3 Add to Cart & Checkout
1. From a product detail page, click "Add to Cart"
2. Navigate to the cart page (`/store/namaste-crafts/cart`)
3. Verify cart shows: item name, quantity controls, line total, subtotal
4. Adjust quantity — observe the subtotal recalculate
5. Click "Checkout" to proceed
6. Fill in the checkout form: name, phone, address, delivery zone, payment method
7. Submit the order
8. Verify the confirmation page shows: order ID, summary, and estimated delivery

### 1.4 Order Lookup
1. From the confirmation page or `/store/namaste-crafts/orders`, enter the order ID
2. Verify order status, items, and delivery details are displayed

---

## Persona 2: Seller Owner

### 2.1 Sign In
1. Navigate to `http://localhost:3000/signin`
2. Click "Sign in with demo account"
3. Observe redirect to the seller dashboard
4. Note the **Demo data** badge in the sidebar — this indicates fixture data

### 2.2 Dashboard Overview
1. Verify the dashboard shows: 4 metric cards (Orders, Revenue, Conversations, AI Credits)
2. Observe setup progress indicator
3. Verify low-stock alert section
4. Verify recent orders list
5. Verify quick action buttons

### 2.3 Catalog Management
1. Navigate to **Catalog** from sidebar
2. Verify product list with search bar and status filters (draft/published)
3. Click "Add Product" — observe the product creation form
4. Fill in product details, add an image URL, set variants, and save (simulated)
5. Click an existing product to view the edit form

### 2.4 Inventory
1. Navigate to **Inventory** from sidebar
2. Verify inventory list with stock levels, low-stock badges
3. Click an item — verify SKU, stock breakdown, reorder point, activity log

### 2.5 Orders & Payments
1. Navigate to **Orders** from sidebar
2. Verify order list with search, status/source/payment filters
3. Observe responsive layout: table on desktop, cards on mobile (resize window)
4. Click an order to view detail page
5. Verify: financial snapshot, customer info, delivery address, payment attempts, activity timeline
6. Observe action buttons based on order status (Confirm, Prepare, Dispatch, etc.)
7. Click an action — verify the confirmation dialog with reason field

### 2.6 Inbox & Conversations
1. Navigate to **Inbox** from sidebar
2. Verify conversation list with channel badges (Facebook, WhatsApp, Instagram)
3. Use search and state/channel filters
4. Click a conversation to view detail
5. Verify message timeline with sender badges, delivery status
6. Observe takeover/release controls for AI automation
7. When human controls, verify the composer is enabled
8. Observe provider health context and AI activity traces

### 2.7 Integrations
1. Navigate to **Integrations** from sidebar
2. Verify connection cards with provider badge, status, capabilities, events count
3. Click a connection to view detail
4. Verify capabilities grid, health section, webhook events table
5. Observe "Replay" button for failed webhooks, "Reconnect" for disconnected

### 2.8 AI Assistant
1. Navigate to **Assistant** from sidebar
2. Verify policy overview: enabled status, language, tone, max iterations, cost budget
3. Navigate to **Knowledge** — verify document list with status badges, lifecycle actions
4. Navigate to **Console** — type a message to see the simulated bot response with tool traces
5. Navigate to **History** — verify action trace log with expandable details

### 2.9 Storefront Admin
1. Navigate to **Storefront** from sidebar
2. **Profile tab**: Verify store name, tagline, contact info, social links, theme colors, readiness checklist
3. **Delivery tab**: Verify delivery rules with zones, fee types, COD badges, active status
4. **Payments tab**: Verify payment method cards (COD, Merchant QR) with enabled status
5. **Preview tab**: Verify public URL link, published/readiness status, "Open Storefront" button

### 2.10 Analytics
1. Navigate to **Analytics** from sidebar
2. Toggle period selector: Today → This Week → This Month
3. Verify metrics update: orders, revenue, conversations, average order value, conversion rate
4. Verify top products table and source/channel breakdowns

### 2.11 Billing
1. Navigate to **Billing** from sidebar
2. Verify plan card: Grow plan, Rs. 1,999/month, active status, period dates
3. Verify plan limits sidebar: products, AI credits, orders/month, integrations, team seats
4. **Key demo point — Quota bars**:
   - Observe 4 quota bars with different fill levels
   - **Green** bar (normal) — products: comfortably under limit
   - **Yellow** bar (warning_70) — AI credits: 70% used
   - Verify each bar shows used/limit counts and level label
   - Note: Mock data demonstrates the 70% threshold; the UI supports all 4 states (normal, 70%, 90%, exceeded)
5. Verify usage events table with metric, quantity, source, date

### 2.12 Team
1. Navigate to **Team** from sidebar
2. Verify team member list with avatars, names, emails, role badges, joined dates
3. Observe role legend at the bottom explaining each role
4. Note the disabled "Invite Member" button (placeholder)

### 2.13 Settings
1. Navigate to **Settings** from sidebar
2. Verify workspace info: name, slug, created date
3. Verify billing link
4. Verify session info: logged-in user, email, role badge
5. Observe danger zone with disabled "Delete Workspace" button

### 2.14 Audit Log
1. Navigate to **Audit** from sidebar
2. Verify audit event table with actor info, type badges, actions, resource details
3. Use resource type and action filters
4. Observe correlation IDs and timestamps

---

## Persona 3: Seller Operator (Restricted Role)

### 3.1 Switch to Operator
1. From the seller dashboard, use the role switcher in the profile menu
2. Select "Operator" role
3. Observe the sidebar updates — some sections may be restricted

### 3.2 Verify Read Access
1. Navigate through: Catalog, Inventory, Orders, Inbox
2. Verify all data is visible and functional

### 3.3 Verify Viewer Mode
1. Switch to "Viewer" role using the role switcher
2. Navigate to Orders — observe the **Viewer** badge
3. Verify action buttons (Confirm, Cancel, etc.) are hidden
4. Navigate to Billing — verify "Manage Subscription" button is hidden
5. Navigate to Team — verify "Invite Member" button is hidden
6. Navigate to Storefront — verify "Save Changes" button is hidden
7. Navigate to Analytics — observe the Viewer badge, data is visible

---

## Cross-Cutting Verification

### Responsive Layout
1. Resize the browser to mobile width (< 640px)
2. Verify the sidebar collapses to a hamburger menu
3. Verify tables switch to card layouts where implemented
4. Verify all touch targets are at least 44px

### Reduced Motion
1. Enable `prefers-reduced-motion: reduce` in browser DevTools
2. Navigate the landing page — verify scroll animations are instant
3. Navigate the seller dashboard — verify transitions are near-instant

### Demo Indicator
1. Verify the "Demo data" badge is visible in the sidebar at all times
2. Verify the demo explanation banner can be collapsed but the badge persists

### Accessibility
1. Tab through the navigation — verify focus-visible outlines
2. Use a screen reader on the billing quota bars — verify ARIA labels read correctly
3. Verify all interactive elements have minimum 44px touch targets

---

## End of Demo

All features demonstrated use deterministic fixture data. No backend, API, or database is required. The showcase is a complete frontend representation of the Kreyora seller platform.
