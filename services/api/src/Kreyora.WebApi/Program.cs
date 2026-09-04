using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Hangfire;
using Kreyora.Application;
using Kreyora.Infrastructure;
using Kreyora.Infrastructure.BackgroundJobs;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Errors;
using Kreyora.Infrastructure.Logging;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Media;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Storefront;
using Kreyora.ServiceDefaults;
using Kreyora.WebApi.Configuration;
using Kreyora.WebApi.Seeding;
using Kreyora.WebApi.Storefront;
using Kreyora.WebApi.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<SensitiveDataEnricher>()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
        formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddServiceDefaults();

builder.Services
    .AddOptions<AppSettings>()
    .BindConfiguration(AppSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseSettings>()
    .BindConfiguration(DatabaseSettings.SectionName);

builder.Services
    .AddOptions<CorsSettings>()
    .BindConfiguration(CorsSettings.SectionName);

builder.Services
    .AddOptions<PublicStorefrontOptions>()
    .BindConfiguration(PublicStorefrontOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => options.IsValidForEnvironment(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")),
        "Development storefront slug routes are allowed only in Development or Testing.")
    .ValidateOnStart();

var publicStorefrontOptions = builder.Configuration.GetSection(PublicStorefrontOptions.SectionName).Get<PublicStorefrontOptions>() ?? new PublicStorefrontOptions();
var trustedProxyAddresses = publicStorefrontOptions.TrustedProxyAddresses
    .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
    .Where(address => address is not null)
    .Cast<IPAddress>()
    .ToArray();
if (trustedProxyAddresses.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in trustedProxyAddresses) options.KnownProxies.Add(address);
    });
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHangfireServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "Kreyora.Dev.Antiforgery" : "__Host-Kreyora.Antiforgery";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
{
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "Kreyora.Dev.Auth" : "__Host-Kreyora.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            ProblemDetailsFactory.Create(StatusCodes.Status429TooManyRequests, "Too Many Requests", "Please retry later."), cancellationToken);
    };
    options.AddFixedWindowLimiter("auth-registration", limiter => { limiter.PermitLimit = 3; limiter.Window = TimeSpan.FromHours(1); });
    options.AddFixedWindowLimiter("auth-sign-in", limiter => { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(15); });
    options.AddFixedWindowLimiter("auth-password-reset", limiter => { limiter.PermitLimit = 3; limiter.Window = TimeSpan.FromHours(1); });
    options.AddPolicy("public-reads", httpContext =>
    {
        var publicOptions = httpContext.RequestServices.GetRequiredService<IOptions<PublicStorefrontOptions>>().Value;
        return PublicPartition(httpContext, publicOptions, "read", publicOptions.ReadRequestsPerMinute, TimeSpan.FromMinutes(1));
    });
    options.AddPolicy("public-quotes", httpContext =>
    {
        var publicOptions = httpContext.RequestServices.GetRequiredService<IOptions<PublicStorefrontOptions>>().Value;
        return PublicPartition(httpContext, publicOptions, "quote", publicOptions.QuoteRequestsPerTenMinutes, TimeSpan.FromMinutes(10));
    });
    options.AddPolicy("public-sessions", httpContext =>
    {
        var publicOptions = httpContext.RequestServices.GetRequiredService<IOptions<PublicStorefrontOptions>>().Value;
        return PublicPartition(httpContext, publicOptions, "session", publicOptions.SessionRequestsPerTenMinutes, TimeSpan.FromMinutes(10));
    });
    options.AddPolicy("public-orders", httpContext =>
    {
        var publicOptions = httpContext.RequestServices.GetRequiredService<IOptions<PublicStorefrontOptions>>().Value;
        return PublicPartition(httpContext, publicOptions, "order", publicOptions.OrderRequestsPerHour, TimeSpan.FromHours(1));
    });
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddOpenApi();

var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>();
if (corsSettings?.AllowedOrigins is { Length: > 0 })
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(corsSettings.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("X-Correlation-ID", "api-supported-versions", "ETag", "Retry-After");
        });
    });
}

var app = builder.Build();

if (trustedProxyAddresses.Length > 0)
{
    app.UseForwardedHeaders();
}

if (args.Contains("--migrate"))
{
    await MigrationRunner.ApplyMigrationsAsync(app.Services);
    return;
}

if (args.Contains("--seed"))
{
    await DevSeedHook.SeedDevelopmentDataAsync(app.Services);
    return;
}

if (app.Services.GetService<JobStorage>() is not null)
{
    using var scope = app.Services.CreateScope();
    var reservationOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<InventoryReservationOptions>>().Value;
    if (reservationOptions.ExpiryJobEnabled)
    {
        RecurringJob.AddOrUpdate<InventoryReservationExpiryJob>(
            "inventory-reservation-expiry",
            job => job.RunAsync(),
            Cron.Minutely);
    }
    RecurringJob.AddOrUpdate<MediaCleanupJob>("media-cleanup", job => job.RunAsync(), Cron.Daily);
    RecurringJob.AddOrUpdate<CheckoutSessionExpiryJob>("checkout-session-expiry", job => job.RunAsync(), Cron.Minutely);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options =>
    {
        options.WithTitle("Kreyora API");
        options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
    });

    if (app.Services.GetService<JobStorage>() is not null)
    {
        app.MapHangfireDashboard("/hangfire");
    }
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<PublicStorefrontContextMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapServiceDefaults();
app.MapControllers();

app.Run();

static RateLimitPartition<string> PublicPartition(HttpContext httpContext, PublicStorefrontOptions options, string family, int permitLimit, TimeSpan window)
{
    var publicContext = httpContext.RequestServices.GetService<Kreyora.Application.Storefront.IPublicStorefrontContextAccessor>()?.Current;
    var slug = publicContext?.PlatformSlug ?? "unresolved";
    var address = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetFixedWindowLimiter($"{family}:{slug}:{address}", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true
    });
}

namespace Kreyora.WebApi
{
    public partial class Program { }
}
