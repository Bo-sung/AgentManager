using AgentManager.Core.Events;

namespace AgentManager.Core.Orchestration;

/// <summary>A neutral, engine- and UI-agnostic description of one change to a session's transcript or
/// scalar run-state, produced by <see cref="TranscriptProjector"/>. The frontend applies these: the GUI
/// builds/updates WPF <c>Blocks.cs</c> items (localizing the activity markers), a CLI prints them. No
/// <c>L()</c> labels and no WPF types cross this boundary — that is the whole point of the projector.</summary>
public abstract record TranscriptDelta;

// ---- session scalar updates ----
public sealed record EngineSessionIdSet(string SessionId) : TranscriptDelta;
public sealed record TokensAdded(long Input, long Output) : TranscriptDelta;
/// <summary>Authoritative end-of-turn usage (raw turn totals). The frontend reconciles: TokensIn/Out =
/// turn-base + these (per-message usage undercounts; result.usage is the truth).</summary>
public sealed record TurnUsageSet(long Input, long Output) : TranscriptDelta;
public sealed record CostAdded(double Usd) : TranscriptDelta;
public sealed record StatusSet(string Status) : TranscriptDelta; // "done" | "error"

// ---- activity markers (neutral; frontend localizes the run-signal label) ----
public enum ActivityKind { Connected, ConnectedModel, Streaming, Receiving, Thinking, ToolRunning }
public sealed record ActivitySignal(ActivityKind Kind, string? Arg) : TranscriptDelta;

// ---- transcript items ----
public sealed record UserSentTextSet(string SentText) : TranscriptDelta;            // mutate the last user block
public sealed record AssistantStreamAppend(string Delta) : TranscriptDelta;         // ensure live block, append
public sealed record AssistantStreamReplace(string Text, string? OriginalText) : TranscriptDelta; // finalize live block
public sealed record AssistantAdd(string Text, string? OriginalText) : TranscriptDelta;           // new block (no stream)
public sealed record AssistantStreamEnd : TranscriptDelta;                          // drop live tracking, keep content
public sealed record ThinkingAdd(string Text) : TranscriptDelta;
public sealed record ToolAdd(string ToolUseId, string Kind, string Name, string? CommandText) : TranscriptDelta;
/// <summary>A tool finished. Carries the RAW content (untrimmed) — the frontend trims for the block body
/// but the test-artifact derivation needs the full output, so trimming stays frontend-side (as before).</summary>
public sealed record ToolFinished(string ToolUseId, string Content, string? OriginalContent, bool IsError) : TranscriptDelta;
public sealed record ErrorAdd(string Message, bool IsStaleSession) : TranscriptDelta;

// ---- side-effect signals (frontend reacts; a CLI may ignore) ----
public sealed record StartNativeObserver : TranscriptDelta;       // agy session start
public sealed record TaskListArtifactUpdate(string InputJson) : TranscriptDelta;  // TodoWrite
public sealed record QuotaRecorded(QuotaUpdate Quota) : TranscriptDelta;
public sealed record TotalsChanged : TranscriptDelta;
public sealed record TurnFinished(bool IsError) : TranscriptDelta; // summary artifact + attention + quick-replies + run-end

/// <summary>The event→transcript reducer lifted out of <c>AppViewModel.Run.cs:Apply</c> as a PURE state
/// machine (overhaul (a) step 4). It consumes the already-headless <see cref="NormalizedEvent"/> stream
/// and emits ordered <see cref="TranscriptDelta"/>s; it owns only the two pieces of per-session reducer
/// state that the original kept implicitly — the streaming-replace tracking (the <c>_liveText</c> trap:
/// AssistantDelta→AssistantText replace has NO correlation id, only temporal order) and the gemini
/// stderr-dump brace tracking. Everything UI-affine (the live blocks, the tool-by-id table, the last
/// user block, localized labels, attention/review/artifact actions) stays in the frontend, driven by the
/// deltas. This makes the riskiest reducer logic golden-testable without a live engine, and lets a CLI
/// reuse it. The projector is single-threaded by contract (called on the owner thread), like the original.</summary>
public sealed class TranscriptProjector
{
    private readonly HashSet<string> _streaming = new();                 // sessions with an open streaming block
    private readonly Dictionary<string, (int Depth, int Ttl)> _stderrDump = new(); // gemini multi-line stderr dump tracking

