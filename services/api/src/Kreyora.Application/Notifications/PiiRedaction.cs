using Kreyora.Domain.Notifications;

namespace Kreyora.Application.Notifications;

public static class PiiRedaction
{
    public static string RedactEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var trimmed = email.Trim();
        var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex == trimmed.Length - 1) return "***";

        var username = trimmed[..atIndex];
        var domain = trimmed[(atIndex + 1)..];

        var redactedUsername = username.Length switch
        {
            1 => "*",
            2 => $"{username[0]}*",
            _ => $"{username[0]}***"
        };

        return $"{redactedUsername}@{domain}";
    }

    public static string RedactPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var trimmed = phone.Trim();
        if (trimmed.Length <= 4) return "****";

        var prefixLength = Math.Min(4, trimmed.Length - 4);
        var prefix = trimmed[..prefixLength];
        var suffix = trimmed[^4..];

        return $"{prefix}****{suffix}";
    }

    public static string RedactContact(string? contact, NotificationChannel channel)
    {
        if (string.IsNullOrWhiteSpace(contact)) return string.Empty;
        return channel switch
        {
            NotificationChannel.Email => RedactEmail(contact),
            NotificationChannel.Sms => RedactPhone(contact),
            _ => contact.Length > 4 ? $"{contact[..2]}****" : "****"
        };
    }
}

