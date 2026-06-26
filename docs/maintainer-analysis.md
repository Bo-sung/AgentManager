# AgentManager — 유지보수자 분석 (Maintainer Analysis)

> 프로젝트를 유지보수 가능한 수준으로 이해하기 위한 부분별 레퍼런스.
> AgentManager의 워커 태스크 시스템으로 6개 부분을 병렬 분석해 생성 (Claude Sonnet 4.6).

## 목차
- Core: Agents & Session
- Core: Events, Observation, Hosting & Workspace
- Core: Workers & Scheduling
- UI: ViewModels
- UI: Views, Controls, Resources & Persistence
- Infrastructure: Bootstrap, Config & Tests

---

## Core: Agents & Session

### Project Dependencies

`AgentManager.Core.csproj` targets `net10.0` with no `<PackageReference>` or `<ProjectReference>` entries — the library is pure BCL only. `AgentManager.Core.Hosting` (for `ConPtyHost`) and `AgentManager.Core.Events` / `AgentManager.Core.Translation` are other namespaces within the same assembly.

---

### Types in `Agents/`

#### `AgentCapabilities` (sealed record)
Declares what an engine supports via boolean flags: `Permissions`, `Thinking`, `Sessions`, `Images`, `TokenUsage`, `Quota`. UI panels use this to show/hide controls. Constructed once per adapter instance.

#### `PermissionDecision` (sealed record)
User's verdict on a permission request: `Allow` (bool), optional `Reason` (string), `ForSession` (bool). `ForSession = true` maps to `"acceptForSession"` in the codex app-server protocol — it suppresses re-approval for the same tool type within the session.

#### `SandboxMode` (enum)
`ReadOnly | WorkspaceWrite | DangerFullAccess`. **Engine semantics differ significantly** — see invariants below.

#### `SessionOptions` (sealed record)
Immutable configuration bundle for one engine turn. Key properties: `WorkingDirectory`, `Model`, `ResumeSessionId`, `BypassPermissions`, `Sandbox`, `McpConfigPath`, `Images` (list of file paths), `AttachedDocsText` (pre-rendered verbatim doc content, prepended after translation), `AdditionalDirectories`, `ReasoningEffort`, `ExtraEnvironment` (extra env vars injected by `AgentSession`), `NativeHookSpoolDirectory`, `NativeHookCommand`, `BypassHookTrust`.

#### `IAgentAdapter` (interface)
Core contract for a CLI engine. Members:

| Member | Role |
|--------|------|
| `Id` | Engine identifier string (`"claude"`, `"codex"`, etc.) |
| `Capabilities` | `AgentCapabilities` |
| `CloseStdinAfterStart` | Whether stdin must be closed immediately after initial lines |
| `KillAfterTurnCompleted` | DIM, default `false`; true means `AgentSession` kills the process on `TurnCompleted` |
| `BuildStartInfo(executablePath, options, prompt)` | Creates `ProcessStartInfo`; prompt is already EN-translated |
| `InitialStdinLines(prompt, options)` | Lines written to stdin right after start |
| `ParseLine(line)` | Parses one stdout JSONL line → zero or more `NormalizedEvent` |
| `BuildPermissionResponse(request, decision)` | DIM returning `null`; overridden by Claude and codex-appserver |

#### `IPtyTurnRunner` (interface)
Alternative contract for TTY-only engines (`AgyAdapter`). Single method: `RunTurnAsync(executablePath, options, prompt, emitAsync, ct)`. The adapter drives the entire turn via ConPTY and feeds events through `emitAsync`. `AgentSession` checks `adapter is IPtyTurnRunner` and branches before spawning any process.

#### `StdioJsonAdapter` (abstract class : IAgentAdapter)
Base for `ClaudeAdapter`, `CodexAdapter`, `CodexAppServerAdapter`, `PiAdapter`. Provides guarded `ParseLine`: skips blank lines, swallows `JsonDocument.Parse` failures, clones `RootElement` (to outlive the `using` scope), then delegates to abstract `ParseRoot(JsonElement root, string line)`. Also re-declares `KillAfterTurnCompleted` and `BuildPermissionResponse` as `virtual` — see invariant §3.

#### `ClaudeAdapter` (sealed class : StdioJsonAdapter)
Claude Code CLI via `--output-format stream-json --input-format stream-json --verbose`. `CloseStdinAfterStart = false` (process stays alive waiting for stdin). `InitialStdinLines` emits: (1) `control_request/initialize` JSON; (2) `user` message with content array (text block + base64 image blocks for each path in `options.Images`). `ParseRoot` maps:

- `system/init` → `SessionStarted(session_id, model, tool_count, cwd)`
- `rate_limit_event` → `QuotaUpdate(utilization or -1, resetsAt, rateLimitType, status)`
- `assistant` → `TokenUsage` + `AssistantText` / `Thinking` / `ToolUseStarted` per content block
- `user` with `tool_result` content → `ToolResult(tool_use_id, content, is_error, isSubagent)`
- `control_request/can_use_tool` → `PermissionRequest(request_id, tool_name, input_json, tool_use_id)`
- `result` → `TurnCompleted(result, is_error, total_cost_usd, num_turns, turnUsage)`

`BuildPermissionResponse`: allow → `control_response` with `behavior="allow"`, `updatedInput`, `toolUseID`; deny → `behavior="deny"`, `message`, `interrupt=true`. `AddNativeHookSettings` (private) injects `--settings <JSON>` with `SubagentStart`/`SubagentStop` hooks when `options.NativeHookCommand` is set.

#### `CodexAdapter` (sealed class : StdioJsonAdapter)
Codex CLI `exec --json`. `CloseStdinAfterStart = true` — stdin must close or Codex hangs at "Reading additional input from stdin…". Prompt is a positional CLI argument; `InitialStdinLines` returns `[]`. Resume mode (`options.ResumeSessionId` non-null) uses `exec resume <id>` — a different sub-command that omits `-C` (working directory flag) and replaces `--sandbox` with `-c sandbox_mode=`. `ParseRoot` maps:

- `thread.started` → `SessionStarted(thread_id, null, 0, null)`
- `item.started` with `command_execution` → `ToolUseStarted(id, "shell", {command})`
- `item.started` with `file_change` → `ToolUseStarted(id, "apply_patch", raw)`
- `item.completed` with `command_execution` → `ToolResult(id, aggregated_output, exit_code != 0)`
- `item.completed` with `file_change` → `ToolResult(id, raw, status == "failed")`
- `item.completed` with `agent_message` → `AssistantText(text)`
- `turn.completed` → `TokenUsage` + `TurnCompleted(null, false, null, null)`
- `error` → `EngineError(message)`
- `turn.failed` → `EngineError(error.message)` + `TurnCompleted(null, true, null, null)`

#### `CodexAppServerAdapter` (sealed class : StdioJsonAdapter)
Codex `app-server` JSON-RPC over stdio. `CloseStdinAfterStart = false`, `KillAfterTurnCompleted = true`. **Stateful — single-use per turn** (see invariant §1). Instance fields: `_options`, `_prompt`, `_threadId` (set during handshake), `_lastUsage` (accumulated token count), `_turnFailed`. Three fixed JSON-RPC request IDs: `InitializeId=1`, `ThreadId=2`, `TurnId=3`. Handshake sequence:

1. `InitialStdinLines` → sends `initialize` request (id=1)
2. `ParseRoot` receives id=1 response → emits two `EngineWriteback` lines: `initialized` notification + `thread/start` (or `thread/resume`) request (id=2)
3. Receives id=2 response → extracts `_threadId` from `result.thread.id`, emits `SessionStarted`, emits `EngineWriteback` for `turn/start` (id=3)
4. Subsequent messages are server notifications (no id) or server→client requests (with id, requires response)

Token usage from `thread/tokenUsage/updated` (cumulative) is stored in `_lastUsage` without emitting; only flushed on `turn/completed`. `BuildPermissionResponse` encodes `decision.ForSession ? "acceptForSession" : "accept"` vs `"decline"`. Always uses `approvalPolicy=untrusted` + `sandbox=danger-full-access` (Windows sandbox spawn is not feasible).

#### `AgyAdapter` (sealed class : IAgentAdapter, IPtyTurnRunner)
Antigravity `agy` Gemini CLI. Dual interface — IAgentAdapter is a stub (`BuildStartInfo` throws `NotSupportedException`; `ParseLine` returns `[]`); real work is in `RunTurnAsync`. Calls `ConPtyHost.RunAsync(cmd, workingDirectory, 10min, ct)`, strips VT escape sequences via `ConPtyHost.StripVt`, then reads the resume conversation ID from `~/.gemini/antigravity-cli/cache/last_conversations.json` (cwd-keyed map). Emits `SessionStarted` (if ID found) + `AssistantText` + `TurnCompleted`, or `EngineError` + `TurnCompleted` on non-zero exit. No permissions, no token events, no streaming. Command line assembled with `Win32Args.Quote`.

#### `PiAdapter` (sealed class : StdioJsonAdapter)
pi.dev RPC mode. `KillAfterTurnCompleted = true`. Spawns `node <dist/cli.js> --mode rpc` (`executablePath` is the JS file; `node` is the actual executable). `InitialStdinLines` sends `get_state` then `prompt` (with base64 images). **Stateful — single-use per turn** (fields `_lastUsage`, `_turnErrored`). `ParseRoot` maps:

- `response` (success=true, command=get_state) → `SessionStarted(sessionId, model, 0, null)`
- `message_update/text_delta` → `AssistantDelta(delta)`
- `message_update/thinking_end` → `Thinking(content)` (emitted whole, not streamed)
- `tool_execution_start` → `ToolUseStarted(toolCallId, toolName, args)`
- `tool_execution_end` → `ToolResult(toolCallId, content, isError)`
- `message_end` → accumulates `_lastUsage`; if `stopReason="error"`, sets `_turnErrored` + `EngineError`; else emits final `AssistantText`
- `agent_end` → `TurnCompleted(null, _turnErrored, null, null, _lastUsage)`

`extension_ui_request` (permission protocol) is silently ignored in v1.

#### `AdapterJson` (internal static class)
Shared helpers used by all stdio adapters: `Utf8NoBom` (`UTF8Encoding(false)`), `Str(JsonElement, name)` / `Lng(JsonElement, name)` null-safe accessors, `NewStdioStartInfo(executablePath, workingDirectory)` creating a redirect+no-window+BOM-free-UTF8 `ProcessStartInfo`.

#### `ToolNames` (internal static class)
Constants shared across Codex exec and app-server: `Shell = "shell"`, `ApplyPatch = "apply_patch"`, `WebSearch = "web_search"`.

#### `Win32Args` (internal static class)
`Quote(string)` implements Win32 command-line quoting rules (backslash doubling only immediately before `"`). Used exclusively by `AgyAdapter.BuildCommandLine` for ConPTY command assembly.

---

### Types in `Session/`

#### `AgentSession` (sealed class)
Owns and drives one engine process. Constructor parameters: `IAgentAdapter adapter`, `string executablePath`, `ITranslator? translator`, `bool translationEnabled`. Key surface:

| Member | Role |
|--------|------|
| `Adapter` | Read-only property exposing the adapter |
| `TranslationEnabled` | Settable; can be flipped between turns |
| `EventReceived` | `Action<NormalizedEvent>?` — fires after translation for every event |
| `PermissionHandler` | `Func<PermissionRequest, Task<PermissionDecision>>?` — blocks the read loop until resolved; auto-denies if null |
| `RunAsync(options, userPrompt, ct)` | Async turn entry point |

---

### Key Relationships

```
AgentSession
  ├── IAgentAdapter  (BuildStartInfo, InitialStdinLines, ParseLine,
  │                   BuildPermissionResponse, CloseStdinAfterStart, KillAfterTurnCompleted)
  │     ├── StdioJsonAdapter (abstract base)
  │     │     ├── ClaudeAdapter
  │     │     ├── CodexAdapter
  │     │     ├── CodexAppServerAdapter
  │     │     └── PiAdapter
  │     └── AgyAdapter  (also implements IPtyTurnRunner)
  ├── IPtyTurnRunner  (checked via `adapter is IPtyTurnRunner`; bypasses process lifecycle)
  └── ITranslator     (optional; KO→EN on input; EN→KO on AssistantText + subagent ToolResult)
```

`AdapterJson`, `ToolNames`, `Win32Args` are internal utility classes — no external consumers.

---

### Agent Lifecycle (AgentSession.RunAsync)

1. **Input translation** — if `TranslationEnabled && translator != null`: `translator.TranslateAsync(userPrompt, SourceToTarget)`. If changed, emit `PromptTranslated(englishPrompt)` for inspection.
2. **Document prepend** — if `options.AttachedDocsText` non-empty, prepend to `prompt` (after translation; content must stay verbatim).
3. **PTY branch** — if `adapter is IPtyTurnRunner pty`: `pty.RunTurnAsync(…, EmitTranslatedAsync, ct)` then return. `AgentSession` owns no process.
4. **Process spawn** — `adapter.BuildStartInfo(executablePath, options, prompt)` → apply `options.ExtraEnvironment` into `psi.Environment` → create `options.NativeHookSpoolDirectory` if non-empty, inject `AGENTMANAGER_HOOK_SPOOL` → `Process.Start()`.
5. **Cancellation wiring** — `ct.Register` callback: `proc.Kill(entireProcessTree: true)`.
6. **stderr pump** — `Task.Run` draining stderr lines → `EngineError(line)` via `Emit`.
7. **Stdin init** — write `adapter.InitialStdinLines(prompt, options)` + flush. If `adapter.CloseStdinAfterStart`, close stdin.
8. **Stdout read loop** — for each stdout line, `adapter.ParseLine(line)` → for each `NormalizedEvent`:
   - `EngineWriteback`: write `wb.Line` to stdin + flush; `continue` (never surfaced to caller).
   - All other events: `EmitTranslatedAsync(ev, ct)` (applies EN→KO translation on `AssistantText` and `ToolResult` where `FromSubagent=true && !IsError`), then `Emit(ev)` → `EventReceived?.Invoke(ev)`.
   - `PermissionRequest`: call `PermissionHandler` (awaited; blocks loop), write `adapter.BuildPermissionResponse(pr, decision)` to stdin if non-null.
   - `TurnCompleted`: if `stdinOpen`, close stdin, set `stdinOpen=false`. If `adapter.KillAfterTurnCompleted`, kill process.
