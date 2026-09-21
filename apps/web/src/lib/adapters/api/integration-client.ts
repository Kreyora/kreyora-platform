import { apiFetch } from "@/lib/api";
import { getCsrfToken } from "./auth-client";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { IntegrationClient } from "@/lib/ports/integration-client";
import type {
  ChannelConnection,
  ConnectionHealth,
  ConnectionStatus,
  PaginatedResult,
  ProviderType,
  WebhookEvent,
  WebhookReplayResult,
} from "@/lib/types";

const headers = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });

interface ApiChannelCapabilities {
  canReceiveText: boolean;
  canReceiveMedia: boolean;
  canSendText: boolean;
  canSendMedia: boolean;
  canSendLinkPreview: boolean;
  requiresTemplatesOutsideWindow: boolean;
  supportsReactions: boolean;
  supportsDeliveryReceipts: boolean;
  supportsReadReceipts: boolean;
  enforces24HourWindow: boolean;
  supportsTokenRefresh: boolean;
  requiresSignatureVerification: boolean;
}

interface ApiChannelConnectionDto {
  id: string;
  tenantId: string;
  storeId?: string;
  channel: string;
  externalAccountId: string;
  displayName: string;
  status: string;
  hasCredentials: boolean;
  keyVersion?: string;
  capabilities: ApiChannelCapabilities;
  tokenExpiresAt?: string;
  refreshTokenExpiresAt?: string;
  lastRefreshedAt?: string;
  lastValidatedAt?: string;
  lastHealthCheckAt?: string;
  healthSummary?: string;
  healthDetails?: string;
  createdAt: string;
  modifiedAt?: string;
}

interface ApiConnectionDiagnosticsDto {
  connectionId: string;
  channel: string;
  displayName: string;
  status: string;
  isHealthy: boolean;
  healthSummary?: string;
  lastErrorMessage?: string;
  lastHealthCheckAt?: string;
  tokenExpiresAt?: string;
  webhookUrl: string;
  eventsProcessed24h: number;
  eventsFailed24h: number;
  deadLetterCount: number;
  lastEventAt?: string;
}

interface ApiWebhookEventDto {
  id: string;
  connectionId: string;
  channel: string;
  providerEventId: string;
  eventType?: string;
  status: string;
  attemptCount: number;
  maxAttempts: number;
  failureClassification?: string;
  errorMessage?: string;
  occurredAt: string;
  receivedAt: string;
  processedAt?: string;
  deadLetteredAt?: string;
}

interface ApiPagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

interface ApiWebhookReplayResult {
  succeeded: boolean;
  errorMessage?: string;
  resumedAt?: string;
}

function mapStatus(status: string): ConnectionStatus {
  const s = status.toLowerCase();
  if (s === "active" || s === "connected") return "connected";
  if (s === "pending" || s === "expired") return "pending_reauth";
  if (s === "degraded") return "error";
  return "disconnected";
}

function mapProvider(channel: string): ProviderType {
  const c = channel.toLowerCase();
  if (c === "whatsapp") return "whatsapp";
  if (c === "instagram") return "instagram";
  if (c === "messenger" || c === "facebook") return "facebook";
  if (c === "telegram") return "telegram";
  if (c === "viber") return "viber";
  if (c === "tiktok") return "tiktok";
  return "simulator";
}

function mapEventStatus(status: string): WebhookEvent["status"] {
  const s = status.toLowerCase();
  if (s === "processed" || s === "success") return "processed";
  if (s === "deadletter" || s === "dead_letter") return "dead_letter";
  return "failed";
}

function mapConnection(dto: ApiChannelConnectionDto): ChannelConnection {
  const status = mapStatus(dto.status);
  const provider = mapProvider(dto.channel);

  return {
    id: dto.id,
    tenantId: dto.tenantId,
    provider,
    accountName: dto.displayName,
    accountIdentifier: dto.externalAccountId,
    status,
    capabilities: {
      canReceiveMessages: dto.capabilities?.canReceiveText ?? true,
      canSendMessages: dto.capabilities?.canSendText ?? true,
      canSendMedia: dto.capabilities?.canSendMedia ?? false,
      canReceiveMedia: dto.capabilities?.canReceiveMedia ?? false,
      supportsTemplates: dto.capabilities?.requiresTemplatesOutsideWindow ?? false,
      supportsDeliveryReceipts: dto.capabilities?.supportsDeliveryReceipts ?? true,
    },
    health: {
      status,
      tokenExpiresAt: dto.tokenExpiresAt,
      webhookUrl: `/v1/webhooks/${dto.channel.toLowerCase()}/${dto.id}`,
      eventsProcessed24h: 0,
      eventsFailed24h: 0,
    },
    connectedAt: dto.createdAt,
    updatedAt: dto.modifiedAt ?? dto.createdAt,
  };
}

