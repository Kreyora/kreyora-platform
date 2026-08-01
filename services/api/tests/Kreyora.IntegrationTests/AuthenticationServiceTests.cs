using Kreyora.Application.Authentication;
using Kreyora.Application.Messaging;
using Kreyora.Infrastructure.Authentication;
using Kreyora.Infrastructure.Email;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kreyora.IntegrationTests;

public class AuthenticationServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;

    public AuthenticationServiceTests(PostgresFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task PasswordReset_ForExistingUser_SendsOfficialTokenInConfiguredResetUrl_AndTokenCannotBeReused()
    {
        var emailSender = new CapturingEmailSender();
        await using var services = CreateServices(emailSender);
        await MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var email = await CreateUserAsync(userManager);

        await service.RequestPasswordResetAsync(email);

        var emailMessage = Assert.Single(emailSender.Messages);
        Assert.Equal(email, emailMessage.RecipientEmail);
        Assert.Equal("Reset your Kreyora password", emailMessage.Subject);
        Assert.Contains("This link expires in 60 minutes.", emailMessage.HtmlBody);
        Assert.Contains("If you did not request this", emailMessage.TextBody);

        var resetUri = ExtractResetUri(emailMessage.TextBody);
        Assert.Equal("https://seller.kreyora.test/recover/reset", resetUri.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(resetUri.Query);
        Assert.Equal(email, query["email"].Single());
        var token = query["token"].Single();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var reset = await service.ResetPasswordAsync(new ResetPasswordRequest(email, token, "Changed!Password1"));
        var replay = await service.ResetPasswordAsync(new ResetPasswordRequest(email, token, "Another!Password1"));

        Assert.True(reset.Succeeded, string.Join("; ", reset.Errors));
        Assert.False(replay.Succeeded);
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, "Changed!Password1"));
    }

    [Fact]
    public async Task PasswordReset_ForUnknownUser_DoesNotSendEmail()
    {
        var emailSender = new CapturingEmailSender();
        await using var services = CreateServices(emailSender);
        await MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        await service.RequestPasswordResetAsync($"unknown-{Guid.NewGuid():N}@kreyora.test");

        Assert.Empty(emailSender.Messages);
    }

    [Fact]
    public async Task PasswordReset_RejectsInvalidAndExpiredTokens()
    {
        var emailSender = new CapturingEmailSender();
        await using var services = CreateServices(emailSender, TimeSpan.FromMilliseconds(1));
        await MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var email = await CreateUserAsync(userManager);

        var invalid = await service.ResetPasswordAsync(new ResetPasswordRequest(email, "not-an-identity-token", "Changed!Password1"));
        await service.RequestPasswordResetAsync(email);
        var expiredToken = QueryHelpers.ParseQuery(ExtractResetUri(Assert.Single(emailSender.Messages).TextBody).Query)["token"].Single()!;
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        var expired = await service.ResetPasswordAsync(new ResetPasswordRequest(email, expiredToken, "Changed!Password1"));

        Assert.False(invalid.Succeeded);
        Assert.False(expired.Succeeded);
    }

    [Fact]
    public async Task PasswordReset_SmtpFailure_IsNotExposedOrLoggedWithCredentials()
    {
        var logs = new RecordingLoggerProvider();
        var emailSender = new ThrowingEmailSender(new InvalidOperationException("smtp-user: smtp-password-secret"));
        await using var services = CreateServices(emailSender, loggerProvider: logs);
        await MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        var email = await CreateUserAsync(userManager);

        await service.RequestPasswordResetAsync(email);

        var entry = Assert.Single(logs.Messages, message => message.Contains("Password reset email delivery failed", StringComparison.Ordinal));
        Assert.Contains("Password reset email delivery failed", entry);
        Assert.DoesNotContain("smtp-user", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("smtp-password-secret", entry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, entry, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task MigrateAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
    }

    private static async Task<string> CreateUserAsync(UserManager<ApplicationUser> userManager)
    {
        var email = $"reset-{Guid.NewGuid():N}@kreyora.test";
        var user = new ApplicationUser
        {
            DisplayName = "Password Reset Tester",
            Email = email,
            UserName = email
        };
        var creation = await userManager.CreateAsync(user, "Original!Password1");
        Assert.True(creation.Succeeded, string.Join("; ", creation.Errors.Select(error => error.Description)));
        return email;
    }

    private ServiceProvider CreateServices(IEmailSender emailSender, TimeSpan? tokenLifetime = null, RecordingLoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });
        services.AddHttpContextAccessor();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = tokenLifetime ?? TimeSpan.FromMinutes(60));
        services.AddSingleton<IOptions<SmtpEmailOptions>>(Options.Create(new SmtpEmailOptions
        {
            ApplicationName = "Kreyora",
            Host = "smtp.kreyora.test",
            Port = 587,
            Security = SmtpSecurityMode.StartTls,
            SenderEmail = "no-reply@kreyora.test",
            SenderDisplayName = "Kreyora",
            ApplicationPublicUrl = "https://seller.kreyora.test",
            PasswordResetTokenLifetimeMinutes = 60
        }));
        services.AddSingleton(emailSender);
        services.AddSingleton<IEmailSender>(emailSender);
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        return services.BuildServiceProvider();
    }

    private static Uri ExtractResetUri(string textBody)
    {
        var resetUrl = textBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)[1].Trim();
        return new Uri(resetUrl);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEmailSender(Exception exception) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.FromException(exception);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}
