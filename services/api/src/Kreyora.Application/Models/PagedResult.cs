using System.Diagnostics.CodeAnalysis;

namespace Kreyora.Application.Models;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
    Justification = "Factory method for empty paged results is the standard pattern")]
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }

    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;

    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Empty(int page = 1, int pageSize = PageRequest.DefaultPageSize)
    {
        return new PagedResult<T>
        {
            Items = [],
            Page = page,
            PageSize = pageSize,
            TotalCount = 0
        };
    }
}
