using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentManager.Core.Engines;

/// <summary>
/// Records the user's explicit approval to run each CUSTOM engine's launch command (arbitrary exe + argument list).
/// One approved fingerprint per engine id, persisted to a DEDICATED file
/// <c>%LOCALAPPDATA%/AgentManager/trusted-engines.json</c> — deliberately NOT co-located in the hand-editable
/// <c>engines/&lt;id&gt;.json</c> (a manual edit of exe+args must NOT be able to forge its own approval) and NOT in
/// state.json (keeps trust independently headless-testable). A changed exe or args yields a different fingerprint, so
/// the old approval becomes stale and the run is re-gated automatically. Robust: a missing/unparseable file loads as
/// empty (never throws); Save is atomic (temp + move).
/// </summary>
public sealed class EngineTrustStore
{
    private readonly Dictionary<string, string> _trusted; // engineId -> approved fingerprint
    private readonly string _path;

    private EngineTrustStore(string path, Dictionary<string, string> trusted)
    {
        _path = path;
        _trusted = trusted;
    }

    /// <summary>Default trust file under LocalApplicationData (next to settings.json / state.json).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "trusted-engines.json");

    /// <summary>Load the trust map. A missing or corrupt file yields an empty store. Never throws.</summary>
    public static EngineTrustStore Load(string? path = null)
    {
        path ??= DefaultPath;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOpts) is { } parsed)
                foreach (var kv in parsed)
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        map[kv.Key] = kv.Value;
        }
        catch { /* bad/missing file → empty store */ }
        return new EngineTrustStore(path, map);
    }

    /// <summary>Whether <paramref name="engineId"/> currently has EXACTLY <paramref name="fingerprint"/> approved.</summary>
    public bool IsTrusted(string engineId, string fingerprint) =>
        _trusted.TryGetValue(engineId, out var f) && string.Equals(f, fingerprint, StringComparison.Ordinal);

    /// <summary>Record (and persist) approval of <paramref name="fingerprint"/> for <paramref name="engineId"/>,
    /// replacing any previous fingerprint for that engine.</summary>
    public void Trust(string engineId, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(engineId) || string.IsNullOrWhiteSpace(fingerprint)) return;
        _trusted[engineId] = fingerprint;
        try { Save(); } catch { /* best-effort */ }
    }

    /// <summary>Forget any approval for <paramref name="engineId"/> (e.g. the engine was removed). Persists.</summary>
    public void Revoke(string engineId)
    {
        if (_trusted.Remove(engineId))
            try { Save(); } catch { /* best-effort */ }
    }

    /// <summary>Stable fingerprint of a launch command. exe + each arg are NUL-delimited so that distinct argument
    /// LISTS never collide (e.g. ["a","b"], ["a b"] and ["ab"] all hash differently). SHA-256 over UTF-8, hex.</summary>
    public static string Fingerprint(string exe, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        sb.Append((exe ?? "").Trim()).Append('\0');
        foreach (var a in args ?? [])
            sb.Append(a ?? "").Append('\0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_trusted, JsonOpts);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
