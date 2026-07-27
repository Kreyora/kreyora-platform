using Kreyora.Domain.Abstractions;

namespace Kreyora.UnitTests.Domain;

public class IdGeneratorTests
{
    [Fact]
    public void NewId_ProducesNonEmptyString()
    {
        var id = IdGenerator.NewId();

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(26, id.Length);
    }

    [Fact]
    public void NewId_ProducesUniqueValues()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => IdGenerator.NewId()).ToHashSet();

        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public void NewId_WithTimestamp_IsLexicographicallySortable()
    {
        var earlier = IdGenerator.NewId(DateTimeOffset.UtcNow.AddMinutes(-10));
        var later = IdGenerator.NewId(DateTimeOffset.UtcNow);

        Assert.True(string.Compare(earlier, later, StringComparison.Ordinal) < 0);
    }
}
