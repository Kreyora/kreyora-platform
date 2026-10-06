using Kreyora.Infrastructure.Ai;

namespace Kreyora.UnitTests.Ai;

public sealed class AiOptionsValidatorTests
{
    private static List<string> Errors(AiOptions options) => AiOptionsValidator.Errors(options).ToList();

    [Fact]
    public void Defaults_AreValid_DisabledAndFake_WithoutAnyKey()
    {
        var options = new AiOptions();

        Assert.Empty(Errors(options));
        Assert.False(options.Enabled);
        Assert.Equal(AiMode.Fake, options.Mode);
        Assert.False(options.DataPolicy.AllowPersonalData);
    }

    [Fact]
    public void Live_RequiresPrimaryProfile()
    {
        var options = AiTestSetup.LiveOptions();
        options.Profiles.Remove("Primary");

        Assert.Contains(Errors(options), e => e.Contains("Ai:Profiles:Primary is required"));
    }

    [Fact]
    public void Live_RequiresKeyAndModel_ForEveryReferencedProfile()
    {
        var options = AiTestSetup.LiveOptions();
        options.Providers["GoogleAiStudio"].ApiKey = null;
        options.Profiles["Primary"].Model = "";

        var errors = Errors(options);

        Assert.Contains(errors, e => e.Contains("Ai:Providers:GoogleAiStudio:ApiKey is required"));
        Assert.Contains(errors, e => e.Contains("Ai:Profiles:Primary:Model is required"));
    }

    [Fact]
    public void Live_RejectsProfilePointingAtUnknownProvider()
    {
        var options = AiTestSetup.LiveOptions();
        options.Profiles["Fallback"].Provider = "Nowhere";

        Assert.Contains(Errors(options), e => e.Contains("'Nowhere' is not configured"));
    }

    [Fact]
    public void Fake_DoesNotNeedKeys_EvenWithLiveStyleProfiles()
    {
        var options = AiTestSetup.LiveOptions();
        options.Mode = AiMode.Fake;
        foreach (var provider in options.Providers.Values)
        {
            provider.ApiKey = null;
        }

        Assert.Empty(Errors(options));
    }

    [Theory]
    [InlineData("http://openrouter.ai/api/v1")]
    [InlineData("not a url")]
    public void ProviderBaseUrl_MustBeHttps(string baseUrl)
    {
        var options = new AiOptions { Providers = { ["OpenRouter"] = new AiProviderOptions { BaseUrl = baseUrl } } };

        Assert.Contains(Errors(options), e => e.Contains("must be an absolute HTTPS URL"));
    }

    [Fact]
    public void UnknownProfileName_IsRejected()
    {
        var options = new AiOptions { Profiles = { ["Tertiary"] = new AiProfileOptions() } };

        Assert.Contains(Errors(options), e => e.Contains("Tertiary is not a known profile"));
    }

    [Fact]
    public void PersonalData_OnAFreeTierProvider_IsRejectedAtStartup()
    {
        var options = AiTestSetup.LiveOptions();
        options.DataPolicy.AllowPersonalData = true; // OpenRouter/GoogleAiStudio free tiers are NoTraining=false

        var errors = Errors(options);

        Assert.Contains(errors, e => e.Contains("requires a NoTraining provider") && e.Contains("Primary"));
        Assert.Contains(errors, e => e.Contains("requires a NoTraining provider") && e.Contains("Fallback"));
    }

    [Fact]
    public void PaidNoTrainingProvider_CanBeAddedByConfigurationAlone_AndMayCarryPersonalData()
    {
        // R-PAID: e.g. OpenAI directly (or any OpenAI-compatible paid endpoint) is only configuration.
        var options = AiTestSetup.LiveOptions(withFallback: false);
        options.Providers["OpenAi"] = new AiProviderOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "paid-key",
            NoTraining = true,
            MaxTokensParameter = "max_completion_tokens"
        };
        options.Profiles["Primary"] = new AiProfileOptions { Provider = "OpenAi", Model = "any-paid-model" };
        options.DataPolicy.AllowPersonalData = true;

        Assert.Empty(Errors(options));
    }

    [Theory]
    [InlineData(0, 800)]
    [InlineData(121, 800)]
    [InlineData(30, 8)]
    [InlineData(30, 9000)]
    public void Limits_AreBounded(int timeoutSeconds, int maxOutputTokens)
    {
        var options = new AiOptions { Limits = { TimeoutSeconds = timeoutSeconds, MaxOutputTokens = maxOutputTokens } };

        Assert.NotEmpty(Errors(options));
    }
}
