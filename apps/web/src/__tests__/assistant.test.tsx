import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const APP_DIR = path.resolve(__dirname, "../app");

describe("AI Assistant — route file verification", () => {
  const routes = [
    { path: "(seller)/assistant", label: "assistant policy" },
    { path: "(seller)/assistant/knowledge", label: "knowledge documents" },
    { path: "(seller)/assistant/console", label: "test console" },
    { path: "(seller)/assistant/history", label: "action history" },
  ];

  for (const route of routes) {
    it(`${route.label} page file exists`, () => {
      const file = path.join(APP_DIR, route.path, "page.tsx");
      expect(fs.existsSync(file)).toBe(true);
    });
  }
});

describe("AI Assistant — policy page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/assistant/page.tsx"),
    "utf-8",
  );

  it("uses getAssistantConfig from AI client", () => {
    expect(content).toContain("getAssistantConfig");
  });

  it("displays config fields", () => {
    expect(content).toContain("isEnabled");
    expect(content).toContain("language");
    expect(content).toContain("tone");
    expect(content).toContain("maxToolIterations");
    expect(content).toContain("costBudgetPerConversation");
    expect(content).toContain("autoEscalateOnLowConfidence");
  });

  it("has sub-navigation to all assistant pages", () => {
    expect(content).toContain("/assistant/knowledge");
    expect(content).toContain("/assistant/console");
    expect(content).toContain("/assistant/history");
  });

  it("has simulated configuration disclaimer", () => {
    expect(content).toContain("Configuration changes are simulated");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });
});

describe("AI Assistant — knowledge documents page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/assistant/knowledge/page.tsx"),
    "utf-8",
  );

  it("uses listKnowledge from AI client", () => {
    expect(content).toContain("listKnowledge");
  });

  it("has document status badges", () => {
    expect(content).toContain("DOC_STATUS");
    expect(content).toContain("Pending Review");
    expect(content).toContain("Approved");
    expect(content).toContain("Rejected");
    expect(content).toContain("Archived");
  });

  it("shows document metadata (fileName, chunkCount)", () => {
    expect(content).toContain("fileName");
    expect(content).toContain("chunkCount");
  });

  it("has lifecycle actions (approve, reject, archive)", () => {
    expect(content).toContain("Approve");
    expect(content).toContain("Reject");
    expect(content).toContain("Archive");
    expect(content).toContain("handleAction");
  });

  it("has Upload Document placeholder button", () => {
    expect(content).toContain("Upload Document");
  });

  it("viewer sees no action buttons", () => {
    expect(content).toContain("isViewer");
    expect(content).toContain("!isViewer");
  });

  it("has simulated lifecycle disclaimer", () => {
    expect(content).toContain("simulated");
  });
});

describe("AI Assistant — test console page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/assistant/console/page.tsx"),
    "utf-8",
  );

  it("has chat interface with input", () => {
    expect(content).toContain("chatMessages");
    expect(content).toContain('aria-label="Test console input"');
    expect(content).toContain("handleSend");
  });

  it("uses getActionTraces for tool trace data", () => {
    expect(content).toContain("getActionTraces");
  });

  it("has tool-trace overlay with tool details", () => {
    expect(content).toContain("Tool Trace");
    expect(content).toContain("toolCalls");
    expect(content).toContain("durationMs");
  });

  it("shows confidence score", () => {
    expect(content).toContain("confidenceScore");
  });

  it("shows token count and cost band", () => {
    expect(content).toContain("tokenCount");
    expect(content).toContain("COST_MAP");
    expect(content).toContain("costBand");
  });

  it("shows escalation state indicator", () => {
    expect(content).toContain("ESCALATION_MAP");
    expect(content).toContain("escalationState");
  });

  it("has fixture data disclaimer", () => {
    expect(content).toContain("This console uses fixture data");
  });
});

describe("AI Assistant — action history page", () => {
  const content = fs.readFileSync(
    path.join(APP_DIR, "(seller)/assistant/history/page.tsx"),
    "utf-8",
  );

  it("uses getActionTraces from AI client", () => {
    expect(content).toContain("getActionTraces");
  });

  it("has trace list with intent and conversation link", () => {
    expect(content).toContain("intent");
    expect(content).toContain("conversationId");
    expect(content).toContain("/inbox/");
  });

  it("shows tool calls count and confidence", () => {
    expect(content).toContain("toolCalls.length");
    expect(content).toContain("confidenceScore");
  });

  it("has cost band and escalation badges", () => {
    expect(content).toContain("COST_MAP");
    expect(content).toContain("ESCALATION_MAP");
    expect(content).toContain("costBand");
    expect(content).toContain("escalationState");
  });

  it("has expandable row with tool call details", () => {
    expect(content).toContain("expandedId");
    expect(content).toContain("aria-expanded");
    expect(content).toContain("Tool Calls");
  });

  it("shows generated response (redacted preview)", () => {
    expect(content).toContain("Generated Response");
    expect(content).toContain("responseGenerated");
  });

  it("shows token count and latency", () => {
    expect(content).toContain("tokenCount");
    expect(content).toContain("latencyMs");
  });

  it("shows ViewerBadge for viewer role", () => {
    expect(content).toContain("ViewerBadge");
  });

  it("has fixture data disclaimer", () => {
    expect(content).toContain("fixture data");
  });
});
