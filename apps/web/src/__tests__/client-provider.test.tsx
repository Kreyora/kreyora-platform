import { describe, it, expect } from "vitest";
import { renderHook } from "@testing-library/react";
import {
  ClientProvider,
  useClients,
  useIdentityClient,
  useCatalogClient,
  useOrderClient,
} from "@/lib/providers/client-provider";
import type { ReactNode } from "react";

function wrapper({ children }: { children: ReactNode }) {
  return <ClientProvider>{children}</ClientProvider>;
}

describe("ClientProvider", () => {
  it("provides all client instances through useClients", () => {
    const { result } = renderHook(() => useClients(), { wrapper });
    const clients = result.current;

    expect(clients.identity).toBeDefined();
    expect(clients.catalog).toBeDefined();
    expect(clients.inventory).toBeDefined();
    expect(clients.storefront).toBeDefined();
    expect(clients.checkout).toBeDefined();
    expect(clients.order).toBeDefined();
    expect(clients.payment).toBeDefined();
    expect(clients.conversation).toBeDefined();
    expect(clients.integration).toBeDefined();
    expect(clients.ai).toBeDefined();
    expect(clients.billing).toBeDefined();
    expect(clients.reporting).toBeDefined();
    expect(clients.audit).toBeDefined();
  });

  it("individual hooks return working clients", async () => {
    const { result: identity } = renderHook(() => useIdentityClient(), { wrapper });
    const { result: catalog } = renderHook(() => useCatalogClient(), { wrapper });
    const { result: order } = renderHook(() => useOrderClient(), { wrapper });

    const session = await identity.current.getCurrentSession();
    expect(session.user.id).toBeTruthy();

    const products = await catalog.current.listProducts();
    expect(products.items.length).toBeGreaterThan(0);

    const orders = await order.current.listOrders();
    expect(orders.items.length).toBeGreaterThan(0);
  });

  it("allows overriding specific clients", () => {
    const customCatalog = {
      listProducts: async () => ({ items: [], cursor: null, hasMore: false, totalCount: 0 }),
      getProduct: async () => {
        throw new Error("not implemented");
      },
      getVariants: async () => [],
      getCollections: async () => [],
    };

    function customWrapper({ children }: { children: ReactNode }) {
      return (
        <ClientProvider clients={{ catalog: customCatalog }}>
          {children}
        </ClientProvider>
      );
    }

    const { result } = renderHook(() => useCatalogClient(), {
      wrapper: customWrapper,
    });

    expect(result.current).toBe(customCatalog);
  });
});
