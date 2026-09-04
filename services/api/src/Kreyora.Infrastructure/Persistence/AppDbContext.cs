using Kreyora.Application.Tenancy;
using Kreyora.Domain.Audit;
using Kreyora.Domain.Catalog;
using Kreyora.Domain.Common;
using Kreyora.Domain.Customers;
using Kreyora.Domain.Inventory;
using Kreyora.Domain.Orders;
using Kreyora.Domain.Storefront;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    private readonly ITenantContextAccessor? tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContextAccessor? tenantContext = null) : base(options)
    {
        this.tenantContext = tenantContext;
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<CatalogCommandIdempotency> CatalogCommandIdempotencyRecords => Set<CatalogCommandIdempotency>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<InventoryReservationCommand> InventoryReservationCommands => Set<InventoryReservationCommand>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreProductPublication> StoreProductPublications => Set<StoreProductPublication>();
    public DbSet<StoreCommandIdempotency> StoreCommandIdempotencyRecords => Set<StoreCommandIdempotency>();
    public DbSet<DeliveryRule> DeliveryRules => Set<DeliveryRule>();
    public DbSet<DeliveryRuleZone> DeliveryRuleZones => Set<DeliveryRuleZone>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();
    public DbSet<CheckoutSessionItem> CheckoutSessionItems => Set<CheckoutSessionItem>();
    public DbSet<CheckoutSessionCommand> CheckoutSessionCommands => Set<CheckoutSessionCommand>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderCommand> OrderCommands => Set<OrderCommand>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        builder.Entity<OutboxMessage>().HasQueryFilter(message => message.TenantId == CurrentTenantId);
        builder.Entity<AuditEvent>().HasQueryFilter(auditEvent => auditEvent.TenantId == CurrentTenantId);
        builder.Entity<SupportAccessGrant>().HasQueryFilter(grant => grant.TenantId == CurrentTenantId);
        builder.Entity<Product>().HasQueryFilter(product => product.TenantId == CurrentTenantId);
        builder.Entity<ProductVariant>().HasQueryFilter(variant => variant.TenantId == CurrentTenantId);
        builder.Entity<CatalogCommandIdempotency>().HasQueryFilter(record => record.TenantId == CurrentTenantId);
        builder.Entity<InventoryItem>().HasQueryFilter(item => item.TenantId == CurrentTenantId);
        builder.Entity<StockMovement>().HasQueryFilter(movement => movement.TenantId == CurrentTenantId);
        builder.Entity<InventoryReservation>().HasQueryFilter(reservation => reservation.TenantId == CurrentTenantId);
        builder.Entity<InventoryReservationCommand>().HasQueryFilter(command => command.TenantId == CurrentTenantId);
        builder.Entity<MediaAsset>().HasQueryFilter(asset => asset.TenantId == CurrentTenantId);
        builder.Entity<Store>().HasQueryFilter(store => store.TenantId == CurrentTenantId);
        builder.Entity<StoreProductPublication>().HasQueryFilter(publication => publication.TenantId == CurrentTenantId);
        builder.Entity<StoreCommandIdempotency>().HasQueryFilter(record => record.TenantId == CurrentTenantId);
        builder.Entity<DeliveryRule>().HasQueryFilter(rule => rule.TenantId == CurrentTenantId);
        builder.Entity<DeliveryRuleZone>().HasQueryFilter(zone => zone.TenantId == CurrentTenantId);
        builder.Entity<Customer>().HasQueryFilter(customer => customer.TenantId == CurrentTenantId);
        builder.Entity<CheckoutSession>().HasQueryFilter(session => session.TenantId == CurrentTenantId);
        builder.Entity<CheckoutSessionItem>().HasQueryFilter(item => item.TenantId == CurrentTenantId);
        builder.Entity<CheckoutSessionCommand>().HasQueryFilter(command => command.TenantId == CurrentTenantId);
        builder.Entity<Order>().HasQueryFilter(order => order.TenantId == CurrentTenantId);
        builder.Entity<OrderItem>().HasQueryFilter(item => item.TenantId == CurrentTenantId);
        builder.Entity<OrderCommand>().HasQueryFilter(command => command.TenantId == CurrentTenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.ModifiedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private string? CurrentTenantId => tenantContext?.Current?.TenantId;

    private void EnforceTenantOwnership()
    {
        foreach (var auditEntry in ChangeTracker.Entries<AuditEvent>())
        {
            if (auditEntry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit events are append-only and cannot be changed or deleted.");
            }
        }

        foreach (var movementEntry in ChangeTracker.Entries<StockMovement>())
        {
            if (movementEntry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Stock movements are append-only and cannot be changed or deleted.");
            }
        }

        foreach (var commandEntry in ChangeTracker.Entries<InventoryReservationCommand>())
        {
            if (commandEntry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Inventory reservation commands are append-only and cannot be changed or deleted.");
            }
        }

        foreach (var commandEntry in ChangeTracker.Entries<StoreCommandIdempotency>())
        {
            if (commandEntry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Store command records are append-only and cannot be changed or deleted.");
            }
        }

        foreach (var commandEntry in ChangeTracker.Entries<OrderCommand>())
        {
            if (commandEntry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Order command records are append-only and cannot be changed or deleted.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var context = tenantContext?.Current
                ?? throw new InvalidOperationException("A verified tenant context is required to change tenant-owned data.");

            if (!string.Equals(entry.Entity.TenantId, context.TenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Tenant-owned data cannot be changed outside the verified tenant context.");
            }

            if (context.IsReadOnlySupport && entry.Entity is not AuditEvent)
            {
                throw new InvalidOperationException("Read-only PlatformSupport context cannot change tenant-owned data.");
            }
        }
    }
}
