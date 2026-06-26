// Headless smoke tests — adapter parsing/args (zero tokens) + GitWorktree
// end-to-end against a throwaway temp repo (zero tokens, real git).
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;
using AgentManager.Core.Hosting;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Workers;

if (args.Contains("--sched-check"))
{
    RunSchedCheck();
    return;
}

if (args.Contains("--worker-prompt-check"))
{
    WorkerPromptCheck();
    return;
}

if (args.Contains("--worker-task-store-check"))
{
    WorkerTaskStoreCheck();
    return;
}

// Live headless E2E of the task queue (real engine, no GUI). Run in YOUR terminal — costs a few tokens:
// dotnet run --project src/AgentManager.Smoke -- --worker-task-run
if (args.Contains("--worker-task-run"))
{
    await WorkerTaskRunCheckAsync();
    return;
}

if (args.Contains("--sched-create-check"))
{
    RunSchedCreateCheck();
    return;
}

if (args.Contains("--native-observer-check"))
{
    await NativeObserverCheckAsync();
    return;
}

if (args.Contains("--subagent-failure-check"))
{
    SubagentFailureCheck();
    return;
}

if (args.Contains("--claude-agents-probe"))
{
    await ClaudeAgentsProbeCheckAsync();
    return;
}

if (args.Contains("--agy-observer-check"))
{
    await AgyObserverCheckAsync();
    return;
}

if (args.Contains("--codex-hook-args-check"))
{
    CodexHookArgsCheck();
    return;
}

if (args.Contains("--claude-hook-args-check"))
{
    ClaudeHookArgsCheck();
    return;
}

if (args.Contains("--live-claude-native-observer"))
{
    await LiveClaudeNativeObserverAsync();
    return;
}

// Live approval round-trip (costs a few engine tokens): dotnet run -- --live-approval
if (args.Contains("--live-approval"))
{
    await LiveApprovalAsync();
    return;
}

// Full product E2E (real Claude + Ollama translation + worktree + merge): dotnet run -- --e2e
if (args.Contains("--e2e"))
{
    await E2EAsync();
    return;
}

// Stage 2 spike: codex app-server JSON-RPC round-trip incl. interactive approval.
// dotnet run -- --appserver-probe
if (args.Contains("--appserver-probe"))
{
    await AppServerProbeAsync();
    return;
}

// Stage 2 integration test: the real product path (AgentSession + CodexAppServerAdapter)
// with an auto-accepting PermissionHandler. dotnet run -- --live-stage2
if (args.Contains("--live-stage2"))
{
    await LiveStage2Async();
    return;
}

// Repro: run codex through BOTH product paths exactly like the app does
// (model arg included). dotnet run -- --codex-check
if (args.Contains("--codex-check"))
{
    await CodexCheckAsync();
    return;
}


// agy 어댑터 실제품 경로 테스트 (2턴 resume 포함) — 사용자 터미널에서 실행할 것:
// dotnet run --project src/AgentManager.Smoke -- --live-agy
if (args.Contains("--live-agy"))
{
    await LiveAgyAsync();
    return;
}

static async Task LiveAgyAsync()
{
    var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
    if (!File.Exists(exe)) { Console.WriteLine("[agy-live] not installed"); return; }
    var tmp = Directory.CreateTempSubdirectory("am_agylive_").FullName;
    Console.WriteLine($"[agy-live] cwd={tmp}");

    string? sid = null; var done = false; var err = false;
    async Task Turn(string prompt, string? resume)
    {
        done = false; err = false;
        var session = new AgentSession(new AgyAdapter(), exe);
        session.EventReceived += ev =>
        {
            Console.WriteLine("  " + Describe(ev));
            if (ev is SessionStarted ss) sid = ss.SessionId;
            if (ev is TurnCompleted tc) { done = true; err = tc.IsError; }
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await session.RunAsync(new SessionOptions { WorkingDirectory = tmp, BypassPermissions = true }, prompt, cts.Token);
    }

    await Turn("Create a file named probe.txt containing exactly agy-live using a shell command, then stop.", null);
    var fileOk = File.Exists(Path.Combine(tmp, "probe.txt"));
    Console.WriteLine($"[agy-live] turn1 done={done} err={err} file={fileOk} sid={sid}");
    var sid1 = sid;
    await Turn("What file did you just create? Answer with only the file name.", sid1);
    Console.WriteLine($"[agy-live] turn2 done={done} err={err}");
    Console.WriteLine(fileOk && done && !err && sid1 is not null ? "agy live PASS" : "agy live FAIL");
    try { Directory.Delete(tmp, true); } catch { }
}

// agy 자식 프로세스 인증 검사 — 사용자 터미널에서 실행할 것:
// dotnet run --project src/AgentManager.Smoke -- --agy-check
if (args.Contains("--agy-check"))
{
    await AgyCheckAsync();
    return;
}

// agy PTY 스파이크: ConPTY(의사 콘솔)로 띄우면 출력/인증이 풀리는지 판가름.
// dotnet run -- --agy-pty-check
if (args.Contains("--agy-pty-check"))
{
    await AgyPtyCheckAsync();
    return;
}

static async Task AgyPtyCheckAsync()
{
    var agy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
    if (!File.Exists(agy)) { Console.WriteLine("[agy-pty] not installed"); return; }
    var tmp = Directory.CreateTempSubdirectory("am_agypty_").FullName;
    Console.WriteLine($"[agy-pty] spawning under ConPTY cwd={tmp}");

    var cmd = $"\"{agy}\" -p \"Say exactly OK\" --dangerously-skip-permissions";
    var (output, exit) = await ConPtyHost.RunAsync(cmd, tmp, TimeSpan.FromMinutes(3));
    var clean = ConPtyHost.StripVt(output);
    Console.WriteLine($"[agy-pty] exit={exit} rawLen={output.Length}");
    var tail = clean.Length > 1200 ? clean[^1200..] : clean;
    Console.WriteLine("[agy-pty] cleaned output tail:");
    Console.WriteLine(tail);
    Console.WriteLine(clean.Contains("OK") ? "agy PTY spike: OUTPUT VISIBLE — PASS" : "agy PTY spike: no expected output — FAIL");
    try { Directory.Delete(tmp, true); } catch { }
}

// B: subagent transcript 의 실패/rate-limit 추론 (오프라인 어서션).
static void WorkerPromptCheck()
{
    // preamble + task 조립, 빈 preamble 폴백, 공백 트림 검증
    var p = WorkerDefaults.ComposePrompt("RULES", "do the thing");
    var ok1 = p == "RULES\n\n## Task\ndo the thing";
    var ok2 = WorkerDefaults.ComposePrompt("", "only task") == "only task";
    var ok3 = WorkerDefaults.ComposePrompt("  RULES  ", "  task  ") == "RULES\n\n## Task\ntask";
    var ok4 = WorkerDefaults.DefaultMaxConcurrentWorkers >= 1 && WorkerDefaults.BehaviorPreamble.Contains("## Report");
    // 보고 병합: 단일은 그대로, 다수는 위임 순서로 라벨 부착
    var ok5 = WorkerDefaults.MergeReports([("W1", "only")]) == "only";
    var merged = WorkerDefaults.MergeReports([("Alpha", "a-result"), ("Beta", "b-result")]);
    var ok6 = merged == "## Worker 1 (Alpha)\na-result\n\n## Worker 2 (Beta)\nb-result";
    var ok = ok1 && ok2 && ok3 && ok4 && ok5 && ok6;
    Console.WriteLine($"[worker-prompt] compose={ok1} emptyPreamble={ok2} trim={ok3} defaults={ok4} mergeOne={ok5} mergeMany={ok6} -> {(ok ? "PASS" : "FAIL")}");
}

// WorkerTaskStore: backlog/per-worker-queue domain logic (ingest, assign, run-order, reorder, isolation).
static void WorkerTaskStoreCheck()
{
    var tmp = Directory.CreateTempSubdirectory("am_wts_").FullName;
    const string proj = "projA";
    var dir = Path.Combine(tmp, proj);
    Directory.CreateDirectory(dir);
    var store = new WorkerTaskStore();
    var changes = 0; store.Changed += () => changes++;

    // ingest: a 2-item array (one with explicit title, one title-from-first-line)
    File.WriteAllText(Path.Combine(dir, "a.json"),
        "[{\"title\":\"T1\",\"prompt\":\"do one\",\"engine\":\"cc\"},{\"prompt\":\"do two\"}]");
    var added = store.IngestFile(Path.Combine(dir, "a.json"));
    var t1 = added.Count > 0 ? added[0] : null;
    var t2 = added.Count > 1 ? added[1] : null;
    var okIngest = added.Count == 2 && store.Backlog(proj).Count() == 2
        && t1!.Title == "T1" && t1.Engine == "cc" && t1.ProjectId == proj && t1.Status == WorkerTaskStatus.Backlog
        && t2!.Title == "do two";

    // partial/garbage read → nothing added, file left for the caller
    File.WriteAllText(Path.Combine(dir, "bad.json"), "{ not json");
    var okBad = store.IngestFile(Path.Combine(dir, "bad.json")).Count == 0 && store.Backlog(proj).Count() == 2;

    // assign both to W1 → queue order 1,2; backlog empties
    store.Assign(t1!.Id, "W1");
    store.Assign(t2!.Id, "W1");
    var q = store.QueueFor("W1").ToList();
    var okAssign = !store.Backlog(proj).Any() && q.Count == 2
        && q[0].Id == t1.Id && q[0].Order == 1 && q[1].Order == 2
        && store.WorkerIdsWithTasks(proj).SequenceEqual(["W1"]);

    var okNext1 = store.NextRunnable("W1")?.Id == t1.Id;

    // run t1: while running, nothing else is runnable; when done it leaves the queue → next is t2
    store.SetStatus(t1.Id, WorkerTaskStatus.Running);
    var okRunning = store.NextRunnable("W1") is null && store.QueueFor("W1").Count() == 2;
    store.SetStatus(t1.Id, WorkerTaskStatus.Done);
    var okNext2 = store.NextRunnable("W1")?.Id == t2.Id && store.QueueFor("W1").Count() == 1;
    var okAssignedTo = store.AssignedTo("W1").Select(t => t.Id).SequenceEqual([t1.Id, t2.Id]);

    // add a 3rd, queued after t2, then reorder it above t2
    var t3 = store.IngestFile(WriteSpool(dir, "c.json", "{\"title\":\"T3\",\"prompt\":\"three\"}"))[0];
    store.Assign(t3.Id, "W1");
    var okThird = store.QueueFor("W1").Select(t => t.Id).SequenceEqual([t2.Id, t3.Id]);
    store.Move(t3.Id, -1);
    var okMove = store.QueueFor("W1").Select(t => t.Id).SequenceEqual([t3.Id, t2.Id]);

    // unassign t2 → back to backlog, off W1's queue
    store.Unassign(t2.Id);
    var okUnassign = store.Backlog(proj).Any(t => t.Id == t2.Id)
        && store.QueueFor("W1").Select(t => t.Id).SequenceEqual([t3.Id]);

    // second worker is isolated
    store.Assign(t2.Id, "W2");
    var okIso = store.QueueFor("W2").Single().Id == t2.Id && store.QueueFor("W1").Single().Id == t3.Id
        && store.WorkerIdsWithTasks(proj).OrderBy(x => x).SequenceEqual(["W1", "W2"]);

    // clear finished (t1 done + t3 failed) off W1; delete t2
    store.SetStatus(t3.Id, WorkerTaskStatus.Failed);
    store.ClearFinished("W1");
    var okClear = !store.AssignedTo("W1").Any();
    store.Delete(t2.Id);
    var okDelete = store.Find(t2.Id) is null;

    // crash reconciliation: a running task → re-queued as assigned so its worker isn't stuck
    var rstore = new WorkerTaskStore();
    rstore.Load(
    [
        new WorkerTaskDto { Id = "r1", ProjectId = proj, Prompt = "x", AssignedWorkerId = "W9", Status = WorkerTaskStatus.Running, Order = 1 },
        new WorkerTaskDto { Id = "r2", ProjectId = proj, Prompt = "y", AssignedWorkerId = "W9", Status = WorkerTaskStatus.Assigned, Order = 2 },
        new WorkerTaskDto { Id = "r3", ProjectId = proj, Prompt = "z", AssignedWorkerId = "W9", Status = WorkerTaskStatus.Done, Order = 3 },
    ]);
    var reconciled = rstore.ReconcileInterrupted();
    var okReconcile = reconciled == 1
        && rstore.Find("r1")!.Status == WorkerTaskStatus.Assigned   // running → assigned
        && rstore.Find("r3")!.Status == WorkerTaskStatus.Done       // done untouched
        && rstore.NextRunnable("W9")?.Id == "r1";                   // worker no longer stuck

    // report capture + per-origin inbox feed (only finished tasks with a report; dispatch order)
    var rs = new WorkerTaskStore();
    rs.Load(
    [
        new WorkerTaskDto { Id = "p1", ProjectId = proj, Prompt = "a", OriginSessionId = "orchX", Status = WorkerTaskStatus.Done, Order = 1 },
        new WorkerTaskDto { Id = "p2", ProjectId = proj, Prompt = "b", OriginSessionId = "orchX", Status = WorkerTaskStatus.Assigned, Order = 2 },
        new WorkerTaskDto { Id = "p3", ProjectId = proj, Prompt = "c", OriginSessionId = "other", Status = WorkerTaskStatus.Done, Order = 1 },
    ]);
    rs.SetReport("p1", "REPORT-1");
    rs.SetReport("p2", "REPORT-2");  // stored, but not finished → excluded from feed
    rs.SetReport("p3", "REPORT-3");
    var feed = rs.ReportsForOrigin("orchX").ToList();
    var okReport = feed.Count == 1 && feed[0].Id == "p1" && feed[0].Report == "REPORT-1"
        && rs.Find("p2")!.Report == "REPORT-2"
        && rs.ReportsForOrigin("other").Single().Id == "p3";
    rs.SetStatus("p2", WorkerTaskStatus.Done);  // now finished → joins the feed in order
    var okReportOrder = rs.ReportsForOrigin("orchX").Select(t => t.Id).SequenceEqual(["p1", "p2"]);

    var ok = okIngest && okBad && okAssign && okNext1 && okRunning && okNext2 && okAssignedTo
        && okThird && okMove && okUnassign && okIso && okClear && okDelete && okReconcile
        && okReport && okReportOrder && changes > 0;
    Console.WriteLine($"[worker-task-store] ingest={okIngest} bad={okBad} assign={okAssign} next1={okNext1} "
        + $"running={okRunning} next2={okNext2} assignedTo={okAssignedTo} reorder={okThird && okMove} "
        + $"unassign={okUnassign} isolation={okIso} clear={okClear} delete={okDelete} reconcile={okReconcile} "
        + $"report={okReport && okReportOrder} changes={changes}");
    Console.WriteLine($"[worker-task-store] {(ok ? "PASS" : "FAIL")}");
    try { Directory.Delete(tmp, true); } catch { }
    Environment.Exit(ok ? 0 : 1);

    static string WriteSpool(string dir, string name, string json)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }
}

