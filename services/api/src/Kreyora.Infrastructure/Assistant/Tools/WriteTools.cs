using System.Text.Json;
using Kreyora.Application.Assistant;
using Kreyora.Application.Conversations;
using Microsoft.Extensions.DependencyInjection;

namespace Kreyora.Infrastructure.Assistant.Tools;

// The M09-S05 write tools (ADR-020). Each is a thin adapter: validated arguments → one write service → minimized
// result. None of them can mark payment, fulfil, change prices, publish, or act for another conversation.

internal static class WriteToolArgs
{
    public static List<AssistantCartLine> Items(JsonElement arguments) =>
        arguments.TryGetProperty("items", out var list)
            ? [.. list.EnumerateArray().Select(i => new AssistantCartLine(i.GetProperty("variantId").GetString()!, i.GetProperty("quantity").GetInt32()))]
            : [];

    public static string ItemsSchema(int maxItems) => $$$"""
        {"type":"array","minItems":1,"maxItems":{{{maxItems}}},"description":"Variants and quantities","items":{"type":"object","properties":{
          "variantId":{"type":"string","pattern":"{{{ToolArgs.IdPattern}}}"},
          "quantity":{"type":"integer","minimum":1,"maximum":5}
        },"required":["variantId","quantity"],"additionalProperties":false}}
        """;

    public static AssistantToolResult Fail<T>(AssistantWriteOutcome<T> outcome) =>
        AssistantToolResult.Failed(outcome.ErrorCode!, outcome.Message!, outcome.Data);
}

public sealed class QuoteCartTool : IAssistantTool
{
    public string Name => "QuoteCart";

    public int Version => 1;

    public string Description => "Exact total for items delivered to a place: item prices, delivery fee, total, delivery time and cash on delivery. Quote these numbers exactly. Returns a quoteId for CreateCheckoutLink.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "items":{{WriteToolArgs.ItemsSchema(10)}},
          "place":{"type":"string","minLength":1,"maxLength":80,"description":"Where to deliver, e.g. 'Pokhara'"}
        },"required":["items","place"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var outcome = await services.GetRequiredService<IAssistantQuoteService>().QuoteAsync(context, ToolArgs.String(arguments, "place")!, WriteToolArgs.Items(arguments), cancellationToken);
        if (!outcome.IsSuccess) return WriteToolArgs.Fail(outcome);
        var quote = outcome.Value!;
        return AssistantToolResult.Success(new
        {
            quoteId = quote.QuoteId,
            place = quote.Place,
            currency = "NPR",
            lines = quote.Lines.Select(l => new { variantId = l.VariantId, title = l.ProductTitle, variant = l.VariantName, quantity = l.Quantity, unitPriceNpr = l.UnitPriceNpr, lineSubtotalNpr = l.LineSubtotalNpr }),
            merchandiseSubtotalNpr = quote.MerchandiseSubtotalNpr,
            deliveryFeeNpr = quote.DeliveryFeeNpr,
            totalNpr = quote.TotalNpr,
            etaText = quote.EtaText,
            cashOnDelivery = quote.CashOnDelivery,
            validUntil = quote.ExpiresAt
        }, quote.Lines.Count);
    }
}

public sealed class ReserveInventoryTool : IAssistantTool
{
    public string Name => "ReserveInventory";

    public int Version => 1;

    public string Description => "Hold items for this customer for about 15 minutes. First call without confirmationId returns a summary to show the customer; only after they agree, call again with the same items and the confirmationId.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "items":{{WriteToolArgs.ItemsSchema(5)}},
          "confirmationId":{"type":"string","pattern":"{{ToolArgs.IdPattern}}","description":"From the previous call, after the customer agreed"}
        },"required":["items"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var outcome = await services.GetRequiredService<IAssistantHoldService>().ReserveAsync(context, WriteToolArgs.Items(arguments), ToolArgs.String(arguments, "confirmationId"), cancellationToken);
        if (!outcome.IsSuccess) return WriteToolArgs.Fail(outcome);
        var result = outcome.Value!;
        return result.DryRun
            ? AssistantToolResult.Success(new { dryRun = true, wouldPropose = result.Proposal!.Items }, result.Proposal.Items.Count)
            : AssistantToolResult.Success(new
            {
                holds = result.Holds.Select(h => new { reservationId = h.ReservationId, variantId = h.VariantId, quantity = h.Quantity, heldUntil = h.ExpiresAt }),
                note = "Items are held until the time shown. Send a checkout link so the customer can complete the order."
            }, result.Holds.Count);
    }
}

