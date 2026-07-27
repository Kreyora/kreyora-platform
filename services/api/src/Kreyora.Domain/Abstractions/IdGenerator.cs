namespace Kreyora.Domain.Abstractions;

/// <summary>
/// Produces sortable, globally unique identifiers using ULID.
/// ULIDs are 128-bit, lexicographically sortable, and encode a timestamp prefix
/// making them ideal for distributed primary keys with natural ordering.
/// </summary>
public static class IdGenerator
{
    public static string NewId() => Ulid.NewUlid().ToString();

    public static string NewId(DateTimeOffset timestamp) => Ulid.NewUlid(timestamp).ToString();
}
