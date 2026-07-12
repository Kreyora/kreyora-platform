import { describe, it, expect } from "vitest";

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const h = hex.replace("#", "");
  return {
    r: parseInt(h.substring(0, 2), 16),
    g: parseInt(h.substring(2, 4), 16),
    b: parseInt(h.substring(4, 6), 16),
  };
}

function relativeLuminance(hex: string): number {
  const { r, g, b } = hexToRgb(hex);
  const [rs, gs, bs] = [r, g, b].map((c) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
}

function contrastRatio(hex1: string, hex2: string): number {
  const l1 = relativeLuminance(hex1);
  const l2 = relativeLuminance(hex2);
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

const WCAG_AA_NORMAL = 4.5;
const WCAG_AA_LARGE = 3;

describe("Token accessibility — contrast ratios", () => {
  const canvas = "#FFFFFF";
  const canvasSubtle = "#F5F5F2";
  const inkPrimary = "#111111";
  const inkSecondary = "#626262";
  const surfaceDark = "#1E2021";
  const onDark = "#F5F5F2";

  it("ink-primary on canvas meets WCAG AA for normal text", () => {
    expect(contrastRatio(inkPrimary, canvas)).toBeGreaterThanOrEqual(WCAG_AA_NORMAL);
  });

  it("ink-secondary on canvas meets WCAG AA for large text", () => {
    expect(contrastRatio(inkSecondary, canvas)).toBeGreaterThanOrEqual(WCAG_AA_LARGE);
  });

  it("ink-primary on canvas-subtle meets WCAG AA for normal text", () => {
    expect(contrastRatio(inkPrimary, canvasSubtle)).toBeGreaterThanOrEqual(WCAG_AA_NORMAL);
  });

  it("on-dark text on dark surface meets WCAG AA for normal text", () => {
    expect(contrastRatio(onDark, surfaceDark)).toBeGreaterThanOrEqual(WCAG_AA_NORMAL);
  });

  const semanticOnCanvas: Array<[string, string]> = [
    ["success #1a7a3a", "#1a7a3a"],
    ["warning #9a6700", "#9a6700"],
    ["danger #c93131", "#c93131"],
    ["info #1a6fb5", "#1a6fb5"],
  ];

  for (const [name, hex] of semanticOnCanvas) {
    it(`${name} on canvas meets WCAG AA for large text`, () => {
      expect(contrastRatio(hex, canvas)).toBeGreaterThanOrEqual(WCAG_AA_LARGE);
    });
  }
});
