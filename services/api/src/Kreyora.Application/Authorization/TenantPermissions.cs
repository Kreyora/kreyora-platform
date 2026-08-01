using Kreyora.Application.Tenancy;
using Kreyora.Domain.Tenancy;

namespace Kreyora.Application.Authorization;

public static class TenantPermissions
{
    public const string MembershipManage = "memberships.manage";
    public const string SettingsWrite = "settings.write";
    public const string CatalogWrite = "catalog.write";
    public const string InventoryWrite = "inventory.write";
    public const string OrdersWrite = "orders.write";
    public const string ConversationsWrite = "conversations.write";
    public const string PaymentsManage = "payments.manage";
    public const string PaymentsRead = "payments.read";
    public const string IntegrationsWrite = "integrations.write";
    public const string AiConfigurationWrite = "ai.configuration.write";
    public const string BillingManage = "billing.manage";
    public const string ReportingRead = "reporting.read";
    public const string AuditRead = "audit.read";
    public const string SupportGrantManage = "support-grants.manage";
    public const string PermissionsRead = "permissions.read";

    public static IReadOnlyList<string> All { get; } =
    [
        MembershipManage, SettingsWrite, CatalogWrite, InventoryWrite, OrdersWrite, ConversationsWrite,
        PaymentsManage, PaymentsRead, IntegrationsWrite, AiConfigurationWrite, BillingManage,
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
            TenantRole.Operator => permission is SettingsWrite or CatalogWrite or InventoryWrite or OrdersWrite
                or ConversationsWrite or PaymentsRead or IntegrationsWrite or AiConfigurationWrite
                or ReportingRead or PermissionsRead,
            TenantRole.Viewer => permission is PaymentsRead or ReportingRead or PermissionsRead,
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
