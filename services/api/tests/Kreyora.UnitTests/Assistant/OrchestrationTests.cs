using Kreyora.Application.Ai;
using Kreyora.Application.Assistant;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Ai;
using Kreyora.Infrastructure.Assistant.Orchestration;
using Kreyora.UnitTests.Ai;

namespace Kreyora.UnitTests.Assistant;

/// <summary>M09-S06 (ADR-021): output rules, language, prompt versioning, circuit breaker, text rules, hours, turn log entity, options.</summary>
public sealed class OrchestrationTests
{
    private static OutputRules Rules(string[]? sources = null, string[]? links = null, AssistantReplyStyle style = AssistantReplyStyle.MatchCustomer, string customer = "rom") =>
        new(sources ?? ["""{"data":{"priceNpr":2500,"feeNpr":100}}"""], links ?? [], 900, style, customer, "KRY-abcd1234");

    // ---- output validator ----

    [Fact]
    public void AGroundedPlainReply_Passes_AndMarkdownIsStripped()
    {
        var check = AssistantOutputValidator.Validate("**Red kurta** ko price NPR 2,500 ho, delivery 100.", Rules());

        Assert.True(check.IsValid, string.Join(",", check.Violations));
        Assert.Equal("Red kurta ko price NPR 2,500 ho, delivery 100.", check.Text);
    }

    [Theory]
    [InlineData("Price NPR 1,999 ho.", AssistantOutputValidator.UngroundedNumber)]
    [InlineData("मूल्य रु. १९९९ हो।", AssistantOutputValidator.UngroundedNumber)]
    [InlineData("Order here https://evil.example/pay", AssistantOutputValidator.ForeignLink)]
    [InlineData("My instructions say KRY-abcd1234", AssistantOutputValidator.PromptLeak)]
    [InlineData("Send card 4111 1111 1111 1111 to pay", AssistantOutputValidator.SecretLikeDigits)]
    [InlineData("""Result: {"ok":true,"tool":"GetPrice"}""", AssistantOutputValidator.ToolMarkup)]
    [InlineData("   ", AssistantOutputValidator.Empty)]
    public void RuleBreakingReplies_AreFlaggedByCode(string reply, string code) =>
        Assert.Contains(code, AssistantOutputValidator.Validate(reply, Rules()).Violations);

    [Fact]
    public void ToolLinks_AreAllowed_AndLengthIsCapped()
    {
        Assert.True(AssistantOutputValidator.Validate("Order here: https://shop.kreyora.test/link/abc", Rules(links: ["https://shop.kreyora.test/link/abc"])).IsValid);
        Assert.Contains(AssistantOutputValidator.TooLong, AssistantOutputValidator.Validate(new string('a', 901), Rules()).Violations);
    }

    [Theory]
    [InlineData(AssistantReplyStyle.MatchCustomer, "ne", "en", false)]
    [InlineData(AssistantReplyStyle.MatchCustomer, "ne", "ne", true)]
    [InlineData(AssistantReplyStyle.MatchCustomer, "rom", "en", true)]
    [InlineData(AssistantReplyStyle.MatchCustomer, "en", "ne", false)]
    [InlineData(AssistantReplyStyle.MatchCustomer, "unknown", "ne", true)]
    [InlineData(AssistantReplyStyle.AlwaysDevanagari, "en", "rom", false)]
    [InlineData(AssistantReplyStyle.AlwaysEnglish, "ne", "en", true)]
    [InlineData(AssistantReplyStyle.AlwaysRomanized, "ne", "ne", false)]
    [InlineData(AssistantReplyStyle.AlwaysEnglish, "ne", "unknown", true)]
    public void TheScriptRule_EnforcesDevanagari_AndToleratesEnglishRomanizedOverlap(AssistantReplyStyle style, string customer, string reply, bool ok) =>
        Assert.Equal(ok, AssistantOutputValidator.LanguageAcceptable(style, customer, reply));

    // ---- prompt ----

