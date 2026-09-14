using System.Text.Json;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Notifications;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Common;
using Kreyora.Domain.Notifications;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Notifications;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Persistence.Entities;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests.Notifications;

public sealed class NotificationOutboxLifecycleTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public NotificationOutboxLifecycleTests(PostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task OutboxProcessor_ConsumesOutboxMessage_AndCreatesNotificationRequest()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-outbox-create");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        // Add outbox message for order.confirmed.v1
        var outboxMessage = new OutboxMessage
        {
            TenantId = tenant.Id,
            Type = "order.confirmed.v1",
            Content = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                storeId = order.StoreId,
                action = "Confirm"
            }),
            CreatedAt = clock.UtcNow
        };
        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var processor = new OutboxNotificationProcessorJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxNotificationProcessorJob>.Instance);

        var count = await processor.ProcessTenantAsync(services, tenant.Id);
        Assert.Equal(1, count);

        // Verify outbox message is marked processed
        db.ChangeTracker.Clear();
        var updatedMessage = await db.OutboxMessages.SingleAsync(m => m.Id == outboxMessage.Id);
        Assert.NotNull(updatedMessage.ProcessedAt);
        Assert.Null(updatedMessage.Error);

        // Verify notification request was created
        var notification = await db.NotificationRequests.SingleAsync(n => n.TenantId == tenant.Id && n.SourceEventId == outboxMessage.Id);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal("order_confirmed", notification.TemplateCode);
        Assert.Equal(NotificationChannel.Email, notification.Channel);
        Assert.Equal(order.CustomerEmail, notification.RecipientContact);
        Assert.Equal(order.CustomerName, notification.RecipientName);
        Assert.Equal(0, notification.AttemptCount);
    }

    [Fact]
    public async Task OutboxProcessor_IsIdempotent_DoesNotDuplicateNotificationRequests()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-outbox-idem");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var outboxMessage = new OutboxMessage
        {
            TenantId = tenant.Id,
            Type = "order.confirmed.v1",
            Content = JsonSerializer.Serialize(new { orderId = order.Id, orderNumber = order.OrderNumber }),
            CreatedAt = clock.UtcNow
        };
        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var processor = new OutboxNotificationProcessorJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxNotificationProcessorJob>.Instance);

        // Process 1
        await processor.ProcessTenantAsync(services, tenant.Id);

        // Un-mark processed to simulate worker retry / re-processing
        outboxMessage.ProcessedAt = null;
        await db.SaveChangesAsync();

        // Process 2
        await processor.ProcessTenantAsync(services, tenant.Id);

        // Verify still exactly 1 notification request
        db.ChangeTracker.Clear();
        var notifications = await db.NotificationRequests.Where(n => n.TenantId == tenant.Id && n.SourceEventId == outboxMessage.Id).ToListAsync();
        Assert.Single(notifications);
    }

    [Fact]
    public async Task OutboxProcessor_IgnoresEventsWithoutTemplates()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-outbox-ignore");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var outboxMessage = new OutboxMessage
        {
            TenantId = tenant.Id,
            Type = "order.prepared.v1",
            Content = JsonSerializer.Serialize(new { orderId = "some-id" }),
            CreatedAt = clock.UtcNow
        };
        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var processor = new OutboxNotificationProcessorJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxNotificationProcessorJob>.Instance);

        var count = await processor.ProcessTenantAsync(services, tenant.Id);
        Assert.Equal(1, count);

        // Message is processed, but 0 notification requests created
        var updated = await db.OutboxMessages.SingleAsync(m => m.Id == outboxMessage.Id);
        Assert.NotNull(updated.ProcessedAt);

        var notifications = await db.NotificationRequests.Where(n => n.TenantId == tenant.Id).ToListAsync();
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task NotificationDelivery_DevSink_RendersTemplateAndLogsOutput()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-deliver-dev");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        var outboxMessage = new OutboxMessage
        {
            TenantId = tenant.Id,
            Type = "order.confirmed.v1",
            Content = JsonSerializer.Serialize(new { orderId = order.Id, orderNumber = order.OrderNumber }),
            CreatedAt = clock.UtcNow
        };
        db.OutboxMessages.Add(outboxMessage);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);

        // 1. Process outbox
        var processor = new OutboxNotificationProcessorJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxNotificationProcessorJob>.Instance);
        await processor.ProcessTenantAsync(services, tenant.Id);

        // 2. Deliver
        var deliveryJob = new NotificationDeliveryJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NotificationDeliveryJob>.Instance);
        var delivered = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, delivered);

        // Verify notification request status
        db.ChangeTracker.Clear();
        var notification = await db.NotificationRequests
            .Include(n => n.DeliveryAttempts)
            .SingleAsync(n => n.TenantId == tenant.Id && n.SourceEventId == outboxMessage.Id);

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        Assert.NotNull(notification.DeliveredAt);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Null(notification.NextRetryAt);

        var attempt = Assert.Single(notification.DeliveryAttempts);
        Assert.True(attempt.Succeeded);
        Assert.Equal(DevelopmentNotificationProvider.Name, attempt.ProviderName);
        Assert.StartsWith("dev-", attempt.ProviderReference);

        // Verify development delivery log was created
        var log = await db.NotificationDeliveryLogs.SingleAsync(l => l.TenantId == tenant.Id && l.NotificationRequestId == notification.Id);
        Assert.Contains(order.OrderNumber, log.SubjectRendered);
        Assert.Contains(order.CustomerName, log.BodyRendered);
        Assert.Equal("h***@example.com", log.RecipientRedacted);
    }

    [Fact]
    public async Task NotificationDelivery_BoundedRetries_TransitionsToDeadLettered()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-retry-dlq");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var order = await CreateDirectOrderAsync(db, tenant.Id);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        // Setup notification request directly
        var notification = NotificationRequest.Create(
            tenant.Id,
            "order.confirmed.v1",
            "source-01",
            "order_confirmed",
            1,
            NotificationChannel.Email,
            "customer@example.com",
            order.CustomerName,
            maxAttempts: 3);
        db.NotificationRequests.Add(notification);
        await db.SaveChangesAsync();

        // Use failing delivery provider
        var failingProvider = new FailingNotificationProvider();
        var services = BuildServices(db, accessor, clock, failingProvider);
        var deliveryJob = new NotificationDeliveryJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NotificationDeliveryJob>.Instance);

        // Attempt 1 -> Failed, backoff 1 minute
        var d1 = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, d1);
        db.ChangeTracker.Clear();
        var n1 = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Failed, n1.Status);
        Assert.Equal(1, n1.AttemptCount);
        Assert.Equal(clock.UtcNow.AddMinutes(1), n1.NextRetryAt);

        // Advance clock by 30 seconds -> NextRetryAt not reached -> not delivered
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        db.ChangeTracker.Clear();
        var d1_skip = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(0, d1_skip);
        db.ChangeTracker.Clear();
        var n1_nochange = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(1, n1_nochange.AttemptCount);

        // Advance clock past 1 minute -> Attempt 2 -> Failed, backoff 5 minutes
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        db.ChangeTracker.Clear();
        var d2 = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, d2);
        db.ChangeTracker.Clear();
        var n2 = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Failed, n2.Status);
        Assert.Equal(2, n2.AttemptCount);
        Assert.Equal(clock.UtcNow.AddMinutes(5), n2.NextRetryAt);

        // Advance clock past 5 minutes -> Attempt 3 -> Max attempts reached -> DeadLettered!
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        db.ChangeTracker.Clear();
        var d3 = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, d3);
        db.ChangeTracker.Clear();
        var n3 = await db.NotificationRequests.Include(n => n.DeliveryAttempts).SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.DeadLettered, n3.Status);
        Assert.Equal(3, n3.AttemptCount);
        Assert.NotNull(n3.DeadLetteredAt);
        Assert.Null(n3.NextRetryAt);
        Assert.Equal(3, n3.DeliveryAttempts.Count);
        Assert.All(n3.DeliveryAttempts, a => Assert.False(a.Succeeded));
    }

    [Fact]
    public async Task NotificationReplay_AuthorizedOwner_ResetsDeadLetterToPending_AndAudits()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-replay-auth");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var notification = NotificationRequest.Create(
            tenant.Id, "order.confirmed.v1", "source-replay", "order_confirmed", 1,
            NotificationChannel.Email, "customer@example.com", "Hari Thapa", maxAttempts: 1);
        notification.MarkDelivering(clock.UtcNow);
        notification.RecordFailure("SMTP Connection Refused", clock.UtcNow, new NotificationRetryPolicy(1));
        Assert.Equal(NotificationStatus.DeadLettered, notification.Status);

        db.NotificationRequests.Add(notification);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var notificationService = services.GetRequiredService<INotificationService>();

        // Replay call
        var replayResult = await notificationService.ReplayAsync(
            new ReplayNotificationRequest(notification.Id, "replay-key-01"));

        Assert.True(replayResult.IsSuccess);
        Assert.Equal(NotificationStatus.Pending, replayResult.Value!.Status);
        Assert.Equal(0, replayResult.Value.AttemptCount);
        Assert.Null(replayResult.Value.DeadLetteredAt);

        // Verify audit event
        db.ChangeTracker.Clear();
        var audit = await db.AuditEvents.FirstOrDefaultAsync(a => a.TargetId == notification.Id && a.Action == "notification.replay");
        Assert.NotNull(audit);

        // Now deliver with working provider
        var deliveryJob = new NotificationDeliveryJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<NotificationDeliveryJob>.Instance);

        var delivered = await deliveryJob.DeliverTenantAsync(services, tenant.Id);
        Assert.Equal(1, delivered);

        db.ChangeTracker.Clear();
        var finalNotification = await db.NotificationRequests.SingleAsync(n => n.Id == notification.Id);
        Assert.Equal(NotificationStatus.Delivered, finalNotification.Status);
    }

    [Fact]
    public async Task NotificationReplay_NonAdminOrOperator_IsForbidden()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-replay-forbidden");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var notification = NotificationRequest.Create(
            tenant.Id, "order.confirmed.v1", "source-forbidden", "order_confirmed", 1,
            NotificationChannel.Email, "customer@example.com", "Hari Thapa", maxAttempts: 1);
        notification.MarkDelivering(clock.UtcNow);
        notification.RecordFailure("Error", clock.UtcNow, new NotificationRetryPolicy(1));
        db.NotificationRequests.Add(notification);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var notificationService = services.GetRequiredService<INotificationService>();

        // Switch to Operator context
        using (accessor.BeginScope(OperatorContext(tenant.Id)))
        {
            var replayResult = await notificationService.ReplayAsync(
                new ReplayNotificationRequest(notification.Id, "op-replay"));

            Assert.True(replayResult.IsFailure);
            Assert.Equal(403, replayResult.Error!.Status);
        }
    }

    [Fact]
    public async Task NotificationQueries_RedactCustomerPii_InListAndDetail()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-pii-redact");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var notification = NotificationRequest.Create(
            tenant.Id, "order.confirmed.v1", "source-pii", "order_confirmed", 1,
            NotificationChannel.Email, "sensitive.customer@example.com", "Ram Bahadur");
        db.NotificationRequests.Add(notification);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var notificationService = services.GetRequiredService<INotificationService>();

        // List
        var listResult = await notificationService.GetNotificationsAsync(new NotificationQuery());
        Assert.True(listResult.IsSuccess);
        var item = Assert.Single(listResult.Value!.Items);
        Assert.Equal("s***@example.com", item.RecipientContactRedacted);
        Assert.DoesNotContain("sensitive", item.RecipientContactRedacted);

        // Detail
        var detailResult = await notificationService.GetNotificationAsync(notification.Id);
        Assert.True(detailResult.IsSuccess);
        Assert.Equal("s***@example.com", detailResult.Value!.RecipientContactRedacted);
    }

    [Fact]
    public async Task DeadLetterQuery_ReturnsOnlyDeadLetteredNotifications_AndRequiresOwnerOrAdmin()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(db, "notif-dlq-view");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        // 1. Pending notification
        var pending = NotificationRequest.Create(
            tenant.Id, "order.confirmed.v1", "s1", "order_confirmed", 1,
            NotificationChannel.Email, "pending@example.com");

        // 2. DeadLettered notification
        var deadLettered = NotificationRequest.Create(
            tenant.Id, "order.cancelled.v1", "s2", "order_cancelled", 1,
            NotificationChannel.Email, "dead@example.com", maxAttempts: 1);
        deadLettered.MarkDelivering(clock.UtcNow);
        deadLettered.RecordFailure("Crash", clock.UtcNow, new NotificationRetryPolicy(1));

        db.NotificationRequests.AddRange(pending, deadLettered);
        await db.SaveChangesAsync();

        var services = BuildServices(db, accessor, clock);
        var notificationService = services.GetRequiredService<INotificationService>();

        // Owner queries dead-letter -> returns only dead-lettered
        var dlResult = await notificationService.GetDeadLetterAsync(new NotificationDeadLetterQuery());
        Assert.True(dlResult.IsSuccess);
        var dlItem = Assert.Single(dlResult.Value!.Items);
        Assert.Equal(deadLettered.Id, dlItem.Id);

        // Operator queries dead-letter -> 403 Forbidden
        using (accessor.BeginScope(OperatorContext(tenant.Id)))
        {
            var opResult = await notificationService.GetDeadLetterAsync(new NotificationDeadLetterQuery());
            Assert.True(opResult.IsFailure);
            Assert.Equal(403, opResult.Error!.Status);
        }
    }

    [Fact]
    public async Task CrossTenantIsolation_CannotAccessOrReplayOtherTenantNotification()
    {
        var accessor = new TenantContextAccessor();
        await using var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(db, "notif-iso-a");
        var tenantB = await CreateTenantAsync(db, "notif-iso-b");

        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        // Tenant A creates notification
        NotificationRequest notificationA;
        using (accessor.BeginScope(OwnerContext(tenantA.Id)))
        {
            notificationA = NotificationRequest.Create(
                tenantA.Id, "order.confirmed.v1", "sa", "order_confirmed", 1,
                NotificationChannel.Email, "customerA@example.com", maxAttempts: 1);
            notificationA.MarkDelivering(clock.UtcNow);
            notificationA.RecordFailure("Failed", clock.UtcNow, new NotificationRetryPolicy(1));
            db.NotificationRequests.Add(notificationA);
            await db.SaveChangesAsync();
        }

        // Tenant B attempts to read or replay Tenant A's notification
        using (accessor.BeginScope(OwnerContext(tenantB.Id)))
        {
            var servicesB = BuildServices(db, accessor, clock);
            var serviceB = servicesB.GetRequiredService<INotificationService>();

            var readResult = await serviceB.GetNotificationAsync(notificationA.Id);
            Assert.True(readResult.IsFailure);
            Assert.Equal(404, readResult.Error!.Status);

            var replayResult = await serviceB.ReplayAsync(new ReplayNotificationRequest(notificationA.Id, "cross-replay"));
            Assert.True(replayResult.IsFailure);
            Assert.Equal(404, replayResult.Error!.Status);
        }
    }

    private static ServiceProvider BuildServices(
        AppDbContext db,
        TenantContextAccessor accessor,
        MutableTimeProvider clock,
        INotificationDeliveryProvider? deliveryProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddSingleton<ITimeProvider>(clock);
        services.AddSingleton(Options.Create(new NotificationOptions()));
        services.AddSingleton<INotificationTemplateRegistry, NotificationTemplateRegistry>();
        services.AddScoped<ITenantPermissionAuthorizer, TenantPermissionAuthorizer>();
        services.AddScoped<IAuditEventService>(sp => new AuditEventService(db, accessor, new Correlation("test"), sp.GetRequiredService<ITenantPermissionAuthorizer>()));
        services.AddScoped<INotificationService, NotificationService>();

        if (deliveryProvider is not null)
        {
            services.AddSingleton(deliveryProvider);
        }
        else
        {
            services.AddScoped<INotificationDeliveryProvider, DevelopmentNotificationProvider>();
            services.AddLogging();
        }

        return services.BuildServiceProvider();
    }

    private static async Task<Order> CreateDirectOrderAsync(AppDbContext db, string tenantId)
    {
        var store = Store.Create(tenantId, new StoreSettings("Test Store", $"test-{Guid.NewGuid():N}"[..20], null, StoreThemePreset.Default, null,
            "Kreyora", "test@example.com", null, null, null, null, null, "Terms", "Privacy", "Returns", "Payment"));
        var rule = DeliveryRule.Create(tenantId, store.Id, new DeliveryRuleSettings("Test Delivery", 0, DeliveryFeeType.Flat, 100m, null, "1-2 days", true, true,
            [new DeliveryZoneInput("Kathmandu", "KMC", "Baluwatar")]));
        db.AddRange(store, rule);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var session = CheckoutSession.Create(new CheckoutSessionCreation(
            tenantId, store.Id, null, new string('a', 64), now.AddMinutes(15), now.AddMinutes(15),
            "Hari Thapa", "+9779800000001", "hari@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, new string('b', 64), now.AddDays(30), 1500m, 0m, 100m,
            0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true, now));
        session.Complete(now);
        db.CheckoutSessions.Add(session);
        await db.SaveChangesAsync();

        var order = Order.Create(new OrderCreation(
            tenantId, store.Id, session.Id, null, OrderPaymentMethod.CashOnDelivery,
            "Hari Thapa", "+9779800000001", "hari@example.com", "Baluwatar", null, "Kathmandu",
            "KMC", "Baluwatar", null, 1500m, 0m, 100m, 0m, 0m, 0m, 1600m, "NPR", rule.Id, rule.Name, "1-2 days", true));

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static TenantContext OwnerContext(string tenantId) => new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);
    private static TenantContext OperatorContext(string tenantId) => new(tenantId, "01J00000000000000000000003", "01J00000000000000000000004", TenantRole.Operator);

    private sealed class FailingNotificationProvider : INotificationDeliveryProvider
    {
        public string ProviderName => "FailingProvider";

        public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NotificationDeliveryResult(false, null, "Simulated network timeout connecting to provider."));
        }
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : ITimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
