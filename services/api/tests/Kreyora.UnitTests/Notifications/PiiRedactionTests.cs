using Kreyora.Application.Notifications;
using Kreyora.Domain.Notifications;

namespace Kreyora.UnitTests.Notifications;

public sealed class PiiRedactionTests
{
    [Theory]
    [InlineData("hari@example.com", "h***@example.com")]
    [InlineData("ab@test.com", "a*@test.com")]
    [InlineData("a@test.com", "*@test.com")]
    [InlineData("john.doe@company.org", "j***@company.org")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("notanemail", "***")]
    public void RedactEmail_RedactsCorrectly(string? input, string expected)
    {
        var result = PiiRedaction.RedactEmail(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("+9779800000001", "+977****0001")]
    [InlineData("9800000001", "9800****0001")]
    [InlineData("1234", "****")]
    [InlineData("12", "****")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void RedactPhone_RedactsCorrectly(string? input, string expected)
    {
        var result = PiiRedaction.RedactPhone(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RedactContact_DispatchesByChannel()
    {
        Assert.Equal("h***@example.com", PiiRedaction.RedactContact("hari@example.com", NotificationChannel.Email));
        Assert.Equal("+977****0001", PiiRedaction.RedactContact("+9779800000001", NotificationChannel.Sms));
    }
}

