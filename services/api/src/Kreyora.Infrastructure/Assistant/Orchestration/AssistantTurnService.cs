using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Application.Conversations;
using Kreyora.Application.Models;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Assistant;
using Kreyora.Domain.Conversations;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Tools;
using Kreyora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kreyora.Infrastructure.Assistant.Orchestration;

/// <summary>
/// One bounded assistant turn (M09-S06, ADR-021): lease → gate → deterministic pre-checks → context → bounded model/tool
/// loop → output validation → ownership re-check → gated enqueue; any failure ends in the safe fallback (hand-off
/// notice + EscalateToHuman). Every run that took the lease leaves a redacted <see cref="AssistantTurn"/> row.
/// </summary>
public sealed partial class AssistantTurnService(
    AppDbContext dbContext,
    ITenantContextAccessor tenantContext,
    IAssistantActivationQuery activation,
    IAssistantPolicyQuery policies,
    IAssistantToolContextFactory toolContexts,
    IAssistantToolRegistry registry,
    IKnowledgeRetrievalService knowledge,
    IProductReferenceResolver productReferences,
    IAiChatClient chat,
    IConversationReplyService replies,
    IConversationEscalationService escalation,
    AssistantCircuitBreaker circuit,
    IOptionsMonitor<AiOptions> aiOptions,
    ITimeProvider timeProvider,
    ILogger<AssistantTurnService> logger) : IAssistantTurnService
{
    private static readonly string[] PersonPhrases =
    [
        "talk to a person", "talk to a human", "speak to a person", "real person", "human agent", "customer care", "talk to someone",
        "manche sanga kura", "manxe sanga kura", "manche sita kura", "staff sanga kura", "owner sanga kura", "मान्छेसँग कुरा", "मान्छेसँग बोल्न"
    ];

    [LoggerMessage(Level = LogLevel.Information, Message = "Assistant turn {TurnId} → {Outcome} ({Reason}); model calls {ModelCalls}, tools {ToolCalls}, tokens {Tokens}")]
    private static partial void LogTurn(ILogger logger, string turnId, AssistantTurnOutcome outcome, string reason, int modelCalls, int toolCalls, int tokens);

    [LoggerMessage(Level = LogLevel.Error, Message = "Assistant turn {TurnId} failed unexpectedly")]
    private static partial void LogUnexpected(ILogger logger, Exception ex, string turnId);

    // ---- customer turns ------------------------------------------------------------------------------------------

    public async Task<AssistantTurnResult> RunAsync(string conversationId, string triggerMessageId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var options = aiOptions.CurrentValue;
        var conversation = await dbContext.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenantId, cancellationToken);
        if (conversation is null) return Unpersisted(AssistantTurnOutcome.Blocked, AssistantTurnReasons.ConversationNotFound);
        var trigger = await dbContext.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == triggerMessageId && m.ConversationId == conversationId &&
            m.Direction == MessageDirection.Inbound && m.Origin == MessageOrigin.Customer, cancellationToken);
        if (trigger is null) return Unpersisted(AssistantTurnOutcome.Blocked, AssistantTurnReasons.InvalidTrigger);

        var lease = await AcquireAsync(tenantId, conversationId, triggerMessageId, $"{conversationId}:{triggerMessageId}", false, options, cancellationToken);
        if (lease.Result is not null) return lease.Result;
        var turn = lease.Turn!;

        var run = new TurnRun(turn, conversation, trigger, options);
        try
        {
            var result = await RunGatedAsync(run, cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogUnexpected(logger, ex, turn.Id);
            return await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.UnexpectedError, null, CancellationToken.None);
        }
    }

    private async Task<AssistantTurnResult> RunGatedAsync(TurnRun run, CancellationToken cancellationToken)
    {
        var o = run.Options.Orchestration;
        var now = timeProvider.UtcNow;
        var conversation = run.Conversation!;
        var trigger = run.Trigger!;
        var conversationId = conversation.Id;

        // 1. Gate: kill switches and ownership end silently (the team or operator owns the situation).
        if (!run.Options.Enabled) return await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.PlatformDisabled, null, cancellationToken);
        if (conversation.AutomationMode != AutomationMode.Automated) return await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.AutomationPaused, null, cancellationToken);
        if (!await activation.IsActiveAsync(cancellationToken)) return await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.AssistantInactive, null, cancellationToken);
        if (await NewerCustomerMessageAsync(conversationId, trigger, cancellationToken)) return await FinishAsync(run, AssistantTurnOutcome.Superseded, AssistantTurnReasons.Superseded, null, cancellationToken);

        var policy = await policies.GetEffectiveAsync(cancellationToken);
        run.Policy = policy;
        var outsideHours = IsOutsideHours(policy, now);
        if (outsideHours && policy.OutsideHoursBehavior == OutsideHoursBehavior.DoNotAnswer)
            return await FinishAsync(run, AssistantTurnOutcome.Skipped, AssistantTurnReasons.OutsideHours, null, cancellationToken);

        var repliesLastHour = await dbContext.AssistantTurns.CountAsync(t => t.ConversationId == conversationId && !t.IsPlayground && t.Id != run.Turn.Id &&
            t.StartedAt >= now.AddHours(-1) && (t.Outcome == AssistantTurnOutcome.Replied || t.Outcome == AssistantTurnOutcome.Fallback || t.Outcome == AssistantTurnOutcome.Escalated), cancellationToken);
        if (repliesLastHour >= policy.MaxRepliesPerConversationPerHour)
            return await FinishAsync(run, AssistantTurnOutcome.Skipped, AssistantTurnReasons.ReplyRateLimit, null, cancellationToken);

        // 2. Context (needed for the language of fixed texts too).
        var history = await LoadHistoryAsync(conversationId, run.Turn.Id, o, cancellationToken);
        run.CustomerText = string.Join("\n", history.LatestCustomerTexts);
        run.CustomerLanguage = AssistantText.DetectLanguage(run.CustomerText);

        // 3. Deterministic pre-checks: no model needed.
        var keywordHit = policy.EscalationKeywords.Any(k => Contains(run.CustomerText, k));
        if (keywordHit || PersonPhrases.Any(p => Contains(run.CustomerText, p)))
        {
            return await HandOffAsync(run, AssistantTurnOutcome.Escalated, keywordHit ? AssistantTurnReasons.KeywordEscalation : AssistantTurnReasons.PersonRequested,
                keywordHit ? "keyword_match" : "customer_requests_person", cancellationToken);
        }

        if (history.LatestCustomerTexts.Count == 0 && history.LatestHasMedia)
        {
            if (policy.UnrecognizedMediaBehavior == UnrecognizedMediaBehavior.HandToPerson)
                return await HandOffAsync(run, AssistantTurnOutcome.Escalated, AssistantTurnReasons.UnrecognizedMedia, "outside_scope", cancellationToken);
            return await SendAsync(run, AssistantFixedTexts.AskForDetails(run.CustomerLanguage), AssistantTurnReasons.UnrecognizedMedia, cancellationToken);
        }

        // 4. Budgets and data rule before any model call.
        var budget = await BudgetBlockAsync(run, cancellationToken);
        if (budget is not null) return await HandOffAsync(run, AssistantTurnOutcome.Fallback, budget, "tool_unavailable", cancellationToken);
        var personalData = !run.Options.DataPolicy.SyntheticTenantIds.Contains(run.Turn.TenantId, StringComparer.Ordinal);
        if (personalData && !run.Options.DataPolicy.AllowPersonalData)
            return await HandOffAsync(run, AssistantTurnOutcome.Fallback, AssistantTurnReasons.DataPolicy, "tool_unavailable", cancellationToken);

        var shopName = await dbContext.Stores.AsNoTracking().Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "the shop";
        var toolContext = await toolContexts.ForConversationAsync(conversationId, run.Turn.Id, cancellationToken);
        if (toolContext.IsFailure) return await HandOffAsync(run, AssistantTurnOutcome.Fallback, AssistantTurnReasons.UnexpectedError, "tool_unavailable", cancellationToken);
        var messages = await BuildMessagesAsync(run, shopName, policy, outsideHours, history.Messages, cancellationToken);

        // 5. Bounded loop.
        var loop = await LoopAsync(run, messages, toolContext.Value!, personalData, cancellationToken);
        if (loop.Escalated) return await HandOffAsync(run, AssistantTurnOutcome.Escalated, AssistantTurnReasons.ModelEscalation, null, cancellationToken);
        if (loop.Reply is null) return await HandOffAsync(run, AssistantTurnOutcome.Fallback, loop.Reason, "tool_unavailable", cancellationToken);
        return await SendAsync(run, loop.Reply, AssistantTurnReasons.Replied, cancellationToken);
    }

    // ---- playground -----------------------------------------------------------------------------------------------

    public async Task<Result<AssistantPlaygroundResult>> PlaygroundAsync(AssistantPlaygroundRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = tenantContext.RequireCurrent().TenantId;
        var options = aiOptions.CurrentValue;
        if (request.Messages is null || request.Messages.Count is 0 or > 20 || request.Messages.Any(m => string.IsNullOrWhiteSpace(m.Text) || m.Text.Length > 1000 || m.From is not ("customer" or "shop"))
            || request.Messages[^1].From != "customer")
        {
            return Result<AssistantPlaygroundResult>.ValidationError("Send 1-20 messages (customer or shop, each up to 1,000 characters), ending with a customer message.");
        }

        var lease = await AcquireAsync(tenantId, null, null, $"playground:{IdGenerator.NewId()}", true, options, cancellationToken);
        if (lease.Turn is null) return Result<AssistantPlaygroundResult>.Conflict("The assistant is busy for this shop. Try again in a moment.");
        var run = new TurnRun(lease.Turn, null, null, options) { Playground = true };
        try
        {
            run.CustomerText = string.Join("\n", request.Messages.Reverse().TakeWhile(m => m.From == "customer").Reverse().Select(m => m.Text));
            run.CustomerLanguage = AssistantText.DetectLanguage(run.CustomerText);
            if (!options.Enabled) return Playground(await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.PlatformDisabled, null, cancellationToken), run, null);

            var policy = await policies.GetEffectiveAsync(cancellationToken);
            run.Policy = policy;
            if (policy.EscalationKeywords.Any(k => Contains(run.CustomerText, k)) || PersonPhrases.Any(p => Contains(run.CustomerText, p)))
            {
                return Playground(await FinishAsync(run, AssistantTurnOutcome.Escalated, AssistantTurnReasons.KeywordEscalation, null, cancellationToken), run, AssistantFixedTexts.Handoff(run.CustomerLanguage));
            }

            var budget = await BudgetBlockAsync(run, cancellationToken);
            if (budget is not null) return Playground(await FinishAsync(run, AssistantTurnOutcome.Fallback, budget, null, cancellationToken), run, AssistantFixedTexts.Handoff(run.CustomerLanguage));

            var shopName = await dbContext.Stores.AsNoTracking().Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "the shop";
            var toolContext = await toolContexts.ForSellerPreviewAsync(cancellationToken);
            if (toolContext.IsFailure) return Result<AssistantPlaygroundResult>.Failure(toolContext.Error!);
            var history = request.Messages.Select(m => m.From == "customer" ? AiChatMessage.User(m.Text) : AiChatMessage.Assistant(m.Text)).ToList();
            var messages = await BuildMessagesAsync(run, shopName, policy, IsOutsideHours(policy, timeProvider.UtcNow), history, cancellationToken);
            foreach (var message in request.Messages) run.GroundingSources.Add(message.Text);

            // Made-up messages typed by the owner: synthetic by definition (Q10), so free tiers may answer.
            var loop = await LoopAsync(run, messages, toolContext.Value!, personalData: false, cancellationToken);
            if (loop.Reply is null)
            {
                var outcome = loop.Escalated ? AssistantTurnOutcome.Escalated : AssistantTurnOutcome.Fallback;
                return Playground(await FinishAsync(run, outcome, loop.Escalated ? AssistantTurnReasons.ModelEscalation : loop.Reason, null, cancellationToken), run, AssistantFixedTexts.Handoff(run.CustomerLanguage));
            }

            return Playground(await FinishAsync(run, AssistantTurnOutcome.Replied, AssistantTurnReasons.Playground, null, cancellationToken), run, loop.Reply);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogUnexpected(logger, ex, run.Turn.Id);
            return Playground(await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.UnexpectedError, null, CancellationToken.None), run, null);
        }
    }

    // ---- the loop -------------------------------------------------------------------------------------------------

    private async Task<LoopResult> LoopAsync(TurnRun run, List<AiChatMessage> messages, AssistantToolContext toolContext, bool personalData, CancellationToken cancellationToken)
    {
        var o = run.Options.Orchestration;
        var policy = run.Policy!;
        var maxCalls = Math.Min(policy.MaxToolSteps + 1, o.MaxModelCallsPerTurn);
        var tools = registry.GetDefinitions(toolContext);
        var seenCalls = new HashSet<string>(StringComparer.Ordinal);
        var allowedLinks = new HashSet<string>(StringComparer.Ordinal);
        var toolCalls = 0;
        var corrections = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(o.TurnDeadlineSeconds));
        var started = timeProvider.UtcNow;

        for (var call = 1; call <= maxCalls; call++)
        {
            var remaining = TimeSpan.FromSeconds(o.TurnDeadlineSeconds) - (timeProvider.UtcNow - started);
            if (remaining <= TimeSpan.Zero || deadline.IsCancellationRequested) return LoopResult.Fail(AssistantTurnReasons.TurnDeadline);
            if (run.Turn.InputTokens + run.Turn.OutputTokens >= o.MaxTokensPerTurn) return LoopResult.Fail(AssistantTurnReasons.TokenBudget);
            var dailyBlock = await PlatformDailyBlockAsync(run, cancellationToken);
            if (dailyBlock is not null) return LoopResult.Fail(dailyBlock);

            var now = timeProvider.UtcNow;
            var profile = circuit.IsOpen(AiModelProfile.Primary, now) ? AiModelProfile.Fallback : AiModelProfile.Primary;
            if (profile == AiModelProfile.Fallback && (circuit.IsOpen(AiModelProfile.Fallback, now) || !run.Options.Profiles.ContainsKey(nameof(AiModelProfile.Fallback)) && run.Options.Mode == AiMode.Live))
                return LoopResult.Fail(AssistantTurnReasons.CircuitOpen);

            // The last allowed call (or once tools are exhausted) must answer with what it has.
            var choice = call == maxCalls || toolCalls >= o.MaxToolCallsPerTurn ? AiToolChoice.None : AiToolChoice.Auto;
            var callTimeout = TimeSpan.FromSeconds(o.ModelCallTimeoutSeconds) < remaining ? TimeSpan.FromSeconds(o.ModelCallTimeoutSeconds) : remaining;
            AiChatResult result;
            try
            {
                result = await chat.CompleteAsync(new AiChatRequest([.. messages], tools, choice, profile, policy.MaxOutputTokens, 0.2, callTimeout, personalData), deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return LoopResult.Fail(AssistantTurnReasons.TurnDeadline);
            }

            RecordModelCall(run, result, profile);
            if (!result.IsSuccess)
            {
                if (AssistantCircuitBreaker.CountsAsFailure(result.Failure))
                {
                    foreach (var attempted in result.Attempts.DefaultIfEmpty(profile)) circuit.RecordFailure(attempted, timeProvider.UtcNow);
                }

                return LoopResult.Fail(result.Failure switch
                {
                    AiFailureKind.Disabled => AssistantTurnReasons.PlatformDisabled,
                    AiFailureKind.PolicyViolation => AssistantTurnReasons.DataPolicy,
                    _ => $"{AssistantTurnReasons.ProviderFailure}:{result.Failure.ToString()!.ToLowerInvariant()}"
                });
            }

            circuit.RecordSuccess(profile);
            if (result.ToolCalls.Count == 0)
            {
                if (result.FinishReason == AiFinishReason.Length) return LoopResult.Fail(AssistantTurnReasons.Truncated);
                var check = AssistantOutputValidator.Validate(result.Text, new OutputRules(run.GroundingSources, allowedLinks, o.MaxReplyCharacters,
                    policy.ReplyStyle, run.CustomerLanguage, run.Canary));
                run.Turn.RecordValidation(check.Violations);
                if (check.IsValid) return LoopResult.Ok(check.Text);
                if (corrections >= o.MaxCorrectiveRetries || call == maxCalls) return LoopResult.Fail(AssistantTurnReasons.ValidationFailed);
                corrections++;
                messages.Add(AiChatMessage.Assistant(result.Text));
                messages.Add(AiChatMessage.User(AssistantPrompt.Corrective(check.Violations)));
                continue;
            }

            if (call == maxCalls) return LoopResult.Fail(AssistantTurnReasons.LoopLimit);
            messages.Add(AiChatMessage.Assistant(result.Text, result.ToolCalls));
            var index = 0;
            foreach (var toolCall in result.ToolCalls)
            {
                index++;
                string output;
                if (index > o.MaxToolCallsPerResponse)
                {
                    output = ToolError("too_many_calls", "Only a few tools can be used per step. Use the results you have.");
                }
                else if (toolCalls >= o.MaxToolCallsPerTurn)
                {
                    output = ToolError(AssistantTurnReasons.ToolLimit, "No more tools in this turn. Answer with what you have, or say a team member will confirm.");
                }
                else if (!seenCalls.Add(CallKey(toolCall)))
                {
                    output = ToolError("repeated_call", "You already made this exact call; use its earlier result.");
                }
                else
                {
                    toolCalls++;
                    AssistantToolOutcome outcome;
                    try
                    {
                        outcome = await registry.ExecuteAsync(toolContext, toolCall, deadline.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return LoopResult.Fail(AssistantTurnReasons.TurnDeadline);
                    }

                    var trace = outcome.Trace;
                    run.Turn.RecordToolStep(new TurnToolStep(trace.Tool, trace.ToolVersion, trace.Outcome, trace.DurationMs, trace.ArgumentFields, trace.ArgumentsHash, trace.ResultCount, trace.Replayed, trace.DryRun));
                    output = outcome.ResultJson;
                    run.GroundingSources.Add(output);
                    if (outcome.IsSuccess && trace.Tool == "CreateCheckoutLink" && Url(output) is { } url) allowedLinks.Add(url);
                    if (outcome.IsSuccess && trace.Tool == AssistantPolicy.AlwaysAllowedTool && !trace.DryRun) return LoopResult.Escalate();
                }

                messages.Add(AiChatMessage.ToolResult(toolCall.Id, output));
            }
        }

        return LoopResult.Fail(AssistantTurnReasons.LoopLimit);
    }

    // ---- send, hand off, finish ----------------------------------------------------------------------------------

    private async Task<AssistantTurnResult> SendAsync(TurnRun run, string text, string reason, CancellationToken cancellationToken)
    {
        var conversationId = run.Conversation!.Id;
        // Ownership re-check against committed state right before enqueueing.
        var current = await dbContext.Conversations.AsNoTracking().Where(c => c.Id == conversationId).Select(c => c.AutomationMode).SingleAsync(cancellationToken);
        if (current != AutomationMode.Automated) return await FinishAsync(run, AssistantTurnOutcome.Blocked, AssistantTurnReasons.TakenOverDuringTurn, null, cancellationToken);
        if (await NewerCustomerMessageAsync(conversationId, run.Trigger!, cancellationToken)) return await FinishAsync(run, AssistantTurnOutcome.Superseded, AssistantTurnReasons.Superseded, null, cancellationToken);

        var enqueued = await replies.EnqueueAutomationReplyAsync(conversationId, text, $"assistant-turn:{run.Turn.Id}", cancellationToken);
        return enqueued.IsFailure
            ? await FinishAsync(run, AssistantTurnOutcome.Blocked, $"{AssistantTurnReasons.EnqueueDenied}:{enqueued.Error?.Title}", null, cancellationToken)
            : await FinishAsync(run, AssistantTurnOutcome.Replied, reason, enqueued.Value, cancellationToken);
    }

    /// <summary>Safe fallback / escalation (Q3): the team takes over; one hand-off notice per conversation per cooldown.</summary>
    private async Task<AssistantTurnResult> HandOffAsync(TurnRun run, AssistantTurnOutcome outcome, string reason, string? category, CancellationToken cancellationToken)
    {
        var conversationId = run.Conversation!.Id;
        var now = timeProvider.UtcNow;
        var cooldownStart = now.AddHours(-run.Options.Orchestration.FallbackCooldownHours);
        var recentNotice = await dbContext.AssistantTurns.AnyAsync(t => t.ConversationId == conversationId && t.Id != run.Turn.Id && t.OutboundMessageId != null &&
            (t.Outcome == AssistantTurnOutcome.Fallback || t.Outcome == AssistantTurnOutcome.Escalated) && t.FinishedAt >= cooldownStart, cancellationToken);
        if (category is not null)
        {
            await escalation.EscalateAsync(conversationId, category, cancellationToken); // idempotent; the model may already have escalated
        }

        string? notice = null;
        if (recentNotice)
        {
            run.Turn.RecordValidation([AssistantTurnReasons.FallbackCooldown]);
        }
        else
        {
            var sent = await replies.EnqueueHandoffNoticeAsync(conversationId, AssistantFixedTexts.Handoff(run.CustomerLanguage), $"assistant-handoff:{run.Turn.Id}", cancellationToken);
            notice = sent.IsSuccess ? sent.Value : null;
            if (sent.IsFailure) run.Turn.RecordValidation([$"{AssistantTurnReasons.EnqueueDenied}:{sent.Error?.Title}"]);
        }

        return await FinishAsync(run, outcome, reason, notice, cancellationToken);
    }

    private async Task<AssistantTurnResult> FinishAsync(TurnRun run, AssistantTurnOutcome outcome, string reason, string? outboundMessageId, CancellationToken cancellationToken)
    {
        if (run.Policy is not null) run.Turn.RecordVersions(run.Policy.Version, AssistantPrompt.VersionWithHash, registry.Version);
        run.Turn.Finish(outcome, reason, timeProvider.UtcNow, outboundMessageId);
        dbContext.ChangeTracker.Clear();
        dbContext.AssistantTurns.Update(run.Turn);
        await dbContext.SaveChangesAsync(cancellationToken);
        LogTurn(logger, run.Turn.Id, outcome, run.Turn.ReasonCode, run.Turn.ModelCallCount, run.Turn.ToolSteps.Count, run.Turn.InputTokens + run.Turn.OutputTokens);
        return new AssistantTurnResult(run.Turn.Id, outcome, run.Turn.ReasonCode, run.Turn.OutboundMessageId, false);
    }

    // ---- lease, budgets ---------------------------------------------------------------------------------------------

    private async Task<Lease> AcquireAsync(string tenantId, string? conversationId, string? triggerMessageId, string key, bool playground, AiOptions options, CancellationToken cancellationToken)
    {
        var deadline = TimeSpan.FromSeconds(options.Orchestration.TurnDeadlineSeconds);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var now = timeProvider.UtcNow;
                var liveFrom = now - deadline - TimeSpan.FromMinutes(1);
                var existing = await dbContext.AssistantTurns.SingleOrDefaultAsync(t => t.TurnKey == key, cancellationToken);
                if (existing is not null)
                {
                    if (existing.IsFinished) return new Lease(null, new AssistantTurnResult(existing.Id, existing.Outcome, existing.ReasonCode, existing.OutboundMessageId, true));
                    if (!existing.IsStale(now, deadline)) return new Lease(null, Unpersisted(AssistantTurnOutcome.Skipped, AssistantTurnReasons.ConversationBusy));
                    existing.Restart(now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new Lease(existing, null);
                }

                if (conversationId is not null)
                {
                    var stale = await dbContext.AssistantTurns.Where(t => t.ConversationId == conversationId && t.Outcome == AssistantTurnOutcome.Running && t.StartedAt < liveFrom).ToListAsync(cancellationToken);
                    foreach (var dead in stale) dead.Finish(AssistantTurnOutcome.Abandoned, "abandoned", now);
                    if (await dbContext.AssistantTurns.AnyAsync(t => t.ConversationId == conversationId && t.Outcome == AssistantTurnOutcome.Running && t.StartedAt >= liveFrom, cancellationToken))
                        return new Lease(null, Unpersisted(AssistantTurnOutcome.Skipped, AssistantTurnReasons.ConversationBusy));
                }

                var running = await dbContext.AssistantTurns.CountAsync(t => t.Outcome == AssistantTurnOutcome.Running && t.StartedAt >= liveFrom, cancellationToken);
                if (running >= options.Orchestration.MaxConcurrentTurnsPerTenant) return new Lease(null, Unpersisted(AssistantTurnOutcome.Skipped, AssistantTurnReasons.TenantBusy));

                var turn = AssistantTurn.Start(tenantId, conversationId, triggerMessageId, key, playground, now);
                dbContext.AssistantTurns.Add(turn);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new Lease(turn, null);
            }
            catch (Exception ex) when (attempt < 5 && (ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
                ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation } } ||
                ex is InvalidOperationException && ex.Message.Contains("transient", StringComparison.OrdinalIgnoreCase)))
            {
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.ChangeTracker.Clear();
                return new Lease(null, Unpersisted(AssistantTurnOutcome.Skipped, AssistantTurnReasons.ConversationBusy));
            }
        }
    }

    /// <summary>Daily and platform budgets checked before any model call (Q2). Exhaustion is a safe fallback.</summary>
    private async Task<string?> BudgetBlockAsync(TurnRun run, CancellationToken cancellationToken)
    {
        var o = run.Options.Orchestration;
        var dayStart = new DateTimeOffset(timeProvider.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var tenantTurns = await dbContext.AssistantTurns.CountAsync(t => t.StartedAt >= dayStart && t.ModelCallCount > 0 && t.Id != run.Turn.Id, cancellationToken);
        if (tenantTurns >= o.MaxTurnsPerTenantPerDay) return AssistantTurnReasons.TenantDailyLimit;
        if (circuit.IsOpen(AiModelProfile.Primary, timeProvider.UtcNow) && (circuit.IsOpen(AiModelProfile.Fallback, timeProvider.UtcNow) || run.Options.Mode == AiMode.Fake || !run.Options.Profiles.ContainsKey(nameof(AiModelProfile.Fallback))))
            return AssistantTurnReasons.CircuitOpen;
        return await PlatformDailyBlockAsync(run, cancellationToken);
    }

    private async Task<string?> PlatformDailyBlockAsync(TurnRun run, CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(timeProvider.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var calls = await dbContext.AssistantTurns.IgnoreQueryFilters().Where(t => t.StartedAt >= dayStart && t.Id != run.Turn.Id).SumAsync(t => t.ModelCallCount, cancellationToken);
        return calls + run.Turn.ModelCallCount >= run.Options.Orchestration.MaxModelCallsPerDay ? AssistantTurnReasons.PlatformDailyLimit : null;
    }

    // ---- context ----------------------------------------------------------------------------------------------------

    private async Task<History> LoadHistoryAsync(string conversationId, string currentTurnId, AiOrchestrationOptions o, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.RedactedAt == null)
            .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.ReceivedAt)
            .Take(o.MaxConversationMessages)
            .Select(m => new { m.Direction, m.Origin, m.Kind, m.Text, m.ReceivedAt })
            .ToListAsync(cancellationToken);
        rows.Reverse();

        // Messages up to the last finished turn's trigger were already handled (its reply may still be queued, so it
        // isn't in the timeline yet); only later customer messages are "the latest".
        var handledUntil = await (from turn in dbContext.AssistantTurns.AsNoTracking()
                                  join message in dbContext.Messages.AsNoTracking() on turn.TriggerMessageId equals message.Id
                                  where turn.ConversationId == conversationId && turn.Id != currentTurnId && turn.Outcome != AssistantTurnOutcome.Running
                                  orderby message.ReceivedAt descending
                                  select (DateTimeOffset?)message.ReceivedAt).FirstOrDefaultAsync(cancellationToken);

        var latest = new List<string>();
        var latestHasMedia = false;
        foreach (var row in Enumerable.Reverse(rows))
        {
            if (row.Direction != MessageDirection.Inbound || (handledUntil is { } until && row.ReceivedAt <= until)) break;
            if (!string.IsNullOrWhiteSpace(row.Text)) latest.Insert(0, row.Text);
            else if (row.Kind == MessageKind.Media) latestHasMedia = true;
        }

        var messages = new List<AiChatMessage>();
        var budget = o.MaxConversationCharacters;
        foreach (var row in Enumerable.Reverse(rows))
        {
            var text = row.Text ?? (row.Kind == MessageKind.Media ? "[sent a photo, video or post]" : string.Empty);
            if (text.Length == 0) continue;
            if (text.Length > budget) break;
            budget -= text.Length;
            messages.Insert(0, row.Direction == MessageDirection.Inbound
                ? AiChatMessage.User(text)
                : AiChatMessage.Assistant(row.Origin == MessageOrigin.Staff ? $"[team member] {text}" : text));
        }

        return new History(messages, latest, latestHasMedia);
    }

    private async Task<List<AiChatMessage>> BuildMessagesAsync(TurnRun run, string shopName, AssistantPolicyItem policy, bool outsideHours, List<AiChatMessage> history, CancellationToken cancellationToken)
    {
        var messages = new List<AiChatMessage> { AiChatMessage.System(AssistantPrompt.System(shopName, policy, outsideHours, run.Canary)) };
        run.GroundingSources.Add(AssistantPrompt.HoursText(policy));
        foreach (var message in history.Where(m => m.Content is not null)) run.GroundingSources.Add(message.Content!);

        if (run.CustomerText.Length > 0)
        {
            KnowledgeRetrievalResult? retrieval = null;
            try
            {
                retrieval = await knowledge.RetrieveAsync(run.CustomerText, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUnexpected(logger, ex, run.Turn.Id); // knowledge is optional; the turn continues without it
            }

            if (retrieval is { Passages.Count: > 0 })
            {
                messages.Add(AiChatMessage.System(AssistantPrompt.Knowledge(retrieval)));
                run.GroundingSources.AddRange(retrieval.Passages.Select(p => p.Text));
                run.Turn.RecordCitations(retrieval.Passages.Select(p => new TurnCitation(p.Citation.DocumentId, p.Citation.VersionId, p.Citation.ChunkIndex, p.Score)));
            }

            var references = await productReferences.ResolveAsync(run.CustomerText, cancellationToken);
            if (references.Count > 0) messages.Add(AiChatMessage.System(AssistantPrompt.ProductReferences(references)));
        }

        messages.AddRange(history);
        return messages;
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static void RecordModelCall(TurnRun run, AiChatResult result, AiModelProfile requested)
    {
        var profile = result.Attempts.Count > 0 ? result.Attempts[^1] : requested;
        var input = result.Usage?.InputTokens ?? 0;
        var output = result.Usage?.OutputTokens ?? 0;
        var prices = run.Options.Profiles.TryGetValue(profile.ToString(), out var p) ? p : null;
        var cost = (long)Math.Round((input * (prices?.InputPricePerMillionUsd ?? 0)) + (output * (prices?.OutputPricePerMillionUsd ?? 0)));
        run.Turn.RecordModelCall(new TurnModelCall(profile.ToString(), result.Provider, result.Model, (long)result.Latency.TotalMilliseconds, input, output,
            result.IsSuccess ? result.FinishReason.ToString().ToLowerInvariant() : result.Failure.ToString()!.ToLowerInvariant()), cost);
    }

    private async Task<bool> NewerCustomerMessageAsync(string conversationId, Message trigger, CancellationToken cancellationToken) =>
        await dbContext.Messages.AsNoTracking().AnyAsync(m => m.ConversationId == conversationId && m.Id != trigger.Id &&
            m.Direction == MessageDirection.Inbound && m.Origin == MessageOrigin.Customer && m.ReceivedAt > trigger.ReceivedAt, cancellationToken);

    public static bool IsOutsideHours(AssistantPolicyItem policy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var day = policy.BusinessHours.FirstOrDefault(h => h.Day == local.DayOfWeek);
        if (day is null || day.Closed) return day is not null;
        return TimeOnly.TryParse(day.Opens, System.Globalization.CultureInfo.InvariantCulture, out var opens)
            && TimeOnly.TryParse(day.Closes, System.Globalization.CultureInfo.InvariantCulture, out var closes)
            && (TimeOnly.FromDateTime(local.DateTime) < opens || TimeOnly.FromDateTime(local.DateTime) >= closes);
    }

    private static bool Contains(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) && text.Contains(phrase.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string CallKey(AiToolCall call)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            return $"{call.Name}|{AssistantToolRegistry.Canonical(document.RootElement)}";
        }
        catch (JsonException)
        {
            return $"{call.Name}|{call.ArgumentsJson}";
        }
    }

    private static string? Url(string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToolError(string code, string message) =>
        JsonSerializer.Serialize(new { ok = false, error = new { code, message } });

    private static AssistantTurnResult Unpersisted(AssistantTurnOutcome outcome, string reason) => new(null, outcome, reason, null, false);

    private static Result<AssistantPlaygroundResult> Playground(AssistantTurnResult result, TurnRun run, string? reply) =>
        Result<AssistantPlaygroundResult>.Success(new AssistantPlaygroundResult(run.Turn.Id, result.Outcome, result.ReasonCode, reply,
            [.. run.Turn.ToolSteps.Select(s => new AssistantPlaygroundToolUse(s.Tool, s.Outcome, s.DryRun))], run.Turn.Citations,
            run.Turn.ModelCallCount, run.Turn.InputTokens, run.Turn.OutputTokens, run.Turn.EstimatedCostMicroUsd / 1_000_000m, run.Turn.ValidationCodes));

    private sealed class TurnRun(AssistantTurn turn, Conversation? conversation, Message? trigger, AiOptions options)
    {
        public AssistantTurn Turn { get; } = turn;
        public Conversation? Conversation { get; } = conversation;
        public Message? Trigger { get; } = trigger;
        public AiOptions Options { get; } = options;
        public bool Playground { get; init; }
        public AssistantPolicyItem? Policy { get; set; }
        public string CustomerText { get; set; } = string.Empty;
        public string CustomerLanguage { get; set; } = "unknown";
        public List<string> GroundingSources { get; } = [];
        public string Canary { get; } = "KRY-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    }

    private sealed record Lease(AssistantTurn? Turn, AssistantTurnResult? Result);

    private sealed record History(List<AiChatMessage> Messages, List<string> LatestCustomerTexts, bool LatestHasMedia);

    private sealed record LoopResult(string? Reply, string Reason, bool Escalated)
    {
        public static LoopResult Ok(string reply) => new(reply, AssistantTurnReasons.Replied, false);

        public static LoopResult Fail(string reason) => new(null, reason, false);

        public static LoopResult Escalate() => new(null, AssistantTurnReasons.ModelEscalation, true);
    }
}
