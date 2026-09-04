using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authentication;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Inventory;
using Kreyora.Application.Messaging;
using Kreyora.Application.Storefront;
using Kreyora.Application.Support;
using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authentication;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Email;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Media;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Storefront;
using Kreyora.Infrastructure.Support;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.Infrastructure.Time;
using Microsoft.AspNetCore.Authorization;
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
        services.AddOptions<InventoryReservationOptions>()
            .BindConfiguration(InventoryReservationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MediaStorageOptions>()
            .BindConfiguration(MediaStorageOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.IsValidForEnvironment(environment.IsDevelopment() || environment.IsEnvironment("Testing")),
                "Media storage must use Local only in Development or a complete HTTPS R2 configuration.")
            .ValidateOnStart();
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
            services.AddAuthorization(options =>
            {
                foreach (var permission in TenantPermissions.All)
                {
                    options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
                        .AddRequirements(new TenantPermissionRequirement(permission)));
                }
            });

            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<ITenantMembershipService, TenantMembershipService>();
            services.AddScoped<ITenantPermissionAuthorizer, TenantPermissionAuthorizer>();
            services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
            services.AddScoped<IAuditEventService, AuditEventService>();
            services.AddScoped<ISupportAccessGrantService, SupportAccessGrantService>();
            services.AddScoped<ITenantContextResolutionService, TenantContextResolutionService>();
            services.AddScoped<ITenantQueryService, TenantQueryService>();
            services.AddScoped<ITenantKeyBuilder, TenantKeyBuilder>();
            services.AddScoped<ITenantJobRunner, TenantJobRunner>();
            services.AddScoped<ITenantOutboxProcessor, TenantOutboxProcessor>();
            services.AddScoped<IAuthenticationService, AuthenticationService>();
            services.AddScoped<ICatalogService, CatalogService>();
            services.AddScoped<IStorefrontCatalogReadService, StorefrontCatalogReadService>();
            services.AddScoped<IStorefrontAdministrationService, StorefrontAdministrationService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IMediaAssetService, MediaAssetService>();
            services.AddSingleton<IPrivateObjectStorage>(serviceProvider =>
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>().Value.Provider == "R2"
                    ? new R2PrivateObjectStorage(serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>())
                    : new LocalPrivateObjectStorage(
                        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>(),
                        environment));
            services.AddTransient<InventoryReservationExpiryJob>();
            services.AddTransient<MediaCleanupJob>();
        }

        return services;
    }
}