// Live end-to-end of the task queue without the GUI: spool file → store ingest → assign →
// REAL engine turn (mirrors AppViewModel.DriveWorkerAsync) → verify the worker did the work + status.
static async Task WorkerTaskRunCheckAsync()
{
    var tmp = Path.Combine(Path.GetTempPath(), "am_wtrun_" + Guid.NewGuid().ToString("N")[..8]);
    const string projId = "proj-live";
    var spoolDir = Path.Combine(tmp, "spool", projId);
    var work = Path.Combine(tmp, "work");
    Directory.CreateDirectory(spoolDir);
    Directory.CreateDirectory(work);
    Console.WriteLine($"[wt-run] tmp={tmp}");

    // 1. the skill writes a task to the spool (atomic temp→.json, like an agent would)
    const string taskPrompt = "Create a file named task-done.txt containing exactly TASK-OK using a shell command, then stop.";
    var json = System.Text.Json.JsonSerializer.Serialize(new { title = "make marker file", prompt = taskPrompt, engine = "cc" });
    var stage = Path.Combine(spoolDir, "t.tmp");
    File.WriteAllText(stage, json);
    var spoolFile = Path.Combine(spoolDir, "t.json");
    File.Move(stage, spoolFile);

    // 2. store ingests the file → backlog (file consumed)
    var store = new WorkerTaskStore();
    var added = store.IngestFile(spoolFile);
    if (added.Count > 0) try { File.Delete(spoolFile); } catch { } // host deletes after a successful ingest
    var okIngest = added.Count == 1 && store.Backlog(projId).Count() == 1 && !File.Exists(spoolFile);
    var task = added[0];
    Console.WriteLine($"[wt-run] 1) ingest ok={okIngest} title=\"{task.Title}\" engine={task.Engine}");

    // 3. assign to a worker queue
    store.Assign(task.Id, "w-live");
    var okAssign = store.Find(task.Id)!.Status == WorkerTaskStatus.Assigned && store.NextRunnable("w-live")?.Id == task.Id;
    Console.WriteLine($"[wt-run] 2) assign ok={okAssign} status={store.Find(task.Id)!.Status}");

    // 4. run it on a REAL engine, headless — same status logic as DriveWorkerAsync
    var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
    if (!File.Exists(exe)) exe = FindOnPath("claude") ?? "claude";
    store.SetStatus(task.Id, WorkerTaskStatus.Running);
    var composed = WorkerDefaults.ComposePrompt(WorkerDefaults.BehaviorPreamble, store.Find(task.Id)!.Prompt);
    var produced = false; var turnDone = false; var err = false;
    var session = new AgentSession(new ClaudeAdapter(), exe);
    session.EventReceived += ev =>
    {
        if (ev is AssistantText) produced = true;
        if (ev is TurnCompleted tc) { turnDone = true; err = tc.IsError; }
    };
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try { await session.RunAsync(new SessionOptions { WorkingDirectory = work, BypassPermissions = true }, composed, cts.Token); }
    catch (Exception ex) { Console.WriteLine("[wt-run] EXCEPTION " + ex.Message); err = true; }

    // 5. record result: done only if the turn completed cleanly AND produced a reply (mirrors the runner)
    store.SetStatus(task.Id, turnDone && !err && produced ? WorkerTaskStatus.Done : WorkerTaskStatus.Failed);
    var fileOk = File.Exists(Path.Combine(work, "task-done.txt"));
    var finalStatus = store.Find(task.Id)!.Status;
    var okRun = finalStatus == WorkerTaskStatus.Done && fileOk && store.NextRunnable("w-live") is null;
    Console.WriteLine($"[wt-run] 3) run turnDone={turnDone} err={err} produced={produced} fileCreated={fileOk} status={finalStatus} queueEmpty={store.NextRunnable("w-live") is null}");

    var ok = okIngest && okAssign && okRun;
    Console.WriteLine($"[wt-run] {(ok ? "PASS — task queue works end-to-end via CLI" : "FAIL")}");
    try { Directory.Delete(tmp, true); } catch { }
    Environment.Exit(ok ? 0 : 1);
}

static void SubagentFailureCheck()
{
    var rateLine = "{\"type\":\"assistant\",\"isApiErrorMessage\":true,\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"You've hit your weekly limit — resets 8am (Asia/Seoul)\"}]}}";
    var okLine = "{\"type\":\"assistant\",\"isApiErrorMessage\":false,\"message\":{\"role\":\"assistant\",\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}}";
    var f = SubagentTranscriptInspector.InspectLine(rateLine);
    var n = SubagentTranscriptInspector.InspectLine(okLine);
    var hint = SubagentTranscriptInspector.LooksLikeLimit("You've hit your weekly limit");
    bool ok = f is { Failed: true, RateLimited: true } && (f.Message?.Contains("weekly limit") ?? false)
              && n is null && hint;
    Console.WriteLine($"[subagent-failure] rate={f?.RateLimited} msg=\"{f?.Message}\" normal={(n is null ? "ignored" : "MISFIRE")} hint={hint}");
    Console.WriteLine($"[subagent-failure] {(ok ? "PASS" : "FAIL")}");
    Environment.Exit(ok ? 0 : 1);
}

