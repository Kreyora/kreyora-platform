using System.Text;
using Kreyora.Application.Assistant;
using Kreyora.Domain.Assistant;
using Kreyora.Infrastructure.Assistant;

namespace Kreyora.UnitTests.Assistant;

public sealed class AssistantDomainTests
{
    private static readonly AssistantPlatformCaps Caps = new(6, 60, 800);

    private static AssistantPolicySettings Valid() => new(
        true, AssistantReplyStyle.MatchCustomer, ["ne", "ne-Latn", "en"], AssistantTone.Friendly, "We call customers hajur",
        AssistantPolicy.DefaultHours, OutsideHoursBehavior.AnswerAndPromiseFollowUp, UnrecognizedMediaBehavior.AskForDetails,
        ["wholesale"], [.. AssistantPolicy.ReadTools], 4, 20, 600);

    // ---- policy defaults and rules ----

    [Fact]
    public void Defaults_AreSafe()
    {
        var policy = AssistantPolicy.CreateDefault("01J00000000000000000000001");

        Assert.True(policy.Enabled); // owner preference: on once ready (activation still needs readiness)
        Assert.Null(policy.ReviewedAt); // so it can't go live until the owner reviewed it
        Assert.Equal(AssistantReplyStyle.MatchCustomer, policy.ReplyStyle);
        Assert.Equal(UnrecognizedMediaBehavior.AskForDetails, policy.UnrecognizedMediaBehavior);
        Assert.Equal(OutsideHoursBehavior.AnswerAndPromiseFollowUp, policy.OutsideHoursBehavior);
        // M09-S05 Q9: quote and checkout link on; holds (consequential for other customers) are seller opt-in.
        Assert.Equal(["QuoteCart", "CreateCheckoutLink"], policy.AllowedTools.Where(AssistantPolicy.WriteTools.Contains));
        Assert.DoesNotContain("ReserveInventory", policy.AllowedTools);
        Assert.DoesNotContain("CreateOrderDraft", AssistantPolicy.WriteTools); // Q1: no AI-created orders
        Assert.Contains(AssistantPolicy.AlwaysAllowedTool, policy.AllowedTools);
        Assert.Equal(7, policy.BusinessHours.Count);
        Assert.Contains("refund_or_exchange", AssistantPolicy.FixedEscalationCategories);
        Assert.Empty(AssistantPolicyRules.Validate(new AssistantPolicySettings(policy.Enabled, policy.ReplyStyle, policy.SupportedLanguages,
            policy.Tone, policy.BrandNote, policy.BusinessHours, policy.OutsideHoursBehavior, policy.UnrecognizedMediaBehavior,
            policy.EscalationKeywords, policy.AllowedTools, policy.MaxToolSteps, policy.MaxRepliesPerConversationPerHour, policy.MaxOutputTokens), Caps));
    }

    [Fact]
    public void ValidSettings_PassValidation() => Assert.Empty(AssistantPolicyRules.Validate(Valid(), Caps));

