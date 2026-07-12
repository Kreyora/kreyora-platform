import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("Integrations — route file verification", () => {
  const routes = [
    { path: "(seller)/integrations", label: "connection list" },
    { path: "(seller)/integrations/[id]", label: "connection detail" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("Integrations — connection list page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/integrations/page.tsx"),
    "utf-8",
  );

  it("uses listConnections from integration client", () => {
    expect(content).toContain("listConnections");
  });

  it("has connection cards with provider badges", () => {
    expect(content).toContain("PROVIDER_MAP");
    expect(content).toContain("Facebook");
    expect(content).toContain("Instagram");
  });

  it("shows status badges", () => {
    expect(content).toContain("STATUS_MAP");
    expect(content).toContain("Connected");
    expect(content).toContain("Disconnected");
  });

  it("shows capabilities summary", () => {
    expect(content).toContain("capabilities");
    expect(content).toContain("Receive");
    expect(content).toContain("Send");
  });

  it("shows events processed count", () => {
    expect(content).toContain("eventsProcessed24h");
  });

  it("has Connect Channel placeholder button", () => {
    expect(content).toContain("Connect Channel");
  });

  it("links to connection detail", () => {
    expect(content).toContain("/integrations/");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("has simulated disclaimer", () => {
    expect(content).toContain("simulated");
  });
});

describe("Integrations — connection detail page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/integrations/[id]/page.tsx"),
    "utf-8",
  );

  it("uses getConnection to load data", () => {
    expect(content).toContain("getConnection");
  });

  it("uses getWebhookEvents for event table", () => {
    expect(content).toContain("getWebhookEvents");
  });

  it("has capabilities section with check/cross indicators", () => {
    expect(content).toContain("Capabilities");
    expect(content).toContain("canReceiveMessages");
    expect(content).toContain("canSendMessages");
  });

  it("has health section with status and token expiry", () => {
    expect(content).toContain("Health");
    expect(content).toContain("tokenExpiresAt");
    expect(content).toContain("Token expires");
  });

  it("shows token expiry warning when approaching", () => {
    expect(content).toContain("daysUntilExpiry");
  });

  it("has webhook events table with status badges", () => {
    expect(content).toContain("Webhook Events");
    expect(content).toContain("EVENT_STATUS");
    expect(content).toContain("Processed");
    expect(content).toContain("Failed");
  });

  it("has replay button for failed events", () => {
    expect(content).toContain("Replay");
    expect(content).toContain("handleReplay");
    expect(content).toContain("replayedIds");
  });

  it("has reconnect button for disconnected/error status", () => {
    expect(content).toContain("Reconnect");
    expect(content).toContain("handleReconnect");
  });

  it("has simulated action disclaimer", () => {
    expect(content).toContain("simulated");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});
