import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Analytics — route and content", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/analytics/page.tsx"),
    "utf-8",
  );

  it("page file exists", () => {
    expect(fs.existsSync(path.join(APP_DIR, "(seller)/analytics/page.tsx"))).toBe(true);
  });

  it("uses getAnalytics from reporting client", () => {
    expect(content).toContain("getAnalytics");
  });

  it("has period selector with day/week/month", () => {
    expect(content).toContain("day");
    expect(content).toContain("week");
    expect(content).toContain("month");
    expect(content).toContain("Period selector");
  });

  it("shows key metric cards", () => {
    expect(content).toContain("Orders");
    expect(content).toContain("Revenue");
    expect(content).toContain("Conversations");
    expect(content).toContain("Avg. Order");
    expect(content).toContain("Conversion");
  });

  it("has top products table", () => {
    expect(content).toContain("Top Products");
    expect(content).toContain("topProducts");
  });

  it("shows orders by source breakdown", () => {
    expect(content).toContain("Orders by Source");
    expect(content).toContain("ordersBySource");
  });

  it("shows orders by channel breakdown", () => {
    expect(content).toContain("Orders by Channel");
    expect(content).toContain("ordersByChannel");
  });

  it("supports viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("has disclaimer about simulated data", () => {
    expect(content).toContain("simulated");
  });
});

describe("Billing — route and content", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/billing/page.tsx"),
    "utf-8",
  );

  it("page file exists", () => {
    expect(fs.existsSync(path.join(APP_DIR, "(seller)/billing/page.tsx"))).toBe(true);
  });

  it("uses getPlan, getQuotaStatus, getUsage from billing client", () => {
    expect(content).toContain("getPlan");
    expect(content).toContain("getQuotaStatus");
    expect(content).toContain("getUsage");
  });

  it("shows plan card with name and price", () => {
    expect(content).toContain("plan.name");
    expect(content).toContain("monthlyPrice");
    expect(content).toContain("platformFeePercent");
  });

  it("shows subscription status and period", () => {
    expect(content).toContain("subscription.status");
    expect(content).toContain("currentPeriodStart");
    expect(content).toContain("currentPeriodEnd");
  });

  it("displays plan limits", () => {
    expect(content).toContain("products");
    expect(content).toContain("aiCredits");
    expect(content).toContain("ordersPerMonth");
    expect(content).toContain("socialIntegrations");
    expect(content).toContain("teamSeats");
  });

  it("has quota bars with level-based coloring", () => {
    expect(content).toContain("QuotaBar");
    expect(content).toContain("QUOTA_COLORS");
    expect(content).toContain("normal");
    expect(content).toContain("warning_70");
    expect(content).toContain("warning_90");
    expect(content).toContain("exceeded");
  });

  it("quota bars use distinct visual colors for each level", () => {
    expect(content).toContain("bg-[var(--color-success)]");
    expect(content).toContain("bg-[var(--color-warning)]");
    expect(content).toContain("bg-orange-500");
    expect(content).toContain("bg-[var(--color-danger)]");
  });

  it("quota bars have accessible progressbar role", () => {
    expect(content).toContain('role="progressbar"');
    expect(content).toContain("aria-valuenow");
    expect(content).toContain("aria-valuemax");
  });

  it("shows quota labels for each level", () => {
    expect(content).toContain("70% used");
    expect(content).toContain("90% used");
    expect(content).toContain("Exceeded");
  });

  it("shows usage events table", () => {
    expect(content).toContain("Usage Events");
    expect(content).toContain("e.metric");
    expect(content).toContain("e.quantity");
    expect(content).toContain("e.source");
  });

  it("has manage subscription placeholder button", () => {
    expect(content).toContain("Manage Subscription");
  });

  it("has disclaimer about simulated billing", () => {
    expect(content).toContain("simulated");
  });

  it("supports viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});

describe("Team — route and content", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/team/page.tsx"),
    "utf-8",
  );

  it("page file exists", () => {
    expect(fs.existsSync(path.join(APP_DIR, "(seller)/team/page.tsx"))).toBe(true);
  });

  it("uses getTeamMembers from identity client", () => {
    expect(content).toContain("getTeamMembers");
  });

  it("shows team member avatar, name, and email", () => {
    expect(content).toContain("Avatar");
    expect(content).toContain("displayName");
    expect(content).toContain("email");
  });

  it("shows role badges with variant mapping", () => {
    expect(content).toContain("ROLE_VARIANTS");
    expect(content).toContain("membership.role");
  });

  it("shows joined date", () => {
    expect(content).toContain("joinedAt");
  });

  it("has invite member placeholder button", () => {
    expect(content).toContain("Invite Member");
  });

  it("has role legend/descriptions", () => {
    expect(content).toContain("ROLE_DESCRIPTIONS");
    expect(content).toContain("Full access");
    expect(content).toContain("Read-only");
  });

  it("viewer sees no invite button", () => {
    expect(content).toContain("isViewer");
    expect(content).toContain("ViewerBadge");
  });
});

describe("Settings — route and content", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/settings/page.tsx"),
    "utf-8",
  );

  it("page file exists", () => {
    expect(fs.existsSync(path.join(APP_DIR, "(seller)/settings/page.tsx"))).toBe(true);
  });

  it("uses getCurrentSession from identity client", () => {
    expect(content).toContain("getCurrentSession");
  });

  it("shows workspace info", () => {
    expect(content).toContain("tenant.name");
    expect(content).toContain("tenant.slug");
    expect(content).toContain("tenant.createdAt");
  });

  it("has billing link", () => {
    expect(content).toContain("/billing");
    expect(content).toContain("View billing");
  });

  it("shows session info", () => {
    expect(content).toContain("user.displayName");
    expect(content).toContain("user.email");
    expect(content).toContain("membership.role");
  });

  it("has danger zone with disabled delete", () => {
    expect(content).toContain("Danger Zone");
    expect(content).toContain("Delete Workspace");
    expect(content).toContain("disabled");
  });

  it("has disclaimer about simulation", () => {
    expect(content).toContain("simulated");
  });
});

describe("Audit — route and content", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/audit/page.tsx"),
    "utf-8",
  );

  it("page file exists", () => {
    expect(fs.existsSync(path.join(APP_DIR, "(seller)/audit/page.tsx"))).toBe(true);
  });

  it("uses listAuditEvents from audit client", () => {
    expect(content).toContain("listAuditEvents");
  });

  it("has resource type and action filters", () => {
    expect(content).toContain("resourceFilter");
    expect(content).toContain("actionFilter");
    expect(content).toContain("resourceType");
  });

  it("shows actor info with type badges", () => {
    expect(content).toContain("actor.name");
    expect(content).toContain("actor.type");
    expect(content).toContain("actor.role");
    expect(content).toContain("ACTOR_TYPE_VARIANTS");
  });

  it("shows event details in table", () => {
    expect(content).toContain("evt.action");
    expect(content).toContain("evt.resourceType");
    expect(content).toContain("evt.resourceId");
    expect(content).toContain("evt.details");
    expect(content).toContain("correlationId");
  });

  it("has loading skeleton and empty state", () => {
    expect(content).toContain("Skeleton");
    expect(content).toContain("EmptyState");
  });

  it("supports viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("has disclaimer about simulated data", () => {
    expect(content).toContain("simulated");
  });
});