// A: `claude agents --json` 파싱(오프라인 어서션) + (claude 있으면) 라이브 호출.
static async Task ClaudeAgentsProbeCheckAsync()
{
    const string fixture = "[{\"pid\":15484,\"cwd\":\"J:\\\\prj\\\\x\",\"kind\":\"interactive\",\"startedAt\":1782031919148,\"sessionId\":\"b7ba3331-1e89\"}," +
                           "{\"pid\":71028,\"cwd\":\"J:\\\\prj\\\\AgentManager\",\"kind\":\"background\",\"startedAt\":1782032048549,\"sessionId\":\"ad19458c\"}]";
    var parsed = ClaudeAgentsProbe.Parse(fixture);
    bool ok = parsed.Count == 2 && parsed[0].Pid == 15484 && parsed[1].Kind == "background"
              && parsed[0].SessionId == "b7ba3331-1e89" && ClaudeAgentsProbe.Parse("not json").Count == 0;
    Console.WriteLine($"[agents-probe] parse fixture rows={parsed.Count} -> {(ok ? "PASS" : "FAIL")}");
    var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
    if (File.Exists(exe))
    {
        var live = await ClaudeAgentsProbe.RunAsync(exe);
        Console.WriteLine($"[agents-probe] live rows={live.Count}");
        foreach (var r in live)
            Console.WriteLine($"  pid={r.Pid} kind={r.Kind} cwd={r.Cwd} session={r.SessionId[..Math.Min(12, r.SessionId.Length)]}…");
    }
    else Console.WriteLine("[agents-probe] (claude not found — skipped live)");
    Environment.Exit(ok ? 0 : 1);
}

static async Task NativeObserverCheckAsync()
{
    var spool = Path.Combine(Path.GetTempPath(), "am_native_observer_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(spool);
    var target = new NativeWorkObservationTarget(
        EngineId: "gx",
        ParentSessionId: "app-session-1",
        WorkingDirectory: Environment.CurrentDirectory,
        EngineSessionId: "vendor-session-1",
        ManagedByAgentManager: true);

    var changed = new List<ObservedWorkItem>();
    await using var observer = new HookSpoolNativeWorkObserver("gx", spool);
    observer.WorkItemChanged += (_, item) => changed.Add(item);
    await observer.StartAsync(target);

    await File.WriteAllTextAsync(Path.Combine(spool, "start.json"), """
    {
      "hook_event_name": "SubagentStart",
      "session_id": "vendor-session-1",
      "agent_id": "agent-abc123",
      "agent_type": "Explore",
      "cwd": "J:\\prj\\AgentManager",
      "transcript_path": "C:\\tmp\\parent.jsonl"
    }
    """);

    await Task.Delay(150);

    await File.WriteAllTextAsync(Path.Combine(spool, "stop.json"), """
    {
      "hook_event_name": "SubagentStop",
      "session_id": "vendor-session-1",
      "agent_id": "agent-abc123",
      "agent_type": "Explore",
      "agent_transcript_path": "C:\\tmp\\subagent.jsonl",
      "last_assistant_message": "done"
    }
    """);

    await Task.Delay(250);
    var snapshot = await observer.SnapshotAsync();
    var item = snapshot.SingleOrDefault(i => i.AgentId == "agent-abc123");
    var ok = item is
    {
        EngineId: "gx",
        ParentSessionId: "app-session-1",
        VendorParentSessionId: "vendor-session-1",
        State: ObservedState.Completed,
        Confidence: ObservationConfidence.High,
        ManagedByAgentManager: true,
        LastMessage: "done"
    } && changed.Count >= 2;

    Console.WriteLine($"[native-observer] changes={changed.Count} snapshot={snapshot.Count}");
    Console.WriteLine(ok ? "native observer PASS" : "native observer FAIL");
    try { Directory.Delete(spool, true); } catch { }
}

static async Task AgyObserverCheckAsync()
{
    var home = Path.Combine(Path.GetTempPath(), "am_agy_observer_" + Guid.NewGuid().ToString("N")[..8]);
    var cwd = Path.Combine(home, "project");
    var conversationId = "conv-parent-1";
    var childId = "conv-child-1";
    var cache = Path.Combine(home, ".gemini", "antigravity-cli", "cache");
    var system = Path.Combine(home, ".gemini", "antigravity", "brain", conversationId, ".system_generated");
    var logs = Path.Combine(system, "logs");
    var messages = Path.Combine(system, "messages");
    Directory.CreateDirectory(cwd);
    Directory.CreateDirectory(cache);
    Directory.CreateDirectory(logs);
    Directory.CreateDirectory(messages);

    var escapedCwd = cwd.Replace("\\", "\\\\");
    await File.WriteAllTextAsync(Path.Combine(cache, "last_conversations.json"),
        $"{{\n  \"{escapedCwd}\": \"{conversationId}\"\n}}");

    await File.WriteAllTextAsync(Path.Combine(logs, "transcript.jsonl"),
        $"{{\"step\":\"INVOKE_SUBAGENT\",\"toolOutput\":{{\"conversationId\":\"{childId}\",\"logAbsoluteUri\":\"file:///C:/tmp/child-log.jsonl\",\"workspaceUris\":[\"file:///C:/tmp/subagent-worktree\"],\"role\":\"Implement\"}}}}");

    var changed = new List<ObservedWorkItem>();
    await using var observer = new AgyNativeWorkObserver(home);
    observer.WorkItemChanged += (_, item) => changed.Add(item);
    await observer.StartAsync(new NativeWorkObservationTarget(
        EngineId: "agy",
        ParentSessionId: "app-agy-session-1",
        WorkingDirectory: cwd,
        ManagedByAgentManager: true));

    await Task.Delay(100);
    await File.WriteAllTextAsync(Path.Combine(messages, "done.json"),
        $"{{\n  \"sender\": \"{childId}\",\n  \"recipient\": \"parent\",\n  \"content\": \"completed successfully\"\n}}");

    await Task.Delay(250);
    var snapshot = await observer.SnapshotAsync();
    var item = snapshot.SingleOrDefault(i => i.VendorWorkId == childId);
    var ok = item is
    {
        EngineId: "agy",
        ParentSessionId: "app-agy-session-1",
        VendorParentSessionId: "conv-parent-1",
        Kind: WorkItemKind.NativeSubagent,
        State: ObservedState.Completed,
        Confidence: ObservationConfidence.Medium,
        ManagedByAgentManager: true
    };

    Console.WriteLine($"[agy-observer] changes={changed.Count} snapshot={snapshot.Count}");
    Console.WriteLine(ok ? "agy observer PASS" : "agy observer FAIL");
    try { Directory.Delete(home, true); } catch { }
}

static void CodexHookArgsCheck()
{
    var cwd = Environment.CurrentDirectory;
    var command = NativeHookCommandFactory.WindowsPowerShellSpoolWriter();
    var args = new CodexAdapter().BuildStartInfo("codex", new SessionOptions
    {
        WorkingDirectory = cwd,
        BypassPermissions = true,
        Sandbox = SandboxMode.DangerFullAccess,
        NativeHookSpoolDirectory = Path.Combine(Path.GetTempPath(), "am-hooks"),
        NativeHookCommand = command,
        BypassHookTrust = true
    }, "p").ArgumentList.ToArray();

    var joined = string.Join("\n", args);
    var ok = args.Contains("--dangerously-bypass-hook-trust")
        && joined.Contains("hooks.SubagentStart=", StringComparison.Ordinal)
        && joined.Contains("hooks.SubagentStop=", StringComparison.Ordinal)
        && joined.Contains("AGENTMANAGER_HOOK_SPOOL", StringComparison.Ordinal)
        && joined.Contains("powershell", StringComparison.OrdinalIgnoreCase);

    Console.WriteLine($"[codex-hook-args] args={args.Length}");
    Console.WriteLine(ok ? "codex hook args PASS" : "codex hook args FAIL");
}

static void ClaudeHookArgsCheck()
{
    var cwd = Environment.CurrentDirectory;
    var command = NativeHookCommandFactory.WindowsPowerShellSpoolWriter();
    var args = new ClaudeAdapter().BuildStartInfo("claude", new SessionOptions
    {
        WorkingDirectory = cwd,
        BypassPermissions = true,
        Sandbox = SandboxMode.DangerFullAccess,
        NativeHookSpoolDirectory = Path.Combine(Path.GetTempPath(), "am-hooks"),
        NativeHookCommand = command
    }, "p").ArgumentList.ToArray();

    var joined = string.Join("\n", args);
    var ok = args.Contains("--settings")
        && joined.Contains("SubagentStart", StringComparison.Ordinal)
        && joined.Contains("SubagentStop", StringComparison.Ordinal)
        && joined.Contains("AGENTMANAGER_HOOK_SPOOL", StringComparison.Ordinal)
        && joined.Contains("powershell", StringComparison.OrdinalIgnoreCase);

    Console.WriteLine($"[claude-hook-args] args={args.Length}");
    Console.WriteLine(ok ? "claude hook args PASS" : "claude hook args FAIL");
}

static async Task LiveClaudeNativeObserverAsync()
{
    var exe = FindOnPath("claude") ?? "claude";
    var tmp = Directory.CreateTempSubdirectory("am_claude_native_").FullName;
    await File.WriteAllTextAsync(Path.Combine(tmp, "probe.txt"), "native-observer");
    var spool = Path.Combine(Path.GetTempPath(), "am_claude_hooks_" + Guid.NewGuid().ToString("N")[..8]);
    var command = NativeHookCommandFactory.WindowsPowerShellSpoolScript(spool);
    var options = new SessionOptions
    {
        WorkingDirectory = tmp,
        BypassPermissions = true,
        Sandbox = SandboxMode.DangerFullAccess,
        NativeHookSpoolDirectory = spool,
        NativeHookCommand = command
    };

    var observed = new List<ObservedWorkItem>();
    await using var observer = new HookSpoolNativeWorkObserver("cc", spool);
    observer.WorkItemChanged += (_, item) =>
    {
        lock (observed) observed.Add(item);
    };
    await observer.StartAsync(new NativeWorkObservationTarget("cc", "smoke-claude-session", tmp, null, true));

    var session = new AgentSession(new ClaudeAdapter(), exe);
    var done = false;
    var failed = false;
    session.EventReceived += ev =>
    {
        Console.WriteLine("  " + Describe(ev));
        if (ev is TurnCompleted tc) { done = true; failed = tc.IsError; }
    };

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    await session.RunAsync(options,
        "Use the Explore subagent to list the files in the current working directory. Then answer exactly: done",
        cts.Token);

    await Task.Delay(500);
    var snapshot = await observer.SnapshotAsync();
    var sawCompleted = snapshot.Any(i => i.EngineId == "cc" && i.State == ObservedState.Completed && i.AgentId is not null);
    Console.WriteLine($"[claude-native] done={done} failed={failed} observed={snapshot.Count} spool={spool}");
    Console.WriteLine(done && !failed && sawCompleted ? "claude native observer PASS" : "claude native observer FAIL");
    try { Directory.Delete(tmp, true); } catch { }
}

static string? FindOnPath(string executable)
{
    var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
    var names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd", executable + ".bat", executable } : [executable];
    foreach (var path in paths)
    foreach (var name in names)
    {
        try
        {
            var full = Path.Combine(path, name);
            if (File.Exists(full)) return full;
        }
        catch { }
    }
    return null;
}

static async Task AgyCheckAsync()
{
    var agy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
    if (!File.Exists(agy)) { Console.WriteLine("[agy] not installed"); return; }
    var tmp = Directory.CreateTempSubdirectory("am_agy_").FullName;
    Console.WriteLine($"[agy] spawning as CHILD process (the way the app would) cwd={tmp}");

    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = agy,
        WorkingDirectory = tmp,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = new System.Text.UTF8Encoding(false),
        StandardErrorEncoding = new System.Text.UTF8Encoding(false),
    };
    psi.ArgumentList.Add("-p");
    psi.ArgumentList.Add("Say exactly OK");
    psi.ArgumentList.Add("--dangerously-skip-permissions");
    using var p = System.Diagnostics.Process.Start(psi)!;
    p.StandardInput.Close();
    var stdout = await p.StandardOutput.ReadToEndAsync();
    var stderr = await p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token);
    Console.WriteLine($"[agy] exit={p.ExitCode}");
    Console.WriteLine($"[agy] stdout: {(string.IsNullOrWhiteSpace(stdout) ? "(empty)" : stdout.Trim())}");
    if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine($"[agy] stderr: {stderr.Trim()}");
    Console.WriteLine(stdout.Contains("OK") ? "agy child-process auth PASS" : "agy child-process auth FAIL (or not logged in)");
    try { Directory.Delete(tmp, true); } catch { }
}

