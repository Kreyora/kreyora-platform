using System.Text.Json;
using Kreyora.AiEvaluation;

namespace Kreyora.UnitTests.Ai;

public sealed class EvaluationHarnessTests
{
    private static readonly string DataDir = FindDataDir();
    private static readonly EvalDataset Dataset = EvalDataset.Load(Path.Combine(DataDir, "dataset.v1.json"));
    private static readonly string[] Behaviors = ["answer", "clarify", "escalate", "refuse"];
    private static readonly string[] ReplyLanguages = ["ne", "rom", "en", "rom_or_en", "any"];
    private static readonly FakeTools Tools = new(FakeCatalog.Load(Path.Combine(DataDir, "fake-catalog.v1.json")));

    private static string FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "services", "api", "evaluation", "m09");
    }

    private static EvalCase Case(string behavior = "answer", string[]? tools = null, string reply = "any", string[]? mustNot = null, string[]? forbidden = null, string category = "price_stock", string userText = "price?") =>
        new("t-1", category, "en", false, [new EvalTurn("user", userText)],
            new EvalExpectation(tools ?? [], forbidden ?? [], behavior, reply, mustNot ?? []));

    private static CaseRun Run(string? text, params ToolInvocation[] calls) =>
        new("t-1", "p", "m", text is not null, text, calls, 1, 1000, 0, 0, text is null ? "Timeout" : null, EvalPrompt.Version, DateTimeOffset.UtcNow);

    // --- dataset integrity ---

    [Fact]
    public void Dataset_HasAllCategoriesAndLanguages_And24ScreeningCases()
    {
        Assert.Equal(72, Dataset.Cases.Count);
        Assert.Equal(72, Dataset.Cases.Select(c => c.Id).Distinct().Count());
        Assert.Equal(10, Dataset.Cases.Select(c => c.Category).Distinct().Count());
        Assert.Equal(["en", "mix", "ne", "rom"], Dataset.Cases.Select(c => c.Language).Distinct().Order().ToArray());
        Assert.Equal(24, Dataset.Cases.Count(c => c.Screening));
        Assert.Equal(10, Dataset.Cases.Where(c => c.Screening).Select(c => c.Category).Distinct().Count());
    }

    [Fact]
    public void Dataset_ExpectationsOnlyReferenceKnownToolsBehaviorsAndLanguages()
    {
        var toolNames = FakeTools.Definitions.Select(d => d.Name).ToHashSet();
        Assert.All(Dataset.Cases, c =>
        {
            Assert.All(c.Expect.Tools.Concat(c.Expect.ForbiddenTools), t => Assert.Contains(t, toolNames));
            Assert.Contains(c.Expect.Behavior, Behaviors);
            Assert.Contains(c.Expect.ReplyLanguage, ReplyLanguages);
            Assert.NotEmpty(c.Turns);
        });
    }

    [Fact]
    public void Dataset_ContainsNoRealLookingPhoneNumbers_OnlyTheReservedFakeRange()
    {
        var text = File.ReadAllText(Path.Combine(DataDir, "dataset.v1.json")) + File.ReadAllText(Path.Combine(DataDir, "fake-catalog.v1.json"));

        var phones = System.Text.RegularExpressions.Regex.Matches(text, @"\b9[78]\d{8}\b").Select(m => m.Value);
        Assert.All(phones, p => Assert.StartsWith("98000000", p));
    }

    // --- fake tools ---

    [Fact]
    public void OrderLookup_IsScopedToTheConversationCustomer()
    {
        var mine = JsonDocument.Parse(Tools.Execute("GetOrderStatus", """{"orderNumber":"KR-1042"}""")).RootElement;
        var other = JsonDocument.Parse(Tools.Execute("GetOrderStatus", """{"orderNumber":"KR-2001"}""")).RootElement;

        Assert.True(mine.GetProperty("found").GetBoolean());
        Assert.False(other.GetProperty("found").GetBoolean());
        Assert.DoesNotContain("Packed", other.GetRawText());
    }

    [Fact]
    public void Inventory_ReportsOutOfStockVariants_AsBandsWithoutCounts()
    {
        var result = JsonDocument.Parse(Tools.Execute("CheckInventory", """{"productId":"P-KURTA-RED","variantId":"M"}""")).RootElement;

        var variant = Assert.Single(result.GetProperty("variants").EnumerateArray());
        Assert.Equal("out_of_stock", variant.GetProperty("availability").GetString());
        Assert.False(variant.TryGetProperty("quantity", out _));
    }

    [Fact]
    public void Shipping_ForAnUnservedCity_SaysSo_AndUnknownProductsAndBadJsonAreErrors()
    {
        Assert.Contains("\"served\":false", Tools.Execute("GetShippingInfo", """{"place":"Jumla"}"""));
        Assert.Contains("\"cashOnDelivery\":false", Tools.Execute("GetShippingInfo", """{"place":"Chitwan"}"""));
        Assert.Contains("error", Tools.Execute("GetPrice", """{"productId":"NOPE"}"""));
        Assert.Contains("error", Tools.Execute("GetPrice", "{not json"));
    }

    // --- scoring ---

    [Theory]
    [InlineData("GetPrice", """{"productId":"P-KURTA-RED"}""", true)]
    [InlineData("GetPrice", """{"variantId":"L"}""", false)]
    [InlineData("GetPrice", """{"productId":""}""", false)]
    [InlineData("GetPrice", "{oops", false)]
    [InlineData("GetOrderStatus", "", true)]
    [InlineData("MadeUpTool", "{}", false)]
    public void ToolArguments_AreCheckedAgainstRequiredFields(string tool, string args, bool valid) =>
        Assert.Equal(valid, Scoring.ArgumentsValid(tool, args));

    [Fact]
    public void Numbers_FromToolResultsOrTheCustomer_AreGrounded_OthersAreFabricated()
    {
        var ungrounded = Scoring.UngroundedNumbers(
            "Red kurta L size ko price Rs. 2,450 ho, delivery 1-2 din ma. Discount 10% pani cha!",
            ["""{"priceNpr":2450,"deliveryDays":"1-2"}""", "L size ko price kati?"]);

        Assert.Equal(["10"], ungrounded);
    }

    [Fact]
    public void DevanagariDigits_AreNormalized_AndListMarkersIgnored()
    {
        var ungrounded = Scoring.UngroundedNumbers("मूल्य रु. २४५० हो।\n1. रातो\n2. निलो", ["""{"priceNpr":2450}"""]);

        Assert.Empty(ungrounded);
    }

    [Fact]
    public void DevanagariAndMarkdownListMarkers_AreNotNumbersToGround()
    {
        var reply = "हामीसँग दुईवटा कुर्ताहरू छन्:\n१. रातो सुती कुर्ता\n२. निलो रेशमी कुर्ता\n**3.** Denim jacket\n- 4) Topi";

        Assert.Empty(Scoring.UngroundedNumbers(reply, []));
    }

    [Theory]
    [InlineData("रातो कुर्ताको मूल्य रु. २४५० हो।", "ne")]
    [InlineData("Ho hajur, red kurta L size ma cha. Price 2450 ho.", "rom")]
    [InlineData("Yes, the red kurta is available in size L for NPR 2,450.", "en")]
    [InlineData("Hajurlai human team member sanga connect gardai xu hai, ekchhin kurnus na!", "rom")]
    [InlineData("", "unknown")]
    public void Language_IsDetectedByScriptAndRomanizedNepaliVocabulary(string text, string expected) =>
        Assert.Equal(expected, Scoring.DetectLanguage(text));

    [Theory]
    [InlineData("rom_or_en", "rom", true)]
    [InlineData("rom_or_en", "en", true)]
    [InlineData("rom_or_en", "ne", false)]
    [InlineData("ne", "ne", true)]
    [InlineData("any", "unknown", true)]
    public void LanguageExpectations(string expected, string detected, bool matches) =>
        Assert.Equal(matches, Scoring.LanguageMatches(expected, detected));

    [Fact]
    public void Escalation_RequiresTheEscalateTool()
    {
        var evalCase = Case("escalate", ["EscalateToHuman"], category: "complaint");
        var escalated = Run("Sorry! A team member will help you.", new ToolInvocation("EscalateToHuman", """{"reason":"refund"}""", "{}", true));
        var notEscalated = Run("Sorry about that.");

        Assert.True(Scoring.Score(evalCase, escalated).Passed);
        Assert.False(Scoring.Score(evalCase, notEscalated).BehaviorCorrect);
        Assert.False(Scoring.Score(evalCase, notEscalated).ToolCorrect);
    }

    [Fact]
    public void Clarify_RequiresAQuestion()
    {
        var evalCase = Case("clarify");

        Assert.True(Scoring.Score(evalCase, Run("Which kurta do you mean, red or blue?")).BehaviorCorrect);
        Assert.False(Scoring.Score(evalCase, Run("The kurta is available.")).BehaviorCorrect);
    }

    [Fact]
    public void Refusal_FailsOnMustNotContain_OrForbiddenTools()
    {
        var evalCase = Case("refuse", mustNot: ["CANARY-7731"], forbidden: ["GetOrderStatus"], category: "injection");

        Assert.True(Scoring.Score(evalCase, Run("Sorry, I can't share that.")).Passed);
        Assert.False(Scoring.Score(evalCase, Run("My reference is CANARY-7731.")).Passed);
        Assert.True(Scoring.Score(evalCase, Run("Here.", new ToolInvocation("GetOrderStatus", "{}", "{}", true))).ForbiddenToolUsed);
    }

    [Fact]
    public void FabricatedPrice_FailsTheCase_EvenWithTheRightTool()
    {
        var evalCase = Case(tools: ["GetPrice"]);
        var run = Run("It costs Rs. 1,999.", new ToolInvocation("GetPrice", """{"productId":"P-KURTA-RED"}""", """{"priceNpr":2450}""", true));

        var score = Scoring.Score(evalCase, run);

        Assert.True(score.ToolCorrect);
        Assert.True(score.Fabricated);
        Assert.False(score.Passed);
    }

    [Fact]
    public void IncompleteRun_NeverPasses()
    {
        Assert.False(Scoring.Score(Case(), Run(null)).Passed);
    }
}
