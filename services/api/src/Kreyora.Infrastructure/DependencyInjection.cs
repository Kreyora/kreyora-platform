using Kreyora.Application.Abstractions;
using Kreyora.Application.Authentication;
using Kreyora.Application.Messaging;
using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Authentication;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Email;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kreyora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var smtpOptions = configuration.GetSection(SmtpEmailOptions.SectionName).Get<SmtpEmailOptions>() ?? new SmtpEmailOptions();
        services.AddOptions<SmtpEmailOptions>()
            .BindConfiguration(SmtpEmailOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.IsValidForEnvironment(environment.IsDevelopment()),
                "SMTP settings must use a valid HTTP(S) application URL, contain no header line breaks, include a password when a username is set, and use HTTPS plus TLS outside Development.")
            .ValidateOnStart();
        services.Configure<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromMinutes(smtpOptions.PasswordResetTokenLifetimeMinutes));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<Domain.Abstractions.ITimeProvider, SystemTimeProvider>();

        var connectionString = configuration.GetValue<string>("Database:ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString("kreyora");
        }

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                }));

            services
                .AddIdentityCore<ApplicationUser>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequiredLength = 12;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireDigit = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Lockout.AllowedForNewUsers = true;
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                })
                .AddRoles<IdentityRole>()
                .AddSignInManager()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddIdentityCookies();
            services.AddAuthorization();

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<ITenantMembershipService, TenantMembershipService>();
            services.AddScoped<ITenantContextResolutionService, TenantContextResolutionService>();
            services.AddScoped<ITenantQueryService, TenantQueryService>();
            services.AddScoped<ITenantKeyBuilder, TenantKeyBuilder>();
            services.AddScoped<ITenantJobRunner, TenantJobRunner>();
            services.AddScoped<ITenantOutboxProcessor, TenantOutboxProcessor>();
            services.AddScoped<IAuthenticationService, AuthenticationService>();
        }

        return services;
    }
}
