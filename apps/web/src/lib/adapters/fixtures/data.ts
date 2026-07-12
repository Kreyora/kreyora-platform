import type {
  Session,
  Tenant,
  User,
  Membership,
  TeamMember,
  OnboardingState,
  Product,
  ProductVariant,
  Collection,
  InventoryItem,
  StockMovement,
  InventoryReservation,
  Store,
  StoreReadiness,
  DeliveryRule,
  Order,
  OrderActivity,
  Conversation,
  Message,
  ChannelConnection,
  ConnectionHealth,
  WebhookEvent,
  AssistantConfig,
  KnowledgeDocument,
  AIActionTrace,
  Plan,
  Subscription,
  QuotaStatus,
  UsageEvent,
  DashboardMetrics,
  AnalyticsSnapshot,
  AuditEvent,
  Cart,
  CheckoutQuote,
} from "@/lib/types";
import type { PaymentMethod, PaymentAttempt } from "@/lib/types/payments";

// ─── IDs ─────────────────────────────────────────────────────────────────────

export const TENANT_ID = "tenant-namaste-crafts";
export const TENANT_SLUG = "namaste-crafts";

export const USER_OWNER_ID = "user-sita-shrestha";
export const USER_OPERATOR_ID = "user-rajan-thapa";

export const STORE_ID = "store-namaste-crafts";
export const CONNECTION_FACEBOOK_ID = "conn-facebook-namaste";

// ─── Users & Identity ────────────────────────────────────────────────────────

export const ownerUser: User = {
  id: USER_OWNER_ID,
  email: "sita@namastecrafts.com.np",
  displayName: "Sita Shrestha",
  avatarUrl: "/fixtures/avatars/sita-shrestha.jpg",
  createdAt: "2025-01-01T08:00:00Z",
};

export const operatorUser: User = {
  id: USER_OPERATOR_ID,
  email: "rajan@namastecrafts.com.np",
  displayName: "Rajan Thapa",
  createdAt: "2025-01-05T09:00:00Z",
};

export const tenant: Tenant = {
  id: TENANT_ID,
  name: "Namaste Crafts",
  slug: TENANT_SLUG,
  createdAt: "2025-01-01T08:00:00Z",
};

export const ownerMembership: Membership = {
  id: "membership-sita-owner",
  userId: USER_OWNER_ID,
  tenantId: TENANT_ID,
  role: "owner",
  joinedAt: "2025-01-01T08:00:00Z",
};

export const operatorMembership: Membership = {
  id: "membership-rajan-operator",
  userId: USER_OPERATOR_ID,
  tenantId: TENANT_ID,
  role: "operator",
  joinedAt: "2025-01-10T10:00:00Z",
};

export const session: Session = {
  user: ownerUser,
  tenant,
  membership: ownerMembership,
};

export const teamMembers: TeamMember[] = [
  { user: ownerUser, membership: ownerMembership },
  { user: operatorUser, membership: operatorMembership },
];

export const onboardingState: OnboardingState = {
  steps: [
    { step: "store_profile", status: "completed", completedAt: "2025-01-02T10:00:00Z" },
    { step: "catalog_readiness", status: "completed", completedAt: "2025-01-03T11:00:00Z" },
    { step: "delivery_rules", status: "completed", completedAt: "2025-01-04T09:00:00Z" },
    { step: "payment_setup", status: "completed", completedAt: "2025-01-05T14:00:00Z" },
    { step: "channel_connection", status: "completed", completedAt: "2025-01-06T16:00:00Z" },
    { step: "assistant_policy", status: "incomplete" },
    { step: "activation_review", status: "completed", completedAt: "2025-01-08T12:00:00Z" },
  ],
  isActivationReady: false,
};

// ─── Catalog ─────────────────────────────────────────────────────────────────

export const collections: Collection[] = [
  {
    id: "col-textiles",
    tenantId: TENANT_ID,
    name: "Textiles",
    slug: "textiles",
    description: "Handwoven and hand-knit Nepali textiles",
    productCount: 2,
    sortOrder: 1,
  },
  {
    id: "col-handicrafts",
    tenantId: TENANT_ID,
    name: "Handicrafts",
    slug: "handicrafts",
    description: "Traditional Nepali handicrafts and accessories",
    productCount: 2,
    sortOrder: 2,
  },
  {
    id: "col-home-decor",
    tenantId: TENANT_ID,
    name: "Home Decor",
    slug: "home-decor",
    description: "Decorative items for your home",
    productCount: 1,
    sortOrder: 3,
  },
];

const variantPashminaRed: ProductVariant = {
  id: "var-pashmina-red",
  productId: "prod-pashmina-shawl",
  sku: "NC-PSH-RED",
  name: "Crimson Red",
  options: { color: "Crimson Red" },
  price: { amount: 4500, currency: "NPR" },
  compareAtPrice: { amount: 5500, currency: "NPR" },
  isPublished: true,
  createdAt: "2025-01-03T08:00:00Z",
  updatedAt: "2025-01-03T08:00:00Z",
};

const variantPashminaNatural: ProductVariant = {
  id: "var-pashmina-natural",
  productId: "prod-pashmina-shawl",
  sku: "NC-PSH-NAT",
  name: "Natural Undyed",
  options: { color: "Natural Undyed" },
  price: { amount: 4200, currency: "NPR" },
  isPublished: true,
  createdAt: "2025-01-03T08:00:00Z",
  updatedAt: "2025-01-03T08:00:00Z",
};

const variantDhakaStandard: ProductVariant = {
  id: "var-dhaka-standard",
  productId: "prod-dhaka-topi",
  sku: "NC-DHK-STD",
  name: "Standard Size",
  options: { size: "Standard" },
  price: { amount: 850, currency: "NPR" },
  isPublished: true,
  createdAt: "2025-01-04T09:00:00Z",
  updatedAt: "2025-01-04T09:00:00Z",
};

