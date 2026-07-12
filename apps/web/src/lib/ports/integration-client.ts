import type { ChannelConnection, ConnectionHealth, WebhookEvent, PaginatedResult } from "@/lib/types";

export interface IntegrationClient {
  listConnections(): Promise<ChannelConnection[]>;
  getConnection(id: string): Promise<ChannelConnection>;
  getHealth(connectionId: string): Promise<ConnectionHealth>;
  getWebhookEvents(connectionId: string): Promise<PaginatedResult<WebhookEvent>>;
}