9. **Teardown** — `proc.WaitForExitAsync(ct)` + await stderr pump.

---

### Non-Obvious Invariants

**1. `CodexAppServerAdapter` and `PiAdapter` are single-use instances.**
Both hold mutable per-turn state in instance fields (`_options`, `_prompt`, `_threadId`, `_lastUsage`, `_turnFailed`). The `CodexAppServerAdapter` comment explicitly marks this: "인스턴스는 턴(프로세스) 1회용 — 상태를 가지므로 재사용 금지." Create a fresh adapter instance per `RunAsync` call.

**2. `EngineWriteback` is strictly internal — never reaches `EventReceived`.**
`AgentSession.RunAsync` handles `EngineWriteback` with `continue`, skipping translation and emission. Any subscriber on `EventReceived` will never observe this type. Do not add logic to `EmitTranslatedAsync` that handles `EngineWriteback`.

**3. `StdioJsonAdapter` must re-declare every new `IAgentAdapter` DIM as `virtual`.**
C# DIM dispatch uses the static type of the call site. `AgentSession` holds `IAgentAdapter adapter` — interface type. If a derived class overrides a DIM without `StdioJsonAdapter` also declaring a `virtual`, the override is silently ignored when calling through `IAgentAdapter`. `StdioJsonAdapter` already does this for `KillAfterTurnCompleted` and `BuildPermissionResponse`. Any new DIM added to `IAgentAdapter` **must** get a matching `virtual` in `StdioJsonAdapter`.

**4. `AttachedDocsText` must be prepended after translation, never before.**
The document content is verbatim (code, logs, etc.) and must not pass through `ITranslator`. The prepend is the last step before `BuildStartInfo` or `InitialStdinLines`. Do not restructure this order.

**5. `CodexAdapter` resume vs non-resume CLI surface is incompatible.**
`exec resume <id>` does not accept `-C` (working directory) or `--sandbox`; it takes `-c sandbox_mode=` instead. The `resuming` flag in `BuildStartInfo` guards a separate argument-building path. Merging these paths will break one mode.

**6. Codex exec hangs unless stdin is closed immediately after start.**
`CodexAdapter.CloseStdinAfterStart = true`. Prompt goes as a positional CLI arg; `InitialStdinLines` returns `[]`. `AgentSession` closes stdin after writing initial lines if this flag is set. Never change this or add stdin writes after start for `CodexAdapter`.

**7. `CodexAppServerAdapter` token usage is cumulative, emitted once.**
`thread/tokenUsage/updated` fires multiple times with running totals — not deltas. The adapter stores only the last value in `_lastUsage` and emits it as part of `TurnCompleted`. Emitting each update would cause callers to overcount tokens.

**8. `AgyAdapter.BuildStartInfo` throws `NotSupportedException`.**
This is by design — `AgyAdapter` implements `IAgentAdapter` only to satisfy the interface contract; `AgentSession` detects `IPtyTurnRunner` and never calls `BuildStartInfo`. Safe stub, but calling it directly (e.g., in tests) will throw.

**9. `SandboxMode` semantics are engine-specific and non-uniform.**

| Engine | `ReadOnly` | `WorkspaceWrite` | `DangerFullAccess` |
|--------|-----------|-----------------|-------------------|
| Claude | `--permission-mode plan` (no real sandbox) | `--permission-prompt-tool stdio` | `--permission-prompt-tool stdio` or `--dangerously-skip-permissions` |
| Codex exec | `--sandbox read-only` | `--sandbox workspace-write` | `--dangerously-bypass-approvals-and-sandbox` (if `BypassPermissions`) |
| Codex app-server | always `danger-full-access` + `approvalPolicy=untrusted` | same | same |
| Agy / Pi | no sandbox support | no sandbox support | no sandbox support |

**10. `NativeHookSpoolDirectory` side effect belongs to `AgentSession`, not the adapter.**
`AgentSession.RunAsync` calls `Directory.CreateDirectory(options.NativeHookSpoolDirectory)` and injects `AGENTMANAGER_HOOK_SPOOL` into the process environment before `Process.Start()`. This is intentional: adapters' `BuildStartInfo` must remain pure (no I/O). Do not move directory creation into adapter code.

**11. `AgyAdapter` session ID is read post-turn from a cache file.**
After `ConPtyHost.RunAsync` returns, `TryReadConversationId` parses `~/.gemini/antigravity-cli/cache/last_conversations.json` to find the conversation ID matching `options.WorkingDirectory`. If the file doesn't exist or the cwd isn't listed, `SessionStarted` is not emitted. Resume via `options.ResumeSessionId` passes `--conversation <id>` on the command line.

**12. No NuGet or external dependencies.**
`AgentManager.Core.csproj` has an empty item group — only BCL types are used. All namespaces (`AgentManager.Core.Events`, `AgentManager.Core.Translation`, `AgentManager.Core.Hosting`) are co-located in the same assembly. Adding a NuGet package here would be a structural change.

---

---

## Core: Events, Observation, Hosting & Workspace

### 1. Events

**Source:** `AgentManager.Core/Events/NormalizedEvent.cs`

All event types are C# positional `record` types that inherit from the abstract base record `NormalizedEvent`. There is no event bus, channel, or reactive stream in this assembly — dispatching is the responsibility of callers (e.g., adapter classes in the WPF layer).

| Type | Key Properties | Notes |
|---|---|---|
| `SessionStarted` | `SessionId`, `Model?`, `ToolCount`, `Cwd?` | Emitted once per session init |
| `PromptTranslated` | `SentText` | Fires **only when KO→EN translation changes the text** (inspection use) |
| `AssistantText` | `Text`, `FromSubagent`, `OriginalText?` | EN→KO translation target; replaces preceding `AssistantDelta` |
| `AssistantDelta` | `Delta` | Streaming fragment — **consumers must replace it when the final `AssistantText` for that message arrives** |
| `Thinking` | `Text` | Claude reasoning block; usually not translated |
| `ToolUseStarted` | `ToolUseId`, `Name`, `InputJson` | Tool invocation began |
| `ToolResult` | `ToolUseId`, `Content`, `IsError`, `FromSubagent`, `OriginalContent?` | Only subagent (Task) results are translation targets |
| `TokenUsage` | `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheCreationTokens`, `ReasoningTokens` | Running per-event accounting |
| `QuotaUpdate` | `Utilization`, `ResetsAtUnix`, `RateLimitType`, `Status` | Claude `rate_limit_event` — dashboard use |
| `PermissionRequest` | `RequestId`, `ToolName`, `InputJson`, `ToolUseId?` | Claude `can_use_tool` permission gate |
| `TurnCompleted` | `FinalText?`, `IsError`, `CostUsd?`, `NumTurns?`, `Usage?` | **`Usage` is authoritative for the turn total** — consumers must reconcile accumulated `TokenUsage` events against it |
| `EngineError` | `Message` | Engine stderr / error event |
| `RawUnknown` | `Type`, `Raw` | Forward-compat sink; safely ignored by consumers |
| `EngineWriteback` | `Line` | **Internal adapter protocol only** — stateful handshake lines (e.g., codex `initialized`, `thread.start`). `AgentSession` logs these and **must not forward them to the UI**. |

**Ordering guarantees:** None are specified in this layer. All records are immutable; ordering is the adapter's responsibility.

---

### 2. Observation

**Source:** `AgentManager.Core/Observation/`

#### Core Abstractions

**`INativeWorkObserver`** — the single observer interface:
- `string EngineId` — identifies which engine this observer targets.
- `Task StartAsync(NativeWorkObservationTarget target, CancellationToken ct)` — begin observing. Implementations that use `FileSystemWatcher` or polling start their background machinery here.
- `Task<IReadOnlyList<ObservedWorkItem>> SnapshotAsync(CancellationToken ct)` — pull all known items at a point in time.
- `event EventHandler<ObservedWorkItem>? WorkItemChanged` — push notification fired after every upsert.

**`NativeWorkObservationTarget`** (record):
`EngineId`, `ParentSessionId`, `WorkingDirectory`, `EngineSessionId?`, `ManagedByAgentManager`

**`ObservedWorkItem`** (sealed record) — engine-agnostic view of a work unit:
- Identity: `Id`, `EngineId`, `ParentSessionId`, `VendorParentSessionId?`, `VendorWorkId?`, `AgentId?`
- Classification: `Kind` (`WorkItemKind` enum), `State` (`ObservedState` enum), `Source` (`ObservationSource` enum), `Confidence` (`ObservationConfidence` enum)
- Display: `AgentType?`, `DisplayName?`, `Cwd?`, `TranscriptPath?`, `AgentTranscriptPath?`, `LastMessage?`, `Error?`, `RawJson?`
- Timing: `ManagedByAgentManager`, `StartedAt`, `LastActivityAt`, `CompletedAt?`

**Enums:**
- `WorkItemKind`: `Session`, `NativeSubagent`, `NativeBackgroundSession`, `NativeTask`, `AgentManagerWorker`
- `ObservedState`: `Unknown`, `Starting`, `Running`, `Waiting`, `WaitingPermission`, `Completed`, `Failed`, `Stopped`
- `ObservationSource`: `Unknown`, `Hook`, `AppServerEvent`, `ExecJson`, `Transcript`, `FileSystem`, `ProcessPoll`
- `ObservationConfidence`: `Low`, `Medium`, `High`

#### Implementations

**`HookSpoolNativeWorkObserver(string engineId, string spoolDirectory)`**
- Watches `spoolDirectory` for `*.json` files written by the hook PowerShell script.
- On file arrival, waits 50ms (writer release delay), then calls `NativeHookEvent.TryParse` → `ToObservedWorkItem` → `ApplyFailureInference` → `Merge` → fires `WorkItemChanged`.
- `ApplyFailureInference`: inspects `AgentTranscriptPath` via `SubagentTranscriptInspector.Inspect` and checks `LastAssistantMessage` via `SubagentTranscriptInspector.LooksLikeLimit`. **A hook-reported `Completed` item can be silently promoted to `Failed`** if transcript or message contains a rate-limit phrase.
- Merge semantics (via `ConcurrentDictionary.AddOrUpdate`): `Unknown` state never overwrites a known state; `StartedAt` is set only once (first arrival); `CompletedAt` is sticky.

**`AgyNativeWorkObserver(string? userProfile)`** (`EngineId = "agy"`)
- Targets Antigravity/agy cache at `~/.gemini/antigravity/brain/{conversationId}/.system_generated/`.
- Resolves `conversationId` from `target.EngineSessionId`, else from `~/.gemini/antigravity-cli/cache/last_conversations.json` keyed by CWD.
- Watches three subdirectories: `logs/transcript.jsonl` (scans for `INVOKE_SUBAGENT`/`invoke_subagent` lines, emits `NativeSubagent` with `Medium` confidence), `messages/*.json` (state updates keyed by `conversationId`/`sender`/`recipient`), `tasks/*.log` (emits `NativeTask` with `Low` confidence). All watchers use 80ms delay before ingesting.
- JSON parsing is recursive (`FindString`, `FindFirstStringInArray`, `ContainsText`) — field names may appear at any nesting depth.

**`ClaudeBackgroundSessionObserver(string exePath, TimeSpan? interval)`** (`EngineId = "cc"`)
- Polls `claude agents --json` via `ClaudeAgentsProbe.RunAsync` every 8 seconds (default).
- Excludes: the managed session itself (`IsManagedSelf`), sessions whose `cwd` is not in the same working-tree branch as `target.WorkingDirectory` (`SameWorkingTree` — prefix match, case-insensitive).
- Items disappearing from the poll result are marked `Stopped` on the next cycle — **transition lag is up to one full poll interval (8s default)**.
- `ObservationSource = ProcessPoll`, `Confidence = High`.

**`CompositeNativeWorkObserver(string engineId, params INativeWorkObserver[] children)`**
- Starts all children sequentially in `StartAsync` (subscribes `WorkItemChanged` on each before starting).
- `SnapshotAsync` returns the concatenated results of all children — **no deduplication across children**.
- `WorkItemChanged` from any child is forwarded to callers unchanged.

#### Supporting Types

**`NativeHookEvent`** (sealed record) — parsed hook JSON. `ToObservedWorkItem(string? parentSessionIdOverride, bool managedByAgentManager)` maps `SubagentStop` → `Completed`, `SubagentStart` → `Running`, all other events → `Unknown`. Item `Id` is `{engineId}:{parentSessionId}:{agentId}` when `agentId` is present, else `{engineId}:{parentSessionId}:{event}:{timestampMs}`.

**`NativeHookCommandFactory`** (static) — generates PowerShell hook command strings:
- `WindowsPowerShellSpoolScript(string spoolDirectory)` — writes `am-hook-spool.ps1` to disk; returns `powershell -File <path>` invocation.
- `WindowsPowerShellSpoolWriter(string? spoolDirectory)` — inline `-Command` one-liner that reads stdin and writes to `$env:AGENTMANAGER_HOOK_SPOOL` (or a literal path). Hook scripts name files `{yyyyMMddHHmmssfff}-{guid}.json` (UTC).

**`ClaudeAgentsProbe`** (static) — `Parse(string json)` and `RunAsync(string exePath, ...)` parse `claude agents --json` output into `ClaudeAgentRow(int Pid, string? Cwd, string Kind, long StartedAtUnixMs, string SessionId)`. All errors return empty list — observation is best-effort.

**`SubagentTranscriptInspector`** (static) — reads JSONL transcript for API error lines (`isApiErrorMessage: true`). `SubagentFailure(bool Failed, bool RateLimited, string? Message)`. Rate-limit phrases: `"weekly limit"`, `"usage limit"`, `"rate limit"`, `"rate_limit"`, `"5-hour limit"`, `"five hour"`, `"limit · resets"`, `"limit, resets"`. `InspectLine` is public for smoke-test fixture use. `LooksLikeLimit(string? text)` checks freeform text (e.g., `LastAssistantMessage`) against the same list.