    public static TheoryData<string, AssistantPolicySettings> InvalidSettings => new()
    {
        { "allowedTools", Valid() with { AllowedTools = ["CreateOrderDraft"] } },
        { "allowedTools", Valid() with { AllowedTools = ["DropDatabase"] } },
        { "supportedLanguages", Valid() with { SupportedLanguages = ["fr"] } },
        { "supportedLanguages", Valid() with { SupportedLanguages = [] } },
        { "maxToolSteps", Valid() with { MaxToolSteps = 7 } },
        { "maxToolSteps", Valid() with { MaxToolSteps = 0 } },
        { "maxRepliesPerConversationPerHour", Valid() with { MaxRepliesPerConversationPerHour = 61 } },
        { "maxOutputTokens", Valid() with { MaxOutputTokens = 801 } },
        { "brandNote", Valid() with { BrandNote = new string('x', 301) } },
        { "brandNote", Valid() with { BrandNote = "<script>alert(1)</script>" } },
        { "businessHours", Valid() with { BusinessHours = AssistantPolicy.DefaultHours.Take(6).ToList() } },
        { "businessHours", Valid() with { BusinessHours = AssistantPolicy.DefaultHours.Select(h => h with { Opens = "20:00", Closes = "09:00" }).ToList() } },
        { "escalationKeywords", Valid() with { EscalationKeywords = Enumerable.Range(0, 21).Select(i => $"k{i}").ToList() } },
        { "replyStyle", Valid() with { ReplyStyle = (AssistantReplyStyle)99 } },
        { "unrecognizedMediaBehavior", Valid() with { UnrecognizedMediaBehavior = (UnrecognizedMediaBehavior)0 } },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void InvalidSettings_AreRejected_OnTheRightField(string field, AssistantPolicySettings settings) =>
        Assert.Contains(field, AssistantPolicyRules.Validate(settings, Caps).Keys);

    [Fact]
    public void Apply_AlwaysKeepsEscalateToHuman_AndMarksReviewedOnce()
    {
        var policy = AssistantPolicy.CreateDefault("01J00000000000000000000001");
        var first = DateTimeOffset.Parse("2026-10-06T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        policy.Apply(Valid() with { AllowedTools = ["GetPrice"] }, "owner-1", first);
        policy.Apply(Valid(), "admin-2", first.AddHours(1));

        Assert.Contains(AssistantPolicy.AlwaysAllowedTool, policy.AllowedTools);
        Assert.Equal(first, policy.ReviewedAt);
        Assert.Equal("owner-1", policy.ReviewedByUserId);
    }

    // ---- knowledge text ----

    [Fact]
    public void Text_IsNormalized_ControlCharactersStripped_NewlinesKept()
    {
        var (text, problem) = KnowledgeText.Normalize("  Delivery\r\nPokhara:\u0007 150\tNPR  ");

        Assert.Equal(KnowledgeTextProblem.None, problem);
        Assert.Equal("Delivery\nPokhara: 150\tNPR", text);
    }

    [Theory]
    [InlineData("", KnowledgeTextProblem.Empty)]
    [InlineData("   \u0007 ", KnowledgeTextProblem.Empty)]
    public void EmptyText_IsRejected(string input, KnowledgeTextProblem expected) =>
        Assert.Equal(expected, KnowledgeText.Normalize(input).Problem);

    [Fact]
    public void OverlongText_IsRejected() =>
        Assert.Equal(KnowledgeTextProblem.TooLong, KnowledgeText.Normalize(new string('a', KnowledgeText.MaxCharacters + 1)).Problem);

    [Theory]
    [InlineData("faq.pdf", "%PDF-1.7 ...")]
    [InlineData("faq.docx", "PK\u0003\u0004 word")]
    [InlineData("faq.txt", "%PDF-1.4 disguised")]
    [InlineData("faq.exe", "MZ binary")]
    [InlineData("faq.html", "<html></html>")]
    public void UnsupportedFiles_AreRefused_BeforeExtraction(string fileName, string content) =>
        Assert.Equal(KnowledgeTextProblem.UnsupportedType, KnowledgeText.Extract(fileName, Encoding.UTF8.GetBytes(content)).Problem);

    [Fact]
    public void NulBytes_AreTreatedAsBinary() =>
        Assert.Equal(KnowledgeTextProblem.UnsupportedType, KnowledgeText.Extract("faq.txt", [0x48, 0x00, 0x49]).Problem);

    [Fact]
    public void InvalidUtf8_IsRejected() =>
        Assert.Equal(KnowledgeTextProblem.InvalidEncoding, KnowledgeText.Extract("faq.txt", [0x48, 0xC3, 0x28]).Problem);

    [Fact]
    public void OversizeFiles_AreRejected() =>
        Assert.Equal(KnowledgeTextProblem.TooLarge, KnowledgeText.Extract("faq.txt", new byte[KnowledgeText.MaxUploadBytes + 1]).Problem);

    [Fact]
    public void Utf8MarkdownWithBomAndNepali_IsAccepted()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# डेलिभरी\nकाठमाडौं भित्र १-२ दिन")).ToArray();

        var (text, problem) = KnowledgeText.Extract("delivery.MD", bytes);

        Assert.Equal(KnowledgeTextProblem.None, problem);
        Assert.StartsWith("# डेलिभरी", text);
    }

    // ---- lifecycle ----

    [Fact]
    public void Lifecycle_PendingToActiveToSuperseded_AndInvalidTransitionsThrow()
    {
        var document = KnowledgeDocument.Create("01J00000000000000000000001", "FAQ", KnowledgeCategory.Faq, KnowledgeSource.Text);
        var now = DateTimeOffset.UtcNow;
        var v1 = KnowledgeDocumentVersion.Submit(document, "v1 text", "u1", now, null);
        var v2 = KnowledgeDocumentVersion.Submit(document, "v2 text", "u1", now, null);

        v1.Approve("u2", now);
        v2.Reject("u2", now, "wrong");
        v1.Supersede();

        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(KnowledgeVersionState.Superseded, v1.State);
        Assert.Equal(KnowledgeVersionState.Rejected, v2.State);
        Assert.Throws<InvalidOperationException>(() => v2.Approve("u2", now));
        Assert.Throws<InvalidOperationException>(() => v1.Approve("u2", now));
        Assert.Throws<InvalidOperationException>(() => v2.Supersede());
    }

    [Fact]
    public void Delete_ClearsText_AndReturnsTheStorageKeyToPurge()
    {
        var document = KnowledgeDocument.Create("01J00000000000000000000001", "FAQ", KnowledgeCategory.Faq, KnowledgeSource.Upload);
        var version = KnowledgeDocumentVersion.Submit(document, "text", "u1", DateTimeOffset.UtcNow, null, "tenants/x/assistant-knowledge/d/v.txt", "faq.txt", 4);

        var key = version.MarkDeleted();
        document.MarkDeleted(DateTimeOffset.UtcNow);

        Assert.Equal("tenants/x/assistant-knowledge/d/v.txt", key);
        Assert.Null(version.ContentText);
        Assert.Equal(key, version.OriginalObjectKey); // kept until the purge is confirmed (retryable)
        version.ConfirmOriginalPurged();
        Assert.Null(version.OriginalObjectKey);
        Assert.True(document.IsDeleted);
        Assert.Null(document.ActiveVersionId);
    }

    // ---- activation truth table ----

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]   // seller turned it off
    [InlineData(true, false, true, false)]   // platform kill switch off
    [InlineData(true, true, false, false)]   // a required check failed
    public void Activation_NeedsSellerToggle_PlatformSwitch_AndEveryRequiredCheck(bool seller, bool platform, bool requiredPassed, bool expected)
    {
        AssistantReadinessCheck[] checks =
        [
            new("store_ready", requiredPassed, true, "", []),
            new("channel_connected", true, true, "", []),
            new("knowledge_approved", false, false, "", []) // recommended only: never blocks
        ];

        Assert.Equal(expected, AssistantReadinessService.IsActive(seller, platform, checks));
    }
}
