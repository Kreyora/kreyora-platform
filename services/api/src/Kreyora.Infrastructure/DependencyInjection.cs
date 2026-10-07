using Kreyora.Application.Abstractions;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Audit;
using Kreyora.Application.Authentication;
using Kreyora.Application.Authorization;
using Kreyora.Application.Catalog;
using Kreyora.Application.Conversations;
using Kreyora.Application.Customers;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Application.Inventory;
using Kreyora.Application.Orders;
using Kreyora.Application.Payments;
using Kreyora.Application.Messaging;
using Kreyora.Application.Notifications;
using Kreyora.Application.Storefront;
using Kreyora.Application.Support;
using Kreyora.Application.Tenancy;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant;
using Kreyora.Infrastructure.Assistant.Orchestration;
using Kreyora.Infrastructure.Assistant.Tools;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authentication;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.BackgroundJobs;
using Kreyora.Infrastructure.Catalog;
using Kreyora.Infrastructure.Conversations;
using Kreyora.Infrastructure.Correlation;
using Kreyora.Infrastructure.Customers;
using Kreyora.Infrastructure.Email;
using Kreyora.Infrastructure.Identity;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Integrations.Simulator;
using Kreyora.Infrastructure.Inventory;
using Kreyora.Infrastructure.Notifications;
using Kreyora.Infrastructure.Orders;
using Kreyora.Infrastructure.Payments;
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
        services.AddOptions<StorefrontQuoteOptions>()
            .BindConfiguration(StorefrontQuoteOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<CheckoutSessionOptions>()
            .BindConfiguration(CheckoutSessionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddDataProtection();
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

        services.AddOptions<SecretEncryptionOptions>()
            .BindConfiguration(SecretEncryptionOptions.SectionName);
        services.AddSingleton<ISecretEncryptionService, AesGcmSecretEncryptionService>();
        services.AddSingleton<IChannelProvider>(new SimulatorChannelProvider(SimulatorChannelProvider.AcceptsFixedTestSignatureIn(environment)));
        // Scoped: the send path uses the typed Graph HttpClient (ADR-017).
        services.AddScoped<IChannelProvider, InstagramChannelProvider>();
        services.AddScoped<IChannelProviderRegistry, ChannelProviderRegistry>();

        services.AddOptions<InstagramGraphOptions>()
            .BindConfiguration(InstagramGraphOptions.SectionName);
        services.AddOptions<InstagramWebhookOptions>()
            .BindConfiguration(InstagramWebhookOptions.SectionName);
        services.AddOptions<InstagramMessagingOptions>()
            .BindConfiguration(InstagramMessagingOptions.SectionName);
        services.AddHttpClient<IInstagramGraphClient, InstagramGraphClient>(
            (serviceProvider, httpClient) =>
            {
                var graphOptions = serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<InstagramGraphOptions>>().Value;
                httpClient.BaseAddress = new Uri(graphOptions.BaseAddress.TrimEnd('/') + "/");
                httpClient.Timeout = TimeSpan.FromSeconds(
                    graphOptions.TimeoutSeconds <= 0 ? 15 : graphOptions.TimeoutSeconds);
            });

        // AI boundary (M09-S01, ADR-018): disabled and fake by default; providers/profiles are configuration only.
        services.AddOptions<AiOptions>().BindConfiguration(AiOptions.SectionName).ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<AiOptions>, AiOptionsValidator>();
        services.AddHttpClient(OpenAiCompatibleChatClient.HttpClientName,
            httpClient => httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan); // per-call deadlines
        services.AddSingleton<OpenAiCompatibleChatClient>();
        services.AddSingleton<FakeAiChatClient>();
        services.AddSingleton<IAiChatClient, ResilientAiChatClient>();
        services.AddSingleton<OpenAiCompatibleEmbeddingClient>();
        services.AddSingleton<IAiEmbeddingClient, AiEmbeddingClient>();

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
            services.AddScoped<IStorefrontInventoryReadService, StorefrontInventoryReadService>();
            services.AddScoped<DeliveryRuleService>();
            services.AddScoped<IDeliveryRuleService>(serviceProvider => serviceProvider.GetRequiredService<DeliveryRuleService>());
            services.AddScoped<IDeliveryRuleReadService>(serviceProvider => serviceProvider.GetRequiredService<DeliveryRuleService>());
            services.AddScoped<IStorefrontAdministrationService, StorefrontAdministrationService>();
            services.AddScoped<IStoreReadinessQuery>(sp => (StorefrontAdministrationService)sp.GetRequiredService<IStorefrontAdministrationService>());
            services.AddScoped<IPublicStorefrontContextAccessor, PublicStorefrontContextAccessor>();
            services.AddScoped<IPublicStorefrontResolver, PublicStorefrontResolver>();
            services.AddScoped<IPublicStorefrontService, PublicStorefrontService>();
            services.AddScoped<IStorefrontQuoteService, StorefrontQuoteService>();
            services.AddScoped<ICustomerCheckoutService, CustomerCheckoutService>();
            services.AddScoped<IStorefrontCheckoutSessionService, CheckoutSessionService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ICheckoutInventoryReservationService>(serviceProvider => (InventoryService)serviceProvider.GetRequiredService<IInventoryService>());
        services.AddScoped<IOrderInventoryReservationService>(serviceProvider => (InventoryService)serviceProvider.GetRequiredService<IInventoryService>());
        services.AddScoped<IOrderCreationService, OrderCreationService>();
        services.AddScoped<IOrderOperationService, OrderOperationService>();
        services.AddScoped<IOrderQueryService, OrderQueryService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IStorePaymentConfigurationService, StorePaymentConfigurationService>();
        services.AddScoped<IChannelConnectionService, ChannelConnectionService>();
        services.AddScoped<IWebhookIngressService, WebhookIngressService>();
        services.AddScoped<IWebhookProcessingService, WebhookProcessingService>();
        services.AddScoped<IConversationIngestionService, ConversationIngestionService>();
        services.AddScoped<IConversationQueryService, ConversationQueryService>();
        services.AddScoped<IConversationInboxService, ConversationInboxService>();
        services.AddScoped<IConversationPrivacyService, ConversationPrivacyService>();
        services.AddScoped<IConversationReplyService, ConversationReplyService>();
        // Assistant policy, readiness and knowledge (M09-S02); database-backed, so only with a connection string.
        services.AddScoped<AssistantPolicyService>();
        services.AddScoped<IAssistantPolicyService>(sp => sp.GetRequiredService<AssistantPolicyService>());
        services.AddScoped<IAssistantPolicyQuery>(sp => sp.GetRequiredService<AssistantPolicyService>());
        services.AddScoped<AssistantReadinessService>();
        services.AddScoped<IAssistantReadinessService>(sp => sp.GetRequiredService<AssistantReadinessService>());
        services.AddScoped<IAssistantActivationQuery>(sp => sp.GetRequiredService<AssistantReadinessService>());
        services.AddScoped<KnowledgeService>();
        services.AddScoped<IKnowledgeService>(sp => sp.GetRequiredService<KnowledgeService>());
        services.AddScoped<IApprovedKnowledgeQuery>(sp => sp.GetRequiredService<KnowledgeService>());
        // Retrieval and indexing (M09-S03, ADR-019).
        services.AddScoped<IKnowledgeIndexingService, KnowledgeIndexingService>();
        services.AddScoped<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
        services.AddScoped<IKnowledgeIndexScheduler, HangfireKnowledgeIndexScheduler>();
        services.AddTransient<KnowledgeIndexingJob>();
        // M09-S04 read tools: customer-facing queries, registry (singleton; each call gets its own scope), owner console.
        services.AddScoped<ICustomerCatalogQuery, CustomerCatalogQuery>();
        services.AddScoped<IDeliveryInfoQuery, DeliveryInfoQuery>();
        services.AddScoped<IOrderStatusLookupService, OrderStatusLookupService>();
        services.AddSingleton<IAssistantTool, SearchProductsTool>();
        services.AddSingleton<IAssistantTool, CheckInventoryTool>();
        services.AddSingleton<IAssistantTool, GetPriceTool>();
        services.AddSingleton<IAssistantTool, GetShippingInfoTool>();
        services.AddSingleton<IAssistantTool, GetOrderStatusTool>();
        // M09-S05 write tools (ADR-020) and their services.
        services.AddSingleton<IAssistantTool, QuoteCartTool>();
        services.AddSingleton<IAssistantTool, ReserveInventoryTool>();
        services.AddSingleton<IAssistantTool, ReleaseReservationTool>();
        services.AddSingleton<IAssistantTool, CreateCheckoutLinkTool>();
        services.AddSingleton<IAssistantTool, EscalateToHumanTool>();
        services.AddScoped<AssistantCallContext>();
        services.AddScoped<IAssistantActionStore, AssistantActionStore>();
        services.AddScoped<AssistantCartValidator>();
        services.AddScoped<IConversationHoldAllowance, ConversationHoldAllowance>();
        services.AddScoped<IAssistantQuoteService, AssistantQuoteService>();
        services.AddScoped<IAssistantHoldService, AssistantHoldService>();
        services.AddScoped<AssistantCheckoutLinkService>();
        services.AddScoped<IAssistantCheckoutLinkService>(sp => sp.GetRequiredService<AssistantCheckoutLinkService>());
        services.AddScoped<IAssistantCheckoutLinkHandover>(sp => sp.GetRequiredService<AssistantCheckoutLinkService>());
        services.AddScoped<IConversationInventoryHoldService>(sp => (InventoryService)sp.GetRequiredService<IInventoryService>());
        services.AddScoped<IConversationEscalationService, ConversationEscalationService>();
        // M09-S06 orchestration (ADR-021).
        services.AddSingleton<AssistantCircuitBreaker>();
        services.AddScoped<IAssistantTurnService, AssistantTurnService>();
        services.AddScoped<IAssistantTurnLogQuery, AssistantTurnLogQuery>();
        services.AddTransient<AssistantTurnPurgeJob>();
        services.AddSingleton<IAssistantToolRegistry, AssistantToolRegistry>();
        services.AddScoped<IAssistantToolContextFactory, AssistantToolContextFactory>();
        services.AddScoped<IAssistantToolConsoleService, AssistantToolConsoleService>();
        services.AddScoped<IProductReferenceResolver, ProductReferenceResolver>();
        services.AddOptions<StorefrontLinkOptions>().BindConfiguration("PublicStorefront");
        services.AddScoped<IIntegrationWorkScheduler, HangfireIntegrationWorkScheduler>();
        services.AddScoped<IConversationOutboundReconciler, ConversationOutboundReconciler>();
        services.AddScoped<IOutboundEnqueuer>(sp => (OutboundMessageService)sp.GetRequiredService<IOutboundMessageService>());
        services.AddTransient<WebhookProcessingJob>();
        services.AddScoped<IConversationGate, ConversationGate>();
        services.AddScoped<IOutboundMessageService, OutboundMessageService>();
        services.AddTransient<OutboundDeliveryJob>();
        services.AddScoped<IIntegrationDiagnosticsService, IntegrationDiagnosticsService>();
            services.AddScoped<IMediaAssetService, MediaAssetService>();
            services.AddSingleton<IPrivateObjectStorage>(serviceProvider =>
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>().Value.Provider == "R2"
                    ? new R2PrivateObjectStorage(serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>())
                    : new LocalPrivateObjectStorage(
                        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaStorageOptions>>(),
                        environment));
            services.AddOptions<NotificationOptions>()
                .BindConfiguration(NotificationOptions.SectionName)
                .ValidateDataAnnotations();
            services.AddSingleton<INotificationTemplateRegistry, NotificationTemplateRegistry>();
            services.AddScoped<INotificationDeliveryProvider, DevelopmentNotificationProvider>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddTransient<InventoryReservationExpiryJob>();
            services.AddTransient<CheckoutSessionExpiryJob>();
            services.AddTransient<MediaCleanupJob>();
            services.AddTransient<OutboxNotificationProcessorJob>();
            services.AddTransient<NotificationDeliveryJob>();
        }

        return services;
    }
}