---

### 3. Hosting

**Source:** `AgentManager.Core/Hosting/ConPtyHost.cs`

There is **no DI container setup, no `IHostedService`, and no service registration in this directory**. The Hosting namespace contains a single static utility class.

**`ConPtyHost`** (static, Windows-only via `kernel32.dll` P/Invoke)
- Purpose: Launches TTY-only CLIs (specifically `agy`/Antigravity) that refuse to work without a real pseudo-terminal, and captures their output.
- `RunAsync(string commandLine, string cwd, TimeSpan timeout, CancellationToken ct)` → `(string Output, int ExitCode)`:
  1. Creates two anonymous pipes (input/output) via `CreatePipe`.
  2. Creates a pseudo-console (120 columns × 40 rows) via `CreatePseudoConsole`.
  3. Adds `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` to a `STARTUPINFOEX` attribute list.
  4. Spawns the process via `CreateProcessW`.
  5. Closes the PTY-side pipe handles (`inRead`, `outWrite`) — these must not be held by the host.
  6. Reads output in a background task; waits for process exit with timeout; kills with `entireProcessTree: true` on timeout.
  7. Calls `ClosePseudoConsole` to flush the output pipe to EOF, then waits up to 3s for the reader task.
- `StripVt(string s)` — removes VT/ANSI CSI escape sequences (`VtRegex`) and OSC sequences (`OscRegex`), plus bare ESC and BEL characters.

**Critical:** `ConPtyHost` must not be called on non-Windows. There is no runtime guard; calling it on Linux/macOS will throw `DllNotFoundException`.

---

### 4. Translation

**Source:** `AgentManager.Core/Translation/`

**`ITranslator`**:
- `Task<string> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct)` — failures **must** fall back to original text (contract comment).
- `bool ContainsKorean(string text)`.

**`TranslationDirection`**:
- `SourceToTarget` — user input (source language) → engine (target language); happens before sending, reducing engine token cost.
- `TargetToSource` — engine output (target language) → display (source language).

**`OllamaTranslator(OllamaOptions options, HttpClient? http)`** — sole implementation:
- Calls Ollama `/api/generate` (non-streaming, `temperature=0.1`, `keep_alive=30m`).
- **Localhost normalization**: `localhost` is always replaced with `127.0.0.1` because .NET `HttpClient` resolves `localhost` to `::1` (IPv6) while Ollama's default binding is `127.0.0.1` only.
- **Pre-translation masking** (`Mask`/`Restore`): fenced code blocks (`FencedCodeRegex`), inline code (`InlineCodeRegex`), and `@mention`/`@"..."` patterns (`MentionRegex`) are replaced with `[[N]]` placeholders before translation and restored after. This preserves file paths, code, and references.
- **Prompt framing**: `"INPUT:\n{text}\n\nOUTPUT:"` — prevents instruction-tuned models from acting on imperative inputs instead of translating them.
- **Skip logic**: uses `ScriptFor(language)` to get a script-identifying regex. `SourceToTarget` skips if no source-language characters are present. `TargetToSource` skips if ≥50% of letters are source-language characters (majority vote, not single-char presence).
- **Retry**: 2 attempts. Second attempt doubles the timeout (cold-model load). User cancellation is never retried. Falls back to original text on all remaining failures.
- **`OllamaOptions`** (record): `Endpoint` (`http://localhost:11434`), `Model` (`exaone3.5:7.8b`), `Timeout` (60s), `SourceLanguage` (`"Korean"`), `TargetLanguage` (`"English"`).
- **Static helpers**: `PingAsync(string endpoint, int timeoutMs)` checks `/api/tags`; `ListModelsAsync(string endpoint)` parses the `models[].name` array.
- **Script matchers** (all `[GeneratedRegex]`): `KoreanRegex` (`[가-힣ᄀ-ᇿ㄰-㆏]`), `JapaneseRegex` (kana only — CJK excluded due to Chinese overlap), `CjkRegex`, `CyrillicRegex`, `ArabicRegex`, `DevanagariRegex`. Latin-family languages return `null` from `ScriptFor` — translation is always attempted for them.

---

### 5. Workspace

**Source:** `AgentManager.Core/Workspace/`

#### `GitWorktree` (static)

A "workspace" in this context is a **git worktree** — an isolated working directory branched from the project HEAD, created per agent session so parallel agents never clobber each other or the main working tree.

Records: `WorktreeInfo(string Path, string Branch, bool Isolated)`, `FileChange(string Path, ChangeKind Kind, int Added, int Deleted)`.  
Enum: `ChangeKind` (Added, Modified, Deleted, Renamed, Untracked).

Key methods:
- `IsGitRepoAsync(string dir)` — guard check.
- `CreateAsync(string repoPath, string sessionId, string branch, string worktreesRoot)` — `git worktree add -B {branch} {worktreesRoot}/{sessionId} HEAD`. `-B` replaces a stale branch of the same name; returns `null` if the repo check fails or `git` exits non-zero.
- `RemoveAsync(string repoPath, string worktreePath)` — `git worktree remove --force`.
- `DiscardAsync(string worktreePath)` — `git reset --hard HEAD` then `git clean -fdx` (tracked + untracked, including `.gitignore`d files).
- `CommitAsync(string worktreePath, string commitMessage)` — `git add -A` (**stages all changes**) then `git commit`. Commit stays on the agent branch only.
- `MergeAsync(string repoPath, string branch, string commitMessage, string worktreePath)` — commits to agent branch then `git merge --no-ff {branch}` into the main working tree. On failure, `git merge --abort` is called and changes remain safely on the agent branch.
- `GetChangedFilesAsync(string worktreePath)` — combines `git status --porcelain=v1` and `git diff --numstat HEAD`.
- `GetDiffAsync(string worktreePath, string? file)` — `git diff HEAD`; synthesizes a `+++ b/{file}` style diff for untracked files (capped at 4000 lines with `... diff truncated ...`).

#### `CliSessionDiscovery` (static)

Discovers past CLI sessions written to disk by `claude` and `codex` CLIs outside AgentManager.

Records: `CliHistoryEntry(string EngineId, string SessionId, string Title, DateTime LastWriteUtc, string FilePath)`, `CliTranscriptItem(string Role, string Name, string Text)`.

- **`DiscoverClaude(string projectPath, int max)`**: Scans `~/.claude/projects/{encoded}/`, where encoding replaces every non-alphanumeric character in the absolute path with `-` (`ClaudeProjectDirName`). Reads up to 80 lines of each `.jsonl` to find the first non-sidechain user message. Skips files with no user messages (queue fragments, `isSidechain=true` entries).
- **`DiscoverCodex(string projectPath, int max)`**: Scans `~/.codex/sessions/**/ rollout-*.jsonl` (up to 400 files, most-recent first). CWD is read from the first line's `payload.cwd`. Sessions with `payload.thread_source == "subagent"` are skipped. Title resolution: `~/.codex/session_index.jsonl` (last-wins for same `id`) → first `event_msg/user_message` → `"codex session"`.
- **`LoadTranscript(string engineId, string filePath, int maxItems)`**: Dispatches to `LoadClaudeTranscript` or `LoadCodexTranscript`. Claude extracts `text`, `thinking`, and `tool_use` content blocks. Codex extracts `event_msg` types (`user_message`, `agent_message`, `agent_reasoning`) and `response_item` tool calls. Tool input truncated to 300 characters.

---

### 6. Non-Obvious Invariants for Maintainers

1. **`EngineWriteback` must never reach the UI.** It carries adapter-internal stateful handshake lines (codex `initialized`, `thread.start`, `turn.start`). The adapter/`AgentSession` layer is responsible for intercepting and consuming these. Passing them through to the UI will corrupt the display.

2. **`AssistantDelta` is provisional.** UI consumers must replace the in-progress streaming display when the final `AssistantText` event for the same message arrives. There is no message-correlation ID in the event record — temporal ordering is the only link.

3. **`TurnCompleted.Usage` supersedes streaming `TokenUsage` events.** Engines report a final authoritative usage in `TurnCompleted`; accumulated per-event totals may diverge. Consumers must reconcile on `TurnCompleted`.

4. **Observer upsert merge is field-level with one-way ratchets.** `State == Unknown` never overwrites a known state. `StartedAt` is set only on first upsert (`existing.StartedAt == default`). `CompletedAt` is sticky. Changing merge precedence in `HookSpoolNativeWorkObserver.Merge` or `AgyNativeWorkObserver.Upsert` will silently regress state visibility.

5. **`ApplyFailureInference` overrides hook-reported state.** A subagent that the hook reports as `Completed` will be reclassified to `Failed` if its transcript or `LastAssistantMessage` contains a rate-limit phrase. The hook payload is not authoritative for terminal state.

6. **`ClaudeBackgroundSessionObserver` transitions disappearances to `Stopped` lazily.** A session that exits will appear `Running` for up to one full poll interval (8s default) before being marked `Stopped`. Code that acts on `Stopped` state immediately after session exit will race.

7. **`OllamaTranslator` normalizes `localhost` to `127.0.0.1`.** If Ollama is ever configured to bind to `::1` or a hostname, this normalization will break connectivity. The `Ipv4()` helper applies globally to all endpoint use within the class.

8. **`GitWorktree.CommitAsync` always runs `git add -A`** — it stages all changes in the worktree directory, including any files created by the agent outside of tracked paths. There is no selective staging.

9. **`ConPtyHost` is Windows-only.** All methods call `kernel32.dll` via P/Invoke with no OS guard. Invoking on Linux/macOS throws `DllNotFoundException` at the first `CreatePipe` call.

10. **`AgentManager.Core.csproj` has zero NuGet dependencies** — the entire Core library uses only BCL types (`System.Text.Json`, `System.Diagnostics.Process`, `System.Net.Http`, `FileSystemWatcher`). Do not add framework-specific packages (e.g., Microsoft.Extensions.*) without ensuring the WPF host project composition remains coherent.

---

## Core: Workers & Scheduling

### 1. Type Inventory

#### Workers (`AgentManager.Core.Workers`)

**`SessionRole`** (enum) — classifies a session as `Plain` (ordinary), `Main` (issues delegations), or `Worker` (receives and executes delegations).

**`DelegationState`** (enum) — lifecycle of a single main→worker delegation: `Pending` → `Running` → `Ready` → `Consumed` (or `Failed`). `Ready` means the worker finished but the report has not yet been injected into the main session; `Consumed` means it has.

**`WorkerDelegation`** (sealed record) — pure data record for one main→worker delegation instance. Fields: `Id`, `MainSessionId`, `WorkerSessionId`, `Prompt` (original text before preamble), `Report?` (worker's final output), `State` (`DelegationState`), `CostUsd`, `SharedWorktree` (false = isolated worktree), `CreatedAt`, `CompletedAt?`.

**`WorkerDefaults`** (static class) — holds the default worker behavior preamble text (`BehaviorPreamble` const, verbatim injected before every delegated task), `DefaultMaxConcurrentWorkers` const (value: `2`), `ComposePrompt(string behaviorPreamble, string task)` (combines preamble + `## Task` header + task body), and `MergeReports(IReadOnlyList<(string Worker, string Report)> reports)` (joins multiple worker reports with `## Worker N (id)` headers).

**`WorkerTaskDto`** (sealed record) — a task in the project backlog or a worker's queue. Persisted in app state. Fields: `Id` (prefix `t` + 12 hex chars), `ProjectId`, `Title` (first line of prompt if not provided, capped at 60 chars), `Prompt`, `Engine` (hint: `"cc"`, `"gx"`, `"agy"`, `"pi"`, or empty), `Status` (string; see `WorkerTaskStatus`), `AssignedWorkerId` (empty = unassigned), `Order` (ascending within a worker's queue; 0 while in backlog), `CreatedUtc` (ISO 8601 string).

**`WorkerTaskStatus`** (static class) — lifecycle string constants: `Backlog`, `Assigned`, `Running`, `Done`, `Failed`. Helper predicates: `IsQueued(string s)` returns true for `Assigned` or `Running`; `IsFinished(string s)` returns true for `Done` or `Failed`.

**`TaskSpool`** (static class) — manages the on-disk spool directory where the worker-prompt skill drops task JSON files. `Root` property: `%LOCALAPPDATA%/AgentManager/task-spool`. `DirFor(string projectId)` appends the sanitized project ID. `ReadFile(string path, string projectId)` deserializes a single `SpoolTask` object or a JSON array of `SpoolTask` objects; returns `IReadOnlyList<WorkerTaskDto>` (empty on any parse error — caller must retain the file for retry).

**`SpoolTask`** (`file`-scoped sealed record, internal to `WorkerTask.cs`) — the minimal on-disk shape written by the skill: `title?`, `prompt?`, `engine?`. Not visible outside the file.

**`WorkerTaskStore`** (sealed class) — owns all worker-task domain logic. See §2 and §4 for details.

---

#### Scheduling (`AgentManager.Core.Scheduling`)

**`ScheduleTrigger`** (sealed record) — describes when a job fires. Fields: `Kind` (string: `"Cron"` or `"Event"`), `CadenceText` (human-readable), `CronExpression?` (explicit 5-field cron), `TargetPath?` (for Event kind). `GetNextRunUtc(DateTime? lastRunUtc)` computes the next fire time (Cron only; Event always returns `null`). `TryParseCadenceToCron(string cadenceText)` (static) converts human phrases ("every day at 09:00", "매일 09:00", "monday 14:30", etc.) to a 5-field cron string.

**`CronExpressionEvaluator`** (internal sealed class) — parses a 5-field cron string and iterates forward minute-by-minute to find the next match. Fields `_minutes`, `_hours`, `_daysOfMonth`, `_months`, `_daysOfWeek` are `HashSet<int>?` (`null` = wildcard `*`). Day-7 is normalized to 0 (Sunday). `GetNextRunUtc(DateTime baseTime)` starts at `baseTime + 1 minute` and iterates up to 5 years; throws `InvalidOperationException` if no match is found within that window. `ParseField` (private static) handles `*`, ranges (`-`), lists (`,`), and step expressions (`/`).

