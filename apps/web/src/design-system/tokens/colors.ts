export const colors = {
  canvas: "var(--color-canvas)",
  canvasSubtle: "var(--color-canvas-subtle)",
  inkPrimary: "var(--color-ink-primary)",
  inkSecondary: "var(--color-ink-secondary)",
  border: "var(--color-border)",
  surfaceDark: "var(--color-surface-dark)",
  onDark: "var(--color-on-dark)",
  success: "var(--color-success)",
  successSubtle: "var(--color-success-subtle)",
  warning: "var(--color-warning)",
  warningSubtle: "var(--color-warning-subtle)",
  danger: "var(--color-danger)",
  dangerSubtle: "var(--color-danger-subtle)",
  info: "var(--color-info)",
  infoSubtle: "var(--color-info-subtle)",
  focusRing: "var(--color-focus-ring)",
} as const;

export type ColorToken = keyof typeof colors;
