namespace Kreyora.Application.Models;

public sealed record PageRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;

    public PageRequest Normalize()
    {
        return this with
        {
            Page = Math.Max(1, Page),
            PageSize = Math.Clamp(PageSize, 1, MaxPageSize)
        };
    }

    public int Skip => (Math.Max(1, Page) - 1) * Math.Clamp(PageSize, 1, MaxPageSize);
}
