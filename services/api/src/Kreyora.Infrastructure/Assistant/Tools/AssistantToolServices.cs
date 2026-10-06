using System.Text.Json;
using System.Text.RegularExpressions;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Models;
using Kreyora.Application.Storefront;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kreyora.Infrastructure.Assistant.Tools;

/// <summary>Builds tool contexts from trusted state: the current tenant scope, the conversation and the shop's policy.</summary>
public sealed class AssistantToolContextFactory(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAssistantPolicyQuery policies) : IAssistantToolContextFactory
{
    public async Task<Result<AssistantToolContext>> ForConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var conversation = await dbContext.Conversations.AsNoTracking().Where(c => c.Id == conversationId && c.TenantId == tenantId)
            .Select(c => new { c.StoreId, c.CustomerChannelIdentityId }).SingleOrDefaultAsync(cancellationToken);
        if (conversation is null) return Result<AssistantToolContext>.NotFound("The conversation was not found.");

        var storeId = conversation.StoreId ?? await StoreIdAsync(tenantId, cancellationToken);
        if (storeId is null) return StoreMissing();

        var customerId = await dbContext.CustomerChannelIdentities.AsNoTracking()
            .Where(i => i.Id == conversation.CustomerChannelIdentityId && i.TenantId == tenantId && i.ErasedAt == null)
            .Select(i => i.CustomerId).SingleOrDefaultAsync(cancellationToken);
        var policy = await policies.GetEffectiveAsync(cancellationToken);
        return Result<AssistantToolContext>.Success(new AssistantToolContext(tenantId, storeId, conversationId, conversation.CustomerChannelIdentityId, customerId,
            [.. policy.AllowedTools.Where(AssistantPolicy.ReadTools.Contains)], IsSellerPreview: false));
    }

    public async Task<Result<AssistantToolContext>> ForSellerPreviewAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var storeId = await StoreIdAsync(tenantId, cancellationToken);
        return storeId is null
            ? StoreMissing()
            : Result<AssistantToolContext>.Success(new AssistantToolContext(tenantId, storeId, null, null, null, AssistantPolicy.ReadTools, IsSellerPreview: true));
    }

    private Task<string?> StoreIdAsync(string tenantId, CancellationToken cancellationToken) =>
        dbContext.Stores.AsNoTracking().Where(s => s.TenantId == tenantId).Select(s => s.Id).FirstOrDefaultAsync(cancellationToken);

    private static Result<AssistantToolContext> StoreMissing() =>
        AssistantProblemCodes.Problem<AssistantToolContext>(AssistantToolErrorCodes.StoreUnavailable, "Create the store before using the assistant tools.", 409);
}

/// <summary>Owner tooling (Q8): the registry with schemas, and a seller preview of one tool.</summary>
public sealed class AssistantToolConsoleService(
    IAssistantToolRegistry registry,
    IAssistantToolContextFactory contexts,
    IAssistantPolicyQuery policies) : IAssistantToolConsoleService
{
    public async Task<AssistantToolCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var policy = await policies.GetEffectiveAsync(cancellationToken);
        return new AssistantToolCatalog(registry.Version, registry.Describe(policy.AllowedTools));
    }

    public async Task<Result<AssistantToolPreviewResult>> PreviewAsync(string toolName, AssistantToolPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!registry.Describe([]).Any(t => string.Equals(t.Name, toolName, StringComparison.Ordinal)))
        {
            return AssistantProblemCodes.Problem<AssistantToolPreviewResult>("assistant_tool_not_found", "No read tool has that name.", 404);
        }

        var context = await contexts.ForSellerPreviewAsync(cancellationToken);
        if (context.IsFailure) return Result<AssistantToolPreviewResult>.Failure(context.Error!);

        var arguments = request.Arguments is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } a ? a.GetRawText() : "{}";
        var outcome = await registry.ExecuteAsync(context.Value!, new AiToolCall($"preview-{Guid.NewGuid():N}", toolName, arguments), cancellationToken);
        using var json = JsonDocument.Parse(outcome.ResultJson);
        return Result<AssistantToolPreviewResult>.Success(new AssistantToolPreviewResult(json.RootElement.Clone(), outcome.Trace));
    }
}

