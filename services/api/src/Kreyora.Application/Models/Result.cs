using System.Diagnostics.CodeAnalysis;

namespace Kreyora.Application.Models;

/// <summary>
/// Discriminated result type for service methods. Business errors return
/// Result.Failure instead of throwing exceptions. Controllers map failures
/// to the appropriate ProblemDetails response.
/// </summary>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
    Justification = "Factory methods on Result<T> are the standard pattern for discriminated results")]
public sealed class Result<T>
{
    public T? Value { get; }
    public ErrorDetail? Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result(T value)
    {
        Value = value;
        IsSuccess = true;
    }

    private Result(ErrorDetail error)
    {
        Error = error;
        IsSuccess = false;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(ErrorDetail error) => new(error);

    public static Result<T> NotFound(string detail) => new(new ErrorDetail
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        Title = "Not Found",
        Status = 404,
        Detail = detail
    });

    public static Result<T> ValidationError(string detail, IDictionary<string, string[]>? errors = null) => new(new ErrorDetail
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        Title = "Validation Error",
        Status = 400,
        Detail = detail,
        Errors = errors
    });

    public static Result<T> Conflict(string detail) => new(new ErrorDetail
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        Title = "Conflict",
        Status = 409,
        Detail = detail
    });

    public static Result<T> Forbidden(string detail) => new(new ErrorDetail
    {
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        Title = "Forbidden",
        Status = 403,
        Detail = detail
    });

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<ErrorDetail, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(Value!) : onFailure(Error!);
    }
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(ErrorDetail error) => Result<T>.Failure(error);
    public static Result<T> NotFound<T>(string detail) => Result<T>.NotFound(detail);
}