**`ScheduleDueEventArgs`** (class, inherits `EventArgs`) — carries the `ScheduledJob Job` that fired.

**`IScheduler`** (interface) — `event EventHandler<ScheduleDueEventArgs>? JobDue`, `Start()`, `Stop()`.

**`TimerScheduler`** (sealed class, implements `IScheduler`, `IDisposable`) — sole scheduler implementation. Ticks every 30 seconds via `PeriodicTimer` and fires `JobDue` for overdue enabled jobs.

**`ScheduledJob`** (sealed record) — one scheduled task. Fields: `Id`, `AgentId` (`"cc"`, `"gx"`, `"agy"`), `ProjectId`, `ProjectPath`, `Title`, `Prompt`, `TargetBranch`, `Trigger` (`ScheduleTrigger`), `Enabled`, `LastRunUtc?`. Computed property `NextRunUtc` = `Enabled ? Trigger.GetNextRunUtc(LastRunUtc) : null` — evaluated on every access, not persisted.

**`ScheduleStore`** (static class) — persists `List<ScheduledJob>` to `%LOCALAPPDATA%/AgentManager/schedules.json`. `StorePath` is settable (used by smoke tests to redirect to a temp file). `Load()` uses `JsonFile.ReadOrDefault` (defined elsewhere in Core); `Save(List<ScheduledJob> jobs)` uses `JsonFile.WriteAtomic` (also defined elsewhere).

---

### 2. WorkerTask Lifecycle: Creation → Assignment → Execution → Completion

**Creation (Backlog entry)**

The worker-prompt skill writes a JSON file (single object or array) to the task spool directory at `TaskSpool.DirFor(projectId)`. The file shape is `SpoolTask` (`title`, `prompt`, `engine`). The WPF host watches this directory and calls `WorkerTaskStore.IngestFile(string path)` or `IngestFile(string path, string projectId)` when a new file appears. `IngestFile` delegates to `TaskSpool.ReadFile`, which constructs `WorkerTaskDto` records with `Status = WorkerTaskStatus.Backlog`, `AssignedWorkerId = ""`, `Order = 0`, and a generated `Id` (`"t" + 12 hex chars`). The task is appended to `_tasks` and `Changed` is raised. **The WPF host is responsible for deleting the spool file after ingestion** (this is not done inside Core).

**Assignment (Backlog → Assigned)**

The user selects a backlog task and a target worker in the UI. The host calls `WorkerTaskStore.Assign(string taskId, string workerId)`. This:
1. Computes `nextOrder` = max `Order` among the worker's currently `IsQueued` tasks + 1.
2. Updates the record: `AssignedWorkerId = workerId`, `Status = WorkerTaskStatus.Assigned`, `Order = nextOrder`.
3. Raises `Changed`.

The `Assign` method also accepts tasks in `Failed` state (the guard only checks that `workerId` is non-empty and the task exists). A `Running` task can technically be re-assigned but this is not guarded — the store does not prevent it.

**Execution start (Assigned → Running)**

The host polls `WorkerTaskStore.NextRunnable(string workerId)` to determine whether to start the next task on a given worker. `NextRunnable` returns `null` if **any** task for that worker is in `Running` state; otherwise it returns the first `Assigned` task in `Order` ascending. When the host decides to execute the task, it calls `WorkerTaskStore.SetStatus(taskId, WorkerTaskStatus.Running)` and launches the engine session. This transition is driven entirely by the host — the store has no timer or trigger of its own.

**Completion (Running → Done or Failed)**

When the engine session finishes, the host calls `WorkerTaskStore.SetStatus(taskId, WorkerTaskStatus.Done)` or `SetStatus(taskId, WorkerTaskStatus.Failed)`. `SetStatus` is a direct in-place replacement with no other side effects beyond raising `Changed`. After this, `NextRunnable` for that worker will return the next `Assigned` task (if any).

**Crash recovery**

On app startup, before any execution begins, the host must call `WorkerTaskStore.ReconcileInterrupted()`. This re-queues any `Running` tasks back to `Assigned` (because their worker session was lost in the crash and can never complete them). `ReconcileInterrupted` returns the count of tasks reclassified and raises `Changed` if any were found.

---

### 3. Workers, Tasks, and the Scheduler — Relationships

**Workers and Tasks**

`WorkerTaskStore` has no direct reference to worker objects or engine sessions. The link is the `AssignedWorkerId` string. The host (WPF `AppViewModel`) is responsible for:
- Iterating known workers and calling `NextRunnable(workerId)` to find runnable tasks.
- Launching the actual engine session when a task is runnable.
- Calling `SetStatus` in response to engine events.

This is a **polling model** from the host's perspective: the host observes `WorkerTaskStore.Changed` and/or polls `NextRunnable` to decide when to start tasks. There is no push mechanism inside `WorkerTaskStore` that triggers execution.

**Scheduler and Workers**

The scheduling subsystem (`TimerScheduler`) is **entirely separate** from the worker-task queue. `TimerScheduler` fires `JobDue` with a `ScheduledJob` — it does not create `WorkerTaskDto` records or interact with `WorkerTaskStore`. The host connects these two systems if it chooses: a `JobDue` handler could enqueue a new task via `IngestFile` or `Assign`, but this wiring is not present in Core.

**Thread ownership**

- `WorkerTaskStore` is explicitly **not thread-safe** (documented in the class summary): all calls must be on a single thread (the UI thread).
- `TimerScheduler` runs its `PeriodicTimer` loop on a **background thread** via `Task.Run` in `Start()`. `EvaluateJobs()` is called from that background thread. The `JobDue` event is therefore raised on the background thread — subscribers must marshal to the UI thread if they need to mutate UI state.
- `TimerScheduler` uses a `_lock` object to protect `_jobs` across the timer thread and calls to `Reload()` / `Stop()`.

---

### 4. Queue and Backlog Mechanics

**`WorkerTaskStore` internal storage**

All tasks live in a single `List<WorkerTaskDto> _tasks`. There is no separate data structure for the backlog vs. queues — all states coexist in the same list. Queries materialize filtered/sorted views using LINQ on each call.

**Status semantics**

| Status | Meaning | `AssignedWorkerId` | `Order` |
|---|---|---|---|
| `Backlog` | Unassigned, in project backlog | `""` | `0` |
| `Assigned` | Queued on a worker, waiting to run | worker session ID | ≥ 1 |
| `Running` | Currently executing | worker session ID | ≥ 1 |
| `Done` | Terminal success | worker session ID | preserved |
| `Failed` | Terminal failure | worker session ID | preserved |

`Done` and `Failed` tasks remain in `_tasks` (and in `AssignedTo(workerId)`) until explicitly removed by `ClearFinished(workerId)` or `Delete(taskId)`.

**Dequeue / next-task selection**

`NextRunnable(string workerId)`:
1. If `_tasks.Any(t => t.AssignedWorkerId == workerId && t.Status == Running)` → returns `null`. Enforces strict one-at-a-time execution per worker.
2. Otherwise, returns `QueueFor(workerId).FirstOrDefault(t => t.Status == Assigned)` — i.e., the lowest-`Order` assigned task.

`QueueFor(string workerId)` returns tasks where `IsQueued` is true (Assigned or Running), ordered by `Order` ascending. Running tasks appear in this view, which is why they show in the worker's active queue list.

**Order assignment on Assign**

`Assign` computes the new `Order` as `Max(existing queued orders) + 1`. "Queued" here means `IsQueued` (Assigned or Running). Finished tasks' `Order` values are preserved but not considered, so newly assigned tasks always append after the last active item.

**Reordering**

`Move(string taskId, int dir)` swaps the `Order` values of two adjacent tasks in `QueueFor`. Only tasks where `IsQueued` is true can be reordered; calling `Move` on a `Done` or `Failed` task is a no-op (the `IsQueued` guard in `Move` prevents it).

**Persistence**

`WorkerTaskStore` does not persist itself. `Snapshot()` returns a `List<WorkerTaskDto>` copy. The host calls `Snapshot()` in its `Changed` handler and writes to its own persistence layer.

---

### 5. Non-Obvious Invariants for Maintainers

1. **`WorkerTaskStore` is single-threaded by contract.** The class comment explicitly states "call on a single thread (the UI thread)." Calling `IngestFile`, `Assign`, `SetStatus`, or any mutation from a background thread (e.g., a `FileSystemWatcher` callback or a `TimerScheduler` `JobDue` handler) introduces races on `_tasks`. Always marshal to the UI thread before calling any `WorkerTaskStore` method.

2. **`ReconcileInterrupted` must run before the first `NextRunnable` call.** If a task is left in `Running` state (app crash), `NextRunnable` permanently returns `null` for that worker until the task is re-queued. The host must call `ReconcileInterrupted()` at startup before enabling any task execution path.

3. **`NextRunnable` is a blocking serializer per worker.** As long as ANY task for a worker has `Status == Running`, `NextRunnable` returns `null` for all queued tasks on that worker. If the host calls `SetStatus(id, Running)` and the engine session is later lost without a `SetStatus` call for the terminal state, that worker is permanently stalled until `ReconcileInterrupted` is called again (which requires an app restart).

4. **`Assign` accepts `Failed` tasks.** A failed task can be re-assigned to a worker (the only check is `workerId` non-empty and task existence). The resulting state is `Assigned` with a fresh `Order`. This is intentional as a retry mechanism but maintainers must be aware that `Failed` is not a permanent terminal state from the store's perspective — only `ClearFinished` removes them.

5. **Spool file project ID is derived from the immediate parent directory name.** `IngestFile(string path)` uses `Path.GetFileName(Path.GetDirectoryName(path))` as the project ID. If spool files are dropped anywhere other than directly under `TaskSpool.DirFor(projectId)`, the derived project ID will be wrong (e.g., empty string or a random folder name). The overload `IngestFile(string path, string projectId)` must be used for skill fallback paths (`.am/worker-tasks/`).

6. **`SpoolTask` is `file`-scoped** (`file sealed record`). It is invisible to any other file. If you need to test or extend spool parsing, you cannot reference `SpoolTask` directly — all spool logic is mediated through `TaskSpool.ReadFile`.

7. **`TimerScheduler.Start()` must use `Task.Run`.** The inline comment explains the deadlock: if the `PeriodicTimer` continuation is bound to the UI `SynchronizationContext`, and `Stop()` calls `GetAwaiter().GetResult()` on the UI thread, the result is a classic deadlock (UI thread blocked waiting for a continuation that needs the UI thread). Never remove the `Task.Run` wrapping in `Start()`.

8. **`TimerScheduler.Stop()` blocks the calling thread.** It calls `taskToWait?.GetAwaiter().GetResult()`. This is safe if called from a non-UI thread, but if called directly from the UI thread it will deadlock (see above). The `Dispose()` method calls `Stop()` — ensure `Dispose` is not called synchronously on the UI thread.

9. **`EvaluateJobs` fires `JobDue` after updating `LastRunUtc` and saving to disk.** If the `JobDue` handler throws, the exception is swallowed and the job is still marked as run. A failed handler does not cause a retry — the job's `LastRunUtc` is already committed to `schedules.json`. If reliable execution semantics are needed, the handler must implement its own retry logic.

10. **`ScheduledJob.NextRunUtc` is computed on every property access**, not cached. `CronExpressionEvaluator.GetNextRunUtc` iterates forward minute-by-minute. For jobs with large gaps between `LastRunUtc` and now, or restrictive cron expressions, this iteration could be slow. Do not call `NextRunUtc` in a tight loop over many jobs.

11. **`ScheduleTrigger` Event kind is unimplemented.** `GetNextRunUtc` returns `null` for `Kind != "Cron"` with a comment "NotImplemented: Event-based trigger evaluation is not implemented in v1." `TimerScheduler.EvaluateJobs` checks `job.NextRunUtc.HasValue` — so Event-kind jobs silently never fire. There is no error, log, or fallback.

12. **`DelegationState`/`WorkerDelegation` and `WorkerTaskStatus`/`WorkerTaskDto` are two parallel delegation systems.** `WorkerDelegation` tracks real-time main→worker session delegations (prompt sent, report received, consumed). `WorkerTaskDto` tracks the backlog/queue-managed task lifecycle. They share the concept of "worker session ID" but have no direct cross-reference in Core — the host is responsible for any coordination between them.

13. **`CronExpressionEvaluator` advances from `baseTime + 1 minute`, not `baseTime`.** The constructor of the next run time is `baseTime.AddMinutes(1)`. This means a job with `LastRunUtc = now` will never re-fire in the same minute, even if the cron expression matches the current minute. This is intentional to prevent double-firing on the same tick.

14. **`AgentManager.Core.csproj` has zero NuGet dependencies.** Both `Workers` and `Scheduling` use only BCL types (`System.Text.Json`, `System.IO`, `System.Threading`). `JsonFile.ReadOrDefault` and `JsonFile.WriteAtomic` (used in `ScheduleStore`) are not in these directories — they must be defined elsewhere in `AgentManager.Core`. If you move `ScheduleStore` to a new project, those utilities must move with it.

---

## UI: ViewModels

### ViewModel Hierarchy

```
ObservableObject (Mvvm.cs)
├── AppViewModel  (sealed partial — 16 files)
├── SessionViewModel  (sealed)
├── ProjectViewModel  (sealed)
├── WorkerDelegationViewModel  (sealed)
├── WorkerTaskViewModel  (sealed)
├── WorkerQueueViewModel  (sealed)
├── ReviewChangeViewModel  (sealed)
├── ArtifactViewModel  (sealed)
├── NativeWorkItemViewModel  (sealed)
├── ModelChecklistVm  (sealed)
├── ModelChoice  (sealed)
└── TranscriptItem  (abstract)
    ├── UserBlock, AgentTextBlock, ToolBlock
    ├── ErrorBlock, ThinkingBlock, WorkingBlock
    ├── ApprovalBlock, DelegationBlock

Plain classes (no INPC):
├── ScheduledJobViewModel  (thin immutable wrapper — NOT ObservableObject)
├── HistoryRowViewModel   (immutable row — NOT ObservableObject)
├── CliHistoryItemViewModel  (thin wrapper — NOT ObservableObject)
├── UsageRowVm, UsageBar  (immutable UI data — NOT ObservableObject)
├── EngineOptionVm  (computed display — NOT ObservableObject)
└── ComposerSuggestionItem  (sealed record)

Records / support:
├── PendingAttachment  (sealed record — not persisted)
├── LanguageDef  (sealed record)
└── EngineDef, PiCatalog  (sealed records in EngineRegistry.cs)

Static:
├── EngineRegistry  (engine catalog + adapter factory)
└── TranscriptExporter  (ToMarkdown helper)
```