public sealed class ReleaseReservationTool : IAssistantTool
{
    public string Name => "ReleaseReservation";

    public int Version => 1;

    public string Description => "Release items held for this customer (all of this chat's holds, or the given reservation ids), e.g. when they change their mind.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "reservationIds":{"type":"array","maxItems":5,"items":{"type":"string","pattern":"{{ToolArgs.IdPattern}}"},"description":"Optional: specific holds; empty releases all of this chat's holds"}
        },"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        List<string>? ids = arguments.TryGetProperty("reservationIds", out var list) ? [.. list.EnumerateArray().Select(i => i.GetString()!)] : null;
        var outcome = await services.GetRequiredService<IAssistantHoldService>().ReleaseAsync(context, ids, cancellationToken);
        if (!outcome.IsSuccess) return WriteToolArgs.Fail(outcome);
        return AssistantToolResult.Success(new
        {
            dryRun = context.IsSellerPreview ? true : (bool?)null,
            released = outcome.Value!.Select(h => new { reservationId = h.ReservationId, variantId = h.VariantId, quantity = h.Quantity })
        }, outcome.Value!.Count);
    }
}

public sealed class CreateCheckoutLinkTool : IAssistantTool
{
    public string Name => "CreateCheckoutLink";

    public int Version => 1;

    public string Description => "Create a link to the shop's checkout with these items in the cart. The customer enters their own details and places the order there. Pass the quoteId when the customer agreed to a quote.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "items":{{WriteToolArgs.ItemsSchema(10)}},
          "quoteId":{"type":"string","pattern":"{{ToolArgs.IdPattern}}","description":"Optional: from QuoteCart; the link must match it"}
        },"required":["items"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var outcome = await services.GetRequiredService<IAssistantCheckoutLinkService>().CreateAsync(context, WriteToolArgs.Items(arguments), ToolArgs.String(arguments, "quoteId"), cancellationToken);
        if (!outcome.IsSuccess) return WriteToolArgs.Fail(outcome);
        var link = outcome.Value!;
        return AssistantToolResult.Success(new
        {
            dryRun = link.DryRun ? true : (bool?)null,
            url = link.Url,
            validUntil = link.ExpiresAt,
            items = link.Items,
            note = "Send this link. Prices and stock are checked again at checkout; the customer chooses cash on delivery or QR there."
        }, link.Items.Count);
    }
}

public sealed class EscalateToHumanTool : IAssistantTool
{
    public string Name => "EscalateToHuman";

    public int Version => 1;

    public string Description => "Hand the conversation to a person: the customer asks for one, complaints, refunds or exchanges, custom or wholesale orders, health or safety, legal or payment disputes, abuse, or whenever you are unsure. After this, stop replying.";

    public string ParametersSchema => $$"""
        {"type":"object","properties":{
          "category":{"type":"string","enum":[{{string.Join(',', AssistantEscalationCategories.All.Select(c => $"\"{c}\""))}}],"description":"Why a person is needed"}
        },"required":["category"],"additionalProperties":false}
        """;

    public async Task<AssistantToolResult> ExecuteAsync(IServiceProvider services, AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var category = ToolArgs.String(arguments, "category")!;
        if (context.IsSellerPreview) return AssistantToolResult.Success(new { dryRun = true, wouldEscalate = category }, 0);

        var result = await services.GetRequiredService<IConversationEscalationService>().EscalateAsync(context.ConversationId!, category, cancellationToken);
        return result.IsFailure
            ? AssistantToolResult.Failed(AssistantToolErrorCodes.NotFound, "The conversation could not be handed over. Tell the customer a team member will reply.")
            : AssistantToolResult.Success(new
            {
                escalated = true,
                alreadyWithTeam = !result.Value!.Changed,
                category = result.Value.Category,
                note = "A team member now owns this conversation. Tell the customer briefly that a person will reply, then stop."
            }, 1);
    }
}
