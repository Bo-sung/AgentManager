# OVERHAUL — Phase (a) EXTRACTION MAP

> **Goal of this doc:** a concrete, checklist-style plan for the FIRST step of AgentManager's overhaul —
> **lifting orchestration logic OUT of the WPF ViewModels (`src/AgentManager/ViewModels/AppViewModel.*.cs`,
> ~7,000 LoC across 17 fragments) INTO a headless service layer (`src/AgentManager.Core`) so a CLI and
> the GUI become thin frontends over one shared Core.**
>
> This is **PHASE (a): in-process shared library**. It must be done so that **PHASE (b) — host the Core
> as a LOCAL DAEMON (tmux / dockerd / LSP model) so multiple GUI/CLI windows attach to ONE live core +
> sessions survive window close)** — can later cut a clean IPC line. The (b) boundary is marked in §6.
>
> **Status: ANALYSIS / DESIGN ONLY.** No source is modified here. Every classification cites `file:method`.
>
> **Boundary test applied per responsibility:**
> *DECIDES / OWNS STATE / PERSISTS → Core (service). RENDERS or COLLECTS INPUT → frontend (stays in GUI VM; a CLI provides its own).*

---

## 0. Measured reality (baseline)

- **Already headless (Core, ~4,800 LoC):** `AgentSession` (`Core/Session/AgentSession.cs:RunAsync`),
  `WorkerTaskStore` (`Core/Workers/WorkerTaskStore.cs`), `TimerScheduler`/`ScheduleStore` (`Core/Scheduling/*`),
  `GitWorktree` / `CliSessionDiscovery` (`Core/Workspace/*`), all `IAgentAdapter`s + `NormalizedEvent`
  (`Core/Agents/*`, `Core/Events/NormalizedEvent.cs`), `ITranslator`/`OllamaTranslator`, `SkillInjector`,
  `QuickReplyParser`, observation (`Core/Observation/*`), `ConPtyHost`, `UpdateService`.
  → **Core already owns the engine process + the worker-task domain + git + scheduling as services.**
- **Tangled in ViewModels:** session lifecycle, the turn loop driver, the event→transcript reducer,
  settings/auth/rate-limit, persistence hydration+debounce, spool ingest, usage probing, review/diff,
  delegation orchestration. These are the extraction targets.
- **Key structural fact:** `AgentSession` already emits a clean engine-agnostic event stream
  (`event Action<NormalizedEvent>? EventReceived`) — the *reducer* that turns those events into UI
  transcript blocks (`AppViewModel.Run.cs:Apply`) is the WPF-coupled part, not the emission.

---

## 1. PER-FRAGMENT CLASSIFICATION TABLE

Legend: **S** = SERVICE (→ Core) · **U** = UI (stays in GUI VM; CLI reimplements) · **M** = MIXED (split — seam named).
WPF couplings to sever are noted: `OO`=ObservableObject, `RC`=RelayCommand, `Disp`=Dispatcher/`Application.Current.Dispatcher`,
`OC`=ObservableCollection, `AC`=Application.Current, `CB`=Clipboard, `FW`=FileSystemWatcher (OK in Core, but currently wired with `Disp`).

### 1.1 `AppViewModel.cs` (~1,500 LoC) — **M** (the root hub)

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| Constructor command wiring (40+ `RelayCommand`) | **U** | `RC` — frontend command surface |
| `_allSessions` / `NewSessionId` — canonical session list | **S** → owned by future `SessionService` | `OC` move |
| `_running` Dict<id,CancellationTokenSource> — running-turn registry + `StopActive`/`Dispose` cancel | **S** → `OrchestratorService` | none (plain Dict) |
| Approval broker: `_pendingApprovals`, `HandlePermissionAsync`, `ResolveApproval`, `ExpirePendingApprovals` | **S** → `ApprovalBroker` | `AC.Dispatcher.Invoke` (HandlePermissionAsync) |
| `AttentionRequested` event | **U** (taskbar flash) | stays frontend; Core raises a domain event instead |
| `CreateSession` / `CreateWorkerSession`-seed / `CreateProject` | **M** — domain create + `OC` mutation | seam: service creates domain session → VM projects |
| `DeleteSessionAsync` / `ToggleArchive` / `RenameSession` / `RenameProject` / `RemoveProject` / `AddExtraPath` | **M** — domain mutation + `OC` + `SaveState` | seam: service mutates + persists → emits event |
| `ForkSession` / `CloneTranscriptItem` | **M** | seam: domain fork in service |
| `OpenIde` / `OpenProjectFolder` / `OpenAgyInTerminal` / `FindVsCodeCli` / `TryResolveOnPath` / `Quote` | **M** — process launching is headless-capable; the *trigger* is UI | seam: a `Launcher` service |
| `SessionStatusWatch` (PropertyChanged relay) | **M** — bridges service state changes to UI refresh | seam: domain event → VM refresh |
| `RefreshProjectSessions` / `RefreshProjectCounts` / `ActiveSession` / `ActiveProject` setters | **U** — pure UI selection/projection | `OC` |
| All overlay/form state (`ShowNewAgent`, `NewAgent*`, `ShowAbout`, `NewProject*`, `IsReviewOpen`, `CurrentView`…) | **U** | `OO` |
| `IDialogService? Dialogs` (confirm dialogs) | **U** | stays frontend; service takes a callback/decision |
| `_runtimeTimer`, `_scheduler`, `_nativeObservers` lifecycle | **S** | `Disp` timer → Core timer |

### 1.2 `AppViewModel.Run.cs` (~800 LoC) — **M, leans S** (THE orchestration heart)

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `RunTurnAsync` — adapter/exe resolve, worktree, SessionOptions assembly, AgentSession wiring, concurrency cap, try/catch/finally, native observer start/stop | **S** → `OrchestratorService.RunTurn` | `AC.Dispatcher` (only for the `Apply` callback), `EngineRegistry`, `_running` |
| `Apply(SessionViewModel, NormalizedEvent, tools)` — **the event→transcript reducer** | **S** (the reducer logic) + **U** (mutates `Blocks.cs`) | `OC` Transcript, `L()` i18n labels → seam: emit *domain transcript items*, VM reduces to WPF blocks |
| `EnsureWorktreeAsync` | **S** → `ReviewService`/`WorktreeService` (wraps `GitWorktree`) | `OC` transcript notice |
| `RefreshReviewAsync` / `LoadReviewDiffAsync` / `ScanSessionDiffsBackgroundAsync` / `MergeReviewAsync` / `DiscardReviewAsync` / `CommitReviewAsync` / `QueueLiveReviewRefreshAsync` | **S** → `ReviewService` (wraps `GitWorktree`) | `AC.Dispatcher`, `OC` Changes, `SaveState` |
| `SendDiffFeedbackAsync` | **M** | seam: service builds prompt + run |
| Choice flow: `AnswerCurrentChoice` / `ActivateChoice` / `PopulateQuickReplies` | **M** — wizard state machine + RunTurn trigger | `OC` |
| `RetranslateAsync` / `SendAsync` | **M** | seam: translation in Core, dispatch in VM |
| `StartNativeObserverAsync` / `StopNativeObserverAsync` / `UpsertNativeWorkItem` | **S** (observers are Core) + **U** (`UpsertNativeWorkItem` reads live `_allSessions`) | `AC.Dispatcher` |
| `BuildAttachedDocsText` | **S** | none |
| `LooksRateLimited` / `NativeHookSpoolDirectoryFor` / `SafeFileName` / `ClearNativeHookSpool` | **S** (pure helpers) | none |

