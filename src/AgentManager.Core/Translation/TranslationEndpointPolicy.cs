using System.Net;

namespace AgentManager.Core.Translation;

/// <summary>
/// Egress policy for translation endpoints. Translation sends the user's prompt + the assistant's output —
/// which routinely contain source code, file paths, and sometimes secrets — to the configured endpoint. To stop
/// a mistyped / hand-edited (settings.json is plaintext on disk) / hostile endpoint from silently exfiltrating
/// that in cleartext, sending is allowed ONLY to a loopback host (any scheme — a local model) or over HTTPS
/// (any host — encrypted). A non-loopback plaintext-HTTP endpoint is refused; the translator then passes the
/// text through untranslated rather than leaking it. (SEC: translation egress)
/// </summary>
public static class TranslationEndpointPolicy
{
    /// <summary>Is the endpoint host loopback (localhost / 127.0.0.0-8 / ::1)?</summary>
    public static bool IsLoopback(string? endpoint)
    {
        if (!Uri.TryCreate((endpoint ?? "").Trim(), UriKind.Absolute, out var uri)) return false;
        var h = uri.Host;
        return h is "localhost" or "127.0.0.1" or "::1"
            || (IPAddress.TryParse(h, out var ip) && IPAddress.IsLoopback(ip));
    }

    /// <summary>May the app POST prompt/response text to this endpoint? Loopback (any scheme) or HTTPS only.</summary>
    public static bool AllowsSend(string? endpoint)
    {
        var ep = (endpoint ?? "").Trim();
        if (!Uri.TryCreate(ep, UriKind.Absolute, out var uri)) return true; // malformed → let the caller's own error path handle it
        return uri.Scheme == Uri.UriSchemeHttps || IsLoopback(ep);
    }

    /// <summary>A non-loopback endpoint (prompts would leave this machine) — the UI warns when this is true.</summary>
    public static bool IsRemote(string? endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out _) && !IsLoopback(endpoint);
}
