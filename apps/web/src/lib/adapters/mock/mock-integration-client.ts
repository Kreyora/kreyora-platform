import type { IntegrationClient } from "@/lib/ports/integration-client";
import type { PaginatedResult, WebhookEvent } from "@/lib/types";
import { channelConnections, webhookEvents } from "../fixtures/data";

const MOCK_DELAY_MS = 50;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, MOCK_DELAY_MS));
}

function toPaginated<T>(items: T[]): PaginatedResult<T> {
  return { items, cursor: null, hasMore: false, totalCount: items.length };
}

export const mockIntegrationClient: IntegrationClient = {
  async listConnections() {
    await delay();
    return channelConnections;
  },

  async getConnection(id: string) {
    await delay();
    const connection = channelConnections.find((c) => c.id === id);
    if (!connection) {
      throw new Error(`Connection not found: ${id}`);
    }
    return connection;
  },

  async getHealth(connectionId: string) {
    await delay();
    const connection = channelConnections.find((c) => c.id === connectionId);
    if (!connection) {
      throw new Error(`Connection not found: ${connectionId}`);
    }
    return connection.health;
  },

  async getWebhookEvents(connectionId: string) {
    await delay();
    const events = webhookEvents.filter((e) => e.connectionId === connectionId);
    return toPaginated<WebhookEvent>(events);
  },
};
