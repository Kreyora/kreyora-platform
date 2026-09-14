using Kreyora.Domain.Tenancy;

namespace Kreyora.Domain.Orders;

public static class OrderTransitionPolicy
{
    private static readonly OrderAction[] AllActions = Enum.GetValues<OrderAction>();

    public static bool RequiresReason(OrderAction action) =>
        action is OrderAction.Cancel or OrderAction.RejectPayment or OrderAction.MarkDeliveryFailed;

    public static bool IsDestructive(OrderAction action) =>
        action is OrderAction.Cancel or OrderAction.RejectPayment or OrderAction.MarkDeliveryFailed;

    public static bool ValidateReason(OrderAction action, string? reason, out string? validationError)
    {
        if (RequiresReason(action))
        {
            var trimmed = reason?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 3 || trimmed.Length > 500)
            {
                validationError = OrderActionDenialReasons.ReasonRequired;
                return false;
            }
        }

        validationError = null;
        return true;
    }

    public static bool CanExecute(Order order, OrderAction action, TenantRole? role, out string? denialReason)
    {
        ArgumentNullException.ThrowIfNull(order);

        // 1. Role Authorization Check
        if (role is null || role == TenantRole.Viewer)
        {
            denialReason = OrderActionDenialReasons.RoleNotAuthorized;
            return false;
        }

        if (action is OrderAction.VerifyPayment or OrderAction.RejectPayment or OrderAction.MarkCodCollected)
        {
            if (role != TenantRole.Owner && role != TenantRole.Admin)
            {
                denialReason = OrderActionDenialReasons.RoleNotAuthorized;
                return false;
            }
        }
        else
        {
            if (role != TenantRole.Owner && role != TenantRole.Admin && role != TenantRole.Operator)
            {
                denialReason = OrderActionDenialReasons.RoleNotAuthorized;
                return false;
            }
        }

        // 2. Terminal State Guardrails
        if (order.Status == OrderStatus.Cancelled || order.FulfilmentStatus == FulfilmentStatus.Cancelled)
        {
            denialReason = OrderActionDenialReasons.OrderAlreadyCancelled;
            return false;
        }

        if (order.Status == OrderStatus.Fulfilled || order.FulfilmentStatus == FulfilmentStatus.Delivered)
        {
            if (action == OrderAction.Cancel)
            {
                denialReason = OrderActionDenialReasons.OrderAlreadyFulfilled;
                return false;
            }

            if (action is OrderAction.Prepare or OrderAction.Dispatch or OrderAction.Deliver or OrderAction.MarkDeliveryFailed)
            {
                denialReason = OrderActionDenialReasons.OrderAlreadyFulfilled;
                return false;
            }

            if (action == OrderAction.Confirm)
            {
                denialReason = OrderActionDenialReasons.OrderNotPendingConfirmation;
                return false;
            }
        }

        // 3. Action-Specific State Constraints
        switch (action)
        {
            case OrderAction.Confirm:
                if (order.Status != OrderStatus.PendingConfirmation)
                {
                    denialReason = OrderActionDenialReasons.OrderNotPendingConfirmation;
                    return false;
                }
                break;

            case OrderAction.Cancel:
                if (order.FulfilmentStatus == FulfilmentStatus.Dispatched)
                {
                    denialReason = OrderActionDenialReasons.DispatchedOrderCannotCancelDirectly;
                    return false;
                }
                break;

            case OrderAction.Prepare:
                if (order.Status == OrderStatus.PendingConfirmation)
                {
                    denialReason = OrderActionDenialReasons.OrderNotConfirmed;
                    return false;
                }

                if (order.FulfilmentStatus == FulfilmentStatus.Ready)
                {
                    denialReason = "Order is already prepared and ready.";
                    return false;
                }

                if (order.FulfilmentStatus == FulfilmentStatus.Dispatched)
                {
                    denialReason = "Order has already been dispatched.";
                    return false;
                }

                if (order.FulfilmentStatus != FulfilmentStatus.Unfulfilled && order.FulfilmentStatus != FulfilmentStatus.Failed)
                {
                    denialReason = "Order fulfilment cannot be prepared from current state.";
                    return false;
                }
                break;

            case OrderAction.Dispatch:
                if (order.FulfilmentStatus == FulfilmentStatus.Unfulfilled)
                {
                    denialReason = OrderActionDenialReasons.OrderNotReady;
                    return false;
                }

                if (order.FulfilmentStatus == FulfilmentStatus.Dispatched)
                {
                    denialReason = "Order has already been dispatched.";
                    return false;
                }

                if (order.FulfilmentStatus != FulfilmentStatus.Ready)
                {
                    denialReason = OrderActionDenialReasons.OrderNotReady;
                    return false;
                }

                if (order.PaymentMethod == OrderPaymentMethod.MerchantQr && order.PaymentStatus != PaymentStatus.Paid)
                {
                    denialReason = OrderActionDenialReasons.MerchantQrPaymentUnverified;
                    return false;
                }
                break;

            case OrderAction.Deliver:
                if (order.FulfilmentStatus != FulfilmentStatus.Dispatched)
                {
                    denialReason = OrderActionDenialReasons.OrderNotDispatched;
                    return false;
                }
                break;

            case OrderAction.MarkDeliveryFailed:
                if (order.FulfilmentStatus != FulfilmentStatus.Dispatched)
                {
                    denialReason = OrderActionDenialReasons.OrderNotDispatched;
                    return false;
                }
                break;

            case OrderAction.VerifyPayment:
                if (order.PaymentMethod != OrderPaymentMethod.MerchantQr)
                {
                    denialReason = OrderActionDenialReasons.PaymentMethodNotMerchantQr;
                    return false;
                }

                if (order.PaymentStatus == PaymentStatus.Paid)
                {
                    denialReason = OrderActionDenialReasons.PaymentAlreadyPaid;
                    return false;
                }

                if (order.PaymentStatus != PaymentStatus.AwaitingVerification)
                {
                    denialReason = OrderActionDenialReasons.PaymentNotAwaitingVerification;
                    return false;
                }
                break;

            case OrderAction.RejectPayment:
                if (order.PaymentMethod != OrderPaymentMethod.MerchantQr)
                {
                    denialReason = OrderActionDenialReasons.PaymentMethodNotMerchantQr;
                    return false;
                }

                if (order.PaymentStatus == PaymentStatus.Paid)
                {
                    denialReason = OrderActionDenialReasons.PaymentAlreadyPaid;
                    return false;
                }

                if (order.PaymentStatus != PaymentStatus.AwaitingVerification)
                {
                    denialReason = OrderActionDenialReasons.PaymentNotAwaitingVerification;
                    return false;
                }
                break;

            case OrderAction.MarkCodCollected:
                if (order.PaymentMethod != OrderPaymentMethod.CashOnDelivery)
                {
                    denialReason = OrderActionDenialReasons.PaymentMethodNotCod;
                    return false;
                }

                if (order.PaymentStatus == PaymentStatus.Paid)
                {
                    denialReason = OrderActionDenialReasons.PaymentAlreadyPaid;
                    return false;
                }

                if (order.FulfilmentStatus != FulfilmentStatus.Dispatched && order.FulfilmentStatus != FulfilmentStatus.Delivered)
                {
                    denialReason = OrderActionDenialReasons.CodNotReadyForCollection;
                    return false;
                }

                if (order.PaymentStatus != PaymentStatus.Pending)
                {
                    denialReason = "COD payment is not in pending status.";
                    return false;
                }
                break;

            default:
                denialReason = "Unknown action.";
                return false;
        }

        denialReason = null;
        return true;
    }

    public static IReadOnlyList<OrderActionEvaluation> GetAllowedActions(Order order, TenantRole? role)
    {
        ArgumentNullException.ThrowIfNull(order);
        var evaluations = new List<OrderActionEvaluation>(AllActions.Length);

        foreach (var action in AllActions)
        {
            var isAllowed = CanExecute(order, action, role, out var denialReason);
            evaluations.Add(new OrderActionEvaluation(
                action,
                isAllowed,
                denialReason,
                RequiresReason(action),
                IsDestructive(action)));
        }

        return evaluations;
    }
}