### 1.3 `AppViewModel.Persistence.cs` (~700 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `ApplySettings` / `BuildSettingsDto` / `RestoreState` | **S** → `ProjectStore`/`SettingsService` hydration | reads/writes VM fields + `OC` (Projects/Sessions) |
| `SaveState` / `FlushStateNow` / `WriteSnapshot` / `ReportSaveResult` / debounce (`_saveDebounce` DispatcherTimer) | **S** → `ProjectStore` | `Disp` DispatcherTimer — **must be replaced with a Core timer** for headless |
| `BuildStateDto` / `BuildProjectStates` / `BuildSessionDto` | **M** — mapping; currently reads VM `OC` on UI thread | seam: service owns canonical session list → maps its own DTOs |
| `StartSettingsWatcher` / `DebouncedReloadAsync` / `ReloadSettingsFromDisk` | **S** → `SettingsService` (live reload) | `AC.Dispatcher`, `FW` |
| `WorktreesRoot` / `DefaultWorktreesRoot` | **S** | none |
| `LogPersistError` / `PersistErrorLogPath` | **S** | none |

### 1.4 `AppViewModel.Settings.cs` (~1,100 LoC) — **M** (largest fragment)

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| Backing config fields: `_claudePath/_codexPath/_agyPath/_piPath`, `_ollamaEndpoint/_ollamaModel`, `_translateSource/_translateTarget`, `_defaultModels`, `_preferred`, `_disabledEngines`, `_dismissedCliSessions`, `_warnNoWorktree`, `_approvalPolicy`, `_worktreeBase`, `_autoStartLastSession`, `_streamLogs`, `_telemetry` | **S** → `SettingsService` | none (plain fields) |
| Auth + rate-limit: `_engineAuthMode/_engineApiKey/_engineAutoApi/_engineLimitedUntil`, `HasApiKey`/`AutoApiOnLimit`/`IsEngineLimited`/`WillUseApiOnLimit`/`MarkRateLimited`/`ApiEnvFor`/`ApiEnvVar`/`SaveEngineAuth` | **S** → `SettingsService`/`AuthService` (DPAPI encrypt/decrypt is `Persistence.Dpapi`) | none |
| `UsageSnapshot`/`_usage` (also in Usage.cs) | **S** → `UsageService` | — |
| `QueryPiModelsAsync` / `QueryOllamaModelsAsync` / `RefreshOllamaStatusAsync` / `StartOllama` / `_piCatalog` | **S** → `EngineResolver`/`SettingsService` (engine introspection) | `AC.Dispatcher` (StartOllama) |
| `PolicyToSession` | **S** (pure policy→sandbox map) | none |
| `ApplyAndInjectSkill` / `_skillContent` / `_skillDirs` / `MergeSkillDirs` | **S** → `SettingsService` (writes SKILL.md via Core `SkillInjector`) | none |
| `SignIn` / `DetectEnginePath` / `DetectLabel` / `RefreshDetectLabels` / `CcAccount`/`GxAccount`/`AgyAccount` (`EngineAccounts`) | **M** — process launching S, label U | seam: `Launcher` service + VM label |
| Theme/Accent/Zoom: `_theme/_accent/_bodyScale/_modalScale`, `ZoomBy`/`ZoomReset`/toast timers | **U** (pure WPF `Theme.*`) | `OO`, DispatcherTimer |
| All `Settings*` mirror properties (`SettingsClaudePath`, `SettingsModelCc`, …) + `PullSettingsToEditor`/`OpenSettings`/`CloseSettings`/`SaveSettings`/`OpenSettingsFile` | **U** (editor form state) | `OO` |
| `NormalizeTranslationLang` / `Clean` / `ClampZoom` | **S** (pure) / **U** (zoom) | none |

### 1.5 `AppViewModel.WorkerTasks.cs` (~750 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `_taskStore` (already Core `WorkerTaskStore`) | **S** ✓ | — |
| `DriveWorkerAsync` / `RunOneAsync` / `RunQueueAsync` — execution loop (pull task → `RunTurnAsync` → capture report) | **M** → `WorkerDelegationService`/`OrchestratorService` | reads VM transcript (`worker.Transcript.OfType<AgentTextBlock>()`) — **seam: service captures the worker reply from the event stream, not the VM** |
| Spool ingest: `StartTaskSpoolWatcher` / `WatchSessionTaskSpool` / `RescanTaskSpool` / `ScheduleIngest` / `IngestSpoolFile` / `WithTaskSpoolEnv` | **S** → `SpoolIngestService` | `AC.Dispatcher`, `FW` |
| `InitWorkerTaskCommands` + all `*TaskCommand` | **U** | `RC` |
| `RebuildTaskViews` / `RebuildTaskReports` / `OnReportSelectionChanged` / `OnTaskStoreChanged` | **U** (projection from `_taskStore`) | `OC` |
| `CopyToClipboard` | **U** | `CB` |
| `WorkerTaskViewModel` / `WorkerQueueViewModel` (display wrappers) | **U** | `OO` |

### 1.6 `AppViewModel.Delegation.cs` (~180 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `CreateWorkerSession` | **M** (domain create + `OC`) | seam as in 1.1 |
| `DelegateAsync` — preamble + `RunTurnAsync` + capture final reply as report | **M** → `WorkerDelegationService` | reads VM transcript — same seam as DriveWorkerAsync |
| `InjectReport` / `InjectMergedReports` / `WorkerNameFor` | **S** (report routing into draft) | mutates `Draft` (VM) → seam: domain draft state |
| `Delegations` `OC` + `IsWorkerBusy` / `ReadyReportCount` | **M** (state S, projection U) | `OC` |