// Query supported model ids from app-server. dotnet run -- --codex-models
if (args.Contains("--codex-models"))
{
    await CodexModelsAsync();
    return;
}

static async Task CodexModelsAsync()
{
    var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
    var exe = Directory.EnumerateDirectories(ext, "openai.chatgpt-*")
        .Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe")).FirstOrDefault(File.Exists);
    if (exe is null) { Console.WriteLine("codex.exe not found"); return; }

    var utf8 = new System.Text.UTF8Encoding(false);
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = exe, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        UseShellExecute = false, CreateNoWindow = true,
        StandardOutputEncoding = utf8, StandardErrorEncoding = utf8, StandardInputEncoding = utf8,
    };
    psi.ArgumentList.Add("app-server");
    using var p = System.Diagnostics.Process.Start(psi)!;
    await p.StandardInput.WriteLineAsync("""{"id":1,"method":"initialize","params":{"clientInfo":{"name":"AgentManager","version":"0.1.0"}}}""");
    await p.StandardInput.FlushAsync();
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline && await p.StandardOutput.ReadLineAsync() is { } line)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(line);
        if (!doc.RootElement.TryGetProperty("id", out var id) || id.ValueKind != System.Text.Json.JsonValueKind.Number) continue;
        if (id.GetInt32() == 1)
        {
            await p.StandardInput.WriteLineAsync("""{"method":"initialized"}""");
            await p.StandardInput.WriteLineAsync("""{"id":2,"method":"model/list","params":{}}""");
            await p.StandardInput.FlushAsync();
        }
        else if (id.GetInt32() == 2)
        {
            Console.WriteLine(doc.RootElement.GetRawText());
            break;
        }
    }
    try { p.Kill(entireProcessTree: true); } catch { }
}

static async Task CodexCheckAsync()
{
    static string? FindCodexExe2()
    {
        var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(ext)) return null;
        return Directory.EnumerateDirectories(ext, "openai.chatgpt-*")
            .Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    var exe = FindCodexExe2();
    if (exe is null) { Console.WriteLine("[codex-check] codex.exe not found"); return; }
    const string promptEn = "Implement the Fibonacci sequence using a recursive function, in a new file fib.py.";
    const string model = "gpt-5.5"; // app default: EngineRegistry Models[0]

    async Task<string?> RunPath(string label, IAgentAdapter adapter, SessionOptions opts, string prompt)
    {
        Console.WriteLine($"==== {label} (model={opts.Model}) ====");
        var done = false; var err = false; string? sid = null;
        var session = new AgentSession(adapter, exe);
        session.PermissionHandler = pr =>
        {
            Console.WriteLine($"  APPROVAL {pr.ToolName} -> accept");
            return Task.FromResult(new PermissionDecision(true));
        };
        session.EventReceived += ev =>
        {
            Console.WriteLine("  " + Describe(ev));
            if (ev is SessionStarted ss) sid = ss.SessionId;
            if (ev is TurnCompleted tc) { done = true; err = tc.IsError; }
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await session.RunAsync(opts, prompt, cts.Token); }
        catch (Exception ex) { Console.WriteLine("  EXCEPTION " + ex.Message); err = true; }
        var fib = Path.Combine(opts.WorkingDirectory, "fib.py");
        Console.WriteLine($"  -> turnDone={done} err={err} fib.py={(File.Exists(fib) ? "created" : "MISSING")}");
        return sid;
    }

    var tmp1 = Directory.CreateTempSubdirectory("am_cx_exec_").FullName;
    var sid1 = await RunPath("exec --json turn 1 (bypass, app default)", new CodexAdapter(),
        new SessionOptions { WorkingDirectory = tmp1, BypassPermissions = true, Sandbox = SandboxMode.DangerFullAccess, Model = model }, promptEn);

    if (sid1 is not null)
        await RunPath("exec --json turn 2 (resume)", new CodexAdapter(),
            new SessionOptions { WorkingDirectory = tmp1, BypassPermissions = true, Sandbox = SandboxMode.DangerFullAccess, Model = model, ResumeSessionId = sid1 },
            "Append a doctest-style comment with fib(10)'s value to fib.py.");

    var tmp2 = Directory.CreateTempSubdirectory("am_cx_aps_").FullName;
    await RunPath("app-server (approval)", new CodexAppServerAdapter(),
        new SessionOptions { WorkingDirectory = tmp2, BypassPermissions = false, Sandbox = SandboxMode.DangerFullAccess, Model = model }, promptEn);

    try { Directory.Delete(tmp1, true); Directory.Delete(tmp2, true); } catch { }
}

