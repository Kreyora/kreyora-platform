namespace Kreyora.Domain.Common;

public interface ITenantOwned
{
    string TenantId { get; }
}
