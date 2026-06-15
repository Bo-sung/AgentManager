using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    private async Task EnsureWorktreeAsync(SessionViewModel s)
    {
        if (s.WorktreeAttempted) return;
        s.WorktreeAttempted = true;
        try
        {
            var projectWorktreesRoot = Path.Combine(WorktreesRoot, s.ProjectId);
            Directory.CreateDirectory(projectWorktreesRoot);
            var wt = await GitWorktree.CreateAsync(s.ProjectPath, s.Id, s.Branch, projectWorktreesRoot);
            if (wt is not null) { s.WorktreePath = wt.Path; s.Isolated = true; }
            else if (_warnNoWorktree)
                s.Transcript.Add(new WorkingBlock(L("L.NonGitWorktreeNotice")));
        }
        catch (Exception ex)
        {
            s.Transcript.Add(new WorkingBlock(L("L.WorktreeCreateFailed", ex.Message)));
        }
    }

    private async Task SendAsync()
    {
        var s = ActiveSession;
        if (s is null || !s.CanSend) return;
        var prompt = s.Draft.Trim();
        s.Draft = "";
        var images = s.PendingImages.ToArray();
        s.PendingImages.Clear();
        await RunTurnAsync(s, prompt, images);
    }

    /// <summary>Run one engine turn for a session and stream normalized events into its transcript.</summary>
    private async Task RunTurnAsync(SessionViewModel s, string prompt, string[]? images = null)
    {
        // concurrency cap: protect the machine/quota from too many parallel engines
        if (_running.Count >= MaxConcurrentSessions)
        {
            s.Transcript.Add(new ErrorBlock(L("L.ConcurrentLimitErrorTitle"),
                L("L.ConcurrentLimitErrorBody", _running.Count, MaxConcurrentSessions)));
            return;
        }

        var dispatcher = Application.Current.Dispatcher;
        s.Transcript.Add(new UserBlock(prompt));
        s.Status = "running";
        s.MarkRunStarted(s.TranslationEnabled ? L("L.TranslatingPreparingRun") : L("L.PreparingRun"));

        var adapter = EngineRegistry.CreateAdapter(s.AgentId, s.RequireApproval);
        var exe = EngineRegistry.ResolveExe(s.AgentId, _claudePath, _codexPath);
        if (adapter is null || exe is null)
        {
            s.Transcript.Add(new ErrorBlock(L("L.EngineUnavailableTitle"), L("L.EngineUnavailableBody", s.AgentName)));
            s.Status = "error";
            s.MarkRunEnded(L("L.EngineUnavailableActivity"));
            return;
        }

        // Worktree isolation: each session works in its own git worktree.
        s.Activity = L("L.PreparingWorktree");
        await EnsureWorktreeAsync(s);
        var cwd = s.WorktreePath ?? s.ProjectPath;

        var tools = new Dictionary<string, ToolBlock>();
        var session = new AgentSession(adapter, exe, _translator, s.TranslationEnabled);
        session.EventReceived += ev => dispatcher.Invoke(() => Apply(s, ev, tools));
        if (s.RequireApproval)
            session.PermissionHandler = pr => HandlePermissionAsync(s, pr);

        // turn baseline for end-of-turn usage reconciliation
        s.TurnBaseIn = s.TokensIn;
        s.TurnBaseOut = s.TokensOut;

        var sessionProject = Projects.FirstOrDefault(p => p.Id == s.ProjectId);
        var mcpPath = sessionProject?.McpConfigPath;
        var nativeHookSpoolDirectory = NativeHookSpoolDirectoryFor(s);
        var options = new SessionOptions
        {
            WorkingDirectory = cwd,
            BypassPermissions = !s.RequireApproval, // Stage 1: Claude stdio approvals; Codex falls to sandbox
            Sandbox = s.Sandbox,
            ResumeSessionId = s.EngineSessionId,
            Model = string.IsNullOrWhiteSpace(s.Model) ? null : s.Model,
            McpConfigPath = string.IsNullOrWhiteSpace(mcpPath) ? null : mcpPath,
            Images = images ?? [],
            AdditionalDirectories = sessionProject?.ExtraPaths.ToArray() ?? [],
            ReasoningEffort = string.IsNullOrWhiteSpace(s.ReasoningEffort) ? null : s.ReasoningEffort,
            ExtraEnvironment = ApiEnvFor(s.AgentId),
            NativeHookSpoolDirectory = nativeHookSpoolDirectory,
            NativeHookCommand = s.AgentId is "gx" or "cc" && nativeHookSpoolDirectory is not null
                ? NativeHookCommandFactory.WindowsPowerShellSpoolScript(nativeHookSpoolDirectory)
                : null,
            BypassHookTrust = s.AgentId == "gx",
        };
        var cts = new CancellationTokenSource();
        _running[s.Id] = cts;
        try
        {
            s.NativeWorkItems.Clear();
            ClearNativeHookSpool(options.NativeHookSpoolDirectory);
            await StartNativeObserverAsync(s, options);
            s.Activity = s.TranslationEnabled ? L("L.TranslatingStartingEngine") : L("L.StartingEngine");
            await Task.Run(() => session.RunAsync(options, prompt, cts.Token), cts.Token);
            if (s.Status == "running") s.Status = "done";
            if (s.Status == "done") s.MarkRunEnded(L("L.Completed"));
        }
        catch (OperationCanceledException)
        {
            s.Transcript.Add(new WorkingBlock(L("L.StoppedBlock")));
            s.Status = "idle";
            s.MarkRunEnded(L("L.Stopped"));
        }
        catch (Exception ex)
        {
            s.Transcript.Add(new ErrorBlock(L("L.RunFailed"), ex.Message));
            s.Status = "error";
            s.MarkRunEnded(L("L.Failed"));
        }
        finally
        {
            await StopNativeObserverAsync(s);
            ExpirePendingApprovals(s);
            await RefreshReviewAsync(s);
            SaveState();
            _running.Remove(s.Id);
            cts.Dispose();
        }
    }

    private static string? NativeHookSpoolDirectoryFor(SessionViewModel s)
    {
        if (s.AgentId is not ("gx" or "cc")) return null;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AgentManager",
            "native-hooks");
        return Path.Combine(root, SafeFileName(s.Id));
    }

    private static string SafeFileName(string value)
        => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static void ClearNativeHookSpool(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*.json"))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private async Task StartNativeObserverAsync(SessionViewModel s, SessionOptions? options = null)
    {
        if (_nativeObservers.ContainsKey(s.Id)) return;

        INativeWorkObserver? observer = null;
        if (s.AgentId is "gx" or "cc" && (options?.NativeHookSpoolDirectory ?? NativeHookSpoolDirectoryFor(s)) is { } spool)
            observer = new HookSpoolNativeWorkObserver(s.AgentId, spool);
        else if (s.AgentId == "agy" && !string.IsNullOrWhiteSpace(s.EngineSessionId))
            observer = new AgyNativeWorkObserver();

        if (observer is null) return;
        observer.WorkItemChanged += (_, item) =>
            Application.Current.Dispatcher.Invoke(() => UpsertNativeWorkItem(s, item));
        _nativeObservers[s.Id] = observer;

        await observer.StartAsync(new NativeWorkObservationTarget(
            EngineId: s.AgentId,
            ParentSessionId: s.Id,
            WorkingDirectory: s.WorktreePath ?? s.ProjectPath,
            EngineSessionId: s.EngineSessionId,
            ManagedByAgentManager: true));

        foreach (var item in await observer.SnapshotAsync())
            UpsertNativeWorkItem(s, item);
    }

    private async Task StopNativeObserverAsync(SessionViewModel s)
    {
        if (!_nativeObservers.Remove(s.Id, out var observer)) return;
        await observer.DisposeAsync();
    }

    private static void UpsertNativeWorkItem(SessionViewModel s, ObservedWorkItem item)
    {
        var existing = s.NativeWorkItems.FirstOrDefault(x => x.Id == item.Id);
        if (existing is null)
            s.NativeWorkItems.Insert(0, new NativeWorkItemViewModel(item));
        else
            existing.Update(item);
    }

    public async Task SelectReviewChangeAsync(ReviewChangeViewModel? change)
    {
        var s = ActiveSession;
        if (s is null) return;
        await LoadReviewDiffAsync(s, change);
    }

    private async Task LoadReviewDiffAsync(SessionViewModel s, ReviewChangeViewModel? change, bool quiet = false)
    {
        s.SelectedChange = change;
        if (change is null || string.IsNullOrWhiteSpace(s.WorktreePath))
        {
            s.DiffText = L("L.SelectDiffPrompt");
            return;
        }

        if (!quiet) s.DiffText = L("L.LoadingDiff");
        try
        {
            var diff = await GitWorktree.GetDiffAsync(s.WorktreePath, change.Path);
            var text = string.IsNullOrWhiteSpace(diff) ? L("L.NoTextualDiff") : diff;
            if (s.DiffText != text) s.DiffText = text;
            if (!quiet) SaveState();
        }
        catch (Exception ex)
        {
            s.DiffText = L("L.DiffFailed", ex.Message);
        }
    }

    private readonly HashSet<string> _scannedSessionDiffIds = [];

    private async Task ScanSessionDiffsBackgroundAsync(List<SessionViewModel> sessions)
    {
        foreach (var s in sessions)
        {
            if (s.WorktreePath == null || s.IsRunning) continue;

            lock (_scannedSessionDiffIds)
            {
                if (_scannedSessionDiffIds.Contains(s.Id)) continue;
                _scannedSessionDiffIds.Add(s.Id);
            }

            try
            {
                var changes = await GitWorktree.GetChangedFilesAsync(s.WorktreePath);
                int added = 0;
                int deleted = 0;
                foreach (var c in changes)
                {
                    added += c.Added;
                    deleted += c.Deleted;
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    s.DiffAdded = added;
                    s.DiffRemoved = deleted;
                    s.DiffFiles = changes.Count;
                });
            }
            catch
            {
                lock (_scannedSessionDiffIds)
                {
                    _scannedSessionDiffIds.Remove(s.Id);
                }
            }
        }
    }

    /// <summary>실행 중 라이브 갱신: 툴이 끝날 때마다 호출되며, 버스트를 디바운스(0.8s)해
    /// 활성 세션의 Review pane을 갱신한다 (git status는 가볍지만 연타 방지).</summary>
    private bool _liveReviewQueued;
    private async Task QueueLiveReviewRefreshAsync(SessionViewModel s)
    {
        if (_liveReviewQueued || !ReferenceEquals(s, ActiveSession) || s.WorktreePath is null) return;
        _liveReviewQueued = true;
        try
        {
            await Task.Delay(800);
            if (ReferenceEquals(s, ActiveSession))
                await RefreshReviewAsync(s, quiet: true);
        }
        finally { _liveReviewQueued = false; }
    }

    private async Task RefreshReviewAsync(SessionViewModel? s, bool quiet = false)
    {
        if (s is null) return;
        if (string.IsNullOrWhiteSpace(s.WorktreePath))
        {
            s.Changes.Clear();
            s.SelectedChange = null;
            s.DiffText = L("L.SessionWorktreeMissing");
            s.ReviewStatus = L("L.NoIsolatedWorktree");
            return;
        }

        if (!quiet) s.ReviewStatus = L("L.ScanningChanges");
        try
        {
            var selectedPath = s.SelectedChange?.Path; // keep the user's selection across live refreshes
            var changes = await GitWorktree.GetChangedFilesAsync(s.WorktreePath);

            int added = 0;
            int deleted = 0;
            foreach (var c in changes)
            {
                added += c.Added;
                deleted += c.Deleted;
            }
            s.DiffAdded = added;
            s.DiffRemoved = deleted;
            s.DiffFiles = changes.Count;
            lock (_scannedSessionDiffIds)
            {
                _scannedSessionDiffIds.Add(s.Id);
            }

            // rebuild the list only when it actually changed, so live refreshes don't flicker
            var same = s.Changes.Count == changes.Count;
            for (var i = 0; same && i < changes.Count; i++)
                same = s.Changes[i].Path == changes[i].Path && s.Changes[i].Kind == changes[i].Kind
                    && s.Changes[i].Added == changes[i].Added && s.Changes[i].Deleted == changes[i].Deleted;
            if (!same)
            {
                s.Changes.Clear();
                foreach (var change in changes)
                    s.Changes.Add(new ReviewChangeViewModel(change));
            }

            s.ReviewStatus = changes.Count == 0 ? L("L.NoChanges") : L("L.ChangedFiles", changes.Count);
            if (s.Changes.Count > 0)
            {
                var keep = selectedPath is null ? null : s.Changes.FirstOrDefault(c => c.Path == selectedPath);
                await LoadReviewDiffAsync(s, keep ?? s.Changes[0], quiet);
            }
            else
            {
                s.SelectedChange = null;
                s.DiffText = L("L.NoChangesInWorktree");
            }
        }
        catch (Exception ex)
        {
            s.ReviewStatus = L("L.ReviewRefreshFailed");
            s.DiffText = ex.Message;
        }
    }

    private async Task MergeReviewAsync(SessionViewModel? s)
    {
        if (s?.WorktreePath is null) return;
        s.ReviewStatus = L("L.Merging");
        var (ok, msg) = await GitWorktree.MergeAsync(s.ProjectPath, s.Branch, $"agent: {s.Title}", s.WorktreePath);
        if (ok)
        {
            await GitWorktree.RemoveAsync(s.ProjectPath, s.WorktreePath);
            s.WorktreePath = null;
            s.Isolated = false;
            s.Status = "done";
            s.Activity = L("L.Merged");
            s.Transcript.Add(new WorkingBlock("✓ " + msg));
        }
        else
        {
            s.Transcript.Add(new ErrorBlock(L("L.MergeFailed"), msg));
        }
        s.ReviewStatus = msg;
        await RefreshReviewAsync(s);
        SaveState();
    }

    private async Task DiscardReviewAsync(SessionViewModel? s)
    {
        if (s?.WorktreePath is null) return;
        s.ReviewStatus = L("L.Discarding");
        var (ok, msg) = await GitWorktree.DiscardAsync(s.WorktreePath);
        s.Transcript.Add(ok ? new WorkingBlock("↩ " + msg) : (TranscriptItem)new ErrorBlock(L("L.DiscardFailed"), msg));
        await RefreshReviewAsync(s);
        SaveState();
    }

    private void Apply(SessionViewModel s, NormalizedEvent ev, Dictionary<string, ToolBlock> tools)
    {
        s.MarkRunSignal();
        switch (ev)
        {
            case SessionStarted started:
                if (!string.IsNullOrWhiteSpace(started.SessionId))
                    s.EngineSessionId = started.SessionId;
                if (s.AgentId == "agy")
                    _ = StartNativeObserverAsync(s);
                if (!string.IsNullOrWhiteSpace(started.Model))
                    s.MarkRunSignal(L("L.ConnectedModel", started.Model));
                else
                    s.MarkRunSignal(L("L.Connected"));
                break;
            case PromptTranslated pt:
                if (s.Transcript.OfType<UserBlock>().LastOrDefault() is { } ub)
                    ub.SentText = pt.SentText;
                break;
            case AssistantDelta d:
                // 스트리밍: 라이브 블록에 즉시 덧붙이고, 최종 AssistantText(번역본)가 오면 교체
                if (!_liveText.TryGetValue(s.Id, out var live))
                {
                    live = new AgentTextBlock("") { ModelUsed = s.Model };
                    _liveText[s.Id] = live;
                    s.Transcript.Add(live);
                }
                live.Text += d.Delta;
                s.Activity = L("L.StreamingResponse");
                break;
            case AssistantText at when !string.IsNullOrWhiteSpace(at.Text):
                if (_liveText.Remove(s.Id, out var streamed))
                {
                    streamed.Text = at.Text;
                    streamed.OriginalText = at.OriginalText;
                }
                else
                    s.Transcript.Add(new AgentTextBlock(at.Text) { OriginalText = at.OriginalText, ModelUsed = s.Model });
                s.Activity = L("L.ReceivingResponse");
                break;
            case Thinking th when !string.IsNullOrWhiteSpace(th.Text):
                s.Transcript.Add(new ThinkingBlock(th.Text));
                s.Activity = L("L.ThinkingActivity");
                break;
            case ToolUseStarted u:
                var tb = new ToolBlock(u.ToolUseId, KindOf(u.Name), u.Name) { CommandText = ExtractCommand(u) };
                tools[u.ToolUseId] = tb;
                s.Transcript.Add(tb);
                s.MarkRunSignal(L("L.ToolRunning", u.Name));
                if (u.Name == "TodoWrite")
                    UpsertTaskListArtifact(s, u.InputJson);
                break;
            case ToolResult r:
                if (tools.TryGetValue(r.ToolUseId, out var t))
                {
                    t.Body = Trim(r.Content, 2000);
                    t.OriginalBody = r.OriginalContent is null ? null : Trim(r.OriginalContent, 2000);
                    t.Stat = r.IsError ? L("L.ToolError") : L("L.ToolDone");
                    if (t.CommandText is { } cmd && IsTestCommand(cmd))
                        UpsertTestArtifact(s, cmd, r.Content, r.IsError);
                }
                else
                {
                    s.Transcript.Add(new ToolBlock(r.ToolUseId, "RUN", L("L.Result"))
                    {
                        Body = Trim(r.Content, 2000),
                        OriginalBody = r.OriginalContent is null ? null : Trim(r.OriginalContent, 2000),
                        Stat = r.IsError ? L("L.ToolError") : L("L.ToolDone")
                    });
                }
                // live review: a finished tool may have changed files in the worktree
                _ = QueueLiveReviewRefreshAsync(s);
                break;
            case TokenUsage k:
                // live accumulation; TurnCompleted.Usage reconciles to the turn total
                s.TokensIn += k.InputTokens;
                s.TokensOut += k.OutputTokens;
                RefreshTotals();
                break;
            case QuotaUpdate q:
                RecordUsage(s.AgentId, q);
                break;
            case EngineError e when !SuppressStderr(s, e.Message):
                s.Transcript.Add(new ErrorBlock(L("L.Stderr"), e.Message));
                break;
            case TurnCompleted c:
                _liveText.Remove(s.Id); // 스트리밍 잔여 해제 (최종 텍스트 미도착 시 라이브 내용 그대로 유지)
                if (c.Usage is { } turnUsage)
                {
                    // reconcile: per-message usage undercounts (esp. output); result.usage is the turn total
                    s.TokensIn = s.TurnBaseIn + turnUsage.InputTokens;
                    s.TokensOut = s.TurnBaseOut + turnUsage.OutputTokens;
                }
                if (c.CostUsd is { } turnCost) s.CostUsd += turnCost;
                UpsertSummaryArtifact(s);
                AttentionRequested?.Invoke(c.IsError ? "error" : "done", s);
                s.Status = c.IsError ? "error" : "done";
                s.MarkRunEnded(c.IsError ? L("L.Failed") : L("L.Completed"));
                RefreshTotals();
                break;
        }
        SaveState();
    }

}