static async Task LiveStage2Async()
{
    static string? FindCodexExe()
    {
        var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(ext)) return null;
        return Directory.EnumerateDirectories(ext, "openai.chatgpt-*")
            .Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    var exe = FindCodexExe();
    if (exe is null) { Console.WriteLine("[stage2] codex.exe not found"); return; }
    var tmp = Path.Combine(Path.GetTempPath(), "am_st2_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    Console.WriteLine($"[stage2] cwd={tmp}");

    var approvals = 0;
    string? sessionId = null;
    var sawToolStartAndResult = (start: false, result: false);
    var turnDone = false;

    var session = new AgentSession(new CodexAppServerAdapter(), exe);
    session.PermissionHandler = pr =>
    {
        approvals++;
        Console.WriteLine($"[stage2] APPROVAL {pr.ToolName} req={pr.RequestId} -> accept");
        return Task.FromResult(new PermissionDecision(true));
    };
    session.EventReceived += ev =>
    {
        Console.WriteLine("  " + Describe(ev));
        switch (ev)
        {
            case SessionStarted s: sessionId = s.SessionId; break;
            case ToolUseStarted: sawToolStartAndResult.start = true; break;
            case ToolResult: sawToolStartAndResult.result = true; break;
            case TurnCompleted: turnDone = true; break;
        }
    };

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    await session.RunAsync(
        new SessionOptions { WorkingDirectory = tmp },
        "Run a shell command that writes the exact text stage2-integration into a new file named probe.txt, then stop.",
        cts.Token);

    var ok = File.Exists(Path.Combine(tmp, "probe.txt"))
             && (await File.ReadAllTextAsync(Path.Combine(tmp, "probe.txt"))).Contains("stage2-integration");
    Console.WriteLine($"[stage2] approvals={approvals} session={sessionId} tool={sawToolStartAndResult} turnDone={turnDone} fileOk={ok}");
    Console.WriteLine(ok && approvals > 0 && turnDone && sessionId is not null ? "stage2 integration PASS" : "stage2 integration FAIL");
    try { Directory.Delete(tmp, recursive: true); } catch { }
}

static async Task AppServerProbeAsync()
{
    static string? FindCodex()
    {
        var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(ext)) return null;
        return Directory.EnumerateDirectories(ext, "openai.chatgpt-*")
            .Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    var codex = FindCodex();
    if (codex is null) { Console.WriteLine("[appserver] codex.exe not found"); return; }

    var tmp = Path.Combine(Path.GetTempPath(), "am_aps_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    Console.WriteLine($"[appserver] exe={codex}");
    Console.WriteLine($"[appserver] cwd={tmp}");

    var utf8 = new System.Text.UTF8Encoding(false);
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = codex,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = utf8,
        StandardErrorEncoding = utf8,
        StandardInputEncoding = utf8,
    };
    psi.ArgumentList.Add("app-server");
    using var p = System.Diagnostics.Process.Start(psi)!;
    _ = Task.Run(async () => { while (await p.StandardError.ReadLineAsync() is { } e) Console.WriteLine("[stderr] " + e); });

    async Task Send(object msg)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(msg);
        Console.WriteLine("--> " + (json.Length > 220 ? json[..220] + "…" : json));
        await p.StandardInput.WriteLineAsync(json);
        await p.StandardInput.FlushAsync();
    }

    await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "AgentManager", title = "AgentManager", version = "0.1.0" } } });

    string? threadId = null;
    var approvals = 0;
    var ok = false;
    var deadline = DateTime.UtcNow.AddSeconds(180);
    while (DateTime.UtcNow < deadline)
    {
        var readTask = p.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30))) != readTask) { Console.WriteLine("[appserver] read timeout"); break; }
        var line = readTask.Result;
        if (line is null) { Console.WriteLine("[appserver] EOF"); break; }
        Console.WriteLine("<-- " + (line.Length > 220 ? line[..220] + "…" : line));

        System.Text.Json.JsonDocument doc;
        try { doc = System.Text.Json.JsonDocument.Parse(line); } catch { continue; }
        using var _d = doc;
        var root = doc.RootElement;
        var method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;
        var hasId = root.TryGetProperty("id", out var idEl);

        if (method is null && hasId) // response to one of our requests
        {
            var id = idEl.GetInt32();
            if (id == 1)
            {
                await Send(new { method = "initialized" });
                // sandbox는 danger-full-access: 이 환경에서 codex 윈도우 샌드박스 spawn이 실패하고
                // (제품 모델도 "샌드박스 대신 승인 게이트"), 승인은 approvalPolicy가 강제한다
                await Send(new
                {
                    id = 2,
                    method = "thread/start",
                    @params = new { cwd = tmp, approvalPolicy = "untrusted", sandbox = "danger-full-access" }
                });
            }
            else if (id == 2)
            {
                // ThreadStartResponse: find the thread id wherever it lives
                threadId = FindString(root, "threadId") ?? FindString(root, "id");
                Console.WriteLine($"[appserver] threadId={threadId}");
                await Send(new
                {
                    id = 3,
                    method = "turn/start",
                    @params = new
                    {
                        threadId,
                        input = new object[] { new { type = "text", text = "Run a shell command that writes the exact text stage2-approval-spike into a new file named probe.txt, then stop." } },
                    }
                });
            }
            continue;
        }

        if (method is not null && hasId) // server -> client request (approval etc.)
        {
            Console.WriteLine($"[appserver] SERVER REQUEST {method}");
            if (method.Contains("requestApproval") || method is "execCommandApproval" or "applyPatchApproval")
            {
                approvals++;
                await Send(new { id = idEl.GetInt32(), result = new { decision = "accept" } });
            }
            continue;
        }

        if (method == "turn/completed") { ok = true; break; }
        if (method == "error") Console.WriteLine("[appserver] ERROR notification");
    }

    var probe = Path.Combine(tmp, "probe.txt");
    var fileOk = File.Exists(probe) && (await File.ReadAllTextAsync(probe)).Contains("stage2-approval-spike");
    Console.WriteLine($"[appserver] approvals={approvals} turnCompleted={ok} fileOk={fileOk}");
    Console.WriteLine(ok && fileOk && approvals > 0 ? "appserver probe PASS" : "appserver probe FAIL");
    try { p.Kill(entireProcessTree: true); } catch { }
    try { Directory.Delete(tmp, recursive: true); } catch { }

    static string? FindString(System.Text.Json.JsonElement el, string name)
    {
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name == name && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String) return prop.Value.GetString();
                if (FindString(prop.Value, name) is { } s) return s;
            }
        }
        else if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (FindString(item, name) is { } s) return s;
        }
        return null;
    }
}

// External CLI session discovery against the real disk: dotnet run -- --cli-history <projectPath>
if (args.Length >= 2 && args[0] == "--cli-history")
{
    var found = CliSessionDiscovery.Discover(args[1]);
    Console.WriteLine($"[cli-history] {args[1]} -> {found.Count} entries");
    foreach (var e in found)
        Console.WriteLine($"  {e.EngineId} {e.SessionId[..Math.Min(12, e.SessionId.Length)]}… {e.LastWriteUtc:MM-dd HH:mm} | {e.Title}");
    foreach (var e in found.GroupBy(x => x.EngineId).Select(g => g.First()))
    {
        var tr = CliSessionDiscovery.LoadTranscript(e.EngineId, e.FilePath);
        Console.WriteLine($"[transcript] {e.EngineId} {e.SessionId[..8]}… -> {tr.Count} items");
        foreach (var it in tr.Take(6))
            Console.WriteLine($"  {it.Role,-9} {it.Name,-12} {(it.Text.Length > 70 ? it.Text[..70].Replace('\n', ' ') + "…" : it.Text.Replace('\n', ' '))}");
    }
    return;
}

