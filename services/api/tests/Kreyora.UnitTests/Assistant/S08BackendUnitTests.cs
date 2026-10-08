using Kreyora.Domain.Customers;
using Kreyora.Domain.Integrations;
using Kreyora.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Configuration;

namespace Kreyora.UnitTests.Assistant;

/// <summary>M09-S08: customer profile fields on the identity (Q8) and the job poll interval (Q9).</summary>
public sealed class S08BackendUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AProfile_FillsNameAndHandle_AndStampsTheAttempt()
    {
        var identity = Identity();

        identity.ApplyProfile("  Test Customer ", "@test.customer", Now);

        Assert.Equal(("Test Customer", "test.customer", Now), (identity.DisplayName, identity.Username, identity.ProfileCheckedAt));
    }

    [Fact]
    public void ARefusedLookup_StillStampsTheAttempt_SoItIsNotRetriedOnEveryMessage()
    {
        var identity = Identity();

        identity.ApplyProfile(null, null, Now);

        Assert.Null(identity.DisplayName);
        Assert.Equal(Now, identity.ProfileCheckedAt);
        Assert.False(CustomerChannelIdentity.ProfileCheckDue(identity.ProfileCheckedAt, Now.AddDays(6), TimeSpan.FromDays(7)));
        Assert.True(CustomerChannelIdentity.ProfileCheckDue(identity.ProfileCheckedAt, Now.AddDays(7), TimeSpan.FromDays(7)));
        Assert.True(CustomerChannelIdentity.ProfileCheckDue(null, Now, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void Erasure_ClearsTheHandle_AndBlocksLaterProfiles()
    {
        var identity = Identity();
        identity.ApplyProfile("Test Customer", "test.customer", Now);

        identity.Erase(Now.AddMinutes(1));
        identity.ApplyProfile("Again", "again", Now.AddMinutes(2));

        Assert.Null(identity.DisplayName);
        Assert.Null(identity.Username);
        Assert.Equal(Now, identity.ProfileCheckedAt); // the post-erasure attempt changed nothing
    }

    [Fact]
    public void ALongHandle_IsCapped()
    {
        var identity = Identity();

        identity.ApplyProfile(null, new string('a', 100), Now);

        Assert.Equal(CustomerChannelIdentity.UsernameMaxLength, identity.Username!.Length);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData("1", 1)]
    [InlineData("15", 15)]
    public void ThePollInterval_DefaultsTo2s_AndAcceptsOneToFifteen(string? configured, int expected) =>
        Assert.Equal(expected, HangfireSetup.PollIntervalSeconds(Config(configured)));

    [Theory]
    [InlineData("0")]
    [InlineData("16")]
    public void ThePollInterval_OutsideTheRange_FailsAtStartup(string configured) =>
        Assert.Throws<InvalidOperationException>(() => HangfireSetup.PollIntervalSeconds(Config(configured)));

    private static CustomerChannelIdentity Identity() =>
        CustomerChannelIdentity.Create("01J00000000000000000000001", "01J00000000000000000000002", ChannelType.Instagram, "1234567890", Now);

    private static IConfiguration Config(string? pollSeconds) => new ConfigurationBuilder()
        .AddInMemoryCollection(pollSeconds is null ? [] : new Dictionary<string, string?> { ["BackgroundJobs:PollIntervalSeconds"] = pollSeconds })
        .Build();
}
