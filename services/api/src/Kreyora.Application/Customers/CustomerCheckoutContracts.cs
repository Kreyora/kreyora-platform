using Kreyora.Application.Models;

namespace Kreyora.Application.Customers;

public interface ICustomerCheckoutService
{
    Task<Result<CustomerCheckoutProfile>> ResolveAsync(CustomerCheckoutContact request, string privacyPolicyFingerprint, DateTimeOffset now, DateTimeOffset retentionReviewAt, CancellationToken cancellationToken = default);
}

public sealed record CustomerCheckoutContact(string DisplayName, string Phone, string? Email, bool SaveContact);
public sealed record CustomerCheckoutProfile(string? CustomerId, string DisplayName, string Phone, string? Email);
