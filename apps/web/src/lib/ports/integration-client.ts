import type { ChannelConnection, ConnectionHealth, WebhookEvent, WebhookReplayResult, PaginatedResult } from "@/lib/types";

export interface IntegrationClient {
  listConnections(): Promise<ChannelConnection[]>;
  getConnection(id: string): Promise<ChannelConnection>;
  getHealth(connectionId: string): Promise<ConnectionHealth>;
  getWebhookEvents(connectionId: string, page?: number, pageSize?: number): Promise<PaginatedResult<WebhookEvent>>;
  replayWebhook(id: string, idempotencyKey?: string): Promise<WebhookReplayResult>;
  reconnect(connectionId: string): Promise<ConnectionHealth>;
}