static async Task E2EAsync()
{
    static string Pass(bool ok) => ok ? "PASS" : "FAIL";
    var tmp = Path.Combine(Path.GetTempPath(), "am_e2e_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    var wtRoot = Path.Combine(tmp, "_wts");
    try
    {
        static async Task Git(string dir, params string[] a)
        {
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "git", WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            foreach (var x in a) psi.ArgumentList.Add(x);
            using var p = System.Diagnostics.Process.Start(psi)!; await p.WaitForExitAsync();
        }
        // 1. project git repo
        await Git(tmp, "init", "-q");
        await Git(tmp, "config", "user.email", "t@t"); await Git(tmp, "config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(tmp, "README.md"), "# e2e\n");
        await Git(tmp, "add", "-A"); await Git(tmp, "commit", "-qm", "init");
        Console.WriteLine("[1] project repo ready");

        // 2. worktree isolation
        var wt = await GitWorktree.CreateAsync(tmp, "s1", "agent/e2e", wtRoot);
        Console.WriteLine($"[2] worktree isolation .......... {Pass(wt is not null && Directory.Exists(wt!.Path))}");
        if (wt is null) { Console.WriteLine("E2E ABORTED (not a git repo?)"); return; }

        // 3. translation + engine: Korean prompt -> KO->EN -> Claude writes a file -> EN->KO response
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        var translator = new OllamaTranslator(new OllamaOptions());
        var session = new AgentSession(new ClaudeAdapter(), File.Exists(exe) ? exe : "claude", translator, translationEnabled: true);

        string? koreanReply = null; bool sawTool = false;
        session.EventReceived += ev =>
        {
            switch (ev)
            {
                case ToolUseStarted u: sawTool = true; Console.WriteLine($"    tool: {u.Name}"); break;
                case AssistantText t: koreanReply = t.Text; break;
            }
        };
        var opts = new SessionOptions { WorkingDirectory = wt.Path, BypassPermissions = true };
        Console.WriteLine("[3] running Claude with Korean prompt (translation ON)…");
        await session.RunAsync(opts, "fibonacci.txt 파일을 만들어서 피보나치 수열의 처음 8개 숫자를 적어줘. Write 도구를 쓰고 끝나면 멈춰.");

        var file = Path.Combine(wt.Path, "fibonacci.txt");
        var fileMade = File.Exists(file);
        var replyKorean = koreanReply is not null && System.Text.RegularExpressions.Regex.IsMatch(koreanReply, "[가-힣]");
        Console.WriteLine($"[3a] engine used a tool ......... {Pass(sawTool)}");
        Console.WriteLine($"[3b] file created in worktree ... {Pass(fileMade)}  ({(fileMade ? "fibonacci.txt" : "missing")})");
        Console.WriteLine($"[3c] reply translated to KO ..... {Pass(replyKorean)}  (\"{(koreanReply ?? "").Replace("\n", " ")[..Math.Min(60, (koreanReply ?? "").Length)]}\")");

        // 4. review: changed files + diff
        var changes = await GitWorktree.GetChangedFilesAsync(wt.Path);
        var diff = await GitWorktree.GetDiffAsync(wt.Path);
        Console.WriteLine($"[4] review: {changes.Count} changed file(s), diff {diff.Length} chars .. {Pass(changes.Count >= 1 && diff.Length > 0)}");

        // 5. merge -> main updated, then cleanup worktree
        var (mok, mmsg) = await GitWorktree.MergeAsync(tmp, "agent/e2e", "agent: e2e", wt.Path);
        var onMain = File.Exists(Path.Combine(tmp, "fibonacci.txt"));
        Console.WriteLine($"[5] merge to main .............. {Pass(mok && onMain)}  ({mmsg})");
        await GitWorktree.RemoveAsync(tmp, wt.Path);

        var allOk = wt is not null && fileMade && sawTool && replyKorean && changes.Count >= 1 && mok && onMain;
        Console.WriteLine(allOk ? "\nE2E OK — full product path verified" : "\nE2E had failures (see above)");
    }
    finally { try { Directory.Delete(tmp, true); } catch { } }
}

static async Task LiveApprovalAsync()
{
    var tmp = Path.Combine(Path.GetTempPath(), "am_appr_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    try
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        var session = new AgentSession(new ClaudeAdapter(), File.Exists(exe) ? exe : "claude");
        int asked = 0;
        session.PermissionHandler = pr =>
        {
            asked++;
            Console.WriteLine($"  PermissionRequest #{asked}: {pr.ToolName} → AUTO-ALLOW");
            return Task.FromResult(new PermissionDecision(true));
        };
        session.EventReceived += ev => { if (ev is AssistantText t) Console.WriteLine("  agent: " + t.Text); };

        var options = new SessionOptions { WorkingDirectory = tmp, BypassPermissions = false };
        await session.RunAsync(options, "Create a file named ok.txt containing exactly OK. Use the Write tool. Then stop.");

        var created = File.Exists(Path.Combine(tmp, "ok.txt"));
        Console.WriteLine($"approval round-trip: asked={asked}, file created={created}");
        Console.WriteLine(asked > 0 && created ? "LIVE APPROVAL OK" : "LIVE APPROVAL FAILED");
    }
    finally
    {
        try { Directory.Delete(tmp, true); } catch { }
    }
}

string[] claudeLines =
[
    """{"type":"system","subtype":"init","session_id":"sess-1","model":"claude-sonnet-4-6","tools":[1,2,3],"cwd":"J:\\prj\\AgentManager"}""",
    """{"type":"rate_limit_event","session_id":"sess-1","rate_limit_info":{"status":"allowed_warning","resetsAt":1781132400,"rateLimitType":"seven_day","utilization":0.76}}""",
    """{"type":"assistant","parent_tool_use_id":null,"message":{"usage":{"input_tokens":3,"output_tokens":8,"cache_read_input_tokens":15718,"cache_creation_input_tokens":5745},"content":[{"type":"thinking","thinking":"let me run it"}]}}""",
    """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"echo hi"}}]}}""",
    """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"hi","is_error":false}]}}""",
    """{"type":"assistant","parent_tool_use_id":"toolu_sub","message":{"content":[{"type":"text","text":"all done"}]}}""",
    """{"type":"result","subtype":"success","is_error":false,"result":"all done","total_cost_usd":0.0123,"num_turns":1}""",
];

string[] codexLines =
[
    """{"type":"thread.started","thread_id":"019ea6ad"}""",
    """{"type":"turn.started"}""",
    """{"type":"item.started","item":{"id":"item_0","type":"command_execution","command":"echo codex-spike","status":"in_progress"}}""",
    """{"type":"item.completed","item":{"id":"item_0","type":"command_execution","aggregated_output":"codex-spike\r\n","exit_code":0,"status":"completed"}}""",
    """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"codex-spike"}}""",
    """{"type":"turn.completed","usage":{"input_tokens":27644,"cached_input_tokens":18688,"output_tokens":47,"reasoning_output_tokens":0}}""",
];

// pi RPC 모드 (실측 envelope + happy-path) — docs/PHASE0_PI_RPC_KO.md
string[] piLines =
[
    """{"type":"response","command":"get_state","success":true,"data":{"sessionId":"sess-pi-1","model":{"id":"anthropic/claude-opus-4-7"},"thinkingLevel":"medium","isStreaming":false}}""",
    """{"type":"response","command":"prompt","success":true}""",
    """{"type":"agent_start"}""",
    """{"type":"turn_start"}""",
    """{"type":"message_update","assistantMessageEvent":{"type":"thinking_delta","contentIndex":0,"delta":"hmm"}}""",
    """{"type":"message_update","assistantMessageEvent":{"type":"thinking_end","contentIndex":0,"content":"Let me think."}}""",
    """{"type":"message_update","assistantMessageEvent":{"type":"text_delta","contentIndex":0,"delta":"Hello"}}""",
    """{"type":"message_update","assistantMessageEvent":{"type":"text_delta","contentIndex":0,"delta":" world"}}""",
    """{"type":"tool_execution_start","toolCallId":"call_1","toolName":"bash","args":{"command":"ls"}}""",
    """{"type":"tool_execution_end","toolCallId":"call_1","toolName":"bash","result":{"content":[{"type":"text","text":"file.txt"}]},"isError":false}""",
    """{"type":"message_end","message":{"role":"assistant","content":[{"type":"text","text":"Hello world"}],"usage":{"input":10,"output":5,"cacheRead":0,"cacheWrite":0},"stopReason":"stop"}}""",
    """{"type":"agent_end","messages":[]}""",
];

void Run(string title, IAgentAdapter adapter, string[] lines)
{
    Console.WriteLine($"=== {title} ({adapter.Id}) ===");
    foreach (var line in lines)
        foreach (var ev in adapter.ParseLine(line))
            Console.WriteLine("  " + Describe(ev));
    Console.WriteLine();
}

static string Describe(NormalizedEvent ev) => ev switch
{
    SessionStarted s => $"SessionStarted id={s.SessionId} model={s.Model} tools={s.ToolCount}",
    QuotaUpdate q => $"QuotaUpdate {q.Utilization:P0} type={q.RateLimitType} status={q.Status}",
    Thinking t => $"Thinking \"{Trunc(t.Text)}\"",
    ToolUseStarted u => $"ToolUseStarted {u.Name} id={u.ToolUseId} input={Trunc(u.InputJson)}",
    ToolResult r => $"ToolResult id={r.ToolUseId} err={r.IsError} sub={r.FromSubagent} \"{Trunc(r.Content)}\"",
    AssistantText a => $"AssistantText sub={a.FromSubagent} \"{Trunc(a.Text)}\"",
    TokenUsage k => $"TokenUsage in={k.InputTokens} out={k.OutputTokens} cacheRead={k.CacheReadTokens} reasoning={k.ReasoningTokens}",
    PermissionRequest p => $"PermissionRequest {p.ToolName} req={p.RequestId}",
    TurnCompleted c => $"TurnCompleted err={c.IsError} cost={c.CostUsd} turns={c.NumTurns}",
    EngineError e => $"EngineError {e.Message}",
    RawUnknown x => $"RawUnknown type={x.Type}",
    _ => ev.ToString() ?? "?",
};

static string Trunc(string s) => s.Length > 40 ? s[..40] + "…" : s;

Run("Claude stream-json", new ClaudeAdapter(), claudeLines);
Run("Codex exec --json", new CodexAdapter(), codexLines);
Run("Pi RPC", new PiAdapter(), piLines);
AssertResumeArgs();
AssertSandboxAndModelArgs();
AssertPermissionResponse();
AssertAppServerAdapter();
AssertQuickReplyParser();

static void AssertQuickReplyParser()
{
    static int N(string t) => AgentManager.Core.QuickReplyParser.Parse(t).Count;

    // letter choices trailing the message → A, B
    var r = AgentManager.Core.QuickReplyParser.Parse(
        "Ready for the next step.\n\n- **A)** Start feature/phase1 and build, or\n- **B)** Hold and confirm first?\n\nJust say go.");
    if (r.Count != 2 || r[0].Marker != "A" || r[1].Marker != "B")
        throw new Exception("quickreply: A/B choices not detected");

    // numbered choice with a cue word → 2
    if (N("Which would you prefer?\n1. Rebuild from scratch\n2. Patch in place") != 2)
        throw new Exception("quickreply: numbered choice missed");
    // numbered list of questions → not a pick-one
    if (N("A few questions:\n1. What is it?\n2. What shape?\n3. Which stack?") != 0)
        throw new Exception("quickreply: questions falsely detected");
    // plain numbered plan (no choice cue) → ignored
    if (N("Plan:\n1. Create the solution\n2. Add projects\n3. Wire CI") != 0)
        throw new Exception("quickreply: plain numbered list falsely detected");
    // prose, no options → none
    if (N("All done. Everything builds and tests pass.") != 0)
        throw new Exception("quickreply: false positive on prose");

    Console.WriteLine("quick-reply parser asserts OK");
}

static void AssertAppServerAdapter()
{
    var cwd = Environment.CurrentDirectory;

    static List<NormalizedEvent> Parse(IAgentAdapter a, string line) => a.ParseLine(line).ToList();

    // --- handshake: initialize 응답 → initialized + thread/start writeback ---
    var ad = (IAgentAdapter)new CodexAppServerAdapter();
    var init = ad.InitialStdinLines("hello", new SessionOptions { WorkingDirectory = cwd });
    Assert(init.Count == 1 && init[0].Contains("\"initialize\"") && init[0].Contains("AgentManager"), "appserver initialize line");

    var wb1 = Parse(ad, """{"id":1,"result":{"userAgent":"x"}}""");
    Assert(wb1.Count == 2 && wb1.All(e => e is EngineWriteback), "appserver init response -> 2 writebacks");
    Assert(((EngineWriteback)wb1[0]).Line.Contains("\"initialized\""), "appserver initialized notification");
    var ts = ((EngineWriteback)wb1[1]).Line;
    Assert(ts.Contains("thread/start") && ts.Contains("untrusted") && ts.Contains("danger-full-access"), "appserver thread/start policy");

    // --- thread/start 응답 → SessionStarted + turn/start writeback ---
    var wb2 = Parse(ad, """{"id":2,"result":{"thread":{"id":"th-123","sessionId":"th-123"}}}""");
    Assert(wb2.Count == 2 && wb2[0] is SessionStarted { SessionId: "th-123" }, "appserver SessionStarted");
    var turn = ((EngineWriteback)wb2[1]).Line;
    Assert(turn.Contains("turn/start") && turn.Contains("th-123") && turn.Contains("hello"), "appserver turn/start payload");

    // --- 승인 요청 → PermissionRequest → accept/decline 응답 포맷 ---
    var req = Parse(ad, """{"method":"item/commandExecution/requestApproval","id":0,"params":{"threadId":"th-123","itemId":"call_1"}}""");
    Assert(req.Count == 1 && req[0] is PermissionRequest { RequestId: "0", ToolName: "shell", ToolUseId: "call_1" }, "appserver PermissionRequest");
    var allow = ad.BuildPermissionResponse((PermissionRequest)req[0], new PermissionDecision(true));
    Assert(allow == """{"id":0,"result":{"decision":"accept"}}""", "appserver accept json");
    var deny = ad.BuildPermissionResponse((PermissionRequest)req[0], new PermissionDecision(false, "no"));
    Assert(deny == """{"id":0,"result":{"decision":"decline"}}""", "appserver decline json");

    // --- 아이템/턴 매핑 ---
    var tool = Parse(ad, """{"method":"item/started","params":{"item":{"type":"commandExecution","id":"call_1","command":"echo hi"}}}""");
    Assert(tool.Count == 1 && tool[0] is ToolUseStarted { Name: "shell", ToolUseId: "call_1" }, "appserver ToolUseStarted");
    var toolDone = Parse(ad, """{"method":"item/completed","params":{"item":{"type":"commandExecution","id":"call_1","aggregatedOutput":"hi","exitCode":0,"status":"completed"}}}""");
    Assert(toolDone.Count == 1 && toolDone[0] is ToolResult { Content: "hi", IsError: false }, "appserver ToolResult");
    var msg = Parse(ad, """{"method":"item/completed","params":{"item":{"type":"agentMessage","id":"m1","text":"done!"}}}""");
    Assert(msg.Count == 1 && msg[0] is AssistantText { Text: "done!" }, "appserver AssistantText");
    Parse(ad, """{"method":"thread/tokenUsage/updated","params":{"tokenUsage":{"total":{"inputTokens":100,"outputTokens":7,"cachedInputTokens":50}}}}""");
    var done = Parse(ad, """{"method":"turn/completed","params":{"turn":{"id":"t1","status":"completed"}}}""");
    Assert(done.Count == 1 && done[0] is TurnCompleted { IsError: false, Usage: { InputTokens: 100, OutputTokens: 7, CacheReadTokens: 50 } }, "appserver TurnCompleted+usage");

    // --- 미지원 서버 요청은 에러 응답으로 차단 해제 ---
    var unsup = Parse(ad, """{"method":"item/tool/requestUserInput","id":9,"params":{}}""");
    Assert(unsup.Count == 2 && unsup[0] is EngineWriteback w && w.Line.Contains("-32601") && unsup[1] is EngineError, "appserver unsupported server request");

    // --- resume 경로 ---
    var ad2 = (IAgentAdapter)new CodexAppServerAdapter();
    ad2.InitialStdinLines("again", new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "th-old" });
    var rs = Parse(ad2, """{"id":1,"result":{}}""");
    var resume = ((EngineWriteback)rs[1]).Line;
    Assert(resume.Contains("thread/resume") && resume.Contains("th-old"), "appserver thread/resume");

    Console.WriteLine("codex app-server adapter asserts OK");
}
await TestGitWorktreeAsync();
Console.WriteLine("smoke OK");

static void AssertPermissionResponse()
{
    var req = new PermissionRequest("req-1", "Write", """{"file_path":"a.txt","content":"hi"}""", "toolu_9");
    var allow = new ClaudeAdapter().BuildPermissionResponse(req, new PermissionDecision(true));
    Assert(allow is not null && allow.Contains("\"behavior\":\"allow\"") && allow.Contains("req-1")
        && allow.Contains("toolu_9") && allow.Contains("a.txt"), "Claude allow response");
    var deny = new ClaudeAdapter().BuildPermissionResponse(req, new PermissionDecision(false, "nope"));
    Assert(deny is not null && deny.Contains("\"behavior\":\"deny\"") && deny.Contains("nope")
        && deny.Contains("\"interrupt\":true"), "Claude deny response");
    Assert(((IAgentAdapter)new CodexAdapter()).BuildPermissionResponse(req, new PermissionDecision(true)) is null,
        "Codex has no approval protocol (null)");
    Console.WriteLine("permission response asserts OK");
}

static void AssertSandboxAndModelArgs()
{
    var cwd = Environment.CurrentDirectory;

    // Codex sandbox mapping
    string[] CodexArgs(SandboxMode sb, bool bypass) =>
        new CodexAdapter().BuildStartInfo("codex",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = bypass, Sandbox = sb }, "p").ArgumentList.ToArray();
    Assert(CodexArgs(SandboxMode.DangerFullAccess, true).Contains("--dangerously-bypass-approvals-and-sandbox"), "Codex danger+bypass");
    var ro = CodexArgs(SandboxMode.ReadOnly, true);
    Assert(ro.Contains("--sandbox") && ro.Contains("read-only"), "Codex read-only");
    var ww = CodexArgs(SandboxMode.WorkspaceWrite, true);
    Assert(ww.Contains("--sandbox") && ww.Contains("workspace-write"), "Codex workspace-write");

    // Claude sandbox + model mapping
    string[] ClaudeArgs(SandboxMode sb, string? model = null) =>
        new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, Sandbox = sb, Model = model }, "p").ArgumentList.ToArray();
    var plan = ClaudeArgs(SandboxMode.ReadOnly);
    Assert(plan.Contains("--permission-mode") && plan.Contains("plan"), "Claude read-only→plan");
    Assert(ClaudeArgs(SandboxMode.DangerFullAccess).Contains("--dangerously-skip-permissions"), "Claude bypass");
    var cm = ClaudeArgs(SandboxMode.DangerFullAccess, "sonnet");
    Assert(cm.Contains("--model") && cm.Contains("sonnet"), "Claude --model");
    var xm = new CodexAdapter().BuildStartInfo("codex",
        new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, Model = "gpt-5.1" }, "p").ArgumentList.ToArray();
    Assert(xm.Contains("-m") && xm.Contains("gpt-5.1"), "Codex -m");
    var xe = new CodexAdapter().BuildStartInfo("codex",
        new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, ReasoningEffort = "high" }, "p").ArgumentList.ToArray();
    Assert(xe.Contains("model_reasoning_effort=\"high\""), "Codex reasoning effort -c");
    var ce = new ClaudeAdapter().BuildStartInfo("claude",
        new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, ReasoningEffort = "max" }, "p").ArgumentList.ToArray();
    Assert(ce.Contains("--effort") && ce.Contains("max"), "Claude --effort");
    var cd = new ClaudeAdapter().BuildStartInfo("claude",
        new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, ReasoningEffort = "default" }, "p").ArgumentList.ToArray();
    Assert(!cd.Contains("--effort"), "Claude default effort omitted");

    // MCP passthrough: existing file → --mcp-config; missing file → omitted
    var mcpFile = Path.GetTempFileName();
    try
    {
        var with = new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, McpConfigPath = mcpFile }, "p").ArgumentList.ToArray();
        Assert(with.Contains("--mcp-config") && with.Contains(mcpFile), "Claude --mcp-config");
        var without = new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, McpConfigPath = mcpFile + ".missing" }, "p").ArgumentList.ToArray();
        Assert(!without.Contains("--mcp-config"), "missing mcp file omitted");
    }
    finally { File.Delete(mcpFile); }

    // multi-folder: existing extra dir → claude --add-dir / codex writable_roots; missing dir omitted
    var extra = Directory.CreateTempSubdirectory("am_extra_").FullName;
    try
    {
        var cAdd = new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, AdditionalDirectories = [extra, extra + "_missing"] }, "p").ArgumentList.ToArray();
        Assert(cAdd.Contains("--add-dir") && cAdd.Contains(extra) && !cAdd.Contains(extra + "_missing"), "Claude --add-dir");
        var xAdd = new CodexAdapter().BuildStartInfo("codex",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = false, Sandbox = SandboxMode.WorkspaceWrite, AdditionalDirectories = [extra] }, "p").ArgumentList.ToArray();
        Assert(xAdd.Contains("-c") && xAdd.Any(a => a.StartsWith("sandbox_workspace_write.writable_roots=[") && a.Contains(extra.Replace('\\', '/'))), "Codex writable_roots");
        var xDanger = new CodexAdapter().BuildStartInfo("codex",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, Sandbox = SandboxMode.DangerFullAccess, AdditionalDirectories = [extra] }, "p").ArgumentList.ToArray();
        Assert(!xDanger.Any(a => a.Contains("writable_roots")), "Codex danger: no writable_roots needed");
    }
    finally { Directory.Delete(extra); }
    Console.WriteLine("sandbox/model/mcp/add-dir arg asserts OK");
}