/// <summary>Storefront link settings shared with the public storefront (<c>PublicStorefront</c> section).</summary>
public sealed class StorefrontLinkOptions
{
    public string PlatformBaseDomain { get; set; } = string.Empty;

    public bool EnableDevelopmentSlugRoutes { get; set; }
}

/// <summary>
/// Exact product matching for links to this shop's own storefront (M1, Q6-A). Accepts
/// <c>…/store/{storeSlug}/product/{productSlug}</c> on the platform domain (any host in development slug mode) and
/// <c>https://{storeSlug}.{PlatformBaseDomain}/product/{productSlug}</c>. Never fetches a URL; other shops never match.
/// </summary>
public sealed partial class ProductReferenceResolver(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    ICustomerCatalogQuery catalog,
    IOptions<StorefrontLinkOptions> options) : IProductReferenceResolver
{
    private const int MaxLinks = 10;

    [GeneratedRegex(@"(?:https?://)?(?:[a-z0-9-]+\.)*[a-z0-9-]+(?:\.[a-z]{2,}|(?=:\d)|(?=/store/))(?::\d{1,5})?/[^\s<>""']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex LinkPattern();

    public async Task<IReadOnlyList<ProductReference>> ResolveAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var store = await dbContext.Stores.AsNoTracking().Where(s => s.TenantId == tenantId).Select(s => new { s.Id, s.PlatformSlug }).FirstOrDefaultAsync(cancellationToken);
        if (store is null) return [];

        IEnumerable<Match> links;
        try
        {
            links = [.. LinkPattern().Matches(text).Take(MaxLinks)];
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }

        var references = new List<ProductReference>();
        foreach (var link in links)
        {
            var productSlug = ProductSlugFor(link.Value, store.PlatformSlug, options.Value);
            if (productSlug is null) continue;
            var product = await catalog.GetProductBySlugAsync(store.Id, productSlug, 0, cancellationToken);
            if (product is not null && references.All(r => r.ProductId != product.ProductId))
            {
                references.Add(new ProductReference(product.ProductId, product.Title, product.Slug, "storefront_link"));
            }
        }

        return references;
    }

    /// <summary>The product slug if <paramref name="link"/> is a product page of the store <paramref name="storeSlug"/>; otherwise null.</summary>
    public static string? ProductSlugFor(string link, string storeSlug, StorefrontLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var candidate = link.Contains("://", StringComparison.Ordinal) ? link : "https://" + link;
        if (!Uri.TryCreate(candidate.TrimEnd('.', ',', ')', '!', '?'), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;

        var host = uri.IdnHost.ToLowerInvariant();
        var baseDomain = options.PlatformBaseDomain.Trim().ToLowerInvariant();
        var slug = storeSlug.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        var onPlatform = baseDomain.Length > 0 && (host == baseDomain || host.EndsWith("." + baseDomain, StringComparison.Ordinal));

        // https://{slug}.{base}/product/{productSlug}
        if (baseDomain.Length > 0 && host == $"{slug}.{baseDomain}" && segments is ["product", var p1]) return Valid(p1);

        // …/store/{slug}/product/{productSlug} on the platform domain, or any host in development slug mode
        if ((onPlatform || options.EnableDevelopmentSlugRoutes) && segments is ["store", var s, "product", var p2] && string.Equals(s, slug, StringComparison.OrdinalIgnoreCase)) return Valid(p2);

        return null;

        static string? Valid(string productSlug) => productSlug.Length is > 0 and <= 160 ? productSlug.ToLowerInvariant() : null;
    }
}
