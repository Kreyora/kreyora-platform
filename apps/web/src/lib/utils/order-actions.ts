import type { OrderStatus, PaymentStatus, FulfilmentStatus } from "@/lib/types";
import type { Role } from "@/lib/types/identity";

export type OrderAction =
  | "confirm"
  | "cancel"
  | "verify_payment"
  | "reject_payment"
  | "mark_cod_collected"
  | "prepare"
  | "dispatch"
  | "deliver";

export interface ActionDef {
  action: OrderAction;
  label: string;
  variant: "solid" | "outline" | "ghost";
  requiresReason: boolean;
  destructive: boolean;
}

const ACTION_DEFS: Record<OrderAction, ActionDef> = {
  confirm: { action: "confirm", label: "Confirm Order", variant: "solid", requiresReason: false, destructive: false },
  cancel: { action: "cancel", label: "Cancel Order", variant: "ghost", requiresReason: true, destructive: true },
  verify_payment: { action: "verify_payment", label: "Verify Payment", variant: "solid", requiresReason: false, destructive: false },
  reject_payment: { action: "reject_payment", label: "Reject Payment", variant: "ghost", requiresReason: true, destructive: true },
  mark_cod_collected: { action: "mark_cod_collected", label: "Mark COD Collected", variant: "solid", requiresReason: false, destructive: false },
  prepare: { action: "prepare", label: "Mark Ready", variant: "outline", requiresReason: false, destructive: false },
  dispatch: { action: "dispatch", label: "Mark Dispatched", variant: "outline", requiresReason: false, destructive: false },
  deliver: { action: "deliver", label: "Mark Delivered", variant: "solid", requiresReason: false, destructive: false },
};

export function getAllowedActions(
  status: OrderStatus,
  paymentStatus: PaymentStatus,
  fulfilmentStatus: FulfilmentStatus,
  paymentMethod: "cod" | "merchant_qr",
  role: Role,
): ActionDef[] {
  if (role === "viewer") return [];
  if (status === "fulfilled" || status === "cancelled") return [];

  const actions: OrderAction[] = [];

  if (status === "pending_confirmation") {
    actions.push("confirm", "cancel");
  }

  if (status === "confirmed" || status === "processing") {
    if (paymentMethod === "merchant_qr" && paymentStatus === "awaiting_verification") {
      actions.push("verify_payment", "reject_payment");
    }
    if (paymentMethod === "cod" && paymentStatus === "pending") {
      actions.push("mark_cod_collected");
    }

    if (fulfilmentStatus === "unfulfilled") {
      actions.push("prepare");
    }
    if (fulfilmentStatus === "ready") {
      actions.push("dispatch");
    }
    if (fulfilmentStatus === "dispatched") {
      actions.push("deliver");
    }

    actions.push("cancel");
  }

  return actions.map((a) => ACTION_DEFS[a]);
}