static async Task TestGitWorktreeAsync()
{
    var tmp = Path.Combine(Path.GetTempPath(), "am_smoke_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    try
    {
        async Task<string> Git(params string[] args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "git", WorkingDirectory = tmp, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return o;
        }
        await Git("init", "-q");
        await Git("config", "user.email", "t@t"); await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(tmp, "a.txt"), "base\n");
        await Git("add", "-A"); await Git("commit", "-qm", "init");

        // create
        var wtRoot = Path.Combine(tmp, "_wts");
        var wt = await GitWorktree.CreateAsync(tmp, "s1", "agent/s1", wtRoot);
        Assert(wt is not null && Directory.Exists(wt.Path), "worktree create");

        // change detection (modified + untracked) and diff incl. untracked
        await File.WriteAllTextAsync(Path.Combine(wt!.Path, "a.txt"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(wt.Path, "new.txt"), "hello\n");
        var changes = await GitWorktree.GetChangedFilesAsync(wt.Path);
        Assert(changes.Count == 2, $"changes count = {changes.Count} (expected 2)");
        var diff = await GitWorktree.GetDiffAsync(wt.Path);
        Assert(diff.Contains("changed") && diff.Contains("new.txt"), "diff incl. untracked");

        // discard → clean
        var (dok, _) = await GitWorktree.DiscardAsync(wt.Path);
        Assert(dok && (await GitWorktree.GetChangedFilesAsync(wt.Path)).Count == 0, "discard cleans");

        // commit-only → branch ahead, main untouched
        await File.WriteAllTextAsync(Path.Combine(wt.Path, "a.txt"), "feature\n");
        var (cok, _) = await GitWorktree.CommitAsync(wt.Path, "agent: c1");
        Assert(cok, "commit-only");
        Assert((await File.ReadAllTextAsync(Path.Combine(tmp, "a.txt"))).StartsWith("base"), "main untouched after commit-only");

        // merge (second change) → main updated
        await File.WriteAllTextAsync(Path.Combine(wt.Path, "a.txt"), "feature2\n");
        var (mok, mmsg) = await GitWorktree.MergeAsync(tmp, "agent/s1", "agent: c2", wt.Path);
        Assert(mok, "merge: " + mmsg);
        Assert((await File.ReadAllTextAsync(Path.Combine(tmp, "a.txt"))).StartsWith("feature2"), "main has merged content");

        await GitWorktree.RemoveAsync(tmp, wt.Path);
        Console.WriteLine("GitWorktree end-to-end OK (create/changes/diff/discard/commit-only/merge)");
    }
    finally
    {
        try { Directory.Delete(tmp, true); } catch { }
    }
}

static void AssertResumeArgs()
{
    var cwd = Environment.CurrentDirectory;
    var claude = new ClaudeAdapter().BuildStartInfo(
        "claude",
        new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "sess-1" },
        "next turn");
    var claudeArgs = claude.ArgumentList.ToArray();
    Assert(claudeArgs.Contains("--resume") && claudeArgs.Contains("sess-1"), "Claude resume args missing");

    var codex = new CodexAdapter().BuildStartInfo(
        "codex",
        new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "thread-1", BypassPermissions = true },
        "next turn");
    var codexArgs = codex.ArgumentList.ToArray();
    Assert(codexArgs.Length >= 3 && codexArgs[0] == "exec" && codexArgs[1] == "resume" && codexArgs[2] == "thread-1",
        "Codex resume args missing");
    // exec resume은 -C/--sandbox를 지원하지 않는다 (실측 0.137: unexpected argument '-C')
    Assert(!codexArgs.Contains("-C") && !codexArgs.Contains("--sandbox"), "Codex resume must not pass -C/--sandbox");

    var codexResumeWw = new CodexAdapter().BuildStartInfo(
        "codex",
        new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "thread-1", BypassPermissions = false, Sandbox = SandboxMode.WorkspaceWrite },
        "next turn").ArgumentList.ToArray();
    Assert(codexResumeWw.Contains("-c") && codexResumeWw.Any(a => a.StartsWith("sandbox_mode=")) && !codexResumeWw.Contains("--sandbox"),
        "Codex resume sandbox via -c override");
}

