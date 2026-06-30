using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Orchestration;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    /// <summary>Render attachment docs into prompt text. Text/code docs are inlined as fenced
    /// blocks (verbatim, never translated). Binary office/PDF docs are copied into
    /// <c>&lt;cwd&gt;/.am/attachments/</c> and referenced by absolute path so the agent reads them
    /// with its own tools. agy (ConPTY single-shot) and any copy failure get a transcript warning
    /// but still attach by path.</summary>
    private string BuildAttachedDocsText(SessionViewModel s, string cwd, string[]? docs)
    {
        if (docs is null || docs.Length == 0) return "";
        // Small text/code docs are inlined verbatim; binary office/PDF docs AND large text docs go by PATH
        // PASS-THROUGH (a big inlined block bloats every turn + stalls cc's stream-json init — see PassThrough).
        List<string> inlineDocs = [], passDocs = [];
        foreach (var d in docs)
            (Attachments.PassThrough(d) ? passDocs : inlineDocs).Add(d);

        var sb = new StringBuilder();
        var inline = Attachments.BuildDocsText(inlineDocs);
        if (inline.Length > 0) sb.Append(inline).Append("\n\n");

        if (passDocs.Count > 0)
        {
            var isAgy = s.AgentId == "agy";
            foreach (var doc in passDocs)
            {
                var (refPath, ok) = Attachments.CopyToAttachmentsDir(doc, cwd);
                sb.Append(Attachments.BuildAttachedRef(refPath)).Append('\n');
                if (isAgy)
                    s.Transcript.Add(new WorkingBlock(L("L.AttachmentAgyWarning", Path.GetFileName(refPath))));
                else if (!ok)
                    s.Transcript.Add(new ErrorBlock(L("L.AttachmentWarningTitle"),
                        L("L.AttachmentCopyFailed", Path.GetFileName(refPath))));
            }
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

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

    private RelayCommand? _choiceFreeInputCommand;
    /// <summary>"기타" 자유입력 — 타이핑한 텍스트를 현재 질문의 답으로 기록하고 다음 질문으로 진행한다.
    /// 마지막(또는 단일) 질문이면 전체를 한 턴으로 전송. 위저드 도중 자유입력도 흐름을 끊지 않는다.</summary>
    public RelayCommand ChoiceFreeInputCommand => _choiceFreeInputCommand ??=
        new RelayCommand(_ =>
        {
            if (ActiveSession is not { } s || s.ActiveChoice is not { } flow) return;
            var text = (s.Draft ?? "").Trim();
            if (text.Length == 0) return;
            s.Draft = "";
            AnswerCurrentChoice(s, flow, text);
        });

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
        var runningSameKind = _allSessions.Count(x => x.IsWorker == s.IsWorker && _runs.IsRunning(x.Id));
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

        // agy 백엔드 분기: API 모드(GEMINI_API_KEY로 Antigravity SDK 구동)면 AgySdkAdapter + python,
        // subscription이면 기존 AgyAdapter(CLI/ConPTY)를 바이트 단위로 유지. cc/gx는 어댑터 내
        // 크리덴셜만 바꾸지만 agy는 어댑터 클래스 자체가 바뀌므로 분기를 호출점에 둔다.
        // 결정 로직은 Core TurnPlanner로 추출(헤드리스); 여기선 타입 실패를 지역화된 에러 블록으로 렌더한다.
        var resolution = TurnPlanner.ResolveEngine(new EngineResolveRequest(
            AgentId: s.AgentId,
            RequireApproval: s.RequireApproval,
            ApiMode: s.AgentId == "agy" && IsApiMode("agy"),
            HasApiKey: HasApiKey("agy"),
            ClaudePath: _claudePath, CodexPath: _codexPath, AgyPath: _agyPath, PiPath: _piPath,
            ResolvePython: ResolvePython));
        if (!resolution.Ok)
        {
            var (title, body) = resolution.Error switch
            {
                EngineSetupError.AgyPythonMissing => (L("L.AgyApiModeTitle"), L("L.AgyApiPythonMissing")),
                EngineSetupError.AgyBridgeMissing => (L("L.AgyApiModeTitle"), L("L.AgyApiBridgeMissing")),
                EngineSetupError.AgyKeyMissing => (L("L.AgyApiModeTitle"), L("L.AgyApiKeyMissing")),
                _ => (L("L.EngineUnavailableTitle"), L("L.EngineUnavailableBody", s.AgentName)),
            };
            s.Transcript.Add(new ErrorBlock(title, body));
            s.Status = "error";
            s.MarkRunEnded(L("L.EngineUnavailableActivity"));
            return;
        }
        var adapter = resolution.Adapter!;
        var exe = resolution.Exe!;

        // Worktree isolation: each session works in its own git worktree.
        s.Activity = L("L.PreparingWorktree");
        await EnsureWorktreeAsync(s);
        var cwd = s.WorktreePath ?? s.ProjectPath;
        WatchSessionTaskSpool(cwd, s.ProjectId, s.Id); // skill fallback (.am/worker-tasks/) → backlog; report back to s
        WatchSessionAskSpool(cwd, s.Id);               // ask-user skill (.am/ask/) → structured choice panel

        // Attachment docs: text/code are inlined verbatim; binary office/PDF docs go by PATH
        // PASS-THROUGH (copied under cwd/.am/attachments, gitignored, and referenced in the prompt
        // so the agent reads them with its own tools). agy (ConPTY single-shot) and any copy failure
        // get a clear transcript warning but still attach by path — never crash.
        var attachedDocsText = BuildAttachedDocsText(s, cwd, docs);

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
        var options = TurnPlanner.BuildOptions(new TurnOptionsRequest(
            AgentId: s.AgentId,
            WorkingDirectory: cwd,
            RequireApproval: s.RequireApproval,
            Sandbox: s.Sandbox,
            ResumeSessionId: s.EngineSessionId,
            Model: s.Model,
            McpConfigPath: sessionProject?.McpConfigPath,
            Images: images ?? [],
            AttachedDocsText: attachedDocsText,
            AdditionalDirectories: sessionProject?.ExtraPaths.ToArray() ?? [],
            ReasoningEffort: s.ReasoningEffort,
            ApiEnv: ApiEnvFor(s.AgentId),
            TaskSpoolDir: Path.Combine(cwd, ".am", "worker-tasks", s.Id),
            AskSpoolDir: Path.Combine(cwd, ".am", "ask", s.Id),
            NativeHookSpoolDirectory: NativeHookSpoolDirectoryFor(s)));
        var token = _runs.Start(s.Id);
        try
        {
            s.NativeWorkItems.Clear();
            ClearNativeHookSpool(options.NativeHookSpoolDirectory);
            await StartNativeObserverAsync(s, options);
            s.Activity = s.TranslationEnabled ? L("L.TranslatingStartingEngine") : L("L.StartingEngine");
            await Task.Run(() => session.RunAsync(options, prompt, token), token);
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
                MarkRateLimited(s.AgentId, _usageService.TryGet(s.AgentId, out var snap) ? snap.ResetsAtUnix : 0);
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
            _runs.Complete(s.Id);
        }
    }

    /// <summary>에러 메시지가 rate-limit/사용량 한도로 보이는가(소진 기록 트리거).</summary>
    private static bool LooksRateLimited(string? msg) => CoreHelpers.LooksRateLimited(msg);

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

    /// <summary>Antigravity SDK 브리지를 구동할 Python 인터프리터 경로. PATH의 'python' → 'py' 폴백.
    /// 사용자가 커스텀 인터프리터를 쓰면 확장 가능하도록 별도 메서드(향후 설정 연결점).</summary>
    private static string? ResolvePython()
    {
        foreach (var name in new[] { "python", "python3", "py" })
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var ext in new[] { "", ".exe" })
                    try { var full = Path.Combine(dir.Trim(), name + ext); if (File.Exists(full)) return full; } catch { }
            }
        return null;
    }

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

    /// <summary>Reduce one engine event into transcript/state changes. The classification + streaming-replace
    /// + stderr-suppression logic lives in the headless Core <see cref="TranscriptProjector"/> (golden-tested,
    /// CLI-reusable); this method only APPLIES the resulting domain deltas to the WPF transcript + session
    /// (localizing markers, firing UI side-effects). Overhaul (a) step 4.</summary>
    private void Apply(SessionViewModel s, NormalizedEvent ev, Dictionary<string, ToolBlock> tools)
    {
        s.MarkRunSignal();
        foreach (var delta in _projector.Project(s.Id, s.AgentId, ev))
            ApplyDelta(s, delta, tools);
        SaveState();
    }

    /// <summary>Apply one neutral transcript delta to the live WPF transcript + session. The UI-affine
    /// state the projector deliberately doesn't hold (the live streaming block, the tool-by-id table, the
    /// last user block, localized labels, attention/review/artifact actions) is realized here.</summary>
    private void ApplyDelta(SessionViewModel s, TranscriptDelta delta, Dictionary<string, ToolBlock> tools)
    {
        switch (delta)
        {
            case EngineSessionIdSet d:
                s.EngineSessionId = d.SessionId;
                break;
            case StartNativeObserver:
                _ = StartNativeObserverAsync(s);
                break;
            case ActivitySignal a:
                var label = ActivityLabel(a);
                // Connected/Tool reset the "quiet" timer (MarkRunSignal); streaming/receiving/thinking just
                // update the activity text (the per-event MarkRunSignal() at the top already bumped it).
                if (a.Kind is ActivityKind.Connected or ActivityKind.ConnectedModel or ActivityKind.ToolRunning)
                    s.MarkRunSignal(label);
                else
                    s.Activity = label;
                break;
            case UserSentTextSet d:
                if (s.Transcript.OfType<UserBlock>().LastOrDefault() is { } ub)
                    ub.SentText = d.SentText;
                break;
            case AssistantStreamAppend d:
                // 스트리밍: 라이브 블록에 즉시 덧붙이고, 최종 AssistantText(번역본)가 오면 교체
                if (!_liveText.TryGetValue(s.Id, out var live))
                {
                    live = new AgentTextBlock("") { ModelUsed = s.Model };
                    _liveText[s.Id] = live;
                    s.Transcript.Add(live);
                }
                live.Text += d.Delta;
                break;
            case AssistantStreamReplace d:
                if (_liveText.Remove(s.Id, out var streamed))
                {
                    streamed.Text = d.Text;
                    streamed.OriginalText = d.OriginalText;
                }
                else
                    s.Transcript.Add(new AgentTextBlock(d.Text) { OriginalText = d.OriginalText, ModelUsed = s.Model });
                break;
            case AssistantAdd d:
                s.Transcript.Add(new AgentTextBlock(d.Text) { OriginalText = d.OriginalText, ModelUsed = s.Model });
                break;
            case AssistantStreamEnd:
                _liveText.Remove(s.Id); // 스트리밍 잔여 해제 (최종 텍스트 미도착 시 라이브 내용 그대로 유지)
                break;
            case ThinkingAdd d:
                s.Transcript.Add(new ThinkingBlock(d.Text));
                break;
            case ToolAdd d:
                var tb = new ToolBlock(d.ToolUseId, d.Kind, d.Name) { CommandText = d.CommandText };
                tools[d.ToolUseId] = tb;
                s.Transcript.Add(tb);
                break;
            case TaskListArtifactUpdate d:
                UpsertTaskListArtifact(s, d.InputJson);
                break;
            case ToolFinished d:
                if (tools.TryGetValue(d.ToolUseId, out var t))
                {
                    t.Body = Trim(d.Content, 2000);
                    t.OriginalBody = d.OriginalContent is null ? null : Trim(d.OriginalContent, 2000);
                    t.Stat = d.IsError ? L("L.ToolError") : L("L.ToolDone");
                    if (t.CommandText is { } cmd && IsTestCommand(cmd))
                        UpsertTestArtifact(s, cmd, d.Content, d.IsError);
                }
                else
                {
                    s.Transcript.Add(new ToolBlock(d.ToolUseId, "RUN", L("L.Result"))
                    {
                        Body = Trim(d.Content, 2000),
                        OriginalBody = d.OriginalContent is null ? null : Trim(d.OriginalContent, 2000),
                        Stat = d.IsError ? L("L.ToolError") : L("L.ToolDone")
                    });
                }
                // live review: a finished tool may have changed files in the worktree
                _ = QueueLiveReviewRefreshAsync(s);
                break;
            case TokensAdded d:
                // live accumulation; TurnUsageSet reconciles to the turn total
                s.TokensIn += d.Input;
                s.TokensOut += d.Output;
                break;
            case TurnUsageSet d:
                // reconcile: per-message usage undercounts (esp. output); result.usage is the turn total
                s.TokensIn = s.TurnBaseIn + d.Input;
                s.TokensOut = s.TurnBaseOut + d.Output;
                break;
            case CostAdded d:
                s.CostUsd += d.Usd;
                break;
            case QuotaRecorded d:
                RecordUsage(s.AgentId, d.Quota);
                break;
            case StatusSet d:
                s.Status = d.Status;
                break;
            case ErrorAdd d:
                // claude can't --resume a conversation that no longer exists → the session is dead (stale,
                // delete action); don't stack repeats. Otherwise COALESCE a run of stderr lines into one
                // block — codex/PowerShell error dumps spew many lines, one block per line was a wall.
                if (s.Transcript.LastOrDefault() is ErrorBlock { IsStderr: true } lastErr)
                {
                    if (d.IsStaleSession && lastErr.IsStaleSession) break;
                    if (!d.IsStaleSession && !lastErr.IsStaleSession)
                    {
                        lastErr.Body = lastErr.Body.Length == 0 ? d.Message : lastErr.Body + "\n" + d.Message;
                        break;
                    }
                }
                s.Transcript.Add(new ErrorBlock(L("L.Stderr"), d.Message) { IsStaleSession = d.IsStaleSession, IsStderr = true });
                break;
            case TotalsChanged:
                RefreshTotals();
                break;
            case TurnFinished d:
                UpsertSummaryArtifact(s);
                AttentionRequested?.Invoke(d.IsError ? "error" : "done", s);
                s.MarkRunEnded(d.IsError ? L("L.Failed") : L("L.Completed"));
                PopulateQuickReplies(s);
                break;
        }
    }

    /// <summary>Localize a neutral activity marker for the run-signal/activity line.</summary>
    private static string ActivityLabel(ActivitySignal a) => a.Kind switch
    {
        ActivityKind.Connected => L("L.Connected"),
        ActivityKind.ConnectedModel => L("L.ConnectedModel", a.Arg ?? ""),
        ActivityKind.Streaming => L("L.StreamingResponse"),
        ActivityKind.Receiving => L("L.ReceivingResponse"),
        ActivityKind.Thinking => L("L.ThinkingActivity"),
        ActivityKind.ToolRunning => L("L.ToolRunning", a.Arg ?? ""),
        _ => "",
    };

    private readonly TranscriptProjector _projector = new();

}