const variantDhakaPremium: ProductVariant = {
  id: "var-dhaka-premium",
  productId: "prod-dhaka-topi",
  sku: "NC-DHK-PRM",
  name: "Premium Dhaka Weave",
  options: { size: "Premium" },
  price: { amount: 1500, currency: "NPR" },
  isPublished: true,
  createdAt: "2025-01-04T09:00:00Z",
  updatedAt: "2025-01-04T09:00:00Z",
};

const variantLoktaA5: ProductVariant = {
  id: "var-lokta-a5",
  productId: "prod-lokta-journal",
  sku: "NC-LKT-A5",
  name: "A5 Blank",
  options: { size: "A5", pages: "Blank" },
  price: { amount: 650, currency: "NPR" },
  isPublished: true,
  createdAt: "2025-01-05T10:00:00Z",
  updatedAt: "2025-01-05T10:00:00Z",
};

const variantThangkaTara: ProductVariant = {
  id: "var-thangka-tara",
  productId: "prod-thangka-mini",
  sku: "NC-THG-TARA",
  name: "Green Tara",
  options: { deity: "Green Tara" },
  price: { amount: 3200, currency: "NPR" },
  isPublished: true,
  createdAt: "2025-01-06T11:00:00Z",
  updatedAt: "2025-01-06T11:00:00Z",
};

const variantSingingBowlSmall: ProductVariant = {
  id: "var-singing-bowl-sm",
  productId: "prod-singing-bowl",
  sku: "NC-SB-SM",
  name: "Small (10 cm)",
  options: { size: "Small" },
  price: { amount: 2800, currency: "NPR" },
  isPublished: false,
  createdAt: "2025-01-07T12:00:00Z",
  updatedAt: "2025-01-07T12:00:00Z",
};

export const products: Product[] = [
  {
    id: "prod-pashmina-shawl",
    tenantId: TENANT_ID,
    title: "हातले बुनेको पश्मिना शawl",
    description:
      "नेपाली हिमाली क्षेत्रका कुशल कारीगरहरूले हातले बुनेको नरम पश्मिना शawl। जाडोमा न्यानो र सजिलै लगाउन मिल्ने।",
    slug: "hatale-buneko-pashmina-shawl",
    publishState: "published",
    variants: [variantPashminaRed, variantPashminaNatural],
    media: [
      {
        id: "media-pashmina-1",
        url: "/fixtures/products/pashmina-shawl-red.jpg",
        altText: "Crimson red hand-knit pashmina shawl",
        width: 1200,
        height: 1600,
        mimeType: "image/jpeg",
        sortOrder: 1,
      },
    ],
    collections: ["col-textiles"],
    tags: ["pashmina", "handmade", "winter", "textile"],
    createdAt: "2025-01-03T08:00:00Z",
    updatedAt: "2025-01-03T08:00:00Z",
  },
  {
    id: "prod-dhaka-topi",
    tenantId: TENANT_ID,
    title: "Dhaka Topi — Traditional Nepali Cap",
    description:
      "Authentic Dhaka topi woven in Palpa tradition. Dhaka ko topi — Nepali identity ko symbol. Perfect for festivals and daily wear.",
    slug: "dhaka-topi-traditional",
    publishState: "published",
    variants: [variantDhakaStandard, variantDhakaPremium],
    media: [
      {
        id: "media-dhaka-1",
        url: "/fixtures/products/dhaka-topi.jpg",
        altText: "Traditional Dhaka topi with colorful geometric pattern",
        width: 1000,
        height: 1000,
        mimeType: "image/jpeg",
        sortOrder: 1,
      },
    ],
    collections: ["col-handicrafts"],
    tags: ["dhaka", "topi", "traditional", "festival"],
    createdAt: "2025-01-04T09:00:00Z",
    updatedAt: "2025-01-04T09:00:00Z",
  },
  {
    id: "prod-lokta-journal",
    tenantId: TENANT_ID,
    title: "Lokta Paper Journal",
    description:
      "Handmade journal crafted from sustainable lokta bark paper sourced from the hills of Baglung. Acid-free pages ideal for sketching and journaling.",
    slug: "lokta-paper-journal",
    publishState: "published",
    variants: [variantLoktaA5],
    media: [
      {
        id: "media-lokta-1",
        url: "/fixtures/products/lokta-journal.jpg",
        altText: "Handmade lokta paper journal with deckled edges",
        width: 1000,
        height: 1200,
        mimeType: "image/jpeg",
        sortOrder: 1,
      },
    ],
    collections: ["col-handicrafts"],
    tags: ["lokta", "journal", "eco-friendly", "paper"],
    createdAt: "2025-01-05T10:00:00Z",
    updatedAt: "2025-01-05T10:00:00Z",
  },
  {
    id: "prod-thangka-mini",
    tenantId: TENANT_ID,
    title: "Mini Thangka Painting",
    description:
      "Hand-painted miniature thangka on cotton canvas by Bhaktapur artisans. Each piece includes a brocade frame and certificate of authenticity.",
    slug: "mini-thangka-painting",
    publishState: "published",
    variants: [variantThangkaTara],
    media: [
      {
        id: "media-thangka-1",
        url: "/fixtures/products/thangka-tara.jpg",
        altText: "Mini Green Tara thangka painting",
        width: 800,
        height: 1000,
        mimeType: "image/jpeg",
        sortOrder: 1,
      },
    ],
    collections: ["col-home-decor"],
    tags: ["thangka", "buddhist", "art", "hand-painted"],
    createdAt: "2025-01-06T11:00:00Z",
    updatedAt: "2025-01-06T11:00:00Z",
  },
  {
    id: "prod-singing-bowl",
    tenantId: TENANT_ID,
    title: "Tibetan Singing Bowl Set",
    description:
      "Seven-metal singing bowl with wooden mallet and silk cushion. Hand-hammered in Patan. Currently in draft — photos being finalized.",
    slug: "tibetan-singing-bowl-set",
    publishState: "draft",
    variants: [variantSingingBowlSmall],
    media: [],
    collections: ["col-handicrafts"],
    tags: ["singing-bowl", "meditation", "wellness"],
    createdAt: "2025-01-07T12:00:00Z",
    updatedAt: "2025-01-07T12:00:00Z",
  },
];

