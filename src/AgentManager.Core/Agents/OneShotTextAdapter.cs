using System.Diagnostics;
using AgentManager.Core.Events;

namespace AgentManager.Core.Agents;

/// <summary>
/// Adapter for a simple "one-shot" custom CLI: it takes the prompt (as an argument via the <c>{prompt}</c>
/// placeholder), prints its answer to stdout as plain text, and exits. There is no JSONL protocol — each stdout
/// line becomes an <see cref="AssistantDelta"/>, and <see cref="Session.AgentSession"/> synthesizes
/// <see cref="TurnCompleted"/> on process exit (a non-zero exit code marks it an error).
/// The executable comes from the engine's resolved path; the argument template comes from the manifest's launch args.
/// Args are always passed as a list (never a shell string). Supported placeholders: <c>{prompt}</c>, <c>{model}</c>, <c>{cwd}</c>.
/// </summary>
public sealed class OneShotTextAdapter(string engineId, IReadOnlyList<string> argsTemplate) : IAgentAdapter
{
    private bool _started;

    public string Id => engineId;
    public AgentCapabilities Capabilities { get; } = new(Permissions: false, Thinking: false, Sessions: false, Images: false, TokenUsage: false, Quota: false);
    public bool CloseStdinAfterStart => true; // one-shot: the prompt is in the args, so close stdin so it doesn't wait

    public ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = AdapterJson.NewStdioStartInfo(executablePath, options.WorkingDirectory);
        foreach (var arg in argsTemplate ?? [])
            psi.ArgumentList.Add(Substitute(arg, prompt, options));
        return psi;
    }

    public IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options) => [];

    public IEnumerable<NormalizedEvent> ParseLine(string line)
    {
        if (!_started)
        {
            _started = true;
            yield return new SessionStarted(engineId, null, 0, null);
        }
        yield return new AssistantDelta(line + "\n");
    }

    private static string Substitute(string arg, string prompt, SessionOptions options) => arg
        .Replace("{prompt}", prompt)
        .Replace("{model}", options.Model ?? "")
        .Replace("{cwd}", options.WorkingDirectory);
}
