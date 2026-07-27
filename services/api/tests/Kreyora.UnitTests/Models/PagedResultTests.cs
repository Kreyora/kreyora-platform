using Kreyora.Application.Models;

namespace Kreyora.UnitTests.Models;

public class PagedResultTests
{
    [Fact]
    public void TotalPages_CalculatesCorrectly_WhenItemsDivideEvenly()
    {
        var result = new PagedResult<string>
        {
            Items = ["a", "b"],
            Page = 1,
            PageSize = 10,
            TotalCount = 50
        };

        Assert.Equal(5, result.TotalPages);
    }

    [Fact]
    public void TotalPages_RoundsUp_WhenItemsDoNotDivideEvenly()
    {
        var result = new PagedResult<string>
        {
            Items = ["a"],
            Page = 1,
            PageSize = 10,
            TotalCount = 51
        };

        Assert.Equal(6, result.TotalPages);
    }

    [Fact]
    public void TotalPages_IsZero_WhenPageSizeIsZero()
    {
        var result = new PagedResult<string>
        {
            Items = [],
            Page = 1,
            PageSize = 0,
            TotalCount = 10
        };

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void HasNextPage_IsTrue_WhenNotOnLastPage()
    {
        var result = new PagedResult<string>
        {
            Items = ["a"],
            Page = 1,
            PageSize = 10,
            TotalCount = 25
        };

        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_IsFalse_WhenOnLastPage()
    {
        var result = new PagedResult<string>
        {
            Items = ["a"],
            Page = 3,
            PageSize = 10,
            TotalCount = 25
        };

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void HasPreviousPage_IsFalse_OnFirstPage()
    {
        var result = new PagedResult<string>
        {
            Items = ["a"],
            Page = 1,
            PageSize = 10,
            TotalCount = 25
        };

        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_IsTrue_OnSecondPage()
    {
        var result = new PagedResult<string>
        {
            Items = ["a"],
            Page = 2,
            PageSize = 10,
            TotalCount = 25
        };

        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void Empty_ReturnsEmptyResult()
    {
        var result = PagedResult<string>.Empty(page: 3, pageSize: 15);

        Assert.Empty(result.Items);
        Assert.Equal(3, result.Page);
        Assert.Equal(15, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }
}
