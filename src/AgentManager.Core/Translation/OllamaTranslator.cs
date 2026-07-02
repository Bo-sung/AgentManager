using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AgentManager.Core.Translation;

public sealed record OllamaOptions
{
    public string Endpoint { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "exaone3.5:7.8b";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>번역 전 언어(사용자 입력·표시) — 프롬프트에 쓰는 영어 표기. 예: Korean.</summary>
    public string SourceLanguage { get; init; } = "Korean";
    /// <summary>번역 후 언어(엔진에 전달) — 프롬프트에 쓰는 영어 표기. 예: English.</summary>
    public string TargetLanguage { get; init; } = "English";
}

/// <summary>
/// Local-LLM translator over Ollama's /api/generate. The translation STRATEGY (skip detection, code/@file
/// masking, prompt framing, restore) lives in <see cref="TranslatorBase"/>; this class only implements the
/// actual model call plus Ollama-specific health/model-list helpers.
/// </summary>
public sealed class OllamaTranslator(OllamaOptions options, HttpClient? http = null)
    : TranslatorBase(options.SourceLanguage, options.TargetLanguage)
{
    private readonly OllamaOptions _opt = options;
    private readonly HttpClient _http = http ?? new HttpClient();

    /// <summary>요청용 엔드포인트 정규화. .NET HttpClient는 localhost를 IPv6(::1) 우선 해석하는데
    /// Ollama 기본 바인딩은 127.0.0.1(IPv4)뿐이라 연결이 실패한다 → localhost를 IPv4로 직접 지정.</summary>
    private static string Ipv4(string? endpoint)
    {
        var ep = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.Trim();
        return ep.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    }

    /// <summary>Ollama 서버가 응답하는지 빠른 핑(/api/tags). 번역 게이팅/상태표시용 — 짧은 타임아웃.</summary>
    public static async Task<bool> PingAsync(string endpoint, int timeoutMs = 1500, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = await http.GetAsync($"{Ipv4(endpoint)}/api/tags", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Ollama에 설치된 모델 이름 목록(/api/tags). 실패 시 예외 — 호출부에서 처리.</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(string endpoint, CancellationToken ct = default)
    {
        var ep = Ipv4(endpoint);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var resp = await http.GetAsync($"{ep}/api/tags", ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            foreach (var m in models.EnumerateArray())
                if (m.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                    list.Add(name);
        return list;
    }

    protected override async Task<string?> GenerateAsync(string prompt, CancellationToken ct)
    {
        // Egress guard: never POST prompt/code to a non-loopback plaintext-HTTP endpoint (SEC: translation egress).
        if (!TranslationEndpointPolicy.AllowsSend(_opt.Endpoint)) return null;

        // 유휴 후 첫 호출은 모델 콜드로드(수십 초)로 타임아웃이 나기 쉽다 — 한 번 더, 더 길게 재시도.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(attempt == 0 ? _opt.Timeout : _opt.Timeout * 2);
                using var resp = await _http.PostAsJsonAsync(
                    $"{Ipv4(_opt.Endpoint)}/api/generate",
                    // keep_alive 30m: 턴 사이 모델 퇴출로 인한 반복 콜드로드 방지
                    new { model = _opt.Model, prompt, stream = false, keep_alive = "30m", options = new { temperature = 0.1 } },
                    cts.Token);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
                return doc.RootElement.TryGetProperty("response", out var r) ? r.GetString() : null;
            }
            catch when (!ct.IsCancellationRequested && attempt == 0)
            {
                // retry once (cold load)
            }
            catch
            {
                break; // user cancelled or second failure
            }
        }
        return null;
    }
}