static void RunSchedCheck()
{
    Console.WriteLine("[sched-check] Running tests...");

    // 1. Cadence to Cron parsing test
    Assert(ScheduleTrigger.TryParseCadenceToCron("Every day · 02:00") == "0 2 * * *", "Every day parsing failed");
    Assert(ScheduleTrigger.TryParseCadenceToCron("Mondays · 09:00") == "0 9 * * 1", "Mondays parsing failed");
    Assert(ScheduleTrigger.TryParseCadenceToCron("매일 02:00") == "0 2 * * *", "매일 parsing failed");
    Assert(ScheduleTrigger.TryParseCadenceToCron("매주 월 09:00") == "0 9 * * 1", "매주 월 parsing failed");
    Assert(ScheduleTrigger.TryParseCadenceToCron("매주 토 10:00") == "0 10 * * 6", "매주 토 parsing failed");

    // 2. NextRunUtc Calculation Test
    // 2026-06-13 is a Saturday (DayOfWeek.Saturday = 6)
    var baseTime = new DateTime(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

    // Case 2a: Every day · 02:00 (Today 2am already passed, should be tomorrow 2am)
    var t1 = new ScheduleTrigger { Kind = "Cron", CadenceText = "Every day · 02:00" };
    var n1 = t1.GetNextRunUtc(baseTime);
    Assert(n1 == new DateTime(2026, 6, 14, 2, 0, 0, DateTimeKind.Utc), $"T1 next run was {n1}");

    // Case 2b: Every day · 15:00 (Today 3pm is in the future, should be today 3pm)
    var t2 = new ScheduleTrigger { Kind = "Cron", CadenceText = "Every day · 15:00" };
    var n2 = t2.GetNextRunUtc(baseTime);
    Assert(n2 == new DateTime(2026, 6, 13, 15, 0, 0, DateTimeKind.Utc), $"T2 next run was {n2}");

    // Case 2c: Mondays · 09:00 (Next Monday should be 2026-06-15 09:00)
    var t3 = new ScheduleTrigger { Kind = "Cron", CadenceText = "Mondays · 09:00" };
    var n3 = t3.GetNextRunUtc(baseTime);
    Assert(n3 == new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc), $"T3 next run was {n3}");

    // Case 2d: 매주 토 10:00 (Today is Saturday 12:00, so 10:00 is passed. Next Saturday should be 2026-06-20 10:00)
    var t4 = new ScheduleTrigger { Kind = "Cron", CadenceText = "매주 토 10:00" };
    var n4 = t4.GetNextRunUtc(baseTime);
    Assert(n4 == new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc), $"T4 next run was {n4}");

    // Case 2e: 매주 토 14:00 (Today is Saturday 12:00, so 14:00 is in the future. Next run should be 2026-06-13 14:00)
    var t5 = new ScheduleTrigger { Kind = "Cron", CadenceText = "매주 토 14:00" };
    var n5 = t5.GetNextRunUtc(baseTime);
    Assert(n5 == new DateTime(2026, 6, 13, 14, 0, 0, DateTimeKind.Utc), $"T5 next run was {n5}");

    // Case 2f: Event type trigger (Implemented with NotImplemented comment internally, should return null)
    var t6 = new ScheduleTrigger { Kind = "Event", CadenceText = "On push to spec/", TargetPath = "spec/" };
    var n6 = t6.GetNextRunUtc(baseTime);
    Assert(n6 == null, "Event trigger next run should be null");

    Console.WriteLine("[sched-check] All next run calculations verified.");
    Console.WriteLine("PASS");
}

static void RunSchedCreateCheck()
{
    Console.WriteLine("[sched-create-check] Running tests...");

    // 1. Override ScheduleStore.StorePath to a temporary file
    var tempFile = Path.Combine(Path.GetTempPath(), "am_smoke_sched_" + Guid.NewGuid().ToString("N")[..8] + ".json");
    ScheduleStore.StorePath = tempFile;
    Console.WriteLine($"[sched-create-check] Override StorePath to: {tempFile}");

    try
    {
        // 2. Create ScheduledJob 1
        var trigger = new ScheduleTrigger
        {
            Kind = "Cron",
            CadenceText = "Every day · 02:00",
            CronExpression = "0 2 * * *",
            TargetPath = null
        };
        var job = new ScheduledJob
        {
            Id = "job-smoke-test-1",
            AgentId = "cc",
            ProjectId = "p-smoke-test",
            ProjectPath = "C:/temp/dummy_path",
            Title = "Smoke Test Job",
            Prompt = "Say smoke test",
            TargetBranch = "agent/smoke-test",
            Trigger = trigger,
            Enabled = true,
            LastRunUtc = null
        };

        // 3. Save via ScheduleStore.Save
        ScheduleStore.Save(new List<ScheduledJob> { job });
        Console.WriteLine("[sched-create-check] Job saved successfully.");

        // 4. Load from ScheduleStore.Load and verify round-trip
        var loadedJobs = ScheduleStore.Load();
        Assert(loadedJobs.Count == 1, $"Expected 1 loaded job, got {loadedJobs.Count}");
        var loaded = loadedJobs[0];
        
        Assert(loaded.Id == job.Id, "Id mismatch");
        Assert(loaded.AgentId == job.AgentId, "AgentId mismatch");
        Assert(loaded.ProjectId == job.ProjectId, "ProjectId mismatch");
        Assert(loaded.ProjectPath == job.ProjectPath, "ProjectPath mismatch");
        Assert(loaded.Title == job.Title, "Title mismatch");
        Assert(loaded.Prompt == job.Prompt, "Prompt mismatch");
        Assert(loaded.TargetBranch == job.TargetBranch, "TargetBranch mismatch");
        Assert(loaded.Enabled == job.Enabled, "Enabled mismatch");
        Assert(loaded.LastRunUtc == job.LastRunUtc, "LastRunUtc mismatch");
        Assert(loaded.Trigger.Kind == trigger.Kind, "Trigger.Kind mismatch");
        Assert(loaded.Trigger.CadenceText == trigger.CadenceText, "Trigger.CadenceText mismatch");
        Assert(loaded.Trigger.CronExpression == trigger.CronExpression, "Trigger.CronExpression mismatch");
        Assert(loaded.Trigger.TargetPath == trigger.TargetPath, "Trigger.TargetPath mismatch");
        Console.WriteLine("[sched-create-check] Round-trip verification successful.");

        // 5. Verify NextRunUtc calculation yields a future time
        Assert(loaded.NextRunUtc.HasValue, "NextRunUtc has no value");
        Assert(loaded.NextRunUtc!.Value > DateTime.UtcNow, $"NextRunUtc ({loaded.NextRunUtc.Value}) is not in the future relative to UtcNow ({DateTime.UtcNow})");
        Console.WriteLine($"[sched-create-check] NextRunUtc verified in future: {loaded.NextRunUtc.Value}");

        // 6. Verify due evaluation works when Enabled = true
        // Let's create a due job
        var cronEveryMin = new ScheduleTrigger
        {
            Kind = "Cron",
            CadenceText = "* * * * *",
            CronExpression = "* * * * *"
        };
        var dueJob = new ScheduledJob
        {
            Id = "job-smoke-due-1",
            AgentId = "cc",
            ProjectId = "p-smoke-test",
            ProjectPath = "C:/temp/dummy_path",
            Title = "Due Job",
            Prompt = "Due prompt",
            TargetBranch = "agent/smoke-due",
            Trigger = cronEveryMin,
            Enabled = true,
            LastRunUtc = DateTime.UtcNow.AddMinutes(-5) // 5 minutes ago
        };

        ScheduleStore.Save(new List<ScheduledJob> { dueJob });
        
        // Let's initialize scheduler
        using (var scheduler = new TimerScheduler())
        {
            scheduler.Reload();

            int triggerCount = 0;
            ScheduledJob? triggeredJob = null;
            scheduler.JobDue += (s, e) =>
            {
                triggerCount++;
                triggeredJob = e.Job;
            };

            // Run EvaluateJobs synchronously
            scheduler.EvaluateJobs();

            // Verify trigger occurred
            Assert(triggerCount == 1, $"Expected JobDue to fire 1 time, fired {triggerCount} times");
            Assert(triggeredJob != null, "Triggered job was null");
            Assert(triggeredJob!.Id == dueJob.Id, "Triggered job ID mismatch");
            Assert(triggeredJob.LastRunUtc.HasValue, "Triggered job LastRunUtc is null");
            // Verify it was updated to roughly now (within 10 seconds)
            var diff = DateTime.UtcNow - triggeredJob.LastRunUtc!.Value;
            Assert(Math.Abs(diff.TotalSeconds) < 10, $"LastRunUtc ({triggeredJob.LastRunUtc.Value}) is not close to UtcNow ({DateTime.UtcNow})");

            // Verify that the disk state is also updated
            var diskJobs = ScheduleStore.Load();
            Assert(diskJobs.Count == 1, $"Expected 1 job on disk, got {diskJobs.Count}");
            Assert(diskJobs[0].LastRunUtc.HasValue, "LastRunUtc on disk is null");
            Assert(diskJobs[0].LastRunUtc!.Value == triggeredJob.LastRunUtc.Value, "LastRunUtc on disk does not match memory");

            // 7. Verify that double triggering does NOT occur on next run (since LastRunUtc has been updated)
            // Set the scheduler's internal list to the saved state to simulate reload or persistent state
            scheduler.Reload();
            
            scheduler.EvaluateJobs();
            Assert(triggerCount == 1, $"JobDue triggered again! Duplicate prevention failed. Fire count: {triggerCount}");
        }
        
        Console.WriteLine("[sched-create-check] TimerScheduler due evaluation and duplicate prevention verified successfully.");
        Console.WriteLine("PASS");
    }
    finally
    {
        // Cleanup temp file
        try
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
        catch {}
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