export const allVariants: ProductVariant[] = products.flatMap((p) => p.variants);

// ─── Inventory ───────────────────────────────────────────────────────────────

export const inventoryItems: InventoryItem[] = [
  {
    id: "inv-pashmina-red",
    tenantId: TENANT_ID,
    variantId: "var-pashmina-red",
    productTitle: "हातले बुनेको पश्मिना शawl",
    variantName: "Crimson Red",
    sku: "NC-PSH-RED",
    onHand: 18,
    committed: 2,
    available: 16,
    lowStockThreshold: 5,
    isLowStock: false,
    updatedAt: "2025-02-10T08:00:00Z",
  },
  {
    id: "inv-pashmina-natural",
    tenantId: TENANT_ID,
    variantId: "var-pashmina-natural",
    productTitle: "हातले बुनेको पश्मिना शawl",
    variantName: "Natural Undyed",
    sku: "NC-PSH-NAT",
    onHand: 12,
    committed: 0,
    available: 12,
    lowStockThreshold: 5,
    isLowStock: false,
    updatedAt: "2025-02-10T08:00:00Z",
  },
  {
    id: "inv-dhaka-standard",
    tenantId: TENANT_ID,
    variantId: "var-dhaka-standard",
    productTitle: "Dhaka Topi — Traditional Nepali Cap",
    variantName: "Standard Size",
    sku: "NC-DHK-STD",
    onHand: 45,
    committed: 1,
    available: 44,
    lowStockThreshold: 10,
    isLowStock: false,
    updatedAt: "2025-02-10T08:00:00Z",
  },
  {
    id: "inv-dhaka-premium",
    tenantId: TENANT_ID,
    variantId: "var-dhaka-premium",
    productTitle: "Dhaka Topi — Traditional Nepali Cap",
    variantName: "Premium Dhaka Weave",
    sku: "NC-DHK-PRM",
    onHand: 3,
    committed: 1,
    available: 2,
    lowStockThreshold: 5,
    isLowStock: true,
    updatedAt: "2025-02-10T08:00:00Z",
  },
  {
    id: "inv-lokta-a5",
    tenantId: TENANT_ID,
    variantId: "var-lokta-a5",
    productTitle: "Lokta Paper Journal",
    variantName: "A5 Blank",
    sku: "NC-LKT-A5",
    onHand: 30,
    committed: 0,
    available: 30,
    lowStockThreshold: 8,
    isLowStock: false,
    updatedAt: "2025-02-10T08:00:00Z",
  },
  {
    id: "inv-thangka-tara",
    tenantId: TENANT_ID,
    variantId: "var-thangka-tara",
    productTitle: "Mini Thangka Painting",
    variantName: "Green Tara",
    sku: "NC-THG-TARA",
    onHand: 8,
    committed: 0,
    available: 8,
    lowStockThreshold: 3,
    isLowStock: false,
    updatedAt: "2025-02-10T08:00:00Z",
  },
];

export const stockMovements: StockMovement[] = [
  {
    id: "sm-pashmina-red-initial",
    inventoryItemId: "inv-pashmina-red",
    type: "initial",
    quantity: 20,
    reason: "Opening stock",
    actorId: USER_OWNER_ID,
    createdAt: "2025-01-03T08:30:00Z",
  },
  {
    id: "sm-pashmina-red-commit",
    inventoryItemId: "inv-pashmina-red",
    type: "commitment",
    quantity: -2,
    reason: "Order NC-2025-0042 fulfilled",
    referenceId: "order-0042",
    actorId: "system",
    createdAt: "2025-02-08T14:00:00Z",
  },
  {
    id: "sm-dhaka-premium-adjust",
    inventoryItemId: "inv-dhaka-premium",
    type: "adjustment",
    quantity: -2,
    reason: "Damaged items removed during quality check",
    actorId: USER_OPERATOR_ID,
    createdAt: "2025-02-09T10:00:00Z",
  },
];

export const inventoryReservations: InventoryReservation[] = [
  {
    id: "res-checkout-001",
    tenantId: TENANT_ID,
    variantId: "var-pashmina-red",
    quantity: 1,
    state: "active",
    source: "checkout",
    referenceId: "quote-res-001",
    idempotencyKey: "idem-checkout-001",
    expiresAt: "2025-02-12T12:30:00Z",
    createdAt: "2025-02-12T12:00:00Z",
  },
  {
    id: "res-order-0043",
    tenantId: TENANT_ID,
    variantId: "var-dhaka-premium",
    quantity: 1,
    state: "committed",
    source: "conversation",
    referenceId: "order-0043",
    idempotencyKey: "idem-order-0043",
    expiresAt: "2025-02-10T16:00:00Z",
    createdAt: "2025-02-10T15:00:00Z",
  },
];

// ─── Storefront ──────────────────────────────────────────────────────────────

export const storeReadiness: StoreReadiness = {
  hasProfile: true,
  hasPublishedProducts: true,
  hasDeliveryRules: true,
  hasPaymentMethods: true,
  isReady: true,
  blockers: [],
};

export const store: Store = {
  id: STORE_ID,
  tenantId: TENANT_ID,
  slug: TENANT_SLUG,
  profile: {
    name: "Namaste Crafts",
    tagline: "Handmade treasures from the heart of Nepal",
    description:
      "We curate authentic Nepali handicrafts — from Dhaka topis to hand-knit pashmina — crafted by artisans across Kathmandu Valley and beyond.",
    logoUrl: "/fixtures/store/namaste-crafts-logo.png",
    bannerUrl: "/fixtures/store/namaste-crafts-banner.jpg",
    contactEmail: "hello@namastecrafts.com.np",
    contactPhone: "+977-9841234567",
    socialLinks: {
      facebook: "https://facebook.com/namastecrafts",
      instagram: "https://instagram.com/namastecrafts",
    },
  },
  theme: {
    accentColor: "#C45B28",
    logoUrl: "/fixtures/store/namaste-crafts-logo.png",
    bannerUrl: "/fixtures/store/namaste-crafts-banner.jpg",
  },
  readiness: storeReadiness,
  isPublished: true,
  publishedAt: "2025-01-08T12:00:00Z",
  createdAt: "2025-01-02T10:00:00Z",
  updatedAt: "2025-02-01T09:00:00Z",
};

