using Kreyora.Application.Models;

namespace Kreyora.UnitTests.Models;

public class PageRequestTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
    }

    [Fact]
    public void Normalize_ClampsNegativePage()
    {
        var request = new PageRequest { Page = -5, PageSize = 10 }.Normalize();

        Assert.Equal(1, request.Page);
    }

    [Fact]
    public void Normalize_ClampsOversizedPageSize()
    {
        var request = new PageRequest { Page = 1, PageSize = 500 }.Normalize();

        Assert.Equal(PageRequest.MaxPageSize, request.PageSize);
    }

    [Fact]
    public void Skip_CalculatesCorrectOffset()
    {
        var request = new PageRequest { Page = 3, PageSize = 25 };

        Assert.Equal(50, request.Skip);
    }
}
