"use client";

import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from "react";
import React from "react";
import type { PublicCartItem } from "@/lib/types/public-storefront";

interface CartContextValue {
  items: PublicCartItem[];
  itemCount: number;
  subtotal: number;
  addItem: (item: PublicCartItem) => void;
  removeItem: (variantId: string) => void;
  updateQuantity: (variantId: string, quantity: number) => void;
  clearCart: () => void;
}

const CartContext = createContext<CartContextValue | null>(null);

export function CartProvider({ children, storeSlug }: { children: ReactNode; storeSlug: string }) {
  const storageKey = `kreyora:public-cart:v1:${storeSlug}`;
  const [items, setItems] = useState<PublicCartItem[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    Promise.resolve().then(() => {
      try {
        const saved = sessionStorage.getItem(storageKey);
        setItems(saved ? JSON.parse(saved) as PublicCartItem[] : []);
      } catch {
        setItems([]);
      }
      setLoaded(true);
    });
  }, [storageKey]);

  useEffect(() => {
    if (loaded) sessionStorage.setItem(storageKey, JSON.stringify(items));
  }, [items, loaded, storageKey]);

  const addItem = useCallback((item: PublicCartItem) => {
    setItems((prev) => {
      const existing = prev.find((i) => i.variantId === item.variantId);
      if (existing) {
        return prev.map((i) =>
          i.variantId === item.variantId
            ? { ...i, quantity: i.quantity + item.quantity, unitPriceNpr: item.unitPriceNpr, imageId: item.imageId, imageAlt: item.imageAlt }
            : i,
        );
      }
      return [...prev, item];
    });
  }, []);

  const removeItem = useCallback((variantId: string) => {
    setItems((prev) => prev.filter((i) => i.variantId !== variantId));
  }, []);

  const updateQuantity = useCallback((variantId: string, quantity: number) => {
    if (quantity <= 0) {
      setItems((prev) => prev.filter((i) => i.variantId !== variantId));
      return;
    }
    setItems((prev) =>
      prev.map((i) => (i.variantId === variantId ? { ...i, quantity } : i)),
    );
  }, []);

  const clearCart = useCallback(() => setItems([]), []);

  const itemCount = items.reduce((sum, i) => sum + i.quantity, 0);
  const subtotal = items.reduce(
    (sum, i) => sum + i.unitPriceNpr * i.quantity,
    0,
  );

  return React.createElement(
    CartContext.Provider,
    {
      value: { items, itemCount, subtotal, addItem, removeItem, updateQuantity, clearCart },
    },
    children,
  );
}

export function useCart(): CartContextValue {
  const ctx = useContext(CartContext);
  if (!ctx) {
    throw new Error("useCart must be used within a CartProvider");
  }
  return ctx;
}
