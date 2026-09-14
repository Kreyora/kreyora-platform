using Kreyora.Application.Notifications;
using Kreyora.Domain.Notifications;

namespace Kreyora.Infrastructure.Notifications;

public sealed class NotificationTemplateRegistry : INotificationTemplateRegistry
{
    private static readonly Dictionary<string, IReadOnlyList<NotificationTemplateMapping>> EventMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["order.created.v1"] = [new NotificationTemplateMapping("order_created", 1, NotificationChannel.Email)],
        ["order.confirmed.v1"] = [new NotificationTemplateMapping("order_confirmed", 1, NotificationChannel.Email)],
        ["order.cancelled.v1"] = [new NotificationTemplateMapping("order_cancelled", 1, NotificationChannel.Email)],
        ["order.dispatched.v1"] = [new NotificationTemplateMapping("order_dispatched", 1, NotificationChannel.Email)],
        ["order.delivered.v1"] = [new NotificationTemplateMapping("order_delivered", 1, NotificationChannel.Email)],
        ["order.delivery_failed.v1"] = [new NotificationTemplateMapping("order_delivery_failed", 1, NotificationChannel.Email)],
        ["payment.verified.v1"] = [new NotificationTemplateMapping("payment_verified", 1, NotificationChannel.Email)],
        ["payment.rejected.v1"] = [new NotificationTemplateMapping("payment_rejected", 1, NotificationChannel.Email)]
    };

    public IReadOnlyList<NotificationTemplateMapping> GetTemplatesForEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return [];
        return EventMappings.TryGetValue(eventType, out var mappings) ? mappings : [];
    }

    public RenderedNotification Render(string templateCode, int version, IReadOnlyDictionary<string, string> data)
    {
        var orderNumber = GetValue(data, "orderNumber", "N/A");
        var customerName = GetValue(data, "customerName", "Customer");
        var reason = GetValue(data, "reason", "No reason provided");

        return templateCode.ToLowerInvariant() switch
        {
            "order_created" => new RenderedNotification(
                $"Order Placed - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your order #{orderNumber} has been placed successfully. We will notify you once the seller confirms your order.</p>"),

            "order_confirmed" => new RenderedNotification(
                $"Order Confirmed - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your order #{orderNumber} has been confirmed by the seller and is being prepared.</p>"),

            "order_cancelled" => new RenderedNotification(
                $"Order Cancelled - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your order #{orderNumber} has been cancelled.</p><p><strong>Reason:</strong> {reason}</p>"),

            "order_dispatched" => new RenderedNotification(
                $"Order Dispatched - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your order #{orderNumber} is on its way! Our delivery courier will contact you upon arrival.</p>"),

            "order_delivered" => new RenderedNotification(
                $"Order Delivered - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your order #{orderNumber} has been successfully delivered. Thank you for choosing us!</p>"),

            "order_delivery_failed" => new RenderedNotification(
                $"Delivery Attempt Failed - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Our courier was unable to deliver order #{orderNumber}.</p><p><strong>Note:</strong> {reason}</p>"),

            "payment_verified" => new RenderedNotification(
                $"Payment Verified - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your payment for order #{orderNumber} has been verified successfully.</p>"),

            "payment_rejected" => new RenderedNotification(
                $"Payment Verification Issue - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Your payment verification for order #{orderNumber} was not accepted.</p><p><strong>Reason:</strong> {reason}</p>"),

            _ => new RenderedNotification(
                $"Notification - #{orderNumber}",
                $"<p>Namaste {customerName},</p><p>Update regarding your order #{orderNumber}.</p>")
        };
    }

    private static string GetValue(IReadOnlyDictionary<string, string> data, string key, string defaultValue) =>
        data.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : defaultValue;
}