export const deliveryRules: DeliveryRule[] = [
  {
    id: "rule-kathmandu-valley",
    tenantId: TENANT_ID,
    name: "Kathmandu Valley Delivery",
    zones: ["Kathmandu", "Lalitpur", "Bhaktapur"],
    feeType: "threshold",
    flatFee: { amount: 150, currency: "NPR" },
    freeAbove: { amount: 3000, currency: "NPR" },
    estimatedDays: "1–2 business days",
    codAvailable: true,
    isActive: true,
  },
  {
    id: "rule-kathmandu-outskirts",
    tenantId: TENANT_ID,
    name: "Kathmandu Outskirts",
    zones: ["Kirtipur", "Tokha", "Budhanilkantha", "Godawari"],
    feeType: "flat",
    flatFee: { amount: 250, currency: "NPR" },
    estimatedDays: "2–3 business days",
    codAvailable: true,
    isActive: true,
  },
];

export const paymentMethods: PaymentMethod[] = [
  {
    id: "pm-cod",
    tenantId: TENANT_ID,
    type: "cod",
    label: "Cash on Delivery",
    isEnabled: true,
    instructions: "Pay the delivery rider in cash when your order arrives.",
    updatedAt: "2025-01-05T14:00:00Z",
  },
  {
    id: "pm-merchant-qr",
    tenantId: TENANT_ID,
    type: "merchant_qr",
    label: "Fonepay / eSewa QR",
    isEnabled: true,
    qrImageUrl: "/fixtures/payments/namaste-crafts-qr.png",
    instructions:
      "Scan the QR code and pay the exact order total. Upload your payment screenshot after checkout.",
    updatedAt: "2025-01-05T14:30:00Z",
  },
];

// ─── Orders ──────────────────────────────────────────────────────────────────

const orderActivityConfirmed: OrderActivity[] = [
  {
    id: "oa-0041-created",
    orderId: "order-0041",
    action: "order.created",
    actorId: "system",
    actorName: "System",
    details: { source: "storefront" },
    createdAt: "2025-02-08T10:00:00Z",
  },
  {
    id: "oa-0041-confirmed",
    orderId: "order-0041",
    action: "order.confirmed",
    actorId: USER_OPERATOR_ID,
    actorName: "Rajan Thapa",
    createdAt: "2025-02-08T10:30:00Z",
  },
];

const orderActivityProcessing: OrderActivity[] = [
  {
    id: "oa-0042-created",
    orderId: "order-0042",
    action: "order.created",
    actorId: "system",
    actorName: "System",
    details: { source: "conversation" },
    createdAt: "2025-02-09T14:00:00Z",
  },
  {
    id: "oa-0042-confirmed",
    orderId: "order-0042",
    action: "order.confirmed",
    actorId: USER_OPERATOR_ID,
    actorName: "Rajan Thapa",
    createdAt: "2025-02-09T14:15:00Z",
  },
  {
    id: "oa-0042-processing",
    orderId: "order-0042",
    action: "order.processing",
    actorId: USER_OPERATOR_ID,
    actorName: "Rajan Thapa",
    createdAt: "2025-02-10T09:00:00Z",
  },
];

const orderActivityFulfilled: OrderActivity[] = [
  {
    id: "oa-0040-created",
    orderId: "order-0040",
    action: "order.created",
    actorId: "system",
    actorName: "System",
    details: { source: "storefront" },
    createdAt: "2025-02-05T11:00:00Z",
  },
  {
    id: "oa-0040-confirmed",
    orderId: "order-0040",
    action: "order.confirmed",
    actorId: USER_OWNER_ID,
    actorName: "Sita Shrestha",
    createdAt: "2025-02-05T11:30:00Z",
  },
  {
    id: "oa-0040-fulfilled",
    orderId: "order-0040",
    action: "order.fulfilled",
    actorId: USER_OPERATOR_ID,
    actorName: "Rajan Thapa",
    details: { trackingNote: "Delivered to customer in Patan" },
    createdAt: "2025-02-07T16:00:00Z",
  },
];

