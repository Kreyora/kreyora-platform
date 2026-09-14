using Kreyora.Domain.Notifications;
using Kreyora.Infrastructure.Notifications;

namespace Kreyora.UnitTests.Notifications;

public sealed class NotificationTemplateRegistryTests
{
    private readonly NotificationTemplateRegistry registry = new();

    [Theory]
    [InlineData("order.created.v1", "order_created")]
    [InlineData("order.confirmed.v1", "order_confirmed")]
    [InlineData("order.cancelled.v1", "order_cancelled")]
    [InlineData("order.dispatched.v1", "order_dispatched")]
    [InlineData("order.delivered.v1", "order_delivered")]
    [InlineData("order.delivery_failed.v1", "order_delivery_failed")]
    [InlineData("payment.verified.v1", "payment_verified")]
    [InlineData("payment.rejected.v1", "payment_rejected")]
    public void GetTemplatesForEvent_KnownEvents_ReturnsCorrectMapping(string eventType, string expectedTemplate)
    {
        var templates = registry.GetTemplatesForEvent(eventType);

        var mapping = Assert.Single(templates);
        Assert.Equal(expectedTemplate, mapping.TemplateCode);
        Assert.Equal(1, mapping.TemplateVersion);
        Assert.Equal(NotificationChannel.Email, mapping.Channel);
    }

    [Fact]
    public void GetTemplatesForEvent_UnknownEvent_ReturnsEmpty()
    {
        Assert.Empty(registry.GetTemplatesForEvent("order.prepared.v1"));
        Assert.Empty(registry.GetTemplatesForEvent("payment.cod_collected.v1"));
        Assert.Empty(registry.GetTemplatesForEvent("unknown.event"));
    }

    [Fact]
    public void Render_OrderConfirmed_RendersSubjectAndBody()
    {
        var data = new Dictionary<string, string>
        {
            ["orderNumber"] = "ORD-12345",
            ["customerName"] = "Hari Thapa"
        };

        var rendered = registry.Render("order_confirmed", 1, data);

        Assert.Contains("ORD-12345", rendered.Subject);
        Assert.Contains("Hari Thapa", rendered.Body);
        Assert.Contains("confirmed", rendered.Body);
    }

    [Fact]
    public void Render_OrderCancelled_RendersReason()
    {
        var data = new Dictionary<string, string>
        {
            ["orderNumber"] = "ORD-999",
            ["customerName"] = "Sita Sharma",
            ["reason"] = "Customer changed mind"
        };

        var rendered = registry.Render("order_cancelled", 1, data);

        Assert.Contains("ORD-999", rendered.Subject);
        Assert.Contains("Sita Sharma", rendered.Body);
        Assert.Contains("Customer changed mind", rendered.Body);
    }
}

