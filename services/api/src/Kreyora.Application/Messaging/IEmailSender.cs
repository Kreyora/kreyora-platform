namespace Kreyora.Application.Messaging;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(string RecipientEmail, string Subject, string HtmlBody, string TextBody);
