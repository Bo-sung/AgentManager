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
        if (s.WorktreeOptOut) return; // 사용자가 격리 끔 — 프로젝트 루트 공유(cwd = ProjectPath)
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

    private RelayCommand? _choiceActivateCommand;
    /// <summary>Click/Enter on an option — single-select picks (record + advance/send); multi-select
    /// toggles the checkbox.</summary>
    public RelayCommand ChoiceActivateCommand => _choiceActivateCommand ??=
        new RelayCommand(p => { if (p is ChoiceOption o) ActivateChoice(o); });

    public void ActivateChoice(ChoiceOption o)
    {
        if (ActiveSession is not { } s || s.ActiveChoice is not { } flow) return;
        if (flow.Multi) { o.IsSelected = !o.IsSelected; flow.Current.RaiseSelection(); }
        else AnswerCurrentChoice(s, flow, o.Text);
    }

    private RelayCommand? _choiceSubmitCommand;
    /// <summary>Submit a multi-select page — sends the checked options (comma-joined).</summary>
    public RelayCommand ChoiceSubmitCommand => _choiceSubmitCommand ??=
        new RelayCommand(_ =>
        {
            if (ActiveSession is not { } s || s.ActiveChoice is not { } flow || !flow.Current.HasSelection) return;
            AnswerCurrentChoice(s, flow, string.Join(", ", flow.Current.Options.Where(o => o.IsSelected).Select(o => o.Text)));
        });

    private RelayCommand? _choicePrevCommand;
    /// <summary>Pager: back to the previous question (review/change an answer).</summary>
    public RelayCommand ChoicePrevCommand => _choicePrevCommand ??=
        new RelayCommand(_ => { if (ActiveSession?.ActiveChoice is { HasPrev: true } flow) flow.Page--; });

    private RelayCommand? _dismissChoiceCommand;
    /// <summary>"직접 입력"/건너뛰기 — 선택지를 지워 입력창 복귀(자유 응답).</summary>
    public RelayCommand DismissChoiceCommand => _dismissChoiceCommand ??=
        new RelayCommand(_ => { if (ActiveSession is { } s) s.ActiveChoice = null; });

    /// <summary>Record the current page's answer, advance to the next page, or — on the last page —
    /// send the whole flow as one turn.</summary>
    private void AnswerCurrentChoice(SessionViewModel s, ChoiceFlow flow, string answer)
    {
        flow.Current.Answer = answer;
        if (!flow.IsLast) { flow.Page++; return; }
        var msg = flow.BuildMessage();
        s.ActiveChoice = null;
        if (!s.IsRunning) _ = RunTurnAsync(s, msg); // RunTurnAsync clears ActiveChoice at start
    }

    /// <summary>Heuristic fallback: parse the last assistant message for A/B/1/2 choices into a single
    /// single-select flow. Structured (ask-user skill) choices take precedence and aren't overwritten.</summary>
    private void PopulateQuickReplies(SessionViewModel s)
    {
        if (s.ActiveChoice is { Structured: true }) return; // 스킬 선택지가 패널을 소유 — 휴리스틱이 덮지 않음
        s.ActiveChoice = null;
        if (s.Status == "error") return;
        var last = s.Transcript.OfType<AgentTextBlock>().LastOrDefault();
        if (last is null) return;
        var parsed = Core.QuickReplyParser.Parse(last.Text).ToList();
        if (parsed.Count == 0) return;
        var item = new ChoiceItem { Question = null, Multi = false };
        foreach (var o in parsed) item.Options.Add(new ChoiceOption(o.Marker, o.Label, o.Text));
        s.ActiveChoice = new ChoiceFlow { Items = [item], Structured = false };
    }

    private RelayCommand? _retranslateCommand;
    /// <summary>Re-translate a single assistant message on demand (translation off/odd/missing).</summary>
    public RelayCommand RetranslateCommand => _retranslateCommand ??=
        new RelayCommand(p => { if (p is AgentTextBlock b) _ = RetranslateAsync(b); },
                         p => p is AgentTextBlock { IsRetranslating: false });

    private async Task RetranslateAsync(AgentTextBlock block)
    {
        var s = ActiveSession;
        if (s is null || block.IsRetranslating) return;
        var source = block.TranslationSource;
        if (string.IsNullOrWhiteSpace(source)) return;

        // Use the session's language pair (workers pin their own) or the global translator.
        var translator = s.TranslateSourceLanguage is { } src && s.TranslateTargetLanguage is { } tgt
            ? CreateTranslator(_ollamaEndpoint, _ollamaModel, src, tgt)
            : _translator;
        if (translator is null) return;
        if (!await OllamaTranslator.PingAsync(_ollamaEndpoint, 1500)) return; // Ollama down — composer ⚠ already flags it

        block.IsRetranslating = true;
        try
        {
            var translated = await translator.TranslateAsync(source, TranslationDirection.TargetToSource);
            Application.Current.Dispatcher.Invoke(() =>
            {
                block.OriginalText = string.Equals(translated, source, StringComparison.Ordinal) ? null : source;
                block.Text = translated;
                block.ShowOriginal = false;
            });
            SaveState();
        }
        catch { /* leave the message unchanged on failure */ }
        finally { block.IsRetranslating = false; }
    }

    private async Task SendAsync()
    {
        var s = ActiveSession;
        if (s is null || !s.CanSend) return;
        var prompt = s.Draft.Trim();
        s.Draft = "";
        var images = s.PendingAttachments.Where(a => a.IsImage).Select(a => a.Path).ToArray();
        var docs = s.PendingAttachments.Where(a => !a.IsImage).Select(a => a.Path).ToArray();
        s.PendingAttachments.Clear();
        await RunTurnAsync(s, prompt, images, docs);
    }

    /// <summary>Run one engine turn for a session and stream normalized events into its transcript.</summary>
    private async Task RunTurnAsync(SessionViewModel s, string prompt, string[]? images = null, string[]? docs = null)
    {
        // concurrency cap: 워커와 일반 세션은 별도 cap을 소비(워커 위임이 메인 슬롯을 굶기지 않도록)
        var cap = s.IsWorker ? MaxConcurrentWorkers : MaxConcurrentSessions;
        var runningSameKind = _allSessions.Count(x => x.IsWorker == s.IsWorker && _running.ContainsKey(x.Id));
        if (runningSameKind >= cap)
        {
            s.Transcript.Add(new ErrorBlock(L("L.ConcurrentLimitErrorTitle"),
                L("L.ConcurrentLimitErrorBody", runningSameKind, cap)));
            return;
        }

        var dispatcher = Application.Current.Dispatcher;
        s.ActiveChoice = null; // a new turn supersedes any pending choice
        var attachmentPaths = (images ?? []).Concat(docs ?? []).ToArray();
        s.Transcript.Add(new UserBlock(attachmentPaths.Length > 0 ? Attachments.DisplayNote(prompt, attachmentPaths) : prompt));
        s.Status = "running";
        s.MarkRunStarted(s.TranslationEnabled ? L("L.TranslatingPreparingRun") : L("L.PreparingRun"));

        var adapter = EngineRegistry.CreateAdapter(s.AgentId, s.RequireApproval);
        var exe = EngineRegistry.ResolveExe(s.AgentId, _claudePath, _codexPath, _agyPath, _piPath);
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
        WatchSessionTaskSpool(cwd, s.ProjectId, s.Id); // skill fallback (.am/worker-tasks/) → backlog; report back to s
        WatchSessionAskSpool(cwd, s.Id);               // ask-user skill (.am/ask/) → structured choice panel

        var tools = new Dictionary<string, ToolBlock>();
        // 워커는 생성 시 고정된 번역 언어쌍을 사용(일반 세션은 전역 번역기).
        var translator = s.TranslateSourceLanguage is { } src && s.TranslateTargetLanguage is { } tgt
            ? CreateTranslator(_ollamaEndpoint, _ollamaModel, src, tgt)
            : _translator;
        // 번역 ON 가능 조건: 유저가 켬 && Ollama 활성. 다운이면 번역 레이어 없이 패스스루.
        var translateOn = s.TranslationEnabled && await Core.Translation.OllamaTranslator.PingAsync(_ollamaEndpoint, 1500);
        var session = new AgentSession(adapter, exe, translator, translateOn);
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
            AttachedDocsText = Attachments.BuildDocsText(docs),
            AdditionalDirectories = sessionProject?.ExtraPaths.ToArray() ?? [],
            ReasoningEffort = string.IsNullOrWhiteSpace(s.ReasoningEffort) ? null : s.ReasoningEffort,
            ExtraEnvironment = WithTaskSpoolEnv(ApiEnvFor(s.AgentId), Path.Combine(cwd, ".am", "worker-tasks", s.Id), Path.Combine(cwd, ".am", "ask", s.Id)),
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
            // 실제 rate-limit 실패면 해당 엔진을 소진으로 기록(리셋 전까지 회색/자동전환 트리거)
            if (LooksRateLimited(ex.Message))
                MarkRateLimited(s.AgentId, _usage.TryGetValue(s.AgentId, out var snap) ? snap.ResetsAtUnix : 0);
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

    /// <summary>에러 메시지가 rate-limit/사용량 한도로 보이는가(소진 기록 트리거).</summary>
    private static bool LooksRateLimited(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        var m = msg.ToLowerInvariant();
        return m.Contains("rate limit") || m.Contains("rate_limit") || m.Contains("ratelimit")
            || m.Contains("usage limit") || m.Contains("quota") || m.Contains("429")
            || m.Contains("too many requests") || m.Contains("limit reached") || m.Contains("limit exceeded");
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
        {
            var hook = new HookSpoolNativeWorkObserver(s.AgentId, spool);
            // cc: 훅(in-session subagent)에 더해 `claude agents --json` 폴러로 같은 트리의 별도 세션도 관측.
            observer = s.AgentId == "cc" && EngineRegistry.ResolveExe("cc", _claudePath, _codexPath) is { } ccExe
                ? new CompositeNativeWorkObserver("cc", hook, new ClaudeBackgroundSessionObserver(ccExe))
                : hook;
        }
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

    private void UpsertNativeWorkItem(SessionViewModel s, ObservedWorkItem item)
    {
        // Don't surface AgentManager's OWN managed sessions (peer workers / orchestrator running in the
        // same project tree) as this session's native background work — they're peers, not subagents of s.
        // The poller (Core) only knows to skip its own session id; the live managed-session list is UI state,
        // safely read here on the UI thread.
        if (item.Kind == WorkItemKind.NativeBackgroundSession
            && item.VendorWorkId is { Length: > 0 } vid
            && _allSessions.Any(x => string.Equals(x.EngineSessionId, vid, StringComparison.OrdinalIgnoreCase)))
            return;

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
                // claude can't --resume a conversation that no longer exists → the session is dead.
                // Flag it (cc only for now; other engines have different signatures) so the error
                // block can offer a delete action. Don't stack the prompt on repeated stderr lines.
                var staleSession = s.AgentId == "cc"
                    && e.Message.Contains("No conversation found with session ID", StringComparison.OrdinalIgnoreCase);
                if (staleSession && s.Transcript.LastOrDefault() is ErrorBlock { IsStaleSession: true }) break;
                s.Transcript.Add(new ErrorBlock(L("L.Stderr"), e.Message) { IsStaleSession = staleSession });
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
                PopulateQuickReplies(s);
                RefreshTotals();
                break;
        }
        SaveState();
    }

}