### 1.7 `AppViewModel.DelegationUi.cs` (~330 LoC) — **U** (pure UI)

Modal visibility, delegate-editor draft state (`DelegatePrompt`, `SelectedWorker`, `NewWorker*`),
`WorkerPool` projection, `BusyWorkers`, `RefreshWorkerPool`, `NotifyInbox`, all delegation commands,
`ConfirmDelegate`/`DelegateAll`/`RunDelegationAsync`/`PasteReport`/`Redelegate`/`OpenWorker`.
→ Stays entirely in GUI VM; CLI provides its own delegation UX. Couplings: `OO`, `RC`, `OC`.

### 1.8 `AppViewModel.Usage.cs` (~380 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `_usage` / `RecordUsage` / `RefreshQuotaText` / `CheckUsageAsync` / `ProbeUsageAsync` / `ParseUsageText` | **S** → `UsageService` (probes via `AgentSession`) | `AC.Dispatcher` in ProbeUsageAsync callbacks |
| `UsageSnapshot` record + `MarkRateLimited` interplay | **S** | — |
| `RebuildUsageRows` / `FormatUsageLine` / `AgeText` / `ResetText` / `Pct` | **M** — formatting is shared; `UsageRowVm`/`UsageBar` are U (`GridLength`) | `OO`, `System.Windows.GridLength` |
| `UsageRowVm` / `UsageBar` | **U** | `GridLength` |

### 1.9 `AppViewModel.History.cs` (~210 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `RebuildHistoryRows` / `ApplyHistoryFilters` / `MatchesHistoryFilters` / `OpenHistoryRow` | **U** (projection) | `OC` |
| `_dismissedCliSessions` + `LoadCliHistoryAsync` / `ImportCliSession` / `PopulateImportedTranscriptAsync` | **M** → `CliHistoryService` (calls Core `CliSessionDiscovery` + mutates VM) | `OC`, `AC.Dispatcher` |

### 1.10 `AppViewModel.Scheduling.cs` (~170 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| Overlay state (`ShowNewSchedule`, `NewSchedule*`) | **U** | `OO` |
| `CreateSchedule` / `LoadScheduledJobs` | **S** → `SchedulerService` (wraps Core `ScheduleStore`) | `OC` |
| `Scheduler_JobDue` / `RunScheduledJob` | **M** → `SchedulerService` orchestration (creates session + `RunTurnAsync`) | `AC.Dispatcher`, `OC`, `_allSessions` |

### 1.11 `AppViewModel.Artifacts.cs` (~210 LoC) — **M** (mostly pure helpers)

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `UpsertTaskListArtifact` / `UpsertTestArtifact` / `UpsertSummaryArtifact` / `GetOrAddArtifact` | **S** (artifact derivation from events) | mutates `ArtifactViewModel` (VM) → seam: domain artifacts |
| `ExtractCommand` / `IsTestCommand` / `KindOf` / `Trim` / `Slug` | **S** (pure) | none |
| `SuppressStderr` / `IsBenignStderr` / `_stderrDump` | **S** (stderr filtering, per-session state) | none |
| `FindRepoRoot` / `CreateTranslator` | **S** (pure) | none |

### 1.12 `AppViewModel.AskUser.cs` (~180 LoC) — **M**

| Responsibility (file:method) | Class | Coupling to sever |
|---|---|---|
| `WatchSessionAskSpool` / `ScheduleAskIngest` / `IngestAskFile` | **S** → `SpoolIngestService` | `AC.Dispatcher`, `FW` |
| `ParseChoiceItems` / `BuildChoiceItem` | **S** (pure parsing) | none |
| `ApplyChoiceTranslationAsync` | **M** (translation S + mutates `ChoiceFlow` U) | seam: translate in Core, VM owns display state |

### 1.13 `AppViewModel.Composer.cs` (~340 LoC) — **U** (input collection)

`UpdateComposerSuggestion`/`TriggerComposerSuggestion`/`CloseComposerSuggestion` (the `@`/`/`/`>` token
scanner + mention/file/slash/action catalogs), `ApplySelectedSuggestion` (incl. `>clear`/`>review`/`>settings`
in-app dispatch). → Stays in GUI VM; a CLI has its own input/autocomplete. Couplings: `OO`, `RC`.
(The `>clear`/`>review`/`>settings` actions delegate to `AppViewModel` commands — keep that indirection.)

### 1.14 `AppViewModel.NavCommands.cs` (~140 LoC) — **U** (pure UI)

Navigation/zoom/theme/auth-mode/sign-in segment command wiring (`InitNavCommands`). Stays in GUI VM.

### 1.15 `AppViewModel.Dashboard.cs` (~70 LoC) — **M** (leans U)

`CountBy`/`RefreshCounts`/`TotalTokensLabel`/`TotalCostLabel`/`FleetThroughputLabel`/`RefreshTotals`
(U projection of aggregates over `_allSessions`). `RefreshRunningSessions` (M — triggers live review).
Couplings: `OO`. The *aggregation* could be a Core query; the labels are U.

### 1.16 `AppViewModel.Update.cs` (~130 LoC) — **M**

`_updater` (Core `UpdateService`), `CheckUpdateAsync` (**S**), `ApplyUpdate` (**M** — launches updater +
`Application.Current.Shutdown()` is U). Couplings: `AC.Shutdown`.

### 1.17 `AppViewModel.About.cs` (~30 LoC) — **U** (pure UI)

`AppVersion` / `AboutBuildLabel`. Stays in GUI VM.

### 1.18 Supporting VM types (NOT AppViewModel fragments, but in scope)

| Type | Class | Note |
|---|---|---|
| `SessionViewModel.cs` | **M** | Holds domain state (Id, Status, tokens, worktree, model, role, draft) **+** pure UI (labels, `GridLength` stars, `BusyLine`, permission-mode chip mapping). **Split**: a Core `Session` domain record (data) + this VM (display, stays). |
| `Blocks.cs` (`TranscriptItem`/`UserBlock`/`AgentTextBlock`/`ToolBlock`/…) | **U** | These ARE the UI projection (`ObservableObject`, DataTemplate-rendered, `L()` labels). The DOMAIN transcript is `NormalizedEvent`; a CLI projects its own way. |
| `ChoiceModels.cs` (`ChoiceFlow`/`ChoiceItem`/`ChoiceOption`) | **U** | Quick-reply panel state machine. |
| `WorkerDelegationViewModel` | **U** | Display wrapper over Core `WorkerDelegation` record. |
| `ProjectViewModel` | **M** | Domain-ish (Id/Name/Path/ExtraPaths/McpConfigPath) + UI (`IsActive`, `RenameDraft`). |