export const orders: Order[] = [
  {
    id: "order-0041",
    tenantId: TENANT_ID,
    orderNumber: "NC-2025-0041",
    status: "confirmed",
    paymentStatus: "pending",
    fulfilmentStatus: "unfulfilled",
    source: "storefront",
    items: [
      {
        id: "oi-0041-1",
        variantId: "var-lokta-a5",
        productTitle: "Lokta Paper Journal",
        variantName: "A5 Blank",
        sku: "NC-LKT-A5",
        unitPrice: { amount: 650, currency: "NPR" },
        quantity: 2,
        lineTotal: { amount: 1300, currency: "NPR" },
      },
    ],
    subtotal: { amount: 1300, currency: "NPR" },
    deliveryFee: { amount: 150, currency: "NPR" },
    total: { amount: 1450, currency: "NPR" },
    currency: "NPR",
    customerName: "Anita Gurung",
    customerPhone: "+977-9812345678",
    customerEmail: "anita.gurung@email.com",
    deliveryAddress: {
      line1: "Ward 5, Jhamsikhel Road",
      line2: "Near Pulchowk Chowk",
      city: "Lalitpur",
      district: "Lalitpur",
      province: "Bagmati",
      postalCode: "44700",
      country: "NP",
      contactName: "Anita Gurung",
      contactPhone: "+977-9812345678",
    },
    paymentMethod: "cod",
    activity: orderActivityConfirmed,
    createdAt: "2025-02-08T10:00:00Z",
    updatedAt: "2025-02-08T10:30:00Z",
  },
  {
    id: "order-0042",
    tenantId: TENANT_ID,
    orderNumber: "NC-2025-0042",
    status: "processing",
    paymentStatus: "awaiting_verification",
    fulfilmentStatus: "ready",
    source: "conversation",
    items: [
      {
        id: "oi-0042-1",
        variantId: "var-pashmina-red",
        productTitle: "हातले बुनेको पश्मिना शawl",
        variantName: "Crimson Red",
        sku: "NC-PSH-RED",
        unitPrice: { amount: 4500, currency: "NPR" },
        quantity: 1,
        lineTotal: { amount: 4500, currency: "NPR" },
      },
    ],
    subtotal: { amount: 4500, currency: "NPR" },
    deliveryFee: { amount: 0, currency: "NPR" },
    total: { amount: 4500, currency: "NPR" },
    currency: "NPR",
    customerName: "Bikash Maharjan",
    customerPhone: "+977-9823456789",
    deliveryAddress: {
      line1: "Maharajgunj, Ward 3",
      line2: "Opposite Teaching Hospital Gate",
      city: "Kathmandu",
      district: "Kathmandu",
      province: "Bagmati",
      postalCode: "44600",
      country: "NP",
      contactName: "Bikash Maharjan",
      contactPhone: "+977-9823456789",
    },
    paymentMethod: "merchant_qr",
    activity: orderActivityProcessing,
    createdAt: "2025-02-09T14:00:00Z",
    updatedAt: "2025-02-10T09:00:00Z",
  },
  {
    id: "order-0040",
    tenantId: TENANT_ID,
    orderNumber: "NC-2025-0040",
    status: "fulfilled",
    paymentStatus: "paid",
    fulfilmentStatus: "delivered",
    source: "storefront",
    items: [
      {
        id: "oi-0040-1",
        variantId: "var-dhaka-standard",
        productTitle: "Dhaka Topi — Traditional Nepali Cap",
        variantName: "Standard Size",
        sku: "NC-DHK-STD",
        unitPrice: { amount: 850, currency: "NPR" },
        quantity: 3,
        lineTotal: { amount: 2550, currency: "NPR" },
      },
      {
        id: "oi-0040-2",
        variantId: "var-thangka-tara",
        productTitle: "Mini Thangka Painting",
        variantName: "Green Tara",
        sku: "NC-THG-TARA",
        unitPrice: { amount: 3200, currency: "NPR" },
        quantity: 1,
        lineTotal: { amount: 3200, currency: "NPR" },
      },
    ],
    subtotal: { amount: 5750, currency: "NPR" },
    deliveryFee: { amount: 0, currency: "NPR" },
    total: { amount: 5750, currency: "NPR" },
    currency: "NPR",
    customerName: "Sunita Tamang",
    customerPhone: "+977-9834567890",
    customerEmail: "sunita.t@email.com",
    deliveryAddress: {
      line1: "Patan Durbar Square Area, Mangal Bazaar",
      line2: "South of Krishna Mandir",
      city: "Lalitpur",
      district: "Lalitpur",
      province: "Bagmati",
      postalCode: "44700",
      country: "NP",
      contactName: "Sunita Tamang",
      contactPhone: "+977-9834567890",
    },
    paymentMethod: "cod",
    activity: orderActivityFulfilled,
    createdAt: "2025-02-05T11:00:00Z",
    updatedAt: "2025-02-07T16:00:00Z",
  },
];

export const orderActivitiesByOrderId: Record<string, OrderActivity[]> = {
  "order-0040": orderActivityFulfilled,
  "order-0041": orderActivityConfirmed,
  "order-0042": orderActivityProcessing,
};

// ─── Payments ────────────────────────────────────────────────────────────────

export const paymentAttempts: PaymentAttempt[] = [
  {
    id: "pa-0042-1",
    orderId: "order-0042",
    method: "merchant_qr",
    amount: { amount: 4500, currency: "NPR" },
    status: "awaiting_verification",
    proofUrl: "/fixtures/payments/proof-bikash-0042.jpg",
    createdAt: "2025-02-09T14:30:00Z",
  },
  {
    id: "pa-0040-1",
    orderId: "order-0040",
    method: "cod",
    amount: { amount: 5750, currency: "NPR" },
    status: "verified",
    verifiedBy: USER_OPERATOR_ID,
    verifiedAt: "2025-02-07T16:00:00Z",
    createdAt: "2025-02-07T16:00:00Z",
  },
];

// ─── Conversations ───────────────────────────────────────────────────────────

export const conversations: Conversation[] = [
  {
    id: "conv-facebook-001",
    tenantId: TENANT_ID,
    channel: "facebook",
    state: "bot_active",
    customerName: "Priya Karki",
    customerIdentifier: "fb:priya.karki.9841",
    lastMessage: "Kati ho yo pashmina shawl ko price?",
    lastMessageAt: "2025-02-12T11:45:00Z",
    unreadCount: 1,
    labels: ["product-enquiry"],
    isAutomationActive: true,
    connectionId: CONNECTION_FACEBOOK_ID,
    createdAt: "2025-02-12T11:30:00Z",
    updatedAt: "2025-02-12T11:45:00Z",
  },
  {
    id: "conv-whatsapp-002",
    tenantId: TENANT_ID,
    channel: "whatsapp",
    state: "human_assigned",
    customerName: "Nabin Shrestha",
    customerIdentifier: "wa:+9779845678901",
    lastMessage: "Payment screenshot pathayo, please verify garnus.",
    lastMessageAt: "2025-02-11T09:20:00Z",
    unreadCount: 2,
    assignment: {
      assigneeId: USER_OPERATOR_ID,
      assigneeName: "Rajan Thapa",
      assignedAt: "2025-02-10T15:30:00Z",
    },
    labels: ["payment", "order-0042"],
    isAutomationActive: false,
    connectionId: "conn-whatsapp-namaste",
    createdAt: "2025-02-09T13:00:00Z",
    updatedAt: "2025-02-11T09:20:00Z",
  },
  {
    id: "conv-instagram-003",
    tenantId: TENANT_ID,
    channel: "instagram",
    state: "resolved",
    customerName: "Meera Rai",
    customerIdentifier: "ig:meera.rai.crafts",
    lastMessage: "Dhanyabad! Order ramro aayo.",
    lastMessageAt: "2025-02-07T17:00:00Z",
    unreadCount: 0,
    labels: ["resolved", "positive-feedback"],
    isAutomationActive: false,
    connectionId: "conn-instagram-namaste",
    createdAt: "2025-02-05T10:00:00Z",
    updatedAt: "2025-02-07T17:00:00Z",
  },
];