export const apiIntegrationClient: IntegrationClient = {
  async listConnections(): Promise<ChannelConnection[]> {
    const list = await apiFetch<ApiChannelConnectionDto[]>("/v1/integrations/connections", {
      headers: headers(),
    });

    return list.map(mapConnection);
  },

  async getConnection(id: string): Promise<ChannelConnection> {
    const dto = await apiFetch<ApiChannelConnectionDto>(`/v1/integrations/connections/${id}`, {
      headers: headers(),
    });

    const conn = mapConnection(dto);

    // Also fetch diagnostics to enrich health stats
    try {
      const diag = await apiFetch<ApiConnectionDiagnosticsDto>(
        `/v1/integrations/connections/${id}/diagnostics`,
        { headers: headers() }
      );
      conn.health = {
        status: mapStatus(diag.status),
        lastEventAt: diag.lastEventAt,
        lastErrorMessage: diag.lastErrorMessage,
        tokenExpiresAt: diag.tokenExpiresAt ?? conn.health.tokenExpiresAt,
        webhookUrl: diag.webhookUrl,
        eventsProcessed24h: diag.eventsProcessed24h,
        eventsFailed24h: diag.eventsFailed24h,
      };
    } catch {
      // Degrade gracefully if diagnostics endpoint is unavailable
    }

    return conn;
  },

  async getHealth(connectionId: string): Promise<ConnectionHealth> {
    const diag = await apiFetch<ApiConnectionDiagnosticsDto>(
      `/v1/integrations/connections/${connectionId}/diagnostics`,
      { headers: headers() }
    );

    return {
      status: mapStatus(diag.status),
      lastEventAt: diag.lastEventAt,
      lastErrorMessage: diag.lastErrorMessage,
      tokenExpiresAt: diag.tokenExpiresAt,
      webhookUrl: diag.webhookUrl,
      eventsProcessed24h: diag.eventsProcessed24h,
      eventsFailed24h: diag.eventsFailed24h,
    };
  },

  async getWebhookEvents(
    connectionId: string,
    page: number = 1,
    pageSize: number = 20
  ): Promise<PaginatedResult<WebhookEvent>> {
    const paged = await apiFetch<ApiPagedResult<ApiWebhookEventDto>>(
      `/v1/integrations/connections/${connectionId}/webhooks?page=${page}&pageSize=${pageSize}`,
      { headers: headers() }
    );

    const items: WebhookEvent[] = paged.items.map((e) => ({
      id: e.id,
      connectionId: e.connectionId,
      providerEventId: e.providerEventId,
      eventType: e.eventType ?? "message",
      status: mapEventStatus(e.status),
      retryCount: e.attemptCount,
      processedAt: e.processedAt,
      failureReason: e.errorMessage,
      createdAt: e.receivedAt,
    }));

    return {
      items,
      cursor: null,
      hasMore: paged.hasNextPage,
      totalCount: paged.totalCount,
    };
  },

  async replayWebhook(id: string, idempotencyKey?: string): Promise<WebhookReplayResult> {
    const key = idempotencyKey ?? crypto.randomUUID();
    const csrfToken = await getCsrfToken();

    const res = await apiFetch<ApiWebhookReplayResult>(
      `/v1/integrations/webhooks/${id}/replay`,
      {
        method: "POST",
        headers: {
          ...headers(),
          "Idempotency-Key": key,
          "X-CSRF-Token": csrfToken,
        },
      }
    );

    return {
      success: res.succeeded,
      message: res.errorMessage,
      resumedAt: res.resumedAt,
    };
  },

  async reconnect(connectionId: string): Promise<ConnectionHealth> {
    const csrfToken = await getCsrfToken();

    await apiFetch(`/v1/integrations/connections/${connectionId}/health`, {
      method: "POST",
      headers: {
        ...headers(),
        "X-CSRF-Token": csrfToken,
      },
    });

    return this.getHealth(connectionId);
  },
};

