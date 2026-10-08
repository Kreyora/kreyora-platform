using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kreyora.AiEvaluation;

/// <summary>
/// M09-S08 pipeline mode: a signed-in seller session against a local Kreyora API (cookie auth, CSRF, tenant header,
/// deterministic idempotency keys so re-running a seed never duplicates anything). Never prints secrets.
/// </summary>
public sealed class ApiSession : IDisposable
{
    public const string TenantHeader = "X-Kreyora-Tenant-Id";
    private readonly HttpClient http;
    private string? csrf;

    public ApiSession(string baseUrl, HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true }, disposeHandler: true)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public string? TenantId { get; private set; }

    public async Task SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        await RefreshCsrfAsync(cancellationToken);
        await SendAsync(HttpMethod.Post, "v1/auth/sign-in", new { email, password }, cancellationToken);
        await RefreshCsrfAsync(cancellationToken);
        var workspaces = await SendAsync(HttpMethod.Get, "v1/workspaces", null, cancellationToken);
        var list = workspaces is JsonArray array ? array : workspaces?["items"]?.AsArray();
        TenantId = list?.FirstOrDefault()?["tenantId"]?.GetValue<string>() ?? throw new InvalidOperationException("The signed-in user has no workspace.");
        await RefreshCsrfAsync(cancellationToken);
    }

    public Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, path, null, cancellationToken);

    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var (status, json, text) = await TrySendAsync(method, path, body, cancellationToken);
        if ((int)status is >= 200 and < 300 || (allowNotFound && status == HttpStatusCode.NotFound)) return json;
        throw new HttpRequestException($"{method} /{path} → {(int)status}: {Problem(json, text)}", null, status);
    }

    public async Task<(HttpStatusCode Status, JsonNode? Json, string Text)> TrySendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (TenantId is not null) request.Headers.Add(TenantHeader, TenantId);
        if (method != HttpMethod.Get)
        {
            if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
            request.Headers.Add("Idempotency-Key", IdempotencyKey(method, path, body));
        }

        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                json = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                json = null;
            }
        }

        return (response.StatusCode, json, text);
    }

    public void Dispose() => http.Dispose();

    private async Task RefreshCsrfAsync(CancellationToken cancellationToken) =>
        csrf = (await SendAsync(HttpMethod.Get, "v1/auth/csrf", null, cancellationToken))?["token"]?.GetValue<string>();

    private static string IdempotencyKey(HttpMethod method, string path, object? body) =>
        "m09s08-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(method + path + JsonSerializer.Serialize(body))))[..40];

    private static string Problem(JsonNode? json, string text) =>
        json is JsonObject o ? $"{o["title"]} {o["detail"]}".Trim() : text.Length > 200 ? text[..200] : text;
}