export const messagesByConversationId: Record<string, Message[]> = {
  "conv-facebook-001": [
    {
      id: "msg-fb-001",
      conversationId: "conv-facebook-001",
      direction: "inbound",
      senderName: "Priya Karki",
      senderType: "customer",
      content: "Namaste! Kati ho yo pashmina shawl ko price?",
      attachments: [],
      deliveryState: "delivered",
      createdAt: "2025-02-12T11:30:00Z",
    },
    {
      id: "msg-fb-002",
      conversationId: "conv-facebook-001",
      direction: "outbound",
      senderName: "Namaste Crafts Assistant",
      senderType: "bot",
      content:
        "Namaste Priya! हातले बुनेको पश्मिना शawl Crimson Red variant ko price Rs. 4,500 ho. Natural Undyed variant Rs. 4,200 ma available cha.",
      attachments: [],
      deliveryState: "delivered",
      createdAt: "2025-02-12T11:32:00Z",
    },
    {
      id: "msg-fb-003",
      conversationId: "conv-facebook-001",
      direction: "inbound",
      senderName: "Priya Karki",
      senderType: "customer",
      content: "Delivery Kathmandu ma kati din lagcha?",
      attachments: [],
      deliveryState: "delivered",
      createdAt: "2025-02-12T11:45:00Z",
    },
  ],
  "conv-whatsapp-002": [
    {
      id: "msg-wa-001",
      conversationId: "conv-whatsapp-002",
      direction: "inbound",
      senderName: "Nabin Shrestha",
      senderType: "customer",
      content: "Malai yo red pashmina order garnu thiyo.",
      attachments: [],
      deliveryState: "delivered",
      createdAt: "2025-02-09T13:00:00Z",
    },
    {
      id: "msg-wa-002",
      conversationId: "conv-whatsapp-002",
      direction: "outbound",
      senderName: "Rajan Thapa",
      senderType: "staff",
      content: "Order NC-2025-0042 confirm bhayo. QR bata Rs. 4,500 pay garnus.",
      attachments: [],
      deliveryState: "read",
      createdAt: "2025-02-09T14:20:00Z",
    },
    {
      id: "msg-wa-003",
      conversationId: "conv-whatsapp-002",
      direction: "inbound",
      senderName: "Nabin Shrestha",
      senderType: "customer",
      content: "Payment screenshot pathayo, please verify garnus.",
      attachments: [
        {
          url: "/fixtures/payments/proof-bikash-0042.jpg",
          type: "image/jpeg",
          name: "fonepay-screenshot.jpg",
        },
      ],
      deliveryState: "delivered",
      createdAt: "2025-02-11T09:20:00Z",
    },
  ],
  "conv-instagram-003": [
    {
      id: "msg-ig-001",
      conversationId: "conv-instagram-003",
      direction: "inbound",
      senderName: "Meera Rai",
      senderType: "customer",
      content: "Hi! Do you have Dhaka topi in stock?",
      attachments: [],
      deliveryState: "delivered",
      createdAt: "2025-02-05T10:00:00Z",
    },
    {
      id: "msg-ig-002",
      conversationId: "conv-instagram-003",
      direction: "outbound",
      senderName: "Sita Shrestha",
      senderType: "staff",
      content: "Yes! Standard size Rs. 850, Premium weave Rs. 1,500. COD available in Lalitpur.",
      attachments: [],
      deliveryState: "read",
      createdAt: "2025-02-05T10:15:00Z",
    },
    {
      id: "msg-ig-003",
      conversationId: "conv-instagram-003",
      direction: "inbound",
      senderName: "Meera Rai",
      senderType: "customer",
      content: "Dhanyabad! Order ramro aayo.",
      attachments: [],
      deliveryState: "delivered",
      createdAt: "2025-02-07T17:00:00Z",
    },
  ],
};

// ─── Integrations ────────────────────────────────────────────────────────────

const facebookHealth: ConnectionHealth = {
  status: "connected",
  lastEventAt: "2025-02-12T11:45:00Z",
  webhookUrl: "https://api.kreyora.app/webhooks/facebook/conn-facebook-namaste",
  eventsProcessed24h: 47,
  eventsFailed24h: 0,
  tokenExpiresAt: "2025-05-01T00:00:00Z",
};

export const channelConnections: ChannelConnection[] = [
  {
    id: CONNECTION_FACEBOOK_ID,
    tenantId: TENANT_ID,
    provider: "facebook",
    accountName: "Namaste Crafts",
    accountIdentifier: "fb-page-namaste-crafts-88421",
    status: "connected",
    capabilities: {
      canReceiveMessages: true,
      canSendMessages: true,
      canSendMedia: true,
      canReceiveMedia: true,
      supportsTemplates: false,
      supportsDeliveryReceipts: true,
    },
    health: facebookHealth,
    connectedAt: "2025-01-06T16:00:00Z",
    updatedAt: "2025-02-12T11:45:00Z",
  },
];

