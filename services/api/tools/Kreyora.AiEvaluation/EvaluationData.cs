using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kreyora.AiEvaluation;

/// <summary>Synthetic evaluation data (M09-S01). Read from <c>services/api/evaluation/m09</c>.</summary>
public sealed record EvalDataset(string Version, string SystemPromptCanary, IReadOnlyList<EvalCase> Cases)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static EvalDataset Load(string path) =>
        JsonSerializer.Deserialize<EvalDataset>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Empty dataset: {path}");
}

public sealed record EvalCase(
    string Id,
    string Category,
    string Language,
    bool Screening,
    IReadOnlyList<EvalTurn> Turns,
    EvalExpectation Expect,
    string? Note = null);

public sealed record EvalTurn(string Role, string Text);

/// <param name="Tools">Any one of these must be called (empty: no tool required).</param>
/// <param name="Behavior"><c>answer</c>, <c>clarify</c>, <c>escalate</c> or <c>refuse</c>.</param>
/// <param name="ReplyLanguage"><c>ne</c>, <c>rom</c>, <c>en</c>, <c>rom_or_en</c> or <c>any</c>.</param>
public sealed record EvalExpectation(
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> ForbiddenTools,
    string Behavior,
    string ReplyLanguage,
    IReadOnlyList<string> MustNotContain);

public sealed record FakeCatalog(
    string Version,
    IReadOnlyList<FakeProduct> Products,
    IReadOnlyList<FakeShippingZone> Shipping,
    string PaymentNotes,
    FakeCustomer ConversationCustomer,
    IReadOnlyList<JsonElement> OtherCustomersOrders)
{
    public static FakeCatalog Load(string path) =>
        JsonSerializer.Deserialize<FakeCatalog>(File.ReadAllText(path), EvalDataset.Json)
        ?? throw new InvalidDataException($"Empty catalog: {path}");
}

public sealed record FakeProduct(string Id, string Name, string NameNe, string Category, IReadOnlyList<FakeVariant> Variants);

public sealed record FakeVariant(string Sku, string? Size, string? Color, int PriceNpr, int Stock);

public sealed record FakeShippingZone(string Zone, IReadOnlyList<string> Cities, int FeeNpr, string Days, bool Cod, bool Qr);

public sealed record FakeCustomer(string DisplayName, IReadOnlyList<JsonElement> Orders);
