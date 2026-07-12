import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Inbox — route file verification", () => {
  const routes = [
    { path: "(seller)/inbox", label: "conversation list" },
    { path: "(seller)/inbox/[id]", label: "conversation detail" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Inbox — conversation list page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/inbox/page.tsx"),
    "utf-8",
  );

  it("uses listConversations from conversation client", () => {
    expect(content).toContain("listConversations");
  });

  it("has search input", () => {
    expect(content).toContain('aria-label="Search conversations"');
  });

  it("has state filter", () => {
    expect(content).toContain("stateFilter");
    expect(content).toContain('aria-label="Filter by state"');
  });

  it("has channel filter", () => {
    expect(content).toContain("channelFilter");
    expect(content).toContain('aria-label="Filter by channel"');
  });

  it("renders channel badges", () => {
    expect(content).toContain("CHANNEL_MAP");
    expect(content).toContain("Facebook");
    expect(content).toContain("Instagram");
    expect(content).toContain("WhatsApp");
  });

  it("renders unread count badge", () => {
    expect(content).toContain("unreadCount");
  });

  it("renders state badges", () => {
    expect(content).toContain("STATE_MAP");
    expect(content).toContain("Badge");
  });

  it("shows assignment name", () => {
    expect(content).toContain("assignment");
    expect(content).toContain("assigneeName");
  });

  it("shows labels", () => {
    expect(content).toContain("labels");
  });

  it("links to conversation detail", () => {
    expect(content).toContain("/inbox/");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});

describe("Inbox — conversation detail page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/inbox/[id]/page.tsx"),
    "utf-8",
  );

  it("uses getConversation to load data", () => {
    expect(content).toContain("getConversation");
  });

  it("uses getMessages for message timeline", () => {
    expect(content).toContain("getMessages");
  });

  it("has message timeline with delivery state badges", () => {
    expect(content).toContain("DELIVERY_MAP");
    expect(content).toContain("deliveryState");
    expect(content).toContain("pending");
    expect(content).toContain("delivered");
    expect(content).toContain("failed");
  });

  it("has sender type badges (customer/staff/bot)", () => {
    expect(content).toContain("SENDER_MAP");
    expect(content).toContain("customer");
    expect(content).toContain("staff");
    expect(content).toContain("bot");
  });

  it("has retry indicator for failed messages", () => {
    expect(content).toContain("Retry needed");
  });

  it("has takeover button when automation is active", () => {
    expect(content).toContain("Take Over");
    expect(content).toContain("handleTakeover");
  });

  it("has release button when human has control", () => {
    expect(content).toContain("Release to Bot");
    expect(content).toContain("handleRelease");
  });

  it("shows Bot Paused indicator after takeover", () => {
    expect(content).toContain("Bot Paused");
  });

  it("has staff composer", () => {
    expect(content).toContain("composerText");
    expect(content).toContain("handleSend");
    expect(content).toContain('aria-label="Message composer"');
  });

  it("composer only available when human has control", () => {
    expect(content).toContain("canCompose");
    expect(content).toContain("!automationActive");
  });

  it("has provider health context", () => {
    expect(content).toContain("Channel Health");
    expect(content).toContain("getHealth");
    expect(content).toContain("eventsProcessed24h");
  });

  it("shows attachment indicators", () => {
    expect(content).toContain("attachments");
  });

  it("has simulated messages disclaimer", () => {
    expect(content).toContain("Messages are simulated");
  });

  it("viewer sees no takeover/compose controls", () => {
    expect(content).toContain("isViewer");
    expect(content).toContain("!isViewer");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});
