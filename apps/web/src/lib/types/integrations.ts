import type { TenantId, Timestamp } from "./common";

export type ConnectionStatus = "connected" | "disconnected" | "error" | "pending_reauth";
export type ProviderType = "facebook" | "instagram" | "whatsapp" | "tiktok";

export interface ProviderCapability {
  canReceiveMessages: boolean;
  canSendMessages: boolean;
  canSendMedia: boolean;
  canReceiveMedia: boolean;
  supportsTemplates: boolean;
  supportsDeliveryReceipts: boolean;
}

export interface ConnectionHealth {
  status: ConnectionStatus;
  lastEventAt?: Timestamp;
  lastErrorMessage?: string;
  tokenExpiresAt?: Timestamp;
  webhookUrl: string;
  eventsProcessed24h: number;
  eventsFailed24h: number;
}

export interface ChannelConnection {
  id: string;
  tenantId: TenantId;
  provider: ProviderType;
  accountName: string;
  accountIdentifier: string;
  status: ConnectionStatus;
  capabilities: ProviderCapability;
  health: ConnectionHealth;
  connectedAt: Timestamp;
  updatedAt: Timestamp;
}

export interface WebhookEvent {
  id: string;
  connectionId: string;
  providerEventId: string;
  eventType: string;
  status: "processed" | "failed" | "dead_letter";
  retryCount: number;
  processedAt?: Timestamp;
  failureReason?: string;
  createdAt: Timestamp;
}
