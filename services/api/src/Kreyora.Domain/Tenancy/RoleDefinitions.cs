namespace Kreyora.Domain.Tenancy;

public static class RoleDefinitions
{
    public const string Owner = nameof(Owner);
    public const string Admin = nameof(Admin);
    public const string Operator = nameof(Operator);
    public const string Viewer = nameof(Viewer);
    public const string PlatformSupport = nameof(PlatformSupport);

    public static IReadOnlyList<string> All { get; } = [Owner, Admin, Operator, Viewer, PlatformSupport];

    public static bool IsTenantRole(string? role) =>
        string.Equals(role, Owner, StringComparison.Ordinal) ||
        string.Equals(role, Admin, StringComparison.Ordinal) ||
        string.Equals(role, Operator, StringComparison.Ordinal) ||
        string.Equals(role, Viewer, StringComparison.Ordinal);
}
