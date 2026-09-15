import { apiFetch } from "@/lib/api";
import { selectedWorkspaceId } from "@/lib/session/workspace-selection";
import type { PaymentClient } from "@/lib/ports/payment-client";
import type {
  PaymentMethod,
  PaymentAttempt,
  PaymentAttemptStatus,
} from "@/lib/types/payments";

const headers = () => ({ "X-Kreyora-Tenant-Id": selectedWorkspaceId() ?? "" });

interface ApiPaymentProofItem {
  id: string;
  paymentAttemptId: string;
  contentType: string;
  byteSize: number;
  status: string;
  customerNote?: string;
  uploadExpiresAt: string;
  readyAt?: string;
}

interface ApiPaymentAttemptItem {
  id: string;
  orderId: string;
  method: string;
  status: string;
  amountNpr: number;
  currency: string;
  internalReference: string;
  providerReference?: string;
  verifiedAt?: string;
  verifiedByUserId?: string;
  rejectedAt?: string;
  rejectedByUserId?: string;
  rejectionReason?: string;
  collectedAt?: string;
  collectedByUserId?: string;
  proofs: ApiPaymentProofItem[];
}

function normalizeAttemptStatus(status: string): PaymentAttemptStatus {
  const lower = status.toLowerCase().replace(/[\s-]/g, "_");
  if (lower === "awaitingverification" || lower === "awaitingproof") return "awaiting_verification";
  if (lower === "verified" || lower === "collected") return "verified";
  if (lower === "rejected") return "rejected";
  if (lower === "failed" || lower === "expired") return "failed";
  return "pending";
}

function getBaseUrl(): string {
  return process.env.NEXT_PUBLIC_API_URL ?? "";
}

export const apiPaymentClient: PaymentClient = {
  async getPaymentMethods(_tenantId: string): Promise<PaymentMethod[]> {
    // Return standard payment methods for the active workspace
    return [
      {
        id: "pm-cod",
        tenantId: selectedWorkspaceId() ?? "",
        type: "cod",
        label: "Cash on Delivery",
        isEnabled: true,
        updatedAt: new Date().toISOString(),
      },
      {
        id: "pm-qr",
        tenantId: selectedWorkspaceId() ?? "",
        type: "merchant_qr",
        label: "Merchant QR",
        isEnabled: true,
        updatedAt: new Date().toISOString(),
      },
    ];
  },

  async getPaymentAttempts(orderId: string): Promise<PaymentAttempt[]> {
    const attempts = await apiFetch<ApiPaymentAttemptItem[]>(
      `/v1/orders/${orderId}/payments`,
      { headers: headers() },
    );

    return attempts.map((a) => {
      const readyProof = a.proofs?.find((p) => p.status.toLowerCase() === "ready") ?? a.proofs?.[0];
      const proofId = readyProof?.id;
      const proofUrl = proofId
        ? `${getBaseUrl()}/v1/payments/proofs/${proofId}/content`
        : undefined;

      return {
        id: a.id,
        orderId: a.orderId,
        method: a.method.toLowerCase().includes("qr") ? "merchant_qr" : "cod",
        amount: { amount: a.amountNpr, currency: "NPR" },
        status: normalizeAttemptStatus(a.status),
        proofId,
        proofUrl,
        proofs: a.proofs?.map((p) => ({
          id: p.id,
          paymentAttemptId: p.paymentAttemptId,
          contentType: p.contentType,
          byteSize: p.byteSize,
          status: p.status,
          customerNote: p.customerNote,
        })),
        verifiedBy: a.verifiedByUserId,
        verifiedAt: a.verifiedAt,
        rejectionReason: a.rejectionReason,
        createdAt: a.verifiedAt ?? a.rejectedAt ?? a.collectedAt ?? new Date().toISOString(),
      };
    });
  },

  getProofContentUrl(proofId: string): string {
    return `${getBaseUrl()}/v1/payments/proofs/${proofId}/content`;
  },
};

