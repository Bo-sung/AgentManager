using System.Text;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Session;

namespace AgentManager.Core.Translation;

/// <summary>
/// Translator backed by an ALREADY-INSTALLED coding agent (cc / gx / pi) — no extra model, endpoint, or key to
/// configure, just pick which engine. Each call runs a one-shot, read-only turn with the translation prompt and
/// collects the assistant's text. Trade-off vs a local model: it spends the agent's tokens and pays a per-call
/// process spawn, so it's an opt-in provider rather than the always-on default.
/// </summary>
public sealed class AgentTranslator(string agentId, string exePath, string sourceLanguage, string targetLanguage, string? model = null)
    : TranslatorBase(sourceLanguage, targetLanguage)
{
    /// <summary>Engines usable as translators. agy is excluded — its ConPTY text-only capture is unreliable for
    /// this (see the agy-adapter notes); cc/gx/pi return clean stdio text.</summary>
    public static readonly string[] SupportedEngines = ["cc", "gx", "pi"];

    /// <summary>Whether an engine id can back translation (installed check is the caller's).</summary>
    public static bool Supports(string agentId) => Array.IndexOf(SupportedEngines, agentId) >= 0;

    protected override async Task<string?> GenerateAsync(string prompt, CancellationToken ct)
    {
        var adapter = EngineRegistry.CreateAdapter(agentId, requireApproval: false);
        if (adapter is null || string.IsNullOrWhiteSpace(exePath)) return null;

        // A throwaway working dir; ReadOnly sandbox so the translator turn can never write or execute — it is a
        // pure text completion. BypassPermissions avoids any approval round-trip (there is nothing to approve).
        var cwd = Path.Combine(Path.GetTempPath(), "am-translate");
        try { Directory.CreateDirectory(cwd); } catch { }
        var options = new SessionOptions
        {
            WorkingDirectory = cwd,
            BypassPermissions = true,
            Sandbox = SandboxMode.ReadOnly,
            // Optional: a cheaper/faster model for translation (e.g. cc haiku) instead of the engine default.
            Model = string.IsNullOrWhiteSpace(model) ? null : model,
        };

        var sb = new StringBuilder();
        var session = new AgentSession(adapter, exePath);
        session.EventReceived += ev =>
        {
            // Collect the final assistant text (not streaming deltas / tools). The prompt asks for the translation only.
            if (ev is AssistantText { Text.Length: > 0 } at)
                sb.Append(at.Text);
        };

        try
        {
            await session.RunAsync(options, prompt, ct);
        }
        catch
        {
            return null; // base falls back to the original text
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text;
    }
}
