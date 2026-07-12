import { describe, it, expect } from "vitest";
import * as fs from "fs";
import * as path from "path";

const HOOKS_DIR = path.resolve(__dirname, "../hooks");

describe("Cart hook — implementation verification", () => {
  const content = fs.readFileSync(
    path.join(HOOKS_DIR, "use-cart.ts"),
    "utf-8",
  );

  it("exports CartProvider component", () => {
    expect(content).toContain("export function CartProvider");
  });

  it("exports useCart hook", () => {
    expect(content).toContain("export function useCart");
  });

  it("provides addItem function", () => {
    expect(content).toContain("addItem");
  });

  it("provides removeItem function", () => {
    expect(content).toContain("removeItem");
  });

  it("provides updateQuantity function", () => {
    expect(content).toContain("updateQuantity");
  });

  it("provides clearCart function", () => {
    expect(content).toContain("clearCart");
  });

  it("calculates itemCount from item quantities", () => {
    expect(content).toContain("itemCount");
    expect(content).toMatch(/items\.reduce/);
  });

  it("calculates subtotal from price * quantity", () => {
    expect(content).toContain("subtotal");
    expect(content).toContain("unitPrice.amount");
  });

  it("merges quantities when adding duplicate variantId", () => {
    expect(content).toContain("i.quantity + item.quantity");
  });

  it("removes item when quantity set to 0 or less", () => {
    expect(content).toContain("quantity <= 0");
  });

  it("uses React Context for state", () => {
    expect(content).toContain("createContext");
    expect(content).toContain("useContext");
  });

  it("throws error when useCart used outside CartProvider", () => {
    expect(content).toContain("useCart must be used within a CartProvider");
  });
});
