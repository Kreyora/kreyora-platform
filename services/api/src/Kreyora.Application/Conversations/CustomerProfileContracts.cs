namespace Kreyora.Application.Conversations;

// M09-S08 Q8: customer display names from the channel provider, for staff only.

/// <summary>Collects customers seen during one webhook run; schedules profile lookups only after the run commits.</summary>
public interface ICustomerProfileHook
{
    void IdentitySeen(string tenantId, string identityId, DateTimeOffset? profileCheckedAt);

    void Flush();

    void Discard();
}

/// <summary>Looks up and stores one customer's provider profile (name, username) in the current tenant.</summary>
public interface ICustomerProfileLookupService
{
    /// <returns>A stable outcome code (no values): <c>updated</c>, <c>not_due</c>, <c>disabled</c>, <c>erased</c>, <c>not_found</c>, <c>no_credentials</c> or <c>provider_*</c>.</returns>
    Task<string> LookupAsync(string identityId, CancellationToken cancellationToken = default);
}
