using AgentManager.Core.Settings;

namespace AgentManager.Core.Translation;

/// <summary>A user-added OpenAI-compatible / cloud translation endpoint (the "+ Add custom" entries).</summary>
public sealed class TranslationCustomProvider
{
    /// <summary>Stable id, e.g. <c>"custom:&lt;guid&gt;"</c>. Referenced by <see cref="SettingsService.TranslationSelectedId"/>.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>OpenAI-compatible base URL ending at the version segment (e.g. https://api.openai.com/v1).</summary>
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>API key ENCRYPTED by the frontend (DPAPI) — opaque to Core. The factory decrypts via a delegate.</summary>
    public string ApiKeyEnc { get; set; } = "";
}

/// <summary>Descriptor for a selectable translation provider (built-in or custom) — for the settings picker.</summary>
public sealed record TranslationProviderInfo(string Id, string Kind, string DisplayName);

/// <summary>
/// Builds an <see cref="ITranslator"/> from the settings' selected provider, and enumerates the available
/// providers for the picker. Keeps the provider wiring in Core so a CLI/daemon could translate too later.
/// The frontend injects two delegates: <c>resolveExe</c> (agent id → engine executable) and <c>decryptKey</c>
/// (DPAPI-encrypted blob → plaintext), so Core needs neither engine-path detection nor DPAPI.
/// </summary>
public static class TranslatorFactory
{
    /// <summary>Providers offered in the picker: Ollama, one per installed agent (via <paramref name="isInstalled"/>),
    /// then every custom entry.</summary>
    public static IReadOnlyList<TranslationProviderInfo> Available(SettingsService s, Func<string, bool> isInstalled)
    {
        var list = new List<TranslationProviderInfo> { new("ollama", "ollama", "Ollama (local)") };
        foreach (var id in AgentTranslator.SupportedEngines)
            if (isInstalled(id))
                list.Add(new($"agent:{id}", "agent", $"Agent · {id}"));
        foreach (var c in s.TranslationCustomProviders)
            list.Add(new(c.Id, "openai", string.IsNullOrWhiteSpace(c.Name) ? c.Endpoint : c.Name));
        return list;
    }

    /// <summary>Build the translator for the selected provider (or an explicit <paramref name="providerId"/>).
    /// Returns null if it can't be built (e.g. an agent whose exe isn't found) — the caller then skips translation.
    /// <paramref name="sourceOverride"/>/<paramref name="targetOverride"/> let a worker pin its own language pair.</summary>
    public static ITranslator? Create(
        SettingsService s,
        Func<string, string?> resolveExe,
        Func<string, string?> decryptKey,
        string? providerId = null,
        string? sourceOverride = null,
        string? targetOverride = null)
    {
        var src = sourceOverride ?? s.TranslateSourceLanguage;
        var tgt = targetOverride ?? s.TranslateTargetLanguage;
        var id = string.IsNullOrWhiteSpace(providerId) ? s.TranslationSelectedId : providerId!;

        if (string.IsNullOrWhiteSpace(id) || id == "ollama")
            return new OllamaTranslator(new OllamaOptions
            {
                Endpoint = s.OllamaEndpoint,
                Model = s.OllamaModel,
                Timeout = TimeSpan.FromSeconds(Math.Clamp(s.OllamaTimeoutSeconds, 10, 600)),
                SourceLanguage = src,
                TargetLanguage = tgt,
            });

        if (id.StartsWith("agent:", StringComparison.Ordinal))
        {
            var agentId = id["agent:".Length..];
            if (!AgentTranslator.Supports(agentId)) return null;
            var exe = resolveExe(agentId);
            return string.IsNullOrWhiteSpace(exe) ? null : new AgentTranslator(agentId, exe!, src, tgt, s.TranslationAgentModel);
        }

        var cp = s.TranslationCustomProviders.FirstOrDefault(c => c.Id == id);
        if (cp is null) return null;
        return new OpenAiCompatTranslator(new OpenAiCompatOptions
        {
            Endpoint = cp.Endpoint,
            Model = cp.Model,
            ApiKey = string.IsNullOrWhiteSpace(cp.ApiKeyEnc) ? null : decryptKey(cp.ApiKeyEnc),
            SourceLanguage = src,
            TargetLanguage = tgt,
        });
    }

    /// <summary>Is the selected provider reachable/ready? (Ollama ping, agent exe present, OpenAI /models ping.)
    /// Used to gate translation per turn so a down provider silently passes text through.</summary>
    public static async Task<bool> IsAvailableAsync(
        SettingsService s,
        Func<string, string?> resolveExe,
        Func<string, string?> decryptKey,
        CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(s.TranslationSelectedId) ? "ollama" : s.TranslationSelectedId;
        if (id == "ollama")
            return await OllamaTranslator.PingAsync(s.OllamaEndpoint, 1500, ct);
        if (id.StartsWith("agent:", StringComparison.Ordinal))
            return !string.IsNullOrWhiteSpace(resolveExe(id["agent:".Length..]));
        var cp = s.TranslationCustomProviders.FirstOrDefault(c => c.Id == id);
        if (cp is null) return false;
        return await OpenAiCompatTranslator.PingAsync(cp.Endpoint, string.IsNullOrWhiteSpace(cp.ApiKeyEnc) ? null : decryptKey(cp.ApiKeyEnc), 2500, ct);
    }
}