---

### One-Sentence Responsibility Per Type

| Type | Responsibility |
|---|---|
| `AppViewModel` | Root VM; owns all sessions, projects, navigation state, commands, settings, worker-task store, scheduling, delegation, and cross-cutting file watchers. |
| `SessionViewModel` | Mutable state for one agent session — transcript, token/cost counters, runtime labels, worktree path, role (Plain/Main/Worker), and draft input. |
| `ProjectViewModel` | Project folder, display name, session count badge, MCP config path, and extra `--add-dir` paths. |
| `WorkerDelegationViewModel` | One main→worker delegation card (state machine: Pending→Running→Ready/Failed→Consumed) with report text and cost delta. |
| `WorkerTaskViewModel` | Immutable display projection of one `WorkerTaskDto`; rebuilt from `WorkerTaskStore` on every `Changed` event. |
| `WorkerQueueViewModel` | Per-worker task queue header (worker name + engine) with its ordered `WorkerTaskViewModel` list. |
| `ReviewChangeViewModel` | Wraps `FileChange` from `GitWorktree`; computes `KindLabel` (A/D/R/U/M) and `StatLabel` (+N / -N). |
| `ArtifactViewModel` | Lightweight evidence artifact (kind: tasklist/test/summary) derived from events; stamped `UpdatedAt` on content change. |
| `NativeWorkItemViewModel` | Live display wrapper for `ObservedWorkItem`; updated in place via `Update()` as the observer emits changes. |
| `ScheduledJobViewModel` | Thin immutable wrapper around `ScheduledJob`; computes `NextRunLabel`; rebuilt on every `LoadScheduledJobs`. |
| `HistoryRowViewModel` | Immutable snapshot row for the History view; built via `FromSession()` or `FromDto()`; supports full-text `Matches()`. |
| `CliHistoryItemViewModel` | Wraps `CliHistoryEntry` discovered by `CliSessionDiscovery`; drives the sidebar CLI HISTORY list. |
| `ModelChecklistVm` | Per-engine "preferred models" checklist in Settings; owns `ModelChoice` items and syncs to an external `HashSet<string>` owned by `AppViewModel`. |
| `ModelChoice` | Single model in a checklist; `IsChecked` setter calls back into `ModelChecklistVm.Toggle` immediately. |
| `TranscriptItem` | Abstract base for all blocks; subclasses are rendered by per-type `DataTemplate` in XAML. |
| `UsageRowVm` / `UsageBar` | Immutable UI data for the usage card; `UsageBar` exposes `FillStar`/`RestStar` as `GridLength` for columnar bar rendering without a converter. |
| `EngineOptionVm` | Computed availability/badge state for an engine in the New Agent picker (`IsAvailable`, `Dimmed`, `ShowInstallBadge`, `ShowLimitBadge`, `ShowApiBadge`). |
| `ComposerSuggestionItem` | Sealed record for a composer `@`/`/` autocomplete entry (Label, Value, Type). |
| `PendingAttachment` | Sealed record for a queued file; `IsImage` routes to base64 image block vs. fenced-text inlining; never persisted. |
| `TranscriptExporter` | Static; `ToMarkdown(SessionViewModel)` serialises a transcript to markdown. |
| `EngineRegistry` | Static engine catalog (`All` array, cc/gx/agy/pi); `CreateAdapter`, `ResolveExe`, `DetectExe`, `QueryPiCatalogAsync`. |

---

### AppViewModel Partial-Class Split (16 files)

| Partial file | Contents |
|---|---|
| `AppViewModel.cs` | Constructor + all `Init*Commands()` calls; core fields (`_allSessions`, `_running`, `_nativeObservers`, `_runtimeTimer`, `_scheduler`, `_pendingApprovals`); `ActiveSession`/`ActiveProject` setters; `AttentionRequested` event; `IDialogService? Dialogs`; `SessionStatusWatch`; `MaxConcurrent*` caps; `NewSessionId`. |
| `AppViewModel.Run.cs` | `RunTurnAsync` (main turn pipeline); `Apply` (event dispatcher from `NormalizedEvent` → transcript blocks); `EnsureWorktreeAsync`; `RefreshReviewAsync`/`MergeReviewAsync`/`DiscardReviewAsync`; native observer start/stop; `QueueLiveReviewRefreshAsync`; `QuickReplyCommand`/`RetranslateCommand`. |
| `AppViewModel.WorkerTasks.cs` | `WorkerTaskStore _taskStore`; `BacklogTasks`/`WorkerQueues` collections; `DriveWorkerAsync` / `RunOneAsync` / `RunQueueAsync`; `RebuildTaskViews`; global + session-level task spool watchers; `WithTaskSpoolEnv`; also defines `WorkerTaskViewModel`, `WorkerQueueViewModel`. |
| `AppViewModel.Delegation.cs` | `DelegateAsync`; `CreateWorkerSession`; `InjectReport`; `InjectMergedReports`; `IsWorkerBusy`; `ReadyReportCount`. |
| `AppViewModel.DelegationUi.cs` | Delegation modal state (`ShowWorkerAssign`, `ShowNoIdleWorker`); editor fields (`DelegatePrompt`, `SelectedWorker`, new-worker draft); `WorkerPool`; `CanConfirmDelegate`/`CanDelegateAll`; inbox helpers (`NotifyInbox`, `ReadyReportsActive`); all delegation commands; `DelegateAll` fan-out. |
| `AppViewModel.Settings.cs` | Settings mirror fields (`Settings*`); Ollama status (`OllamaState`, `OllamaRunning`, `CanTranslate`); model checklists init/seed; `DropdownModelsFor`; pi dynamic catalog; auth mode / API key / auto-fallback; zoom (`BodyScale`, `ModalScale`, toast+debounce timers); skill injection; `OpenSettings`/`SaveSettings`/`CloseSettings`/`PullSettingsToEditor`. |
| `AppViewModel.Persistence.cs` | `RestoreState` + `SaveState`; `ApplySettings`/`BuildSettingsDto`; settings.json external-edit watcher (`_settingsWatcher` + `DebouncedReloadAsync` + content-diff guard in `ReloadSettingsFromDisk`); `WorktreesRoot`. |
| `AppViewModel.Dashboard.cs` | Status counts (`RunningCount`/`WaitingCount`/`DoneCount`/`FailedCount`); aggregate `TotalTokensLabel`/`TotalCostLabel`/`FleetThroughputLabel`; `RefreshRunningSessions` (1-second timer target). |
| `AppViewModel.Composer.cs` | `@`/`/` suggestion popup; `TriggerComposerSuggestion`, `UpdateComposerSuggestion`, `ApplySelectedSuggestion`; slash-command handlers (`/clear`, `/review`, `/settings`, `/help`). |
| `AppViewModel.NavCommands.cs` | `ZoomIn/Out/Reset/ResetAllCommand`; `SelectEngineCommand`; `OpenUrlCommand`; `OpenInstallGuideCommand`; `NewAgentForEngineCommand`; `OpenHistoryRowCommand`; `OpenSessionReviewCommand`; `SettingsSegCommand`; `AuthModeCommand`; `SignInCommand`; `ThemeSelectCommand`; `QueryOllamaModelsCommand`; `QueryPiModelsCommand`; `DetectEnginePathCommand`. |
| `AppViewModel.Scheduling.cs` | Schedule overlay state; `CreateSchedule`; `LoadScheduledJobs`; `Scheduler_JobDue` → `RunScheduledJob`. |
| `AppViewModel.History.cs` | `HistoryFilterText`/`HistoryAgentFilter`/`HistoryStatusFilter`; `RebuildHistoryRows`; `ApplyHistoryFilters`; `OpenHistoryRow`; `LoadCliHistoryAsync`; `ImportCliSession`; `PopulateImportedTranscriptAsync`. |
| `AppViewModel.Artifacts.cs` | `UpsertTaskListArtifact`/`UpsertTestArtifact`/`UpsertSummaryArtifact`; `SuppressStderr`/`IsBenignStderr`; `_stderrDump`; `_liveText`; `ExtractCommand`; `KindOf`; `Trim`/`Slug`/`FindRepoRoot`/`CreateTranslator`. |
| `AppViewModel.Usage.cs` | `UsageSnapshot` sealed record; `UsageRowVm`/`UsageBar`; `_usage` dict; `RecordUsage`; `RefreshQuotaText`; `RebuildUsageRows`; `CheckUsageAsync`/`ProbeUsageAsync`; `ParseUsageText`; age/reset formatting. |
| `AppViewModel.About.cs` | `AppVersion` (assembly); `AboutBuildLabel` (exe lastwrite date + engine count). |
| `AppViewModel.Update.cs` | `UpdateService _updater`; `CheckUpdateAsync`/`ApplyUpdate`; launches `scripts/update.ps1 -WaitPid <pid> -Relaunch <exe>` then calls `Application.Current.Shutdown()`. |

---

### Core Communication (Services / Injection / Events / Polling)

| Channel | How used |
|---|---|
| `WorkerTaskStore` | Owned as `_taskStore`; `_taskStore.Changed += OnTaskStoreChanged` → `RebuildTaskViews` + `SaveState`. All mutations (`Assign`, `SetStatus`, `IngestFile`, etc.) go through this store; VMs are rebuilt from it. |
| `TimerScheduler` | `_scheduler` field; `_scheduler.JobDue += Scheduler_JobDue`; `Scheduler_JobDue` dispatches to UI thread via `Application.Current.Dispatcher.InvokeAsync(() => RunScheduledJob(e.Job))`. `_scheduler.Reload()` called after every `ScheduleStore.Save`. |
| `AgentSession` | Created per turn in `RunTurnAsync`; `session.EventReceived += ev => dispatcher.Invoke(() => Apply(s, ev, tools))`; `session.PermissionHandler` wired when `s.RequireApproval`. |
| `INativeWorkObserver` | `_nativeObservers` dict (session→observer); `WorkItemChanged` event marshals to UI via `Dispatcher.Invoke(() => UpsertNativeWorkItem(s, item))`; created in `StartNativeObserverAsync`, disposed in `StopNativeObserverAsync`. |
| `GitWorktree` | Async static methods called directly: `CreateAsync`, `GetChangedFilesAsync`, `GetDiffAsync`, `MergeAsync`, `DiscardAsync`. |
| `OllamaTranslator` | `_translator` field; rebuilt via `CreateTranslator()` whenever settings change. `PingAsync` called at start of every turn (1500 ms timeout) to gate translation. |
| `EngineRegistry` | Static; called for `CreateAdapter`, `ResolveExe`, `Get`, `DetectExe`, `OllamaExe`, `QueryPiCatalogAsync`. |
| `ScheduleStore` | Static; called directly in `CreateSchedule` and `LoadScheduledJobs`. |
| `TaskSpool` | Static Core type; `TaskSpool.Root` for global watcher root; `TaskSpool.DirFor(projectId)` for per-project path injected as `AGENTMANAGER_TASK_SPOOL` env. |
| `IDialogService` | Injected from MainWindow: `viewModel.Dialogs = this`; used via `Dialogs?.Confirm(...)` for destructive operations. `null` in headless/test. |
| `SettingsStore`/`AppStateStore` | Static persistence; called in `RestoreState`/`SaveState`. `SaveState` is called on every meaningful mutation (every `Apply` callback, command, store change). |

---

### Key ICommand Implementations

**`AppViewModel.cs` constructor wires (all `RelayCommand`):**
- `SendCommand` → `SendAsync()` → `RunTurnAsync`; canExecute: `ActiveSession?.CanSend == true && !IsRunning`
- `StopCommand` → `_running[s.Id].Cancel()`
- `NewAgentCommand` / `CreateNewSessionCommand` → builds `SessionViewModel`, calls `RunTurnAsync`
- `DeleteSessionCommand` → stops + optionally removes worktree, then archives
- `MergeCommand` / `DiscardCommand` → `MergeReviewAsync` / `DiscardReviewAsync`
- `ApproveCommand` / `DenyCommand` → `ResolveApproval` (completes `TaskCompletionSource` in `_pendingApprovals`)
- `ExportTranscriptCommand` → `TranscriptExporter.ToMarkdown`
- `ToggleReviewCommand` → toggles `IsReviewOpen`; resets selected diff if closing
- `CheckUsageCommand` → `CheckUsageAsync` (fires real engine turns for cc `/usage` and gx quota probe)

**`AppViewModel.WorkerTasks.cs`:**
- `AssignTaskCommand` — sets `PendingAssign` + `ShowAssignPicker = true`; picker populated by `RefreshWorkerPool()`
- `AssignToWorkerCommand` — calls `_taskStore.Assign(taskId, workerId)`; closes picker
- `AssignToNewWorkerCommand(engineId)` — calls `CreateWorkerSession` (idle, no turn) then `_taskStore.Assign`
- `RunTaskCommand` — `DriveWorkerAsync(workerId, taskId)`; canExecute: `WorkerTaskViewModel.CanRun` (Assigned or Failed)
- `RunQueueCommand` — `DriveWorkerAsync(workerId, null)`; canExecute: `WorkerQueueViewModel.CanRunQueue`

**`AppViewModel.DelegationUi.cs`:**
- `ConfirmDelegateCommand` — creates worker if `DelegateNewWorkerOpen`, then `RunDelegationAsync`; canExecute: `CanConfirmDelegate`
- `DelegateAllCommand` — fan-out loop over all idle workers; `auto: false` so each report lands in inbox, not auto-injected; canExecute: `CanDelegateAll` (≥2 idle workers + prompt set)
- `MergeReportsCommand` → `InjectMergedReports(ActiveSession)` + `NotifyInbox()`
- `PasteReportCommand(WorkerDelegationViewModel)` → `InjectReport` + `NotifyInbox()`

