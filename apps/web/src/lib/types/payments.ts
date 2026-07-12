import type { TenantId, Timestamp, Money } from "./common";

export type PaymentMethodConfigType = "cod" | "merchant_qr";

export interface PaymentMethod {
  id: string;
  tenantId: TenantId;
  type: PaymentMethodConfigType;
  label: string;
  isEnabled: boolean;
  qrImageUrl?: string;
  instructions?: string;
  updatedAt: Timestamp;
}

export type PaymentAttemptStatus = "pending" | "awaiting_verification" | "verified" | "rejected" | "failed";

export interface PaymentAttempt {
  id: string;
  orderId: string;
  method: PaymentMethodConfigType;
  amount: Money;
  status: PaymentAttemptStatus;
  proofUrl?: string;
  verifiedBy?: string;
  verifiedAt?: Timestamp;
  rejectionReason?: string;
  createdAt: Timestamp;
}

export interface PaymentVerification {
  attemptId: string;
  action: "verify" | "reject";
  reason?: string;
}
