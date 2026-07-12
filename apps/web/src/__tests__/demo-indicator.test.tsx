import { describe, it, expect } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { DemoIndicator } from "@/components/demo-indicator";

describe("DemoIndicator", () => {
  it("renders the expanded demo data notice by default", () => {
    render(<DemoIndicator />);
    expect(
      screen.getByText("Demo data — not connected to a live service"),
    ).toBeInTheDocument();
  });

  it("has an accessible status role", () => {
    render(<DemoIndicator />);
    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("collapses to a persistent badge when close button is clicked", () => {
    render(<DemoIndicator />);
    const button = screen.getByRole("button", { name: /collapse/i });
    fireEvent.click(button);

    expect(
      screen.queryByText("Demo data — not connected to a live service"),
    ).not.toBeInTheDocument();

    const badge = screen.getByRole("status");
    expect(badge).toBeInTheDocument();
    expect(badge.textContent).toContain("Demo");
  });

  it("can be re-expanded by clicking the persistent badge", () => {
    render(<DemoIndicator />);

    fireEvent.click(screen.getByRole("button", { name: /collapse/i }));
    expect(
      screen.queryByText("Demo data — not connected to a live service"),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("status"));
    expect(
      screen.getByText("Demo data — not connected to a live service"),
    ).toBeInTheDocument();
  });

  it("never fully disappears — always shows either banner or badge", () => {
    render(<DemoIndicator />);

    expect(screen.getByRole("status")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /collapse/i }));

    expect(screen.getByRole("status")).toBeInTheDocument();
  });
});