---

### INotifyPropertyChanged / Threading Rules

1. **`ObservableObject.Set<T>`** — raises `PropertyChanged` only when `!EqualityComparer<T>.Default.Equals(field, value)`; always uses `[CallerMemberName]`; no attribute codegen.
2. **`RelayCommand.CanExecuteChanged`** — wired to `CommandManager.RequerySuggested`; WPF re-evaluates all `canExecute` predicates on every user input event. Keep predicates cheap (no I/O).
3. **UI-thread-only collections** — all `ObservableCollection<T>` mutations must happen on the dispatcher thread. Cross-thread callers always use `Dispatcher.Invoke[Async]`.
4. **`_runtimeTimer` (DispatcherTimer, 1s)** — fires `RefreshRunningSessions` on the UI thread; calls `RefreshRuntimeLabels()` on each running `SessionViewModel` and triggers live review debounce for the active session.
5. **`RunTurnAsync`** — long-running engine call runs via `await Task.Run(() => session.RunAsync(…), cts.Token)`; all `session.EventReceived` callbacks marshal back to the dispatcher before mutating `SessionViewModel` or collections.
6. **`DriveWorkerAsync`** — runs on the UI thread (no `Task.Run`); uses `await RunTurnAsync(…)` for cooperative yielding. Re-entrancy guarded by `_drivingWorkers.Add(workerId)` returning false.
7. **`ScheduleIngest`** — called from `FileSystemWatcher` callbacks (background thread); dispatches via `Application.Current?.Dispatcher.InvokeAsync(async () => { await Task.Delay(150); IngestSpoolFile(…); })` to debounce partial writes.

---

### Non-Obvious Invariants

**Settings mirror pattern** — live fields (`_claudePath`, `_theme`, `_accent`, etc.) are the persisted truth; `Settings*` properties are editor mirrors. `PullSettingsToEditor()` copies live→mirror on `OpenSettings`. `SaveSettings()` copies mirror→live. `CloseSettings()` reapplies the live theme/accent without saving, reverting any live preview. `ReloadSettingsFromDisk` JSON-serialises both current state and disk state and no-ops if equal, breaking the self-write loop.

**`_liveText` streaming dict** — keyed by session ID; holds the `AgentTextBlock` being streamed. `AssistantDelta` appends to it; `AssistantText` replaces the full text and removes the entry. `TurnCompleted` also removes it (`Remove` is a no-op if already gone). If a turn completes with no final `AssistantText`, the live block (containing accumulated delta text) stays in the transcript rather than being discarded.

**Concurrency cap split** — `RunTurnAsync` checks `runningSameKind >= cap` independently for workers (`MaxConcurrentWorkers`) and non-workers (`MaxConcurrentSessions`). A full worker pool cannot starve main sessions and vice versa.

**`WorktreeAttempted` one-shot flag** — `EnsureWorktreeAsync` bails immediately if `s.WorktreeAttempted` is already true; the flag is set on first call before the async operation so concurrent callers do not race. CLI-imported sessions set it `true` at construction to prevent worktree creation (resume needs the original `ProjectPath`).

**Spool double-watcher** — `StartTaskSpoolWatcher` watches the global `TaskSpool.Root` (all projects); `WatchSessionTaskSpool` also watches `<cwd>/.am/worker-tasks/` per turn start. The latter is the in-repo fallback when the `AGENTMANAGER_TASK_SPOOL` env var isn't propagated to the agent's shell. `_watchedTaskDirs.Add(dir)` guards idempotency.

**`AssignToNewWorkerCommand` idle creation** — creates the worker via `CreateWorkerSession` (no initial turn), then immediately assigns the task. The worker's first engine interaction is the task prompt itself, so there is no polluting "creation" exchange in its transcript or engine context.

**`DriveWorkerAsync` no-output re-queue** — after `RunTurnAsync`, if the worker produced no new `AgentTextBlock` and its status did not change (cap rejection — the turn never ran), the task is re-queued as `Assigned` and the driver stops (`stop = true; break`). This avoids falsely marking a task failed due to a transient cap hit.

**`DelegateAsync` `OriginalText` preference** — when capturing a worker's report, `d.Report = last.OriginalText ?? last.Text`. This sends the engine's native-language (English) output back to the main session rather than a translated display copy, preserving accuracy in agent-to-agent communication.

**`ApprovalBlock` broker** — `_pendingApprovals: Dictionary<string, TaskCompletionSource<PermissionResponse>>` in `AppViewModel.cs`; `HandlePermissionAsync` creates a TCS and adds an `ApprovalBlock` to the transcript; `ResolveApproval` completes it; `ExpirePendingApprovals(s)` (called at turn end) fails all unresolved TCSs for that session, preventing indefinite leaks.

**`SuppressStderr` brace-depth tracker** — `_stderrDump` dict tracks `{`/`}` balance across consecutive `EngineError` events to swallow Gemini's multi-line `xterm.js Parsing error` JSON dumps that cross many events. TTL of 80 lines prevents permanent suppression if the JSON is truncated.

**`ScheduledJobViewModel` not `ObservableObject`** — it is an immutable projection; `LoadScheduledJobs` clears and repopulates `ScheduledJobs` from scratch on every schedule change. Do not attempt to update instances in place.

**`WorkerTaskStore` is the domain truth** — `WorkerTaskViewModel` and `WorkerQueueViewModel` hold no mutable domain state; they are fully rebuilt from the store on every `Changed` event via `RebuildTaskViews`. `Changed` fires on every store mutation including `IngestFile`, which also triggers `SaveState`.

**Zoom debounce** — `BodyScale` and `ModalScale` setters start a 600 ms `DispatcherTimer` (`_zoomSaveTimer`) that calls `SaveState` on expiry; rapid scrolling does not generate a `SaveState` per tick. A separate 1.1 s timer (`_zoomToastTimer`) hides the zoom toast. Both timers are created lazily on first use.

**Pi model `"default"` sentinel** — `DropdownModelsFor("pi")` prepends `"default"` (meaning `~/.pi` default model) before preferred or catalog models. `SetDefaultModel("pi", "default")` removes the key from `_defaultModels` rather than storing it.

---

## UI: Views, Controls, Resources & Persistence

### 1. Views

| File | Class | DataContext | Purpose |
|------|-------|-------------|---------|
| `MainWindow.xaml` | `MainWindow : Window` | `AppViewModel` (set in ctor: `DataContext = _vm`) | Root chrome-less window; contains all transcript block DataTemplates in `Window.Resources`; sidebar, content pane, review pane |
| `Views/OrchestratorView.xaml` | `OrchestratorView : UserControl` | Inherits `AppViewModel` from parent | Fleet dashboard — KPI UniformGrid, worker cards with `SparkStoryboard` 5-bar equalizer, backlog/queue sections, `ShowAssignPicker` Popup |
| `Views/SessionView.xaml` | `SessionView : UserControl` | `SessionViewModel` (injected by `MainWindow`; code-behind reaches `AppViewModel` via `Window.GetWindow(this)?.DataContext`) | Active session — tabbar, status strip, transcript `ListBox` (auto-scroll via `PART_TranscriptScroll`), diff review pane, composer |
| `Views/HistoryView.xaml` | `HistoryView : UserControl` | Inherits `AppViewModel` | History list — virtualized `ListView` (VirtualizationMode=Recycling), `HistoryFilterText`/`HistoryStatusFilter`/`HistoryAgentFilter`, `MouseClick.Command` attached behavior on rows |
| `Views/ScheduledView.xaml` | `ScheduledView : UserControl` | Inherits `AppViewModel` | Scheduled jobs — plain `ItemsControl` over `ScheduledJobs`, `NewScheduleCommand` button |
| `Views/SettingsView.xaml` | `SettingsView : UserControl` | Inherits `AppViewModel` | Settings panel — has local `DataTemplate` for `ModelChecklistVm`; `SettingsToc_Click` scrolls to named sections via `FindName`+`BringIntoView`; `AddExtraPath_Click` opens `OpenFolderDialog` |

**DataContext assignment pattern:** No DI container or locator. `MainWindow` ctor constructs `AppViewModel`, assigns it to `DataContext`, and sets `_vm.Dialogs = new MessageBoxDialogService()`. `SessionView` DataContext is a `SessionViewModel` bound from the parent's `ActiveSession` property. All other UserControls inherit `AppViewModel` by default.

---

### 2. Controls

| Type | Kind | Key DPs / Events | Purpose |
|------|------|-----------------|---------|
| `IconView` | `Control` | `Icon : Geometry`, `Filled : bool` | Renders a 24×24 Geometry at 16×16 (default) using `Foreground` as stroke color (currentColor). `Filled=true` switches to fill (for solid icons like Stop). Non-focusable/non-tab-stop. |
| `StatusDot` | `UserControl` | `Status : string` | Status indicator dot. Values: `"running"` → Accent pulse halo (1→2.6× scale + fade 1.6s loop); `"waiting"` → Warn; `"done"` → Ok; `"error"` → Err; default → Txt3. |
| `Spinner` | `UserControl` | _(none)_ | 11×11 rotating loading ring (AccentDim track + Accent arc). Rotation starts on `Loaded` event; 0.7s linear, infinite. |
| `MarkdownViewer` | `FlowDocumentScrollViewer` | `Text : string` | Full Markdown renderer (headings, bullets, numbered lists, code fences, tables, inline bold/code/links). Code blocks have a one-click copy button. Reads theme brushes (`Txt0`, `Txt1`, `Bg1`, `Line`, `Accent`) from `Application.Current.Resources` at render time. Links open via `Shell.Open`. |
| `DiffViewer` | `FlowDocumentScrollViewer` | `Text : string` | Unified diff renderer. Line prefix mapping: `+++`/`---` → Txt2; `+` → Add/AddBg; `-` → Del/DelBg; `@@` → Info/Bg3; context → Txt1. Scrollbars disabled (host scrolls). |
| `MouseClick` | static attached | `Command : ICommand`, `CommandParameter : object` | Attached behavior: binds `MouseLeftButtonUp` on any `UIElement` to an `ICommand`. Eliminates code-behind click handlers on list rows and nav items. |
| `BorderHover` | static attached | `Brush : Brush`, `Seconds : double` (default 0.14) | Attached behavior: animates a `Border.BorderBrush` color on hover. On first animation, clones a frozen brush into a mutable `SolidColorBrush` (WPF cannot animate frozen brushes in-place); stores original color in `RestColor` attached property for leave animation. |
| `GridLengthAnimation` | `AnimationTimeline` | `From : GridLength`, `To : GridLength`, `EasingFunction` | Animates `ColumnDefinition.Width` (pixel GridLength only). Used for review pane slide (.22s EaseOut). Required because WPF has no built-in `GridLength` animation. |
| `SelectedBrushConverter` | `IMultiValueConverter` | _(converter)_ | Two-string equality check → `AccentLine` resource if equal, `Line` if not. Used for engine selection highlight in worker cards. |
| `PathToThumbnailConverter` | `IValueConverter` | _(converter)_ | File path → 72px-wide `BitmapImage` (CacheOption=OnLoad, Freeze). Releases source file immediately. Returns null on missing file or exception. |
| `PasswordBoxAssistant` | static attached | `BoundPassword : string` | Two-way sync for `PasswordBox.Password` (which has no DP). `Updating` guard flag prevents feedback loop. Default null (not `""`) so setting VM to `""` still fires the change callback and hooks `PasswordChanged`. |

---

### 3. Resources

#### Merge order (established in `App.OnStartup`, not static XAML)

```
Application.Resources.MergedDictionaries (cleared, then re-merged):
  [0]  Theme/Colors.{theme}.xaml   ← palette tokens (must be first)
  [1]  Theme/Theme.xaml            ← styles that StaticResource the palette
  [2]  Theme/Icons.xaml            ← brand brushes + icon Geometry keys
  [3]  Theme/Strings.{lang}.xaml   ← L.* string keys
```

#### `Theme/Colors.Dark.xaml` (representative; 13 variants exist)

SolidColorBrush keys: `Bg0`–`Bg5` (background layers), `Line`/`LineSoft`/`LineBright` (borders), `Txt0`–`Txt3` (text hierarchy), `Accent`/`AccentBright`/`AccentDim`/`AccentLine`/`AccentText`/`Run` (accent family), `Warn`/`Ok`/`Err`/`Info` (status), `Add`/`AddBg`/`Del`/`DelBg` (diff), `Gx`/`GxLine`/`GxDim`/`Agy`/`AgyLine`/`AgyDim` (engine semantic colors). Also exposes `Bg0Color` as a `Color` resource (for Window background).

#### `Theme/Icons.xaml`

Engine brand brushes: `CcBrand`, `GxBrand`, `AgyBrand` (solid), `AgyGradient` (LinearGradientBrush), `PiBrand`, plus `*Line`/`*Dim` variants for each. Implicit `Style` for `IconView`. All icon `Geometry` resources keyed as `IconX` (e.g., `IconCopy`, `IconFile`, `IconPanel`, `IconX`, `IconSettings`, `IconIde`, `IconTranslate`, `IconLayers`, `IconPanel`).

#### `Theme/Theme.xaml`

- **Corner radii:** `RSm` (4), `RMd` (7), `RLg` (10)
- **Storyboards:** `OverlayFadeIn` (opacity 0→1, 0.15s), `OverlayRiseIn` (Y 10→0 + opacity, 0.2s EaseOut)
- **Font resources:** `Sans` = `pack://…/Resources/Fonts/#IBM Plex Sans`; `Mono` = `pack://…/Resources/Fonts/#IBM Plex Mono`
- **Implicit styles:** `TextBlock`, `ToolTip`, `ContextMenu`, `MenuItem`, `Separator`, `ScrollBar`, `ComboBox`, `ComboBoxItem`
- **Named button styles:** `AccentButton`, `ChipButton`, `MenuButton`, `SendButton`, `ComposerIconButton`, `HeaderIconButton`
- **Named toggle styles:** `TogglePillButton`, `ExpanderHeaderToggle`
- **Named input styles:** `ComposerInput`, `SettingsInput` (based on ComposerInput), `ApiKeyBox`
- **Named text styles:** `SelectableText` (read-only TextBox), `Lbl` (Mono 10 Txt2), `CodeChip` (Mono 10 Accent)
- **Named border style:** `ComposerPill`
- **Named control style:** `HudTicks` (decorative corner ticks overlay), `ModelCheck` (custom CheckBox)
- **Named scroll style:** `ScrollThumb`