### 1.19 Summary counts

- **Pure UI (stays):** DelegationUi(1.7), NavCommands(1.14), About(1.17), Composer(1.13), ChoiceModels,
  Blocks, plus the UI halves of mixed ones. **≈ 5 fragments + the UI projection types.**
- **Pure SERVICE (move wholesale, trivially):** essentially none — *every* S responsibility is currently
  interleaved with VM mutation. That interleaving is exactly what this overhaul dissolves.
- **MIXED (the work):** AppViewModel.cs(1.1), Run(1.2), Persistence(1.3), Settings(1.4), WorkerTasks(1.5),
  Delegation(1.6), Usage(1.8), History(1.9), Scheduling(1.10), Artifacts(1.11), AskUser(1.12),
  Dashboard(1.15), Update(1.16), SessionViewModel(1.18), ProjectViewModel(1.18). **≈ 13 mixed seams.**

---

## 2. PROPOSED SERVICE LAYER (the headless Core both frontends call)

Add these to `src/AgentManager.Core` (pure BCL — **no WPF, no NuGet**, per `Core.csproj` invariant).
The GUI `AppViewModel` becomes a **thin adapter**: holds service references, subscribes to the event
stream, projects to `ObservableCollection` on the UI thread, and binds commands to service methods.

### 2.1 Services & public operations

```
EngineResolver            (move EngineRegistry.cs here, minus EngineOptionVm)
  - EngineDef[] All, ResolveExe, CreateAdapter, IsInstalled, DetectExe,
    QueryPiCatalogAsync, OllamaExe, Get(id)
  → currently ViewModels/EngineRegistry.cs; verified WPF-free (only EngineOptionVm is UI).

SettingsService           (owns machine-local config + auth + rate-limit)
  - AppSettingsDto Snapshot(); Apply(AppSettingsDto);
  - ResolveExe paths, ollama endpoint/model, translation lang pair, defaults, disabled engines,
    dismissed CLI sessions, approval policy, worktree base, stream/auto-start flags;
  - HasApiKey/AutoApiOnLimit/IsEngineLimited/WillUseApiOnLimit/MarkRateLimited;
  - ApiEnvFor(engineId)  → IReadOnlyDictionary<string,string>;
  - Encrypt/Decrypt keys (DPAPI via Persistence.Dpapi — move Dpapi to Core or a thin port);
  - SkillInjector.Inject on save.
  Persists: %LOCALAPPDATA%/AgentManager/settings.json (SettingsStore moves to Core).

ProjectStore              (owns the canonical session/project/task state + hydration + debounced save)
  - Load() / Restore()  (the RestoreState logic, but against its OWN domain lists, not VM OC);
  - SaveNow()/ScheduleSave()  (debounce via a Core timer, NOT DispatcherTimer);
  - Projects, Sessions(domain), Tasks(via WorkerTaskStore) as canonical state;
  - BuildSessionDto / BuildProjectStates / BuildStateDto (mapping moves here).
  Persists: state.json (projects+active id) + per-project .am/project.json.

SessionService            (session lifecycle, domain)
  - Create / CreateWorker / Delete / Archive / Rename / Fork (pure domain; returns domain Session);
  - List/Query by project; selection is a frontend concern.

OrchestratorService       (THE turn loop — the heart of Run.cs:RunTurnAsync)
  - Task RunTurn(SessionRef, prompt, images, docs, ct) ;
  - Stop(sessionId);  IsRunning(sessionId);
  - Concurrency caps (MaxConcurrentSessions / MaxConcurrentWorkers);
  - Owns the _running CTS map; resolves adapter+exe via EngineResolver;
  - Wires AgentSession, starts/stops native observers, builds SessionOptions+env;
  - Emits NormalizedEvent per session + lifecycle events (Started/Completed/Failed).
  No Dispatcher, no Application.Current. Caller decides threading.

TranscriptProjector       (the event→transcript reducer, currently Run.cs:Apply)
  - Pure function of (domain transcript state, NormalizedEvent) → (new state, list of domain deltas).
  - No WPF blocks, no L() labels (emit neutral markers; frontend localizes).
  - Owns streaming AssistantDelta replace semantics, tool-table, stderr suppression (SuppressStderr),
    artifact derivation (Upsert*Artifact).

ApprovalBroker            (currently HandlePermissionAsync/ResolveApproval/ExpirePendingApprovals)
  - Task<PermissionDecision> Request(PermissionRequest);  Resolve(requestId, decision);
  - Raises ApprovalRequested event (frontend shows the card; CLI prompts on stdin).

UsageService              (currently Usage.cs)
  - ProbeAsync(engineId);  Record(QuotaUpdate);  Snapshot(engineId);
  - _usage snapshots; ParseUsageText.

WorkerDelegationService   (currently Delegation.cs + WorkerTasks.cs:DriveWorkerAsync)
  - DelegateAsync(main, worker, prompt, sharedWorktree) → capture worker reply from EVENT STREAM;
  - DriveWorkerQueue(workerId); RunOne(taskId);
  - Inject/InjectMerged reports into domain draft;
  - Wraps Core WorkerTaskStore for lifecycle.

SchedulerService          (currently Scheduling.cs)
  - Create/List/Delete jobs; wraps Core ScheduleStore + TimerScheduler;
  - RunScheduledJob orchestration (create session → OrchestratorService.RunTurn).
  (Raise: should jobs be project-local? see §3.)

SpoolIngestService        (currently WorkerTasks.cs + AskUser.cs spool watchers)
  - WatchTaskSpool(cwd, projectId, sessionId); WatchAskSpool(cwd, sessionId);
  - Ingest task files → WorkerTaskStore; ingest ask files → ChoiceRequest event.
  - FileSystemWatcher OK in Core; callbacks raised as events (no Dispatcher).

ReviewService             (currently Run.cs review/diff methods)
  - wraps Core GitWorktree: RefreshReview, GetDiff, Merge, Discard, Commit, ScanDiffs;
  - emits ReviewChanged events.

CliHistoryService         (currently History.cs)
  - wraps Core CliSessionDiscovery: Discover, LoadTranscript, Import.

Launcher                  (currently OpenIde/OpenProjectFolder/OpenAgyInTerminal/SignIn)
  - headless process launching (VS Code, explorer, wt, terminal, engine sign-in).
  Frontend decides whether to surface results.

UpdateRunner              (currently Update.cs)
  - wraps Core UpdateService: CheckAsync. Apply (relaunch) is frontend-specific.
```

