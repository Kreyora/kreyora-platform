using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;

namespace Kreyora.Application.Authorization;

public static class TenantPermissions
{
    public const string MembershipManage = "memberships.manage";
    public const string SettingsRead = "settings.read";
    public const string SettingsWrite = "settings.write";
    public const string CatalogWrite = "catalog.write";
    public const string CatalogRead = "catalog.read";
    public const string StorefrontWrite = "storefront.write";
    public const string StorefrontRead = "storefront.read";
    public const string InventoryWrite = "inventory.write";
    public const string InventoryRead = "inventory.read";
    public const string OrdersWrite = "orders.write";
    public const string OrdersRead = "orders.read";
    public const string ConversationsWrite = "conversations.write";
    public const string ConversationsRead = "conversations.read";
    public const string PaymentsManage = "payments.manage";
    public const string PaymentsRead = "payments.read";
    public const string IntegrationsWrite = "integrations.write";
    public const string IntegrationsRead = "integrations.read";
    public const string AiConfigurationWrite = "ai.configuration.write";
    public const string AiConfigurationRead = "ai.configuration.read";
    public const string BillingManage = "billing.manage";
    public const string ReportingRead = "reporting.read";
    public const string AuditRead = "audit.read";
    public const string SupportGrantManage = "support-grants.manage";
    public const string PermissionsRead = "permissions.read";

    public static IReadOnlyList<string> All { get; } =
    [
        MembershipManage, SettingsRead, SettingsWrite, CatalogRead, CatalogWrite, StorefrontRead, StorefrontWrite, InventoryRead, InventoryWrite,
        OrdersRead, OrdersWrite, ConversationsRead, ConversationsWrite, PaymentsManage, PaymentsRead,
        IntegrationsRead, IntegrationsWrite, AiConfigurationRead, AiConfigurationWrite, BillingManage,
        ReportingRead, AuditRead, SupportGrantManage, PermissionsRead
    ];

    public static bool IsAllowed(TenantContext context, string permission)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsReadOnlySupport)
        {
            return permission is AuditRead or PermissionsRead;
        }

        return context.Role switch
        {
            TenantRole.Owner => true,
            TenantRole.Admin => permission is not BillingManage and not SupportGrantManage,
            TenantRole.Operator => permission is SettingsRead or SettingsWrite or CatalogRead or CatalogWrite
                or StorefrontRead or StorefrontWrite
                or InventoryRead or InventoryWrite or OrdersRead or OrdersWrite or ConversationsRead or ConversationsWrite
                or PaymentsRead or IntegrationsRead or AiConfigurationRead
                or ReportingRead or PermissionsRead,
            TenantRole.Viewer => permission is SettingsRead or CatalogRead or InventoryRead or OrdersRead
                or StorefrontRead
                or ConversationsRead or PaymentsRead or IntegrationsRead or AiConfigurationRead or ReportingRead
                or PermissionsRead,
            _ => false
        };
    }

    public static IReadOnlyList<string> For(TenantContext context) => All.Where(permission => IsAllowed(context, permission)).ToArray();

    public static bool CanManageMembership(TenantContext context, TenantRole targetRole) =>
        IsAllowed(context, MembershipManage) && (context.Role == TenantRole.Owner || targetRole != TenantRole.Owner);
}

public interface ITenantPermissionAuthorizer
{
    bool IsAllowed(TenantContext context, string permission);
    void Demand(string permission);
}