#### `Theme/Strings.{lang}.xaml`

`sys:String` entries; all keys prefixed `L.` (e.g., `L.NewAgent`, `L.Running`, `L.Copy`, `L.Settings`, `L.ApprovalRequired`, `L.Thinking`). Two files: `Strings.Ko.xaml` and `Strings.En.xaml`.

#### `App.xaml` (app-scope resources)

Three converters at Application scope so UserControls can `StaticResource` them at load time: `BoolVis` (BooleanToVisibilityConverter), `SelBrush` (SelectedBrushConverter), `PathThumb` (PathToThumbnailConverter). `SandboxModes` ObjectDataProvider. Converters are at App scope (not Window scope) **because** UserControls are loaded before `MainWindow.Resources` is initialized — placing them at Window scope would cause `StaticResource` lookup failures in child UserControls.

#### `MainWindow.xaml` (`Window.Resources`)

All transcript `DataTemplate` entries keyed by `DataType`: `UserBlock`, `AgentTextBlock`, `ToolBlock`, `DelegationBlock`, `ErrorBlock`, `WorkingBlock`, `ThinkingBlock`, `ApprovalBlock`. Also `KbdChip` border style, `EngineIcon` style.

---

### 4. Theming

**13 color themes** defined in `ThemePalette.All` (dark, light, gray, vs, vscode, monokai, nord, claude, claudedark, codex, codexlight, antigravity, antigravitylight), each mapped to `Theme/Colors.{id}.xaml`.

**Runtime theme swap — `ThemePalette.Apply(theme)`:**
1. Loads the target color dictionary as a `ResourceDictionary`
2. Iterates its keys and overwrites matching entries in `Application.Current.Resources` (key-overwrite, not dictionary-replace)
3. `DynamicResource` consumers update immediately; `StaticResource` captures do NOT change until restart

**Accent swap — `AccentPalette.Apply(name)`:**
8 named presets (ember/amber/teal/azure/violet/coral/green/cobalt) + custom `#RRGGBB`/`#AARRGGBB` hex. Writes new unfrozen `SolidColorBrush` instances to 5 keys: `Accent`, `AccentBright`, `Run`, `AccentDim`, `AccentLine`. All consumers with `DynamicResource` update immediately. Accent is applied after `ThemePalette.Apply` on startup so user accent overrides theme defaults.

---

### 5. Persistence

| Class | File path | Format | Holds |
|-------|-----------|--------|-------|
| `AppStateStore` | `%LOCALAPPDATA%\AgentManager\state.json` | JSON (indented), via `JsonFile.WriteAtomic` | `AppStateDto`: projects, sessions (with transcripts as `TranscriptDto[]`), worker task backlog (`WorkerTaskDto[]`), `ActiveProjectId` |
| `SettingsStore` | `%LOCALAPPDATA%\AgentManager\settings.json` | JSON (indented), via `JsonFile.WriteAtomic` | `AppSettingsDto`: all user preferences (engine paths, models, concurrency caps, theme/accent/language, DPAPI-encrypted API keys, usage snapshots, etc.) |
| `Dpapi` (internal) | _(no file; helper)_ | Windows DPAPI P/Invoke (crypt32.dll `CryptProtectData`/`CryptUnprotectData`) | Encrypt/decrypt API key strings for `EngineApiKey` dict in `AppSettingsDto`. Uses `CRYPTPROTECT_UI_FORBIDDEN` — fails silently (returns `""`) rather than showing a dialog |
| `EngineAccounts` (internal) | CLI-owned files | JSON parsing only (read) | cc: `~/.claude.json` → `oauthAccount.emailAddress`; gx: `~/.codex/auth.json` → JWT `email` claim or `OPENAI_API_KEY`; agy: `~/.gemini/google_accounts.json` → `active` |
| `ImageAttachmentStore` | `%LOCALAPPDATA%\AgentManager\attachments\paste-{yyyyMMdd-HHmmss-fff}.png` | PNG (PngBitmapEncoder) | Clipboard/file image attachments for the composer. `SavePng(BitmapSource)` returns the path or null |
| `MainWindow` (window state) | `%LOCALAPPDATA%\AgentManager\window.json` | Plain CSV: `left,top,width,height,state` | Window placement; always stores `RestoreBounds` (not current size) so maximized windows restore correctly |

**Migration:** If `settings.json` is absent but `state.json` has a `"Settings"` node (legacy format), `SettingsStore.Load()` migrates it on first run.

---

### 6. MVVM Wiring

- **`MainWindow` ctor**: creates `AppViewModel _vm`, sets `DataContext = _vm`, sets `_vm.Dialogs = new MessageBoxDialogService()`.
- **`SessionView`**: DataContext = `SessionViewModel` (set by ItemsControl/ContentControl in `MainWindow.xaml` — the active session from `AppViewModel.ActiveSession`). Code-behind reaches `AppViewModel` via `Window.GetWindow(this)?.DataContext`.
- **`OrchestratorView`, `HistoryView`, `ScheduledView`, `SettingsView`**: no explicit DataContext — inherit `AppViewModel` from parent.
- Commands in DataTemplates inside `MainWindow.Window.Resources` bind via `ElementName=Root` (the `MainWindow` `x:Name`) or `RelativeSource AncestorType=Window` to reach `AppViewModel` commands.
- No DI framework, no ViewModelLocator, no markup extensions for VM creation.

---

### 7. Non-Obvious Invariants

1. **Resource merge order is load-order-sensitive.** `Colors.{theme}.xaml` must precede `Theme.xaml` in the merged sequence — `Theme.xaml` uses `StaticResource` to capture palette values at parse time. Reversing the order causes `StaticResource not found` exceptions at startup.

2. **Runtime theme swap updates `DynamicResource` only.** `ThemePalette.Apply` overwrites individual key entries; any `StaticResource` captures made at XAML load time (e.g., storyboard `To` values captured at parse time) do NOT update until the application restarts. This means theme switching is "partial live" — brushes update, but some geometry/animation values stay frozen.

3. **All transcript DataTemplates live in `MainWindow.Window.Resources`, not `SessionView.xaml`.** The `CopyAgentText_Click` handler must therefore stay in `MainWindow.xaml.cs`. Moving it to `SessionView.xaml.cs` breaks it because the template is defined in Window scope.

4. **`ApprovalBlock` buttons use `ElementName=Root`** to bind `ApproveCommand`/`DenyCommand` — they reach `AppViewModel` via the named root window. This template cannot be used outside the `MainWindow` scope.

5. **`PasswordBoxAssistant.BoundPassword` default is `null`, not `""`**. A VM setting the value to `""` still fires `OnBoundPasswordChanged` (null → "" is a change); if the default were `""` the first assignment would be a no-op and `PasswordChanged` would never be hooked.

6. **`BorderHover` clones frozen brushes before animating.** WPF prohibits `BeginAnimation` on a frozen `SolidColorBrush`. On first hover, the behavior replaces `Border.BorderBrush` with a mutable clone and stores the original color in a private `RestColor` attached property on the Border instance.

7. **`MarkdownViewer` re-reads theme brushes on every `Render()` call** via `Application.Current.TryFindResource`. This keeps colors correct after theme swaps, but already-rendered content does not update until the `Text` property changes.

8. **IBM Plex fonts are bundled in `Resources/Fonts/`**, referenced via `pack://application:,,,/Resources/Fonts/#IBM Plex Sans`. Korean and other non-Latin glyphs fall through to WPF's system font fallback (Malgun Gothic, etc.) — the app is designed around this.

9. **Two settings-related files, not one.** `settings.json` holds `AppSettingsDto` (user-editable). `state.json` holds session/project/transcript state. They are separate by design so `settings.json` can be hand-edited. The `AppSettingsDto.Settings` property on `AppStateDto` is `[JsonIgnore]` to prevent accidental re-merging.

10. **`Dpapi` is `CRYPTPROTECT_UI_FORBIDDEN`**. If DPAPI encryption/decryption fails (e.g., wrong user account, corrupted blob), it returns `""` silently. A blank API key is indistinguishable from a failed decrypt — callers must handle empty-string as "no key."

11. **`EngineAccounts` is strictly read-only and display-only.** It parses existing CLI credential files; it never writes to them and ignores all exceptions. Returns `null` (not `""`) when the engine is not logged in.

12. **Window placement is stored as a CSV string in `window.json`**, not JSON, and always stores `RestoreBounds` (never `Width`/`Height` when maximized) so the next launch opens at a sensible size rather than a full-screen normal window.

13. **`Strings.{lang}.xaml` is the last merged dictionary.** Its `L.*` keys must appear last so they can be overridden per-locale cleanly. `App.L(key, args)` is the runtime accessor; it reads from `Application.Current.Resources[key]` and supports `string.Format`-style args.

---

---

## Infrastructure: Bootstrap, Config & Tests

### 1. `.am/` Directory

The directory `J:\prj\AgentManager\.am\` exists in the repository but contains **no tracked files** (glob returns no results). It is likely a placeholder or holds gitignored per-session artifacts. No code in the read source files references a `.am/` path for reading or writing at runtime.

Runtime data files are stored exclusively under `%LocalAppData%\AgentManager\` (i.e., `C:\Users\<user>\AppData\Local\AgentManager\`):

| File / Path | Purpose | Format |
|-------------|---------|--------|
| `settings.json` | User settings (theme, language, engine paths, API keys, etc.) | JSON (indented, hand-editable) |
| `state.json` | Full app state: projects, sessions, transcripts, scheduled jobs | JSON (large; not hand-editable) |
| `window.json` | Window placement: `left,top,width,height[,max]` | CSV string (5 fields) |
| `crash.log` | Exception log appended by `App.LogException` and `App.FatalCrash` | Plain text, timestamped entries |
| `worktrees\...` | Git worktree directories per session | Filesystem |
| `attachments\` | Attached image files | Binary |

**`SettingsStore`** (`src/AgentManager/Persistence/SettingsStore.cs`):
- `SettingsStore.SettingsPath` → `%LocalAppData%\AgentManager\settings.json`
- `SettingsStore.Load()` — deserializes `settings.json` into `AppSettingsDto`; on first run (file absent), performs a one-time migration from the `Settings` node in `state.json` (via `AppStateStore.StatePath`), then falls back to defaults on any parse error.
- `SettingsStore.Save(AppSettingsDto)` — calls `JsonFile.WriteAtomic(SettingsPath, ...)` (atomic write via temp file).

**`AppStateStore`** (referenced by `SettingsStore`): holds `StatePath` = `%LocalAppData%\AgentManager\state.json`. The two stores are distinct to keep settings human-editable independently of the large session/transcript blob.

The API key is never stored in plain text — it is DPAPI-encrypted (`CurrentUser` scope) before being written to `settings.json`.

---

### 2. `scripts/`

Two PowerShell scripts exist at `J:\prj\AgentManager\scripts\`:

#### `publish.ps1`
- **Purpose**: Produces the distributable single-file binary.
- **Command**: `dotnet publish src\AgentManager\AgentManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist`
- **Output**: `dist\AgentManager.exe` (framework-dependent; requires .NET 10 Desktop Runtime on the target machine).
- **When run**: Manually by a developer before release, or called by `update.ps1` step 2 during an in-app update.
- **Requires**: .NET 10 SDK; must be run from within the repository (script computes root via `MyInvocation.MyCommand.Path`).

#### `update.ps1`
- **Purpose**: In-app updater — launched as a **separate process** by the running app so the main exe is unlocked for replacement.
- **Parameters**: `[int]$WaitPid = 0`, `[string]$Relaunch = ""`
- **Flow**:
  1. If `$WaitPid > 0`, calls `Wait-Process -Id $WaitPid -Timeout 60` to wait for the app to exit.
  2. `git -C $root fetch origin` + `git -C $root pull --ff-only origin $branch` — fast-forward only; aborts with an error message on diverged branches.
  3. If HEAD changed (or `dist\AgentManager.exe` does not exist), calls `.\scripts\publish.ps1` to rebuild.
  4. `Start-Process $target` — relaunches the app (uses `$Relaunch` path if valid, else `dist\AgentManager.exe`).
- **When run**: Triggered by the user clicking "업데이트" in the About modal; only active when the app is running from a source checkout.
- **Requires**: Git on PATH; .NET 10 SDK; `$ErrorActionPreference = "Stop"` — any failure halts and shows an error prompt.
- **Safety**: `dist/` and `bin/` are gitignored, so `git pull` never conflicts with the running binary.

---

### 3. Smoke Tests (`AgentManager.Smoke`)

**Project type**: Console executable (`OutputType=Exe`, `net10.0`).
**Single source file**: `src/AgentManager.Smoke/Program.cs` (~1596 lines).
**No test framework** (no xUnit/NUnit/MSTest). All tests are top-level statements in `Program.cs` with a manual `Assert(bool condition, string message)` static function that throws `InvalidOperationException` on failure.
**Project reference**: Only `AgentManager.Core`.

#### How to Run

```powershell
# Default run (zero-token offline tests + GitWorktree e2e):
dotnet run --project src/AgentManager.Smoke

