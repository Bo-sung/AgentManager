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

    /// <summary>Answers engine permission requests (approval broker). Null = auto-deny.
    /// The engine blocks until the returned task resolves, so this may wait on the user.</summary>
    public Func<PermissionRequest, Task<PermissionDecision>>? PermissionHandler { get; set; }

    public async Task RunAsync(SessionOptions options, string userPrompt, CancellationToken ct = default)
    {
        // Input: KO → EN before the engine ever sees it (this is what cuts engine tokens).
        var prompt = userPrompt;
        if (TranslationEnabled && translator is not null)
            prompt = await translator.TranslateAsync(userPrompt, TranslationDirection.KoToEn, ct);

        var psi = adapter.BuildStartInfo(executablePath, options, prompt);
        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Stop support: killing the process ends the read loop and the turn.
        using var killReg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

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

                // Approval round-trip: the engine blocks until we answer on stdin.
                if (ev is PermissionRequest pr && stdinOpen)
                {
                    var decision = PermissionHandler is not null
                        ? await PermissionHandler(pr)
                        : new PermissionDecision(false, "No approval handler — auto-denied");
                    var response = adapter.BuildPermissionResponse(pr, decision);
                    if (response is not null)
                    {
                        await proc.StandardInput.WriteLineAsync(response.AsMemory(), ct);
                        await proc.StandardInput.FlushAsync(ct);
                    }
                }

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
                    var originalText = at.Text;
                    var translatedText = await translator.TranslateAsync(originalText, TranslationDirection.EnToKo, ct);
                    ev = at with
                    {
                        Text = translatedText,
                        OriginalText = string.Equals(translatedText, originalText, StringComparison.Ordinal) ? null : originalText
                    };
                    break;
                // Only subagent (Task) results are natural language worth translating.
                case ToolResult { FromSubagent: true, IsError: false } tr when !string.IsNullOrWhiteSpace(tr.Content):
                    var originalContent = tr.Content;
                    var translatedContent = await translator.TranslateAsync(originalContent, TranslationDirection.EnToKo, ct);
                    ev = tr with
                    {
                        Content = translatedContent,
                        OriginalContent = string.Equals(translatedContent, originalContent, StringComparison.Ordinal) ? null : originalContent
                    };
                    break;
            }
        }
        Emit(ev);
    }

    private void Emit(NormalizedEvent ev) => EventReceived?.Invoke(ev);
}