export const webhookEvents: WebhookEvent[] = [
  {
    id: "wh-001",
    connectionId: CONNECTION_FACEBOOK_ID,
    providerEventId: "fb-msg-88421-99102",
    eventType: "message.received",
    status: "processed",
    retryCount: 0,
    processedAt: "2025-02-12T11:45:01Z",
    createdAt: "2025-02-12T11:45:00Z",
  },
  {
    id: "wh-002",
    connectionId: CONNECTION_FACEBOOK_ID,
    providerEventId: "fb-msg-88421-99101",
    eventType: "message.received",
    status: "processed",
    retryCount: 0,
    processedAt: "2025-02-12T11:30:02Z",
    createdAt: "2025-02-12T11:30:01Z",
  },
  {
    id: "wh-003",
    connectionId: CONNECTION_FACEBOOK_ID,
    providerEventId: "fb-msg-88421-99050",
    eventType: "message.received",
    status: "failed",
    retryCount: 2,
    failureReason: "Temporary timeout contacting conversation service",
    createdAt: "2025-02-11T08:00:00Z",
  },
];

// ─── AI ──────────────────────────────────────────────────────────────────────

export const assistantConfig: AssistantConfig = {
  tenantId: TENANT_ID,
  isEnabled: true,
  language: "ne-NP",
  tone: "warm and helpful",
  maxToolIterations: 5,
  costBudgetPerConversation: 0.15,
  autoEscalateOnLowConfidence: true,
  updatedAt: "2025-01-07T10:00:00Z",
};

export const knowledgeDocuments: KnowledgeDocument[] = [
  {
    id: "kd-shipping-faq",
    tenantId: TENANT_ID,
    title: "Shipping & Delivery FAQ",
    fileName: "shipping-delivery-faq.pdf",
    fileType: "application/pdf",
    status: "approved",
    chunkCount: 12,
    uploadedBy: USER_OWNER_ID,
    approvedBy: USER_OWNER_ID,
    createdAt: "2025-01-07T09:00:00Z",
    updatedAt: "2025-01-07T10:00:00Z",
  },
  {
    id: "kd-product-care",
    tenantId: TENANT_ID,
    title: "Product Care Guide — Textiles & Handicrafts",
    fileName: "product-care-guide.pdf",
    fileType: "application/pdf",
    status: "approved",
    chunkCount: 8,
    uploadedBy: USER_OPERATOR_ID,
    approvedBy: USER_OWNER_ID,
    createdAt: "2025-01-08T11:00:00Z",
    updatedAt: "2025-01-08T14:00:00Z",
  },
];

export const aiActionTraces: AIActionTrace[] = [
  {
    id: "trace-fb-001",
    tenantId: TENANT_ID,
    conversationId: "conv-facebook-001",
    intent: "product_price_enquiry",
    toolCalls: [
      {
        tool: "SearchProducts",
        input: { query: "pashmina shawl" },
        output: { productIds: ["prod-pashmina-shawl"], count: 1 },
        durationMs: 120,
      },
      {
        tool: "GetPrice",
        input: { variantId: "var-pashmina-red" },
        output: { price: 4500, currency: "NPR" },
        durationMs: 45,
      },
    ],
    responseGenerated:
      "Namaste Priya! हातले बुनेको पश्मिना शawl Crimson Red variant ko price Rs. 4,500 ho.",
    confidenceScore: 0.92,
    escalationState: "none",
    tokenCount: 340,
    costBand: "low",
    latencyMs: 890,
    createdAt: "2025-02-12T11:32:00Z",
  },
];

// ─── Billing ─────────────────────────────────────────────────────────────────

export const growPlan: Plan = {
  id: "plan-grow",
  name: "Grow",
  monthlyPrice: { amount: 2999, currency: "NPR" },
  limits: {
    products: 100,
    aiCredits: 5000,
    ordersPerMonth: 200,
    socialIntegrations: 3,
    teamSeats: 5,
  },
  platformFeePercent: 2.5,
};

export const subscription: Subscription = {
  id: "sub-namaste-grow",
  tenantId: TENANT_ID,
  planId: "plan-grow",
  planName: "Grow",
  status: "active",
  currentPeriodStart: "2025-02-01T00:00:00Z",
  currentPeriodEnd: "2025-03-01T00:00:00Z",
};

export const quotaStatuses: QuotaStatus[] = [
  {
    metric: "orders_per_month",
    limit: 200,
    used: 150,
    level: "warning_70",
    percentUsed: 75,
  },
  {
    metric: "ai_credits",
    limit: 5000,
    used: 2100,
    level: "normal",
    percentUsed: 42,
  },
  {
    metric: "products",
    limit: 100,
    used: 5,
    level: "normal",
    percentUsed: 5,
  },
  {
    metric: "social_integrations",
    limit: 3,
    used: 1,
    level: "normal",
    percentUsed: 33,
  },
];

export const usageEvents: UsageEvent[] = [
  {
    id: "usage-001",
    tenantId: TENANT_ID,
    metric: "orders_per_month",
    quantity: 1,
    source: "order-0041",
    idempotencyKey: "usage-order-0041",
    createdAt: "2025-02-08T10:00:00Z",
  },
  {
    id: "usage-002",
    tenantId: TENANT_ID,
    metric: "ai_credits",
    quantity: 12,
    source: "conv-facebook-001",
    idempotencyKey: "usage-ai-fb-001",
    createdAt: "2025-02-12T11:32:00Z",
  },
  {
    id: "usage-003",
    tenantId: TENANT_ID,
    metric: "orders_per_month",
    quantity: 1,
    source: "order-0042",
    idempotencyKey: "usage-order-0042",
    createdAt: "2025-02-09T14:00:00Z",
  },
];

// ─── Reporting ───────────────────────────────────────────────────────────────

export const dashboardMetrics: DashboardMetrics = {
  setupProgress: 86,
  totalOrders: 42,
  totalRevenue: { amount: 187500, currency: "NPR" },
  openConversations: 2,
  averageReplyTimeMinutes: 8,
  lowStockProducts: 1,
  integrationHealthy: 1,
  integrationTotal: 1,
  aiCreditsUsed: 2100,
  aiCreditsLimit: 5000,
  ordersThisMonth: 150,
  ordersLimit: 200,
};

