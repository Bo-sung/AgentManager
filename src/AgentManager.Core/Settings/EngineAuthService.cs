namespace AgentManager.Core.Settings;

/// <summary>Per-engine authentication + rate-limit state — subscription vs API key, auto-switch-to-API
/// on limit, and rate-limit cooldowns. Headless (Core) so a CLI and the GUI share ONE source of truth;
/// DPAPI encrypt/decrypt and the usage lookup are injected as delegates so Core stays WPF/Windows-port
/// free. The GUI ViewModel holds one instance and forwards to it (no duplicate state in the VM).</summary>
public sealed class EngineAuthService
{
    private readonly Func<string, string> _encrypt;
    private readonly Func<string, string> _decrypt;
    private readonly Func<string, (double Util, long ResetsAtUnix)?> _usageOf;

    private readonly Dictionary<string, string> _authMode = new();   // id → "subscription" | "api"
    private readonly Dictionary<string, string> _apiKey = new();     // id → DPAPI base64
    private readonly Dictionary<string, bool> _autoApi = new();      // id → 한도 도달 시 API 자동 전환(opt-in)
    private readonly Dictionary<string, long> _limitedUntil = new(); // id → rate-limit 차단 해제(unix)

    /// <param name="encrypt">plaintext → DPAPI base64 (injected; Windows-only stays in the app layer).</param>
    /// <param name="decrypt">DPAPI base64 → plaintext.</param>
    /// <param name="usageOf">id → current (utilization 0..1, reset unix) or null — usage lives elsewhere.</param>
    public EngineAuthService(Func<string, string> encrypt, Func<string, string> decrypt,
        Func<string, (double Util, long ResetsAtUnix)?> usageOf)
    {
        _encrypt = encrypt; _decrypt = decrypt; _usageOf = usageOf;
    }

    public bool HasApiKey(string id) => !string.IsNullOrWhiteSpace(_decrypt(_apiKey.GetValueOrDefault(id, "")));
    public bool AutoApiOnLimit(string id) => _autoApi.GetValueOrDefault(id);

    /// <summary>한도 소진 상태인가 — 실제 rate-limit 실패(리셋 전) 또는 사용량 ~100%.</summary>
    public bool IsEngineLimited(string id)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_limitedUntil.TryGetValue(id, out var until) && until > now) return true;
        if (_usageOf(id) is { } u && u.Util >= 0.999 && u.ResetsAtUnix > now) return true;
        return false;
    }

    /// <summary>소진 시 API로 자동 전환되어 계속 사용 가능한가(토글 ON + 키 보유).</summary>
    public bool WillUseApiOnLimit(string id) => AutoApiOnLimit(id) && HasApiKey(id);

    /// <summary>rate-limit 실제 실패를 기록 — 리셋 시각(모르면 +1h)까지 소진 처리.</summary>
    public void MarkRateLimited(string id, long resetUnix)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _limitedUntil[id] = resetUnix > now ? resetUnix : now + 3600;
    }

    /// <summary>이 엔진이 API 키 경로로 도는가 — 명시적 API 모드(키 보유) OR (한도 소진 + 자동전환 + 키).
    /// 어댑터/백엔드 분기(agy: AgyAdapter ↔ AgySdkAdapter)에 사용.</summary>
    public bool IsApiMode(string id) =>
        (_authMode.GetValueOrDefault(id, "subscription") == "api" && HasApiKey(id))
        || (IsEngineLimited(id) && WillUseApiOnLimit(id));

    /// <summary>실행 시 주입할 env: api 모드 OR (한도 소진 + 자동전환 + 키) → { 변수명: 복호화키 }. 없으면 빈 맵.</summary>
    public IReadOnlyDictionary<string, string> ApiEnvFor(string id)
    {
        if (CoreHelpers.ApiEnvVar(id) is not { } envVar) return Empty;
        var useApi = _authMode.GetValueOrDefault(id, "subscription") == "api"
                     || (IsEngineLimited(id) && WillUseApiOnLimit(id));
        if (!useApi) return Empty;
        var key = _decrypt(_apiKey.GetValueOrDefault(id, ""));
        return string.IsNullOrWhiteSpace(key) ? Empty : new Dictionary<string, string> { [envVar] = key };
    }
    private static readonly Dictionary<string, string> Empty = new();

    /// <summary>설정 저장: 모드 + (있으면) 평문 키를 암호화해 보관, 없으면 키 제거.</summary>
    public void SaveEngineAuth(string id, string mode, string plainKey)
    {
        _authMode[id] = mode == "api" ? "api" : "subscription";
        if (!string.IsNullOrWhiteSpace(plainKey)) _apiKey[id] = _encrypt(plainKey.Trim());
        else _apiKey.Remove(id);
    }

    // ----- editor accessors (settings form mirrors) -----
    public string GetAuthMode(string id) => _authMode.GetValueOrDefault(id, "subscription");
    public bool GetAutoApi(string id) => _autoApi.GetValueOrDefault(id);
    public string GetApiKeyPlain(string id) => _decrypt(_apiKey.GetValueOrDefault(id, ""));
    public void SetAutoApi(string id, bool on) => _autoApi[id] = on;

    // ----- persistence (the app state DTO carries the four maps) -----
    public void Load(IDictionary<string, string>? authMode, IDictionary<string, string>? apiKey,
        IDictionary<string, bool>? autoApi, IDictionary<string, long>? limitedUntil)
    {
        Replace(_authMode, authMode); Replace(_apiKey, apiKey);
        Replace(_autoApi, autoApi); Replace(_limitedUntil, limitedUntil);
    }
    private static void Replace<TV>(Dictionary<string, TV> dst, IDictionary<string, TV>? src)
    {
        dst.Clear();
        if (src is not null) foreach (var kv in src) dst[kv.Key] = kv.Value;
    }
    public Dictionary<string, string> SnapshotAuthMode() => new(_authMode);
    public Dictionary<string, string> SnapshotApiKey() => new(_apiKey);
    public Dictionary<string, bool> SnapshotAutoApi() => new(_autoApi);
    public Dictionary<string, long> SnapshotLimitedUntil() => new(_limitedUntil);
}
