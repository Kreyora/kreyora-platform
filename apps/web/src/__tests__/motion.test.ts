import { describe, it, expect } from "vitest";
import { motionClasses } from "@/design-system/primitives/motion";

describe("Motion primitives", () => {
  it("exports all expected motion class names", () => {
    expect(motionClasses.fadeIn).toBe("animate-fade-in");
    expect(motionClasses.fadeOut).toBe("animate-fade-out");
    expect(motionClasses.slideInRight).toBe("animate-slide-in-right");
    expect(motionClasses.slideOutRight).toBe("animate-slide-out-right");
    expect(motionClasses.staggerChildren).toBe("stagger-children");
  });

  it("hoverLift class includes hover transform", () => {
    expect(motionClasses.hoverLift).toContain("hover:");
    expect(motionClasses.hoverLift).toContain("translate");
  });

  it("pressFeedback class includes active scale", () => {
    expect(motionClasses.pressFeedback).toContain("active:");
    expect(motionClasses.pressFeedback).toContain("scale");
  });

  it("globals.css contains nuanced reduced-motion media query", async () => {
    const fs = await import("fs");
    const path = await import("path");
    const cssPath = path.resolve(__dirname, "../app/globals.css");
    const css = fs.readFileSync(cssPath, "utf-8");
    expect(css).toContain("prefers-reduced-motion: reduce");
    expect(css).toContain("animation: none !important");
    expect(css).toContain("transition-duration: 0.01ms");
    expect(css).toContain("scroll-behavior: auto");
  });

  it("reduced-motion preserves element visibility (opacity: 1)", async () => {
    const fs = await import("fs");
    const path = await import("path");
    const cssPath = path.resolve(__dirname, "../app/globals.css");
    const css = fs.readFileSync(cssPath, "utf-8");
    const reducedMotionBlock = css.slice(
      css.indexOf("prefers-reduced-motion: reduce"),
    );
    expect(reducedMotionBlock).toContain("opacity: 1 !important");
  });
});
