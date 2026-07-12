import type { TenantId, Timestamp } from "./common";

export interface AuditActor {
  id: string;
  name: string;
  role: string;
  type: "user" | "system" | "bot";
}

export interface AuditEvent {
  id: string;
  tenantId: TenantId;
  actor: AuditActor;
  action: string;
  resourceType: string;
  resourceId: string;
  details: Record<string, string>;
  correlationId: string;
  createdAt: Timestamp;
}
