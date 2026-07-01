using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgentManager.Core.Translation;

public sealed record OpenAiCompatOptions
{
    /// <summary>OpenAI-compatible base URL, ending at the version segment — e.g. <c>http://localhost:1234/v1</c>
    /// (LM Studio), <c>https://api.openai.com/v1</c>, <c>https://api.groq.com/openai/v1</c>,
    /// <c>https://openrouter.ai/api/v1</c>. The request appends <c>/chat/completions</c>.</summary>
    public string Endpoint { get; init; } = "http://localhost:1234/v1";
    public string Model { get; init; } = "";
    /// <summary>Bearer token for cloud endpoints; null/empty for a keyless local server.</summary>
    public string? ApiKey { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public string SourceLanguage { get; init; } = "Korean";
    public string TargetLanguage { get; init; } = "English";
}

/// <summary>
/// Translator over any OpenAI-compatible <c>/chat/completions</c> endpoint — one shape that covers most local
/// servers (LM Studio, llama.cpp, vLLM, Ollama's OpenAI mode) AND cloud providers (OpenAI, Groq, OpenRouter,
/// and Anthropic/others behind a compatible proxy). The translation strategy lives in <see cref="TranslatorBase"/>;
/// this only performs the HTTP call. Only the model call is here — endpoint/key/consent policy is the caller's.
/// </summary>
public sealed class OpenAiCompatTranslator(OpenAiCompatOptions options, HttpClient? http = null)
    : TranslatorBase(options.SourceLanguage, options.TargetLanguage)
{
    private readonly OpenAiCompatOptions _opt = options;
    private readonly HttpClient _http = http ?? new HttpClient();

    private static string Base(string? endpoint) =>
        (string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:1234/v1" : endpoint.Trim()).TrimEnd('/');

    /// <summary>Whether the endpoint host is loopback — the caller uses this to require HTTPS/consent for remote hosts.</summary>
    public static bool IsLoopback(string? endpoint)
    {
        if (!Uri.TryCreate(Base(endpoint), UriKind.Absolute, out var uri)) return false;
        var h = uri.Host;
        return h is "localhost" or "127.0.0.1" or "::1"
            || (System.Net.IPAddress.TryParse(h, out var ip) && System.Net.IPAddress.IsLoopback(ip));
    }

    private static void Auth(HttpRequestMessage req, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    /// <summary>Reachability + auth check (GET /models). For status gating / settings validation.</summary>
    public static async Task<bool> PingAsync(string endpoint, string? apiKey, int timeoutMs = 2500, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base(endpoint)}/models");
            Auth(req, apiKey);
            using var resp = await http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Model ids offered by the endpoint (GET /models → data[].id). Throws on failure — caller handles.</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(string endpoint, string? apiKey, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base(endpoint)}/models");
        Auth(req, apiKey);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var m in data.EnumerateArray())
                if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s)
                    list.Add(s);
        return list;
    }

    protected override async Task<string?> GenerateAsync(string prompt, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_opt.Timeout);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base(_opt.Endpoint)}/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = _opt.Model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.1,
                    stream = false,
                }),
            };
            Auth(req, _opt.ApiKey);
            using var resp = await _http.SendAsync(req, cts.Token);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            // choices[0].message.content
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var msg)
                && msg.TryGetProperty("content", out var content))
                return content.GetString();
            return null;
        }
        catch
        {
            return null; // base falls back to the original text
        }
    }
}