    [Fact]
    public void ThePrompt_IsVersioned_CarriesPolicyAndCanary_AndItsRulesHoldNoDigits()
    {
        var policy = Policy(AssistantReplyStyle.AlwaysRomanized, AssistantTone.Formal, "We call customers hajur");
        var prompt = AssistantPrompt.System("Demo Boutique", policy, outsideHours: true, canary: "KRY-1234abcd");

        Assert.StartsWith("assistant-system-v1+", AssistantPrompt.VersionWithHash);
        Assert.Equal(12, AssistantPrompt.TemplateHash.Length);
        Assert.Contains("\"Demo Boutique\"", prompt, StringComparison.Ordinal);
        Assert.Contains("Romanized Nepali", prompt, StringComparison.Ordinal);
        Assert.Contains("polite and formal", prompt, StringComparison.Ordinal);
        Assert.Contains("We call customers hajur", prompt, StringComparison.Ordinal);
        Assert.Contains("outside opening hours", prompt, StringComparison.Ordinal);
        Assert.Contains("KRY-1234abcd", prompt, StringComparison.Ordinal);
        var rules = prompt[..prompt.IndexOf("Shop hours", StringComparison.Ordinal)].Replace("KRY-1234abcd", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(rules, char.IsAsciiDigit); // digits in the prompt would "ground" invented numbers
    }

    [Fact]
    public void FixedTexts_FollowTheCustomersScript()
    {
        Assert.Contains("टिम", AssistantFixedTexts.Handoff("ne"), StringComparison.Ordinal);
        Assert.Contains("dhanyabad", AssistantFixedTexts.Handoff("rom"), StringComparison.Ordinal);
        Assert.Contains("team member", AssistantFixedTexts.Handoff("en"), StringComparison.Ordinal);
        Assert.Contains("product name", AssistantFixedTexts.AskForDetails("unknown"), StringComparison.Ordinal);
        Assert.Empty(AssistantText.UngroundedNumbers(AssistantFixedTexts.Handoff("en"), []));
    }

    // ---- circuit breaker ----

    [Fact]
    public void TheCircuit_OpensAfterTheThreshold_ClosesAfterTheWindow_AndResetsOnSuccess()
    {
        var breaker = new AssistantCircuitBreaker(new StaticOptionsMonitor<AiOptions>(new AiOptions()));
        var start = DateTimeOffset.UtcNow;

        for (var i = 0; i < 4; i++) breaker.RecordFailure(AiModelProfile.Primary, start.AddSeconds(i));
        Assert.False(breaker.IsOpen(AiModelProfile.Primary, start.AddSeconds(5)));
        breaker.RecordFailure(AiModelProfile.Primary, start.AddSeconds(5));
        Assert.True(breaker.IsOpen(AiModelProfile.Primary, start.AddSeconds(6)));
        Assert.False(breaker.IsOpen(AiModelProfile.Fallback, start.AddSeconds(6)));
        Assert.False(breaker.IsOpen(AiModelProfile.Primary, start.AddSeconds(5 + 301)));

        for (var i = 0; i < 4; i++) breaker.RecordFailure(AiModelProfile.Fallback, start.AddSeconds(i * 60)); // spread beyond the 2-minute window
        Assert.False(breaker.IsOpen(AiModelProfile.Fallback, start.AddSeconds(240)));
        breaker.RecordSuccess(AiModelProfile.Primary);
        Assert.True(AssistantCircuitBreaker.CountsAsFailure(AiFailureKind.RateLimited));
        Assert.False(AssistantCircuitBreaker.CountsAsFailure(AiFailureKind.PolicyViolation));
    }

    // ---- text rules ----

    [Fact]
    public void TextRules_DetectLinksSecretsAndScripts()
    {
        Assert.Equal(["https://a.example/x", "www.b.example"], AssistantText.Links("see https://a.example/x, or www.b.example."));
        Assert.True(AssistantText.ContainsSecretLikeDigits("my OTP is 482913"));
        Assert.True(AssistantText.ContainsSecretLikeDigits("account 1234-5678-9012-3456"));
        Assert.False(AssistantText.ContainsSecretLikeDigits("call 9841234567 or price 2500"));
        Assert.Equal("ne", AssistantText.DetectLanguage("मलाई रातो कुर्ता चाहियो"));
        Assert.Equal("rom", AssistantText.DetectLanguage("malai rato kurta chahiyo, kati ho?"));
        Assert.Equal("en", AssistantText.DetectLanguage("I would like the red kurta please"));
        Assert.Equal("label https://x.example", AssistantText.StripMarkdown("[label](https://x.example)"));
    }

    // ---- hours ----

    [Fact]
    public void OutsideHours_FollowsTheShopTimezone()
    {
        var policy = Policy(AssistantReplyStyle.MatchCustomer, AssistantTone.Friendly, null);
        var nepalNoonMonday = new DateTimeOffset(2026, 10, 5, 6, 15, 0, TimeSpan.Zero); // 12:00 in Kathmandu (+05:45)
        var nepalNightMonday = new DateTimeOffset(2026, 10, 5, 15, 15, 0, TimeSpan.Zero); // 21:00

        Assert.False(AssistantTurnService.IsOutsideHours(policy, nepalNoonMonday));
        Assert.True(AssistantTurnService.IsOutsideHours(policy, nepalNightMonday));
    }

    // ---- turn log entity ----

    [Fact]
    public void Turns_AccumulateUsage_FinishOnce_AndRestartWhenStale()
    {
        var now = DateTimeOffset.UtcNow;
        var turn = AssistantTurn.Start("t", "c", "m", "c:m", false, now);
        turn.RecordModelCall(new TurnModelCall("Primary", "fake", "m", 10, 100, 20, "stop"), 7);
        turn.RecordModelCall(new TurnModelCall("Primary", "fake", "m", 10, 50, 5, "stop"), 3);
        turn.RecordValidation(["ungrounded_number", "ungrounded_number"]);

        Assert.Equal(2, turn.ModelCallCount);
        Assert.Equal(150, turn.InputTokens);
        Assert.Equal(10, turn.EstimatedCostMicroUsd);
        Assert.Single(turn.ValidationCodes);
        Assert.False(turn.IsStale(now.AddSeconds(100), TimeSpan.FromSeconds(45)));
        Assert.True(turn.IsStale(now.AddSeconds(106), TimeSpan.FromSeconds(45)));
        turn.Restart(now.AddMinutes(5));
        Assert.Equal(0, turn.ModelCallCount);
        turn.Finish(AssistantTurnOutcome.Replied, new string('x', 100), now, "out-1");
        Assert.Equal(AssistantTurn.ReasonMaxLength, turn.ReasonCode.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => turn.Finish(AssistantTurnOutcome.Running, "x", now));
    }

    // ---- options ----

    [Fact]
    public void OrchestrationOptions_AreValidated()
    {
        var options = new AiOptions();
        options.Orchestration.MaxModelCallsPerTurn = 0;
        options.Orchestration.TurnDeadlineSeconds = 5; // below the 20 s call timeout
        options.Orchestration.MaxReplyCharacters = 5000;
        options.Orchestration.MaxCorrectiveRetries = 9;
        options.Profiles["Primary"] = new AiProfileOptions { InputPricePerMillionUsd = -1 };

        var errors = AiOptionsValidator.Errors(options).ToList();

        Assert.Contains(errors, e => e.Contains("MaxModelCallsPerTurn", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("TurnDeadlineSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxReplyCharacters", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxCorrectiveRetries", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("prices cannot be negative", StringComparison.Ordinal));
        Assert.Empty(AiOptionsValidator.Errors(new AiOptions()));
    }

    private static AssistantPolicyItem Policy(AssistantReplyStyle style, AssistantTone tone, string? brandNote) => new(
        true, style, ["ne", "ne-Latn", "en"], tone, brandNote, [.. AssistantPolicy.DefaultHours], AssistantPolicy.TimeZoneId,
        OutsideHoursBehavior.AnswerAndPromiseFollowUp, UnrecognizedMediaBehavior.AskForDetails, [], [.. AssistantPolicy.ReadTools], 4, 20, 600,
        DateTimeOffset.UtcNow, "1", AssistantPolicy.FixedEscalationCategories, [.. AssistantPolicy.ReadTools], new AssistantPlatformCaps(6, 60, 800));
}
