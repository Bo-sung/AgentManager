using System.Diagnostics;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Translation;

namespace AgentManager.Core.Session;

/// <summary>
/// Owns one engine process: spawns it via an adapter, applies the translation layer
/// (KO→EN on input, EN→KO on assistant text / subagent results), and emits normalized
/// events. Multiple sessions can run in parallel — each its own process + state.
/// </summary>
public sealed class AgentSession(
    IAgentAdapter adapter,
    string executablePath,
    ITranslator? translator = null,
    bool translationEnabled = false)
{
    public IAgentAdapter Adapter => adapter;
    public bool TranslationEnabled { get; set; } = translationEnabled;

    /// <summary>Raised for every normalized event (translation already applied).</summary>
    public event Action<NormalizedEvent>? EventReceived;

    public async Task RunAsync(SessionOptions options, string userPrompt, CancellationToken ct = default)
    {
        // Input: KO → EN before the engine ever sees it (this is what cuts engine tokens).
        var prompt = userPrompt;
        if (TranslationEnabled && translator is not null)
            prompt = await translator.TranslateAsync(userPrompt, TranslationDirection.KoToEn, ct);

        var psi = adapter.BuildStartInfo(executablePath, options, prompt);
        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Drain stderr → EngineError
        var stderrPump = Task.Run(async () =>
        {
            string? l;
            while ((l = await proc.StandardError.ReadLineAsync(ct)) is not null)
                if (!string.IsNullOrWhiteSpace(l)) Emit(new EngineError(l));
        }, ct);

        bool stdinOpen = !adapter.CloseStdinAfterStart;
        foreach (var line in adapter.InitialStdinLines(prompt))
            await proc.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await proc.StandardInput.FlushAsync(ct);
        if (adapter.CloseStdinAfterStart)
            proc.StandardInput.Close();

        string? outLine;
        while ((outLine = await proc.StandardOutput.ReadLineAsync(ct)) is not null)
            foreach (var ev in adapter.ParseLine(outLine))
            {
                await EmitTranslatedAsync(ev, ct);
                // A keep-open engine (Claude) waits for more stdin after the turn; close
                // stdin on completion so it exits and the read loop ends.
                if (ev is TurnCompleted && stdinOpen)
                {
                    proc.StandardInput.Close();
                    stdinOpen = false;
                }
            }

        await proc.WaitForExitAsync(ct);
        await stderrPump;
    }

    private async Task EmitTranslatedAsync(NormalizedEvent ev, CancellationToken ct)
    {
        if (TranslationEnabled && translator is not null)
        {
            switch (ev)
            {
                case AssistantText at when !string.IsNullOrWhiteSpace(at.Text):
                    ev = at with { Text = await translator.TranslateAsync(at.Text, TranslationDirection.EnToKo, ct) };
                    break;
                // Only subagent (Task) results are natural language worth translating.
                case ToolResult { FromSubagent: true, IsError: false } tr when !string.IsNullOrWhiteSpace(tr.Content):
                    ev = tr with { Content = await translator.TranslateAsync(tr.Content, TranslationDirection.EnToKo, ct) };
                    break;
            }
        }
        Emit(ev);
    }

    private void Emit(NormalizedEvent ev) => EventReceived?.Invoke(ev);
}
