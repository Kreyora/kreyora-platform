using System.Net.Mail;
using System.Text.RegularExpressions;
using Kreyora.Domain.Common;

namespace Kreyora.Domain.Customers;

public sealed class Customer : BaseEntity, ITenantOwned
{
    private static readonly Regex NepalMobile = new("^9\\d{9}$", RegexOptions.Compiled);

    private Customer() { }

    public string TenantId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string NormalizedPhone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? NormalizedEmail { get; private set; }
    public DateTimeOffset PrivacyAcknowledgedAt { get; private set; }
    public string PrivacyPolicyFingerprint { get; private set; } = string.Empty;
    public DateTimeOffset RetentionReviewAt { get; private set; }
    public DateTimeOffset LastCheckoutAt { get; private set; }

    public static Customer Create(string tenantId, CustomerContact contact, string privacyPolicyFingerprint, DateTimeOffset now, DateTimeOffset retentionReviewAt)
    {
        var customer = new Customer { TenantId = Require(tenantId, nameof(tenantId), 26) };
        customer.Update(contact, privacyPolicyFingerprint, now, retentionReviewAt);
        return customer;
    }

    public void Update(CustomerContact contact, string privacyPolicyFingerprint, DateTimeOffset now, DateTimeOffset retentionReviewAt)
    {
        ArgumentNullException.ThrowIfNull(contact);
        DisplayName = Require(contact.DisplayName, nameof(contact.DisplayName), 160);
        Phone = NormalizePhone(contact.Phone);
        NormalizedPhone = Phone;
        Email = NormalizeEmail(contact.Email);
        NormalizedEmail = Email;
        PrivacyPolicyFingerprint = Require(privacyPolicyFingerprint, nameof(privacyPolicyFingerprint), 64);
        PrivacyAcknowledgedAt = now;
        RetentionReviewAt = retentionReviewAt > now ? retentionReviewAt : throw new ArgumentOutOfRangeException(nameof(retentionReviewAt));
        LastCheckoutAt = now;
    }

    public static CustomerContact NormalizeContact(CustomerContact contact) => new(
        Require(contact.DisplayName, nameof(contact.DisplayName), 160), NormalizePhone(contact.Phone), NormalizeEmail(contact.Email));

    public static string NormalizePhone(string value)
    {
        var compact = Require(value, nameof(value), 24).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var local = compact.StartsWith("+977", StringComparison.Ordinal) ? compact[4..] : compact;
        if (!NepalMobile.IsMatch(local)) throw new ArgumentException("A valid Nepal mobile phone number is required.", nameof(value));
        return $"+977{local}";
    }

    public static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = Require(value, nameof(value), 320);
        try
        {
            var address = new MailAddress(normalized);
            if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase)) throw new FormatException();
            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new ArgumentException("Customer email is invalid.", nameof(value));
        }
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
        return normalized.Length > maximumLength ? throw new ArgumentOutOfRangeException(parameterName) : normalized;
    }
}

public sealed record CustomerContact(string DisplayName, string Phone, string? Email);