### 2.2 The EVENT STREAM (NOT WPF events)

Core exposes **two layers of events**, both as plain `Action<T>`/`IObservable<T>` (no `Application.Current`):

1. **Per-turn engine events:** reuse the existing `AgentSession.EventReceived` (`NormalizedEvent`)
   — already headless. Frontends subscribe; the GUI reduces via a VM-side `Apply` that builds `Blocks.cs`.
2. **App-level domain events:** a single `IDomainEventBus` (or `IObservable<AppEvent>`) carrying:
   - `SessionLifecycle(sessionId, kind)` — created/started/done/error/stopped/deleted
   - `TranscriptDelta(sessionId, domainItem)` — from `TranscriptProjector`
   - `ApprovalRequested(sessionId, request)` — from `ApprovalBroker`
   - `TaskQueueChanged(projectId)` / `DelegationChanged(mainId)` — from stores
   - `UsageUpdated(engineId)` / `ReviewChanged(sessionId)` / `SettingsChanged` / `StatePersisted(ok)`
   - `ChoiceRequested(sessionId, flow)` — from ask spool
   - `NativeWorkChanged(sessionId, item)`

The GUI `AppViewModel` subscribes once in its ctor and marshals each handler to the UI thread
(`Dispatcher.InvokeAsync`) before touching `ObservableCollection`. A future CLI subscribes and prints.

> **Why this matters for (b):** these two event layers + the service operations in §2.1 are *exactly*
> the IPC surface (§6). Keep every operation data-in/data-out and every event serializable.

### 2.3 The GUI VM as thin adapter (after extraction)

```
AppViewModel
  ├─ holds: EngineResolver, SettingsService, ProjectStore, SessionService,
  │         OrchestratorService, WorkerDelegationService, SchedulerService,
  │         UsageService, ReviewService, SpoolIngestService, ApprovalBroker, Launcher
  ├─ ctor: new up services; subscribe each AppEvent → Dispatcher.InvokeAsync(Project…)
  ├─ ObservableCollections: Sessions/ActiveSessions/ProjectSessions/.../Transcript (Blocks.cs)
  ├─ Commands (RelayCommand): delegate to service.* , then the event bus refreshes the OCs
  └─ UI-only state: overlays, zoom, theme, composer suggestion, choice panel, attention
```

The `Apply` reducer moves OUT of the VM: `OrchestratorService` runs the turn → `TranscriptProjector`
produces domain deltas → `TranscriptDelta` event → VM appends the matching `Blocks.cs` item (localized).

---

## 3. STATE / PERSISTENCE SPLIT (confirm + flag)

**Confirmed (already done, commit 0e13df4 — see `Persistence/ProjectStateStore.cs`):**

| Scope | Location | Owner | Contents |
|---|---|---|---|
| **Project-local** (travels with project) | `<project>/.am/project.json` (`ProjectStateStore.PathFor`) | `ProjectStore` | sessions + worker backlog/queues (`ProjectStateDto.Sessions`/`WorkerTasks`) |
| **Machine-local** | `%LOCALAPPDATA%/AgentManager/` | service | |
| ↳ app state | `state.json` (`AppStateStore.StatePath`) | `ProjectStore` | projects list + active project id ONLY (sessions/tasks arrays now empty — legacy fields kept for migration) |
| ↳ settings | `settings.json` (`SettingsStore.SettingsPath`) | `SettingsService` | all `AppSettingsDto` (paths, ollama, auth/keys via DPAPI, rate-limit, models, theme, zoom…) |
| ↳ schedules | `schedules.json` (`ScheduleStore.StorePath`) | `SchedulerService` | `List<ScheduledJob>` |
| ↳ worktrees | `…/worktrees/` (`AppViewModel.WorktreesRoot`) | `ReviewService`/`ProjectStore` | per-session git worktrees |
| ↳ native hooks | `%APPDATA%/AgentManager/native-hooks/` (`Run.cs:NativeHookSpoolDirectoryFor`) | `OrchestratorService` | per-session hook spool |
| ↳ task spool | `%LOCALAPPDATA%/AgentManager/task-spool` (`TaskSpool.Root`) | `SpoolIngestService` | canonical global spool |
| ↳ session spools | `<cwd>/.am/worker-tasks/<sid>/`, `<cwd>/.am/ask/<sid>/` | `SpoolIngestService` | session-scoped (project-local) |
| ↳ attachments | `<cwd>/.am/attachments/` (`ImageAttachmentStore`/`Attachments`) | `OrchestratorService` | copied binary docs |