export const analyticsSnapshots: Record<"day" | "week" | "month", AnalyticsSnapshot> = {
  day: {
    period: "day",
    orderCount: 3,
    revenue: { amount: 11700, currency: "NPR" },
    conversationCount: 5,
    averageOrderValue: { amount: 3900, currency: "NPR" },
    conversionRate: 0.18,
    topProducts: [
      { productId: "prod-pashmina-shawl", title: "हातले बुनेको पश्मिना शawl", orderCount: 1 },
      { productId: "prod-dhaka-topi", title: "Dhaka Topi — Traditional Nepali Cap", orderCount: 1 },
      { productId: "prod-lokta-journal", title: "Lokta Paper Journal", orderCount: 1 },
    ],
    ordersBySource: { storefront: 2, conversation: 1 },
    ordersByChannel: { facebook: 1, whatsapp: 1, storefront: 1 },
  },
  week: {
    period: "week",
    orderCount: 12,
    revenue: { amount: 48500, currency: "NPR" },
    conversationCount: 28,
    averageOrderValue: { amount: 4042, currency: "NPR" },
    conversionRate: 0.22,
    topProducts: [
      { productId: "prod-dhaka-topi", title: "Dhaka Topi — Traditional Nepali Cap", orderCount: 5 },
      { productId: "prod-pashmina-shawl", title: "हातले बुनेको पश्मिना शawl", orderCount: 3 },
      { productId: "prod-thangka-mini", title: "Mini Thangka Painting", orderCount: 2 },
    ],
    ordersBySource: { storefront: 7, conversation: 4, manual: 1 },
    ordersByChannel: { facebook: 4, whatsapp: 3, instagram: 2, storefront: 3 },
  },
  month: {
    period: "month",
    orderCount: 42,
    revenue: { amount: 187500, currency: "NPR" },
    conversationCount: 96,
    averageOrderValue: { amount: 4464, currency: "NPR" },
    conversionRate: 0.24,
    topProducts: [
      { productId: "prod-dhaka-topi", title: "Dhaka Topi — Traditional Nepali Cap", orderCount: 15 },
      { productId: "prod-pashmina-shawl", title: "हातले बुनेको पश्मिना शawl", orderCount: 10 },
      { productId: "prod-lokta-journal", title: "Lokta Paper Journal", orderCount: 8 },
    ],
    ordersBySource: { storefront: 24, conversation: 15, manual: 3 },
    ordersByChannel: { facebook: 12, whatsapp: 10, instagram: 8, storefront: 12 },
  },
};

// ─── Audit ───────────────────────────────────────────────────────────────────

export const auditEvents: AuditEvent[] = [
  {
    id: "audit-001",
    tenantId: TENANT_ID,
    actor: {
      id: USER_OWNER_ID,
      name: "Sita Shrestha",
      role: "owner",
      type: "user",
    },
    action: "product.published",
    resourceType: "product",
    resourceId: "prod-pashmina-shawl",
    details: { title: "हातले बुनेको पश्मिना शawl", publishState: "published" },
    correlationId: "corr-catalog-001",
    createdAt: "2025-01-03T08:05:00Z",
  },
  {
    id: "audit-002",
    tenantId: TENANT_ID,
    actor: {
      id: USER_OPERATOR_ID,
      name: "Rajan Thapa",
      role: "operator",
      type: "user",
    },
    action: "order.status_changed",
    resourceType: "order",
    resourceId: "order-0042",
    details: { from: "confirmed", to: "processing" },
    correlationId: "corr-order-0042",
    createdAt: "2025-02-10T09:00:00Z",
  },
  {
    id: "audit-003",
    tenantId: TENANT_ID,
    actor: {
      id: "system",
      name: "Integration Service",
      role: "system",
      type: "system",
    },
    action: "webhook.processed",
    resourceType: "channel_connection",
    resourceId: CONNECTION_FACEBOOK_ID,
    details: { eventType: "message.received", providerEventId: "fb-msg-88421-99102" },
    correlationId: "corr-webhook-001",
    createdAt: "2025-02-12T11:45:01Z",
  },
];

// ─── Checkout ──────────────────────────────────────────────────────────────────

export const demoCart: Cart = {
  items: [
    {
      variantId: "var-pashmina-red",
      productTitle: "हातले बुनेको पश्मिना शawl",
      variantName: "Crimson Red",
      imageUrl: "/fixtures/products/pashmina-shawl-red.jpg",
      unitPrice: { amount: 4500, currency: "NPR" },
      quantity: 1,
      available: true,
    },
    {
      variantId: "var-dhaka-standard",
      productTitle: "Dhaka Topi — Traditional Nepali Cap",
      variantName: "Standard Size",
      imageUrl: "/fixtures/products/dhaka-topi.jpg",
      unitPrice: { amount: 850, currency: "NPR" },
      quantity: 1,
      available: true,
    },
  ],
  subtotal: { amount: 5350, currency: "NPR" },
  itemCount: 2,
};

export const demoCheckoutQuote: CheckoutQuote = {
  subtotal: { amount: 5350, currency: "NPR" },
  deliveryFee: { amount: 150, currency: "NPR" },
  total: { amount: 5500, currency: "NPR" },
  items: demoCart.items,
  deliveryQuote: {
    ruleId: "rule-kathmandu-valley",
    ruleName: "Kathmandu Valley Delivery",
    fee: { amount: 150, currency: "NPR" },
    estimatedDays: "1–2 business days",
    codAvailable: true,
    expiresAt: "2025-02-12T13:00:00Z",
  },
  availablePaymentMethods: [
    {
      type: "cod",
      label: "Cash on Delivery",
      description: "Pay the delivery rider in cash when your order arrives.",
      isAvailable: true,
    },
    {
      type: "merchant_qr",
      label: "Fonepay / eSewa QR",
      description: "Scan and pay, then upload your payment screenshot.",
      isAvailable: true,
      qrImageUrl: "/fixtures/payments/namaste-crafts-qr.png",
      instructions:
        "Scan the QR code and pay the exact order total. Upload your payment screenshot after checkout.",
    },
  ],
  reservationId: "quote-res-001",
  expiresAt: "2025-02-12T13:00:00Z",
};