    /// <summary>Reduce one event for a session into ordered transcript deltas. <paramref name="agentId"/>
    /// gates engine-specific behaviour (agy native observer, cc stale-session flag). Returns an empty list
    /// for events that produce no transcript change (e.g. a fully-suppressed stderr line).</summary>
    public IReadOnlyList<TranscriptDelta> Project(string sessionId, string agentId, NormalizedEvent ev)
    {
        var deltas = new List<TranscriptDelta>(4);
        switch (ev)
        {
            case SessionStarted started:
                if (!string.IsNullOrWhiteSpace(started.SessionId))
                    deltas.Add(new EngineSessionIdSet(started.SessionId));
                if (agentId == "agy")
                    deltas.Add(new StartNativeObserver());
                deltas.Add(string.IsNullOrWhiteSpace(started.Model)
                    ? new ActivitySignal(ActivityKind.Connected, null)
                    : new ActivitySignal(ActivityKind.ConnectedModel, started.Model));
                break;

            case PromptTranslated pt:
                deltas.Add(new UserSentTextSet(pt.SentText));
                break;

            case AssistantDelta d:
                _streaming.Add(sessionId); // first delta opens the live block (frontend creates it)
                deltas.Add(new AssistantStreamAppend(d.Delta));
                deltas.Add(new ActivitySignal(ActivityKind.Streaming, null));
                break;

            case AssistantText at when !string.IsNullOrWhiteSpace(at.Text):
                deltas.Add(_streaming.Remove(sessionId)
                    ? new AssistantStreamReplace(at.Text, at.OriginalText)
                    : new AssistantAdd(at.Text, at.OriginalText));
                deltas.Add(new ActivitySignal(ActivityKind.Receiving, null));
                break;

            case Thinking th when !string.IsNullOrWhiteSpace(th.Text):
                deltas.Add(new ThinkingAdd(th.Text));
                deltas.Add(new ActivitySignal(ActivityKind.Thinking, null));
                break;

            case ToolUseStarted u:
                deltas.Add(new ToolAdd(u.ToolUseId, CoreHelpers.KindOf(u.Name), u.Name, CoreHelpers.ExtractCommand(u)));
                deltas.Add(new ActivitySignal(ActivityKind.ToolRunning, u.Name));
                if (u.Name == "TodoWrite")
                    deltas.Add(new TaskListArtifactUpdate(u.InputJson));
                break;

            case ToolResult r:
                deltas.Add(new ToolFinished(r.ToolUseId, r.Content, r.OriginalContent, r.IsError));
                break;

            case TokenUsage k:
                deltas.Add(new TokensAdded(k.InputTokens, k.OutputTokens));
                deltas.Add(new TotalsChanged());
                break;

            case QuotaUpdate q:
                deltas.Add(new QuotaRecorded(q));
                break;

            case EngineError e when !Suppress(sessionId, e.Message):
                var stale = agentId == "cc"
                    && e.Message.Contains("No conversation found with session ID", StringComparison.OrdinalIgnoreCase);
                deltas.Add(new ErrorAdd(e.Message, stale));
                break;

            case TurnCompleted c:
                _streaming.Remove(sessionId); // release streaming residue (keep live content if no final text arrived)
                deltas.Add(new AssistantStreamEnd());
                if (c.Usage is { } usage)
                    deltas.Add(new TurnUsageSet(usage.InputTokens, usage.OutputTokens));
                if (c.CostUsd is { } cost)
                    deltas.Add(new CostAdded(cost));
                deltas.Add(new StatusSet(c.IsError ? "error" : "done"));
                deltas.Add(new TurnFinished(c.IsError));
                deltas.Add(new TotalsChanged());
                break;
        }
        return deltas;
    }

    /// <summary>Whether to swallow a stderr line. Benign lines are dropped; gemini's multi-line xterm.js
    /// "Parsing error" dump (a JS object spanning many lines) is tracked by brace depth and eaten whole,
    /// with a TTL so a truncated dump never swallows forever. Per-session state — moved verbatim from the
    /// VM's SuppressStderr.</summary>
    private bool Suppress(string sessionId, string m)
    {
        if (CoreHelpers.IsBenignStderr(m)) return true;
        if (_stderrDump.TryGetValue(sessionId, out var st) && st.Depth > 0)
        {
            var depth = st.Depth + m.Count(c => c == '{') - m.Count(c => c == '}');
            var ttl = st.Ttl - 1;
            _stderrDump[sessionId] = (ttl <= 0 ? 0 : Math.Max(0, depth), ttl);
            return true;
        }
        if (m.Contains("xterm.js: Parsing error"))
        {
            _stderrDump[sessionId] = (Math.Max(1, m.Count(c => c == '{') - m.Count(c => c == '}')), 80);
            return true;
        }
        return false;
    }
}
