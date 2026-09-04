using Kreyora.Application.Customers;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Customers;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kreyora.Infrastructure.Customers;

public sealed class CustomerCheckoutService(AppDbContext dbContext, ITenantContextAccessor tenantContext) : ICustomerCheckoutService
{
    public async Task<Result<CustomerCheckoutProfile>> ResolveAsync(CustomerCheckoutContact request, string privacyPolicyFingerprint, DateTimeOffset now, DateTimeOffset retentionReviewAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        tenantContext.RequireCurrent();
        try
        {
            var contact = Customer.NormalizeContact(new CustomerContact(request.DisplayName, request.Phone, request.Email));
            if (!request.SaveContact) return Result<CustomerCheckoutProfile>.Success(new CustomerCheckoutProfile(null, contact.DisplayName, contact.Phone, contact.Email));
            var phoneMatch = await dbContext.Customers.SingleOrDefaultAsync(customer => customer.NormalizedPhone == contact.Phone, cancellationToken);
            var emailMatch = contact.Email is null ? null : await dbContext.Customers.SingleOrDefaultAsync(customer => customer.NormalizedEmail == contact.Email, cancellationToken);
            if (phoneMatch is not null && emailMatch is not null && phoneMatch.Id != emailMatch.Id)
                return Result<CustomerCheckoutProfile>.Conflict("Customer contact details conflict with separate existing profiles.");
            var customer = phoneMatch ?? emailMatch;
            if (customer is null)
            {
                customer = Customer.Create(tenantContext.RequireCurrent().TenantId, contact, privacyPolicyFingerprint, now, retentionReviewAt);
                dbContext.Customers.Add(customer);
            }
            else
            {
                customer.Update(contact, privacyPolicyFingerprint, now, retentionReviewAt);
            }
            return Result<CustomerCheckoutProfile>.Success(new CustomerCheckoutProfile(customer.Id, customer.DisplayName, customer.Phone, customer.Email));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return Result<CustomerCheckoutProfile>.ValidationError(exception.Message);
        }
    }
}
