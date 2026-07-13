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

    /// <summary>Raised (best-effort) when an EN→KO translation of streamed output FAILS (timeout/provider error) so
    /// the UI can surface it instead of silently showing the untranslated text. May fire more than once per turn.</summary>
    public event Action? TranslationFailed;

    /// <summary>Answers engine permission requests (approval broker). Null = auto-deny.
    /// The engine blocks until the returned task resolves, so this may wait on the user.</summary>
    public Func<PermissionRequest, Task<PermissionDecision>>? PermissionHandler { get; set; }

    public async Task RunAsync(SessionOptions options, string userPrompt, CancellationToken ct = default)
    {
        // Input: KO → EN before the engine ever sees it (this is what cuts engine tokens).
        var prompt = userPrompt;
        if (TranslationEnabled && translator is not null)
        {
            prompt = await translator.TranslateAsync(userPrompt, TranslationDirection.SourceToTarget, ct);
            if (!string.Equals(prompt, userPrompt, StringComparison.Ordinal))
                Emit(new PromptTranslated(prompt)); // 검수용: 실제 전송된 영문
        }

        // Attached documents are prepended AFTER translation: their content stays verbatim
        // (never mangled by the translator) and is delivered as plain prompt text to any engine.
        if (!string.IsNullOrEmpty(options.AttachedDocsText))
            prompt = options.AttachedDocsText + "\n\n" + prompt;

        // TTY 전용 엔진(agy): 어댑터가 ConPTY로 턴을 직접 구동 (stdio 파이프 경로 미사용)
        if (adapter is IPtyTurnRunner pty)
        {
            await pty.RunTurnAsync(executablePath, options, prompt, ev => EmitTranslatedAsync(ev, ct), ct);
            return;
        }

        var psi = adapter.BuildStartInfo(executablePath, options, prompt);
        foreach (var kv in options.ExtraEnvironment)
            psi.Environment[kv.Key] = kv.Value;
        if (!string.IsNullOrWhiteSpace(options.NativeHookSpoolDirectory))
        {
            Directory.CreateDirectory(options.NativeHookSpoolDirectory);
            psi.Environment["AGENTMANAGER_HOOK_SPOOL"] = options.NativeHookSpoolDirectory;
        }
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
        foreach (var line in adapter.InitialStdinLines(prompt, options))
            await proc.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await proc.StandardInput.FlushAsync(ct);
        if (adapter.CloseStdinAfterStart)
            proc.StandardInput.Close();

        // Safety net for a child that won't exit after the turn completes (e.g. resumed from PC sleep on a
        // dead connection): the grace timer force-kills it so the read loop below can't block forever.
        using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Inactivity watchdog: if the engine emits NO output for this long, treat the turn as stalled (e.g. the
        // child hung WITHOUT ever emitting a completion event, so the grace-kill above never gets scheduled) and
        // finalize it instead of blocking forever. The timer resets on every line, so a legitimately long-but-quiet
        // tool call (a slow build/test) is not cut off.
        var idleTimeout = options.TurnInactivityTimeout ?? DefaultTurnInactivityTimeout; // Settings-driven; Zero = disabled
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (idleTimeout > TimeSpan.Zero) idleCts.CancelAfter(idleTimeout);
        string? outLine;
        var sawCompleted = false;
        var stalled = false;
        while (true)
        {
            try { outLine = await proc.StandardOutput.ReadLineAsync(idleCts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // watchdog fired (not a user stop) → the turn stalled; kill it and let the code below synthesize a
                // completion so the turn finalizes and any output already produced is captured as the report.
                stalled = true;
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                break;
            }
            if (outLine is null) break;
            if (idleTimeout > TimeSpan.Zero) idleCts.CancelAfter(idleTimeout); // activity → reset the stall timer
            foreach (var ev in adapter.ParseLine(outLine))
            {
                if (ev is TurnCompleted) sawCompleted = true;
                // Stateful handshake (codex app-server): the adapter asks us to send a line.
                if (ev is EngineWriteback wb)
                {
                    if (stdinOpen)
                    {
                        await proc.StandardInput.WriteLineAsync(wb.Line.AsMemory(), ct);
                        await proc.StandardInput.FlushAsync(ct);
                    }
                    continue; // internal — never surfaces to translation/UI
                }

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

                if (ev is TurnCompleted)
                {
                    // A keep-open engine (Claude) waits for more stdin after the turn; close
                    // stdin on completion so it exits and the read loop ends.
                    if (stdinOpen)
                    {
                        proc.StandardInput.Close();
                        stdinOpen = false;
                    }
                    // Server-style engines (codex app-server) keep running after stdin EOF.
                    if (adapter.KillAfterTurnCompleted)
                    {
                        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                    }
                    else
                    {
                        // The turn is logically complete. If the child fails to exit promptly (e.g. resumed
                        // from PC sleep with a dead connection), the ReadLineAsync loop would block forever and
                        // the turn would stay "running" until manually stopped — force-kill after a grace period
                        // so the loop ends and the turn finalizes cleanly (its report is already captured).
                        _ = ForceKillAfterGraceAsync(proc, graceCts.Token);
                    }
                }
            }
        }

        graceCts.Cancel(); // the read loop ended (child exited on its own) → cancel any pending grace-kill
        await proc.WaitForExitAsync(ct);
        // One-shot engines (custom CLIs) print their answer and exit without a JSONL "turn_completed" line.
        // If the adapter never signaled completion, synthesize it on process exit so the turn ends cleanly.
        // Existing engines always emit TurnCompleted, so this never fires for them.
        if (!sawCompleted && !ct.IsCancellationRequested)
        {
            var isError = stalled || (proc.HasExited && proc.ExitCode != 0);
            await EmitTranslatedAsync(new TurnCompleted(null, isError, null, null), ct);
        }
        await stderrPump;
    }

    /// <summary>Default inactivity watchdog window when the caller doesn't set <see cref="SessionOptions.TurnInactivityTimeout"/>
    /// (e.g. the CLI / usage probe). No output for this long ⇒ the turn is stalled (the child hung, possibly without ever
    /// emitting a completion event); the read loop resets it on every line so a long, quiet tool call is never cut off.
    /// Users override it in Settings; <c>TimeSpan.Zero</c> disables the watchdog.</summary>
    private static readonly TimeSpan DefaultTurnInactivityTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Grace-period force-kill for a keep-open child that didn't exit after its turn completed
    /// (e.g. resumed from PC sleep on a dead connection). Cancelled via <paramref name="graceToken"/> when the
    /// child exits on its own first, so a healthy turn is never killed.</summary>
    private static async Task ForceKillAfterGraceAsync(Process proc, CancellationToken graceToken)
    {
        try
        {
            // A healthy keep-open child exits ~1-2s after stdin EOF (grace then cancels, so normal sessions are
            // untouched). Some configs (e.g. Claude Code as a worker) consistently fail to exit on the post-turn
            // stdin close — for them this grace IS the finalize latency, so keep it short. Verified: cc exits well
            // under this window when it exits at all.
            await Task.Delay(TimeSpan.FromSeconds(6), graceToken);
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch { /* cancelled (child already exited / user stop) or the process is already gone */ }
    }

    private async Task EmitTranslatedAsync(NormalizedEvent ev, CancellationToken ct)
    {
        if (TranslationEnabled && translator is not null)
        {
            switch (ev)
            {
                case AssistantText at when !string.IsNullOrWhiteSpace(at.Text):
                    var originalText = at.Text;
                    var outcome = await translator.TranslateWithOutcomeAsync(originalText, TranslationDirection.TargetToSource, ct);
                    if (outcome.Status == TranslateStatus.Failed) TranslationFailed?.Invoke();
                    ev = at with
                    {
                        Text = outcome.Text,
                        OriginalText = string.Equals(outcome.Text, originalText, StringComparison.Ordinal) ? null : originalText
                    };
                    break;
                // Only subagent (Task) results are natural language worth translating.
                case ToolResult { FromSubagent: true, IsError: false } tr when !string.IsNullOrWhiteSpace(tr.Content):
                    var originalContent = tr.Content;
                    var outcome2 = await translator.TranslateWithOutcomeAsync(originalContent, TranslationDirection.TargetToSource, ct);
                    if (outcome2.Status == TranslateStatus.Failed) TranslationFailed?.Invoke();
                    ev = tr with
                    {
                        Content = outcome2.Text,
                        OriginalContent = string.Equals(outcome2.Text, originalContent, StringComparison.Ordinal) ? null : originalContent
                    };
                    break;
            }
        }
        Emit(ev);
    }

    private void Emit(NormalizedEvent ev) => EventReceived?.Invoke(ev);
}
