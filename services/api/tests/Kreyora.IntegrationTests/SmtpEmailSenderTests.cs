using DotNet.Testcontainers.Builders;
using Kreyora.Application.Messaging;
using Kreyora.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task MailKitSender_DeliversToControlledMailpitInbox()
    {
        await using var mailpit = new ContainerBuilder("axllent/mailpit:v1.30.6")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1025))
            .Build();
        await mailpit.StartAsync();

        var sender = new SmtpEmailSender(Options.Create(new SmtpEmailOptions
        {
            ApplicationName = "Kreyora",
            Host = mailpit.Hostname,
            Port = mailpit.GetMappedPublicPort(1025),
            Security = SmtpSecurityMode.None,
            SenderEmail = "no-reply@kreyora.test",
            SenderDisplayName = "Kreyora",
            ApplicationPublicUrl = "http://localhost:3000"
        }));

        await sender.SendAsync(new EmailMessage(
            "smtp-proof@kreyora.test",
            "Kreyora SMTP proof",
            "<p>SMTP transport proof.</p>",
            "SMTP transport proof."));

        using var client = new HttpClient();
        var capturedText = await client.GetStringAsync(
            $"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}/view/latest.txt");

        Assert.Contains("SMTP transport proof.", capturedText);
    }
}
