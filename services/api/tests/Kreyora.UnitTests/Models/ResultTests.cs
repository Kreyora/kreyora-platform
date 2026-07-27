using Kreyora.Application.Models;

namespace Kreyora.UnitTests.Models;

public class ResultTests
{
    [Fact]
    public void Success_ContainsValue()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_ContainsError()
    {
        var error = new ErrorDetail
        {
            Type = "test",
            Title = "Test Error",
            Status = 400,
            Detail = "Something went wrong"
        };
        var result = Result<int>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void NotFound_Creates404Error()
    {
        var result = Result<string>.NotFound("Item not found");

        Assert.True(result.IsFailure);
        Assert.Equal(404, result.Error!.Status);
        Assert.Equal("Not Found", result.Error.Title);
        Assert.Equal("Item not found", result.Error.Detail);
    }

    [Fact]
    public void ValidationError_Creates400Error()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Name is required"]
        };
        var result = Result<string>.ValidationError("Validation failed", errors);

        Assert.True(result.IsFailure);
        Assert.Equal(400, result.Error!.Status);
        Assert.NotNull(result.Error.Errors);
        Assert.Contains("Name", result.Error.Errors!.Keys);
    }

    [Fact]
    public void Conflict_Creates409Error()
    {
        var result = Result<string>.Conflict("Already exists");

        Assert.True(result.IsFailure);
        Assert.Equal(409, result.Error!.Status);
    }

    [Fact]
    public void Forbidden_Creates403Error()
    {
        var result = Result<string>.Forbidden("Not allowed");

        Assert.True(result.IsFailure);
        Assert.Equal(403, result.Error!.Status);
    }

    [Fact]
    public void Match_CallsOnSuccess_WhenSuccessful()
    {
        var result = Result<int>.Success(10);

        var output = result.Match(
            onSuccess: v => v * 2,
            onFailure: _ => -1);

        Assert.Equal(20, output);
    }

    [Fact]
    public void Match_CallsOnFailure_WhenFailed()
    {
        var result = Result<int>.NotFound("Not found");

        var output = result.Match(
            onSuccess: v => v * 2,
            onFailure: e => e.Status);

        Assert.Equal(404, output);
    }

    [Fact]
    public void StaticHelper_Success_Works()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void StaticHelper_NotFound_Works()
    {
        var result = Result.NotFound<string>("Missing");

        Assert.True(result.IsFailure);
        Assert.Equal(404, result.Error!.Status);
    }
}