**Still machine-global that SHOULD be reconsidered for project-local (flag, don't block (a)):**

- **`schedules.json` is global but each `ScheduledJob` already carries `ProjectId`/`ProjectPath`
  (`Core/Scheduling/ScheduledJob.cs`). A schedule is project-scoped → it should live in
  `<project>/.am/schedules.json` so it travels with the project (same rationale as the session/task split
  in 0e13df4). Candidate to move; the migration mirrors the existing global→local one in `RestoreState`.
- **API keys (`EngineApiKey`, DPAPI blobs)** stay machine-local by necessity — DPAPI is per-user/machine.
  Do NOT move. (A daemon would hold these centrally; CLI clients never see the plaintext.)
- **`task-spool` root** is the *canonical* ingest dir; the per-session `.am/worker-tasks/<sid>/` is the
  project-local fallback. Keep as-is (already dual-path in `RescanTaskSpool`).

> **(a)-compat note:** nothing above forces a machine-global file to be project-local. Keep the split
> as-is for (a); only `schedules.json` is flagged for a possible later move.

---

## 4. WHAT STAYS UI (frontend — never enters Core)

Explicit list of inherently-frontend pieces (CLI provides its own equivalents):

- **Transcript block rendering** — `Blocks.cs` (`UserBlock`/`AgentTextBlock`/`ToolBlock`/`ErrorBlock`/
  `WorkingBlock`/`ThinkingBlock`/`ApprovalBlock`/`DelegationBlock`) are `ObservableObject` + per-type
  `DataTemplate`. The *domain* transcript is `NormalizedEvent`; blocks are the GUI projection.
- **Choice / quick-reply panel** — `ChoiceModels.cs` (`ChoiceFlow`/`ChoiceItem`/`ChoiceOption`),
  `PopulateQuickReplies` heuristic, the wizard UI (`AnswerCurrentChoice` UI half).
- **Composer input** — `AppViewModel.Composer.cs` (`@`/`/`/`>` suggestion scan, mention/file/action
  catalogs, `ApplySelectedSuggestion`), `PendingAttachment`, the composer `TextBox` code-behind.
- **Drag-drop / clipboard** — `SessionView.xaml.cs` (`Session_DragOver`/`Session_Drop`), `CopyToClipboard`,
  `PasteClipboardImage`, `AttachImage_Click`.
- **MarkdownViewer / DiffViewer** — `Controls/MarkdownViewer.cs`, `Controls/DiffViewer.cs`, `IconView`,
  `Spinner`, `StatusDot`, all `*BrushConverter`s, `GridLengthAnimation`, `BorderHover`, `MouseClick`.
- **Native work visualizers / computer-use** — `NativeWorkItemViewModel` rendering, any on-screen
  computer-use surfaces (observation data is Core; the rendering is UI).
- **Overlays / modals / zoom / theme** — all `Show*`/`NewAgent*`/`NewProject*`/`Settings*` mirrors,
  `BodyScale`/`ModalScale`/toast, `Theme.ThemePalette`/`Theme.AccentPalette`.
- **Attention signal** — `AttentionRequested` (taskbar flash/sound).
- **Dialogs** — `IDialogService` (confirm prompts); service receives a decision callback instead.
- **History table projection** — `HistoryRowViewModel`, filter UI.
- **Aggregates display** — dashboard labels (`TotalTokensLabel`, etc.); the *aggregation* can be a Core query but labels are UI.

---

## 5. MIGRATION ORDER (dependency-ordered; each step shippable, no behavior change)

**Guiding rule:** extract behind a seam, keep the VM calling it. After each step the GUI must build
and behave identically. Verify: `dotnet build src/AgentManager/AgentManager.csproj -c Release` → 0/0.

> Order is smallest-blast-radius first. Each step's "seam" = the method set moved + the VM call sites
> that now delegate. Risk rating in brackets.

### Step 0 — Relocate pure helpers to Core  [trivial, ~0.5d]
Move statics with zero WPF deps: `Slug`/`KindOf`/`Trim`/`IsTestCommand`/`ExtractCommand`
(`Artifacts.cs`), `IsBenignStderr`/`SuppressStderr`/`LooksRateLimited` (`Run.cs`/`Artifacts.cs`),
`ParseUsageText` (`Usage.cs`), `FindRepoRoot`/`CreateTranslator` (`Artifacts.cs`), `PolicyToSession`/
`ApiEnvVar`/`Clean`/`NormalizeTranslationLang` (`Settings.cs`), `Quote`/`FindVsCodeCli`/`TryResolveOnPath`
(`AppViewModel.cs`). Keep VM static forwards for one step, then inline.
**Seam:** none — pure relocation. **Risk:** none. Establishes Core can hold shared logic + the move workflow.

### Step 1 — `EngineResolver` → Core  [low, ~1d]  ⭐ **RECOMMENDED FIRST STEP**
Move `ViewModels/EngineRegistry.cs` (`EngineDef`/`PiCatalog` + all resolve/detect/query methods,
verified WPF-free) into Core. Leave `EngineOptionVm` (UI wrapper) in ViewModels. 9 fragments + 2 VMs
reference it; update `using`s. `EngineSlashCommands.cs` can move too (WPF-free).
**Seam:** `EngineRegistry.ResolveExe/CreateAdapter/IsInstalled/DetectExe/QueryPiCatalogAsync` become
`EngineResolver.*`. **Risk:** low — self-contained, no state, no threading. **Highest leverage:**
unblocks every downstream service (Run/Usage/Settings/Composer all need it). Proves the pipeline.

### Step 2 — `SettingsService` (config + auth + rate-limit) → Core  [medium, ~3–4d]
Move the config backing fields + `_engineAuthMode/_engineApiKey/_engineAutoApi/_engineLimitedUntil` +
`_defaultModels/_preferred/_disabledEngines/_dismissedCliSessions` into `SettingsService` holding an
`AppSettingsDto`. Move `ApiEnvFor`/`MarkRateLimited`/`IsEngineLimited`/`HasApiKey`/`SaveEngineAuth`/
`PolicyToSession`. Move `SettingsStore` (+ `Dpapi` port) to Core. `ApplySettings`/`BuildSettingsDto`
become service serialization. The VM keeps the `Settings*` editor mirrors; `SaveSettings` delegates.
**Seam:** VM holds `SettingsService`, reads/writes through it. **Risk:** many call sites (Run/Usage/
Composer/Delegation read config) — mechanical but wide. **Verify:** SaveSettings↔ReloadSettingsFromDisk
self-write loop still ignored; DPAPI round-trips.

### Step 3 — `ProjectStore` (hydration + debounced save) → Core  [medium-high, ~3–4d]
Move `RestoreState`/`SaveState`/`FlushStateNow`/`WriteSnapshot`/`BuildStateDto`/`BuildProjectStates`/
`BuildSessionDto` + the debounce timer (replace `DispatcherTimer` with a `System.Threading.Timer`).
**Crucial:** the store must own the **canonical domain session list** (introduce a Core `Session`
record mirroring `SessionViewModel`'s data fields), so DTO building no longer reads VM `OC`.
**Seam:** `ProjectStore.Sessions` is canonical; the VM's `_allSessions`/`OC`s become projections
(subscribe to store events). **Risk:** this inverts state ownership — touches every fragment that reads
`_allSessions`. Land it together with Step 4's session-model split. **Verify:** migration path
(legacy global → `.am/project.json`) still runs once.

### Step 4 — `Session` domain model + `TranscriptProjector` → Core  [HIGH, ~5–6d]  ⚠ riskiest
Split `SessionViewModel`: a Core `Session` domain record (data) + the VM (display). Extract the
`Apply(SessionViewModel, NormalizedEvent, tools)` reducer (`Run.cs`) into Core `TranscriptProjector`
that consumes `NormalizedEvent` and emits **domain transcript deltas** (neutral — no `L()` labels, no
WPF blocks). The VM reduces domain deltas → `Blocks.cs` (localized). Move artifact derivation
(`Upsert*Artifact`) + `SuppressStderr` into the projector.
**Seam:** `TranscriptProjector` is pure; VM has a thin `Apply(domainDelta)`. **Risk — top:** the reducer
is the event→UI nerve center (streaming `AssistantDelta` replace, live-review triggers, `SaveState`
on every event, attention signals). Streaming replace has NO correlation id (temporal only) — preserve
the `_liveText` semantics exactly. **Verify:** run a cc + gx + agy + pi turn each; transcript identical.

### Step 5 — `OrchestratorService` (RunTurnAsync) → Core  [HIGHEST, ~6–8d]  ⚠ riskiest
Lift `RunTurnAsync` + `EnsureWorktreeAsync` + concurrency caps + the `_running` CTS map + native
observer lifecycle into `OrchestratorService.RunTurn`. It wires `AgentSession`, builds `SessionOptions`
+ env, and emits `NormalizedEvent` (→ projector) + lifecycle events. **No Dispatcher, no
`Application.Current`.** `WorkerTasks.DriveWorkerAsync` + `Delegation.DelegateAsync` move here too —
they must capture the worker reply **from the event stream**, NOT from `worker.Transcript` (severs the
last VM read in the run path). `RunScheduledJob` (Scheduling) wires to it.
**Seam:** `OrchestratorService.RunTurn(sessionRef, …)`. **Risk — top:** core loop, cancellation,
process-tree kill, the permission broker round-trip. Do AFTER Step 4 so it has a clean event sink.
**Verify:** Stop kills the tree; concurrency caps enforced; approval blocks/round-trips; crash reconcile.

### Step 6 — Remaining services (smaller, mostly already-headless glue)  [low–medium, ~3–4d]
`UsageService` (`ProbeUsageAsync`/`RecordUsage`), `SchedulerService` (wrap `ScheduleStore`+
`TimerScheduler`+`RunScheduledJob`), `SpoolIngestService` (task+ask watchers, drop `AC.Dispatcher`),
`ReviewService` (wrap `GitWorktree`), `CliHistoryService` (wrap `CliSessionDiscovery`+import),
`Launcher` (process launching), `ApprovalBroker` (extract from `AppViewModel.cs`), `UpdateRunner`.
**Seam:** each VM handler delegates + subscribes. **Risk:** low — the heavy logic is already Core
(`WorkerTaskStore`, `GitWorktree`, `CliSessionDiscovery`, `TimerScheduler`); only the orchestration
glue + Dispatcher calls move.

After Step 6 the VM is the thin adapter of §2.3: services hold all state/logic; the VM only projects +
binds + collects input. A CLI can then be written against the same services (Phase a complete).

> **Execution note — Steps 3–6 took the Option-B path (in-process, single frontend), NOT the full
> ownership inversion above.** Decided at Step 3: inverting state ownership in-process (a canonical Core
> `Session` record + a `TranscriptProjector` the VM re-reduces + an `OrchestratorService` owning `_running`
> and the session list) costs a full VM↔Core sync — *especially the streaming transcript* — for **zero
> benefit until multiple frontends exist** (`NormalizedEvent` is already the headless domain transcript and
> `Run.cs:Apply` is already its thin frontend projection). So the inversion-flavoured parts of Steps 4 & 5
> are **deferred to phase (b)**, where they pay off (a closed window must not drop live sessions/approvals).
> What landed instead, as real headless extractions with no live-OC inversion (verified by usage):
> **Step 3** `ProjectStore<TSnapshot>` (save *mechanism*; 6642f26), **Step 5-core** `TurnPlanner`
> (`Core/Orchestration/` — engine-resolution decision tree + `SessionOptions` assembly; 757b93a),
> **Step 6** `UsageService` (`Core/Usage/` — snapshot map + capture/select rules; 92ef827). Also deferred as
> in-process churn (value only in phase b): `ApprovalBroker` (would split the pending map into two parallel
> maps), and `ReviewService`/`CliHistoryService`/`SchedulerService` (Core already owns the heavy logic via
> `GitWorktree`/`CliSessionDiscovery`/`ScheduleStore`; the VM glue is mostly OC rebuild + `L()` labels).

---

## 6. (b) DAEMON / IPC BOUNDARY MARKER

The future daemon line cuts **right above the service layer of §2.1/§2.2**:

```
   ┌─────────────────────────────────────────────────────────────┐
   │  FRONTENDS                                                   │
   │   GUI (AppViewModel + Views/Controls)   │   CLI (new)        │
   │   - projects/streams UI projection       │   - prints/events  │
   │   - Blocks.cs, choice panel, composer    │   - stdin prompts  │
   ├─────────────────────── IPC LINE (b) ────────────────────────┤   ← only this changes in (b)
   │  CORE SERVICE LAYER (the §2.1 ops + §2.2 event stream)      │
   │   EngineResolver · SettingsService · ProjectStore ·          │
   │   SessionService · OrchestratorService · ApprovalBroker ·    │
   │   WorkerDelegationService · SchedulerService · UsageService ·│
   │   SpoolIngestService · ReviewService · CliHistoryService …   │
   ├─────────────────────────────────────────────────────────────┤
   │  CORE DOMAIN (already headless)                             │
   │   AgentSession · WorkerTaskStore · GitWorktree · TimerScheduler│
   │   Adapters · NormalizedEvent · Translation · Observation …   │
   └─────────────────────────────────────────────────────────────┘
```

**IPC surface = exactly:** the public operations of every service in §2.1, PLUS the two event layers
in §2.2 (`NormalizedEvent` per session + `IDomainEventBus` app events) as a notifications channel.

**(a) must keep this surface (b)-compatible by obeying these rules NOW:**

1. **No UI types cross the service boundary.** Service ops take/return DTOs/records/`SessionRef`
   (ids), never `SessionViewModel`/`Blocks`/`RelayCommand`/`GridLength`.
2. **No `Application.Current`, no `Dispatcher`, no `DispatcherTimer`, no `ObservableCollection`,
   no `Clipboard` above the line.** (The single biggest current violation — every fragment uses them.)
3. **Operations are data-in/data-out + an event stream.** Every op must be serializable (the daemon
   will be named-pipe / JSON-RPC). No `Action` callbacks that capture UI closures — use the event bus.
4. **State lives in services, not the VM.** The `_running` CTS map, `_taskStore`, `_allSessions`,
   `_pendingApprovals`, `_usage`, debounce timers all move below the line (so a closed window doesn't
   drop them — the daemon survives).
5. **Permissions/decisions round-trip via events.** `ApprovalBroker` raises `ApprovalRequested`;
   whichever frontend is attached answers. (This is what makes a headless daemon viable: it never
   blocks on a GUI dialog.)
6. **Per-session affinity by id, not object ref.** `RunTurn(SessionRef, …)` — the daemon may have
   many attached clients; nothing may assume a singleton VM.

> If (a) obeys 1–6, then (b) is "swap the in-proc service impl for an IPC client + run the services in
> a daemon host." Nothing in the service layer is rewritten.

---

## 7. EFFORT + RISKS

### 7.1 Sizing (calendar days, one dev; includes verify)

| Step | What | Effort | Risk |
|---|---|---|---|
| 0 | Pure helpers → Core | ~0.5d | none |
| 1 ⭐ | `EngineResolver` → Core | ~1d | low |
| 2 | `SettingsService` | ~3–4d | medium |
| 3 | `ProjectStore` (state ownership flip) | ~3–4d | medium-high |
| 4 | `Session` model + `TranscriptProjector` | ~5–6d | **high** |
| 5 | `OrchestratorService` (RunTurn) | ~6–8d | **highest** |
| 6 | Remaining services | ~3–4d | low–medium |
| | **Total Phase (a)** | **~4–5 weeks** | |

### 7.2 Top 3 risks

1. **The `Apply` reducer / transcript coupling (`Run.cs:Apply`).** The whole event→UI nerve center
   mutates WPF `ObservableObject` blocks via `Application.Current.Dispatcher.Invoke`, calls `SaveState()`
   on every event, and fires live-review refreshes. Splitting domain deltas from UI projection is the
   most invasive change. **Specific trap:** streaming `AssistantDelta`→`AssistantText` replace has NO
   message-correlation id (temporal ordering only, per `maintainer-analysis.md` invariant #2); the
   `_liveText` map must be preserved exactly. **Mitigation:** characterize the reducer as a pure state
   machine first (Step 4), golden-transcript-test cc/gx/agy/pi turns before & after.

2. **The threading model / single-threaded-UI-thread assumption.** Today *everything* is the UI thread
   by contract: `WorkerTaskStore` is explicitly "call on a single thread (the UI thread)"
   (`Core/Workers/WorkerTaskStore.cs` doc); the save debounce is a `DispatcherTimer`
   (`Persistence.cs:SaveState`); `FileSystemWatcher` callbacks marshal via `AC.Dispatcher`
   (`WorkerTasks.cs:ScheduleIngest`, `AskUser.cs:ScheduleAskIngest`). A headless Core has **no
   Dispatcher** — it needs an explicit sync context / single-threaded scheduler / channels. Getting
   this right without races (esp. `_taskStore` + `_running` + debounce) is the cross-cutting risk.
   **Mitigation:** introduce ONE Core "command loop" thread (or `SynchronizationContext`) that all
   services marshal to; frontends subscribe on their own thread. Do not spread locks across services.

3. **State-ownership inversion (Step 3+4).** The canonical session list is currently `_allSessions` in
   the VM, and persistence DTOs (`BuildSessionDto`/`BuildProjectStates`) read it **on the UI thread**.
   Moving ownership to `ProjectStore`/`SessionService` inverts who holds truth — every fragment that
   reads/iterates `_allSessions` (Run/Delegation/WorkerTasks/History/Dashboard/Scheduling…) changes.
   **Mitigation:** land Step 3 (store) and Step 4 (session model) together so the canonical list and
   its DTO mapping move atomically; keep the VM's `OC`s as projections fed by store events.

### 7.3 The single best first step

**Step 1 — move `EngineResolver` (current `ViewModels/EngineRegistry.cs`) into Core.**
Verified WPF-free (only the `EngineOptionVm` UI wrapper stays), referenced from 9 fragments, no state,
no threading. Lowest risk, highest leverage: it is a prerequisite for `OrchestratorService`,
`SettingsService`, `UsageService`, and the composer, and it proves the extract-behind-a-seam workflow
end-to-end before touching the risky reducers/loop. Ship it, build 0/0, then proceed to Step 2.

---

## Appendix A — Quick seam-lookup (method → target service)

| Current location | Target service |
|---|---|
| `Run.cs:RunTurnAsync` | `OrchestratorService` |
| `Run.cs:Apply` | `TranscriptProjector` (+ VM `Apply`) |
| `Run.cs:RefreshReviewAsync`/`Merge*`/`Discard*`/`Commit*` | `ReviewService` |
| `Run.cs:EnsureWorktreeAsync` | `ReviewService`/`WorktreeService` |
| `Persistence.cs:RestoreState`/`SaveState`/`FlushStateNow`/`BuildSessionDto` | `ProjectStore` |
| `Persistence.cs:ApplySettings`/`BuildSettingsDto` | `SettingsService` |
| `Settings.cs:_engineAuthMode/_engineApiKey/_engineAutoApi/_engineLimitedUntil` + `ApiEnvFor`/`MarkRateLimited` | `SettingsService`/`AuthService` |
| `Settings.cs:ApplyAndInjectSkill` | `SettingsService` (Core `SkillInjector`) |
| `WorkerTasks.cs:DriveWorkerAsync` + `Delegation.cs:DelegateAsync` | `WorkerDelegationService` |
| `WorkerTasks.cs:StartTaskSpoolWatcher`/`WatchSessionTaskSpool`/`IngestSpoolFile` | `SpoolIngestService` |
| `AskUser.cs:WatchSessionAskSpool`/`IngestAskFile` | `SpoolIngestService` |
| `Usage.cs:ProbeUsageAsync`/`RecordUsage`/`_usage` | `UsageService` |
| `Scheduling.cs:CreateSchedule`/`RunScheduledJob` | `SchedulerService` |
| `History.cs:LoadCliHistoryAsync`/`ImportCliSession` | `CliHistoryService` |
| `Artifacts.cs:Upsert*Artifact`/`SuppressStderr` | `TranscriptProjector` |
| `AppViewModel.cs:HandlePermissionAsync`/`ResolveApproval`/`ExpirePendingApprovals` | `ApprovalBroker` |
| `AppViewModel.cs:OpenIde`/`OpenProjectFolder`/`OpenAgyInTerminal`/`Settings.cs:SignIn` | `Launcher` |
| `EngineRegistry.cs` (minus `EngineOptionVm`) | `EngineResolver` |
| `Update.cs:CheckUpdateAsync` | `UpdateRunner` |

## Appendix B — WPF couplings to sever (checklist per extraction)

For each method moving to Core, confirm NONE remain: `ObservableObject`/`Set(…)`, `RelayCommand`,
`Application.Current` (any), `Application.Current.Dispatcher`, `DispatcherTimer`, `ObservableCollection`,
`System.Windows.*` (incl. `GridLength`, `Visibility`, `Clipboard`, `Theme.*`), `App.L(...)`/`L(...)`
i18n (emit neutral markers; frontend localizes), `IDialogService` (raise an event / take a decision port).