# Individual checks via flag:
dotnet run --project src/AgentManager.Smoke -- --<flag>
```

#### Test Inventory by Flag

| Flag | Type | What it exercises |
|------|------|-------------------|
| *(no flag, default path)* | Offline assert | Claude/Codex/Pi adapter `ParseLine` against fixture JSON arrays (`claudeLines`, `codexLines`, `piLines`); `AssertResumeArgs`; `AssertSandboxAndModelArgs`; `AssertPermissionResponse`; `AssertAppServerAdapter`; `AssertQuickReplyParser`; `TestGitWorktreeAsync` |
| `--sched-check` | Offline assert | `ScheduleTrigger.TryParseCadenceToCron` (EN + KO cadence strings) and `GetNextRunUtc` calculation against fixed UTC baseline (2026-06-13 12:00 UTC) |
| `--sched-create-check` | Offline + in-process | `ScheduleStore` round-trip save/load; `TimerScheduler.EvaluateJobs` due evaluation and duplicate-prevention; overrides `ScheduleStore.StorePath` to a temp file |
| `--worker-prompt-check` | Offline assert | `WorkerDefaults.ComposePrompt` (preamble + task, empty preamble, whitespace trim); `WorkerDefaults.MergeReports` (single pass-through, multi-labeled merge) |
| `--worker-task-store-check` | Offline unit | `WorkerTaskStore`: ingest from spool JSON, bad-JSON graceful skip, assign to worker queue, order/reorder (`Move`), NextRunnable, Running→Done state machine, ClearFinished, Delete, Unassign, worker isolation, crash reconciliation via `ReconcileInterrupted` |
| `--native-observer-check` | Offline + filesystem | `HookSpoolNativeWorkObserver` + `NativeWorkObservationTarget`: writes fake `SubagentStart`/`SubagentStop` JSON files to a temp spool dir, asserts `ObservedWorkItem` state transitions to `ObservedState.Completed`, `ObservationConfidence.High` |
| `--subagent-failure-check` | Offline assert | `SubagentTranscriptInspector.InspectLine` (rate-limit detection); `LooksLikeLimit` heuristic |
| `--claude-agents-probe` | Offline + optionally live | `ClaudeAgentsProbe.Parse` against fixture JSON; if `claude.exe` found, calls `ClaudeAgentsProbe.RunAsync(exe)` live |
| `--codex-hook-args-check` | Offline assert | `CodexAdapter.BuildStartInfo` with hook args verifies `--dangerously-bypass-hook-trust`, `hooks.SubagentStart`, `hooks.SubagentStop`, `AGENTMANAGER_HOOK_SPOOL` in arg list |
| `--claude-hook-args-check` | Offline assert | `ClaudeAdapter.BuildStartInfo` with hook args verifies `--settings`, `SubagentStart`, `SubagentStop`, `AGENTMANAGER_HOOK_SPOOL` in arg list |
| `--agy-observer-check` | Offline + filesystem | `AgyNativeWorkObserver`: writes fake `last_conversations.json` + transcript + message files to temp dir, asserts subagent detection |
| `--agy-pty-check` | **Live (user terminal)** | Spawns `agy.exe` under `ConPtyHost`, verifies ConPTY output is visible; requires agy installed |
| `--agy-check` | **Live (user terminal)** | Spawns `agy.exe` as child process with stdio redirect, verifies auth works from a non-interactive context |
| `--live-agy` | **Live (tokens)** | Two-turn agy session with resume via `AgyAdapter` and `AgentSession` |
| `--live-approval` | **Live (tokens)** | Claude turn with `PermissionHandler` auto-accepting; verifies `ok.txt` is created |
| `--live-claude-native-observer` | **Live (tokens)** | Full Claude turn with `HookSpoolNativeWorkObserver`; verifies subagent hook events surface as `ObservedState.Completed` |
| `--live-stage2` | **Live (tokens)** | Full Codex app-server (`CodexAppServerAdapter`) turn with auto-approving `PermissionHandler` |
| `--appserver-probe` | **Live (tokens)** | Raw JSON-RPC probe of codex app-server over stdio |
| `--codex-check` | **Live (tokens)** | Both codex paths (exec + resume; app-server) with model `gpt-5.5` |
| `--worker-task-run` | **Live (tokens)** | Full worker task lifecycle: spool file → `WorkerTaskStore.IngestFile` → `Assign` → real `AgentSession` with `ClaudeAdapter` → `Done` status |
| `--e2e` | **Live (tokens)** | Full product path: git init → `GitWorktree.CreateAsync` → Korean prompt → `OllamaTranslator` → Claude (file creation) → `GetChangedFilesAsync`/`GetDiffAsync` → `MergeAsync` → `RemoveAsync` |
| `--cli-history <path>` | Live (disk) | `CliSessionDiscovery.Discover` + `LoadTranscript` for a given project path |
| `--codex-models` | Live (codex exe) | Probes codex `app-server` for `model/list` response |

The **default run** (no flags) is the zero-token headless check: adapter parsing assertions + `GitWorktree` end-to-end against a throwaway temp repo. This is what `dotnet run --project src/AgentManager.Smoke` in the README refers to.

---

### 4. Bootstrap Sequence

There is **no DI container** (no `IServiceCollection`, no `Generic.Host`). Dependencies are wired manually.

**Step 1 — Process init (`App.OnStartup` override)**

`App` : `Application` is the `x:Class` of `App.xaml`. `App.OnStartup` runs before any window is shown:

1. `SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX)` (P/Invoke `kernel32.dll`) — suppresses Windows Error Reporting dialogs for the process and **all child processes** (error mode is inherited). This silences crash dialogs from spawned engine CLIs (e.g., codex helper processes).

2. Three global exception handlers are registered:
   - `DispatcherUnhandledException` → calls `App.FatalCrash("Dispatcher", ex)` (sets `args.Handled = true` first to suppress WPF's own handler).
   - `AppDomain.CurrentDomain.UnhandledException` → calls `App.FatalCrash(...)`.
   - `TaskScheduler.UnobservedTaskException` → calls `App.LogException` + `args.SetObserved()` (absorbed, not fatal).

   `App.FatalCrash`: single-entry guarded (via `Interlocked.Exchange` on `_crashing`), appends to `crash.log`, shows a `MessageBox`, then calls `Environment.Exit(1)`.

3. `Persistence.SettingsStore.Load()` — reads `%LocalAppData%\AgentManager\settings.json`, returns `AppSettingsDto` (defaults to `theme="dark"`, `language="ko"` on any failure).

4. Theme resources rebuilt from scratch: `Resources.MergedDictionaries` is cleared, then four `ResourceDictionary` instances are added in this **mandatory order**:
   - `Theme.ThemePalette.FileFor(theme)` → e.g., `Theme/Colors.Dark.xaml` (color tokens)
   - `Theme/Theme.xaml` (semantic tokens referencing the palette via `StaticResource`)
   - `Theme/Icons.xaml`
   - `Theme/Strings.Ko.xaml` or `Theme/Strings.En.xaml`

   The color palette **must precede** `Theme.xaml` because `Theme.xaml` captures palette values via `StaticResource` at merge time. Changing the order breaks all theme color references.

5. `base.OnStartup(e)` — triggers `StartupUri="MainWindow.xaml"` declared in `App.xaml`.

**Step 2 — `MainWindow` constructor**

`MainWindow` : `Window` (`partial`, XAML + code-behind):

1. `InitializeComponent()` — loads `MainWindow.xaml`.
2. `new AppViewModel()` — the sole VM instantiation point. `AppViewModel` is `partial` (split across multiple `.cs` files in `ViewModels/`). Its constructor loads all persisted state (`state.json` via `AppStateStore`), initializes `EngineRegistry`, starts the `TimerScheduler`, registers file watchers for settings.json live reload, etc.
3. `DataContext = _vm` — binds the entire window to `AppViewModel`.
4. `_vm.Dialogs = new MessageBoxDialogService()` — injects the dialog abstraction.
5. `_vm.PropertyChanged` subscription for `ReviewPaneWidth` → triggers `AnimateReviewPane()` (0.22 s `GridLengthAnimation` on `ReviewCol`; cannot be done in XAML because `GridLength` is not animatable via storyboard).
6. `_vm.AttentionRequested += OnAttentionRequested` — wires taskbar flash (`FlashWindowEx` P/Invoke) and approval sound (`SystemSounds.Exclamation`).
7. `CommandBindings` + `InputBindings` for all keyboard shortcuts.
8. `PreviewMouseWheel` → `OnPreviewMouseWheelZoom` — Ctrl+wheel zoom.
9. `RestoreWindowPlacement()` — reads `window.json` (`left,top,width,height[,max]`).
10. `Closing` → `SaveWindowPlacement()` + `_vm.Dispose()`.

**Step 3 — Window shown**

The window appears. `AppViewModel.Dispose()` on close tears down sessions, the scheduler, and file watchers.

**App-scope resources** (declared in `App.xaml`, available to all UserControls via `StaticResource`):
- `BooleanToVisibilityConverter` (key: `BoolVis`)
- `controls:SelectedBrushConverter` (key: `SelBrush`)
- `controls:PathToThumbnailConverter` (key: `PathThumb`)
- `ObjectDataProvider` for `agents:SandboxMode` enum values (key: `SandboxModes`)

These must live at `Application` scope because UserControls are loaded before `Window.Resources` is in the visual tree.

---

### 5. Solution Layout (`AgentManager.slnx`)

The solution file `AgentManager.slnx` declares three projects under a single `/src/` solution folder:

| Project | Path | Type | Target Framework | Role |
|---------|------|------|-----------------|------|
| `AgentManager.Core` | `src/AgentManager.Core/AgentManager.Core.csproj` | Class library | `net10.0` | Engine adapters, normalized events, translation, session execution, GitWorktree, scheduling, observation. No NuGet deps, no project refs. |
| `AgentManager` | `src/AgentManager/AgentManager.csproj` | WinExe | `net10.0-windows` | WPF UI: MVVM (`AppViewModel` partial + component VMs + Views + Controls). Bundles IBM Plex fonts and `Resources\Guide.*.md`. References only `AgentManager.Core`. |
| `AgentManager.Smoke` | `src/AgentManager.Smoke/AgentManager.Smoke.csproj` | Exe | `net10.0` | Headless smoke/integration tests. References only `AgentManager.Core`. |

**Inter-project references**:
```
AgentManager.Core     (no refs)
AgentManager          → AgentManager.Core
AgentManager.Smoke    → AgentManager.Core
```

`AgentManager` and `AgentManager.Smoke` do **not** reference each other.

**Additional `AgentManager` build items**:
- `Resources\Fonts\*.ttf` — IBM Plex Sans/Mono, bundled as WPF `Resource` (OFL license).
- `Resources\Guide.*.md` — per-engine install/setup guide Markdown, rendered in-app by `MarkdownViewer`.
- `app.manifest` — custom application manifest.
- `Version`: `1.12.0` (set in csproj).

---

### 6. Non-Obvious Invariants

**1. Theme resource merge order is load order — not live lookup.**
`Theme.xaml` uses `StaticResource` (not `DynamicResource`) for color palette tokens. If the color palette dictionary is not in `MergedDictionaries` **before** `Theme.xaml` is added, every palette reference in `Theme.xaml` throws a `ResourceNotFoundException` at merge time. The four-dictionary sequence in `App.OnStartup` is order-sensitive; do not reorder. Post-startup theme switching uses `ThemePalette.Apply` which overwrites existing entries rather than clearing/re-adding, avoiding this constraint.

**2. `App.xaml` StartupUri is NOT the startup hook — `OnStartup` is.**
`StartupUri="MainWindow.xaml"` fires only after `base.OnStartup(e)` returns. Error-mode suppression, exception handlers, and theme setup must all happen before `base.OnStartup(e)` is called. Adding logic after `base.OnStartup(e)` means it runs after the window may already be visible.

**3. `SetErrorMode` flags are inherited by child processes.**
`SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX` is set on the app process and propagated to every spawned engine subprocess (codex, claude, etc.). This means a crash in a child process will not pop a WER dialog but also will not be surfaced by Windows — the engine's stderr pump is the only signal. Do not remove these flags thinking they only affect the UI process.

**4. `SettingsStore` migration runs at most once — destructively.**
`SettingsStore.Load()` migrates from `state.json`'s `Settings` node by calling `Save(migrated)`, which creates `settings.json`. On the next load, the migration branch is bypassed because `settings.json` already exists. If `settings.json` is manually deleted, the migration re-runs from whatever is in `state.json` at that moment. Do not add idempotent migration steps that re-derive state — the migration branch assumes a clean first-run.

**5. `window.json` format is raw CSV, not JSON.**
`MainWindow.SaveWindowPlacement` writes `string.Join(",", left, top, width, height, state)` and `RestoreWindowPlacement` parses with `parts = text.Split(',')`. Despite the `.json` extension, the file is not valid JSON. Any tool that tries to parse it as JSON will fail. This is intentional simplicity and should not be changed to JSON without updating both read and write paths.

**6. Smoke tests have a default execution path that runs without flags.**
Running `dotnet run --project src/AgentManager.Smoke` with no arguments does not print usage and exit — it executes the full default path: fixture-based adapter parsing + all `Assert*` functions + `TestGitWorktreeAsync`. The `TestGitWorktreeAsync` call creates a real git repository in `%TEMP%` and runs create/change/diff/discard/commit/merge/remove. This requires `git` on PATH. Without git, `TestGitWorktreeAsync` silently fails (process start exceptions are caught by the `GitWorktree` API internally — ambiguous).

**7. Live/token-consuming smoke checks must be run in a user terminal.**
Flags `--live-agy`, `--agy-pty-check`, `--agy-check`, `--live-approval`, `--e2e`, `--live-stage2`, `--worker-task-run`, `--live-claude-native-observer` all spawn real engine CLIs. `agy` specifically requires a user interactive session (ConPTY) for authentication to work — it will silently fail (no output) when run from a non-interactive context (CI, service account). The comments in `Program.cs` explicitly state: "사용자 터미널에서 실행할 것" (run in user terminal).

**8. `update.ps1` requires fast-forward-only pull.**
The script uses `git pull --ff-only`. Any local commit, staged change, or diverged branch will abort the update with an error message and leave the app at the old version. The "no local modifications" invariant is enforced by the fact that `dist/` and `bin/` are gitignored — if they were tracked, running the app would dirty the working tree and block updates.

**9. `AppViewModel` is the single root of all runtime state.**
There is no DI container. `MainWindow` creates `new AppViewModel()` directly; `AppViewModel` pulls in `EngineRegistry`, `TimerScheduler`, `SettingsStore`, `AppStateStore`, and all session/project/worker collections. Teardown is `_vm.Dispose()` on window close. Any class that needs the VM must receive it via constructor or property injection from `MainWindow` or `AppViewModel` — do not try to resolve it from a service locator.

---
