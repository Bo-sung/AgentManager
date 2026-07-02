using System.Net;

namespace AgentManager.Core.Translation;

/// <summary>
/// Endpoint classification for translation egress WARNINGS (advisory, not enforcement). Translation sends the
/// user's prompt + assistant output — which routinely contain source code, file paths, and sometimes secrets —
/// to the configured endpoint. A custom/remote endpoint (self-hosted LAN box, cloud API) is ALLOWED so
/// legitimate test servers work; the UI just surfaces the risk (prompts leave the machine; plaintext HTTP to a
/// remote host is unencrypted) so the user decides. (SEC: translation egress — advisory)
/// </summary>
public static class TranslationEndpointPolicy
{
    /// <summary>Is the endpoint host loopback (localhost / 127.0.0.0-8 / ::1)? Loopback = stays on this machine.</summary>
    public static bool IsLoopback(string? endpoint)
    {
        if (!Uri.TryCreate((endpoint ?? "").Trim(), UriKind.Absolute, out var uri)) return false;
        var h = uri.Host;
        return h is "localhost" or "127.0.0.1" or "::1"
            || (IPAddress.TryParse(h, out var ip) && IPAddress.IsLoopback(ip));
    }

    /// <summary>A non-loopback endpoint (prompts would leave this machine) — the UI warns when this is true.</summary>
    public static bool IsRemote(string? endpoint) =>
        !string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out _) && !IsLoopback(endpoint);
}
