using System.ComponentModel.DataAnnotations;
using MailKit.Security;

namespace Kreyora.Infrastructure.Email;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    [Required]
    public string ApplicationName { get; set; } = "Kreyora";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public SmtpSecurityMode Security { get; set; } = SmtpSecurityMode.StartTls;

    [Required]
    [EmailAddress]
    public string SenderEmail { get; set; } = string.Empty;

    [Required]
    public string SenderDisplayName { get; set; } = string.Empty;

    [Required]
    public string ApplicationPublicUrl { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int PasswordResetTokenLifetimeMinutes { get; set; } = 60;

    public bool IsValidForEnvironment(bool isDevelopment)
    {
        if (ContainsLineBreak(ApplicationName) || ContainsLineBreak(Host) || ContainsLineBreak(Username) ||
            ContainsLineBreak(SenderEmail) || ContainsLineBreak(SenderDisplayName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Password))
        {
            return false;
        }

        if (!Uri.TryCreate(ApplicationPublicUrl, UriKind.Absolute, out var applicationUri) ||
            (applicationUri.Scheme != Uri.UriSchemeHttp && applicationUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(applicationUri.Query) ||
            !string.IsNullOrEmpty(applicationUri.Fragment))
        {
            return false;
        }

        return isDevelopment || (applicationUri.Scheme == Uri.UriSchemeHttps && Security != SmtpSecurityMode.None);
    }

    public SecureSocketOptions ToSecureSocketOptions() => Security switch
    {
        SmtpSecurityMode.None => SecureSocketOptions.None,
        SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException($"Unsupported SMTP security mode '{Security}'.")
    };

    private static bool ContainsLineBreak(string? value) => value?.Contains('\r') == true || value?.Contains('\n') == true;
}

public enum SmtpSecurityMode
{
    None,
    StartTls,
    SslOnConnect
}
