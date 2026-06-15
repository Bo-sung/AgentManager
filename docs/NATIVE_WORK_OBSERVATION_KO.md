# Native subagent / worker observation plan

> 2026-06-14 기준 설계 메모. 현재 우선순위는 Claude quota가 부족한 상황을 고려해
> **Codex + Antigravity 중심**으로 잡는다. Claude는 hook 기반 설계 후보로 남기되,
> 실제 구현은 Codex/Antigravity 관측 계층을 먼저 만든 뒤 재검증한다.

## 목표

AgentManager는 여러 코딩 에이전트를 한 화면에서 실행/관리하는 도구다.
따라서 각 엔진이 자체 subagent, background worker, manager task를 사용하더라도
AgentManager UI에서 다음 정보를 확인할 수 있어야 한다.

- 어떤 parent session에서 worker/subagent가 시작됐는지
- 현재 running/waiting/completed/failed 중 어디에 있는지
- 어떤 엔진/agent type/model이 사용됐는지
- transcript/log/artifact 위치가 어디인지
- 마지막 결과 요약 또는 실패 사유가 무엇인지

## 핵심 판단

AgentManager 자체 worker 기능은 필요하지만, 먼저 해야 할 일은 **native observer**다.
모델이 자체 내장 worker를 사용했는데 AgentManager가 보지 못하면 통합 관리 도구의 가치가 떨어진다.

따라서 구현 순서는 다음과 같다.

1. Native worker/subagent 관측 모델 추가
2. Codex observer
3. Antigravity observer
4. UI에 session 하위 worker/subagent 상태 표시
5. 이후 AgentManager 자체 worker tool/MCP 구현

## 공통 상태 모델 초안

```csharp
public sealed class ObservedWorkItem
{
    public string Id { get; init; } = "";
    public string EngineId { get; init; } = "";
    public string ParentSessionId { get; init; } = "";
    public string? VendorWorkId { get; init; }
    public string? AgentId { get; init; }

    public WorkItemKind Kind { get; set; }
    public ObservedState State { get; set; }
    public ObservationSource Source { get; set; }
    public ObservationConfidence Confidence { get; set; }

    public string? AgentType { get; set; }
    public string? DisplayName { get; set; }
    public string? Cwd { get; set; }
    public string? TranscriptPath { get; set; }
    public string? AgentTranscriptPath { get; set; }
    public string? LastMessage { get; set; }
    public string? Error { get; set; }
    public string? RawJson { get; set; }

    public bool ManagedByAgentManager { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

상태는 확정/추정을 구분한다. 예를 들어 hook stop 이벤트는 `High`,
transcript mtime 기반 running 추정은 `Low`로 표시한다.

## Codex 관측 설계

### 관측 소스

우선순위는 다음과 같다.

1. Codex hooks: `SubagentStart`, `SubagentStop`
2. `codex app-server`: thread/turn/item notification
3. `codex exec --json`: 단발 실행 event stream
4. local session log scan: fallback

### 확정된 근거

공식 Codex 문서 기준:

- native subagent 기능이 있다.
- hooks에 `SubagentStart` / `SubagentStop`가 있다.
- `SubagentStop`에는 `agent_id`, `agent_type`, `agent_transcript_path`, `last_assistant_message`가 포함된다.
- `codex exec --json`은 `thread.started`, `turn.started`, `item.*`, `turn.completed`, `turn.failed`, `error` JSONL을 제공한다.
- `codex app-server`는 `thread/*`, `turn/*`, `item/*`, `thread/status/changed`, `thread/tokenUsage/updated` notification을 제공한다.

### 구현 방향

Codex는 hook 기반 observer를 먼저 붙인다.

- AgentManager가 관리하는 Codex session에는 launch-time 설정으로 hook spool path를 주입한다.
- hook command는 stdin JSON을 `%TEMP%/AgentManager/native-events/` 또는 app data 아래에 JSON 파일로 기록한다.
- `CodexNativeWorkObserver`는 해당 spool directory를 감시하고 `ObservedWorkItem`으로 병합한다.
- `CodexAppServerAdapter`는 이미 app-server notification을 받고 있으므로, 장기적으로 raw notification 일부를 worker activity로 승격할 수 있다.

### 미확인 / 주의

- 로컬 `codex.exe` 실행 검증은 WindowsApps 권한 문제로 제한될 수 있다.
- app-server notification에 stable worker-spawn event가 있는지는 확정하지 않는다.
- Codex hook event를 canonical source로 두고, app-server/exec JSON은 보조 소스로 사용한다.

## Antigravity / agy 관측 설계

### 관측 소스

우선순위는 다음과 같다.

1. `~/.gemini/antigravity/brain/<session-id>/.system_generated/logs/transcript.jsonl`
2. `.system_generated/messages/*.json`
3. `.system_generated/tasks/task-*.log`
4. `.system_generated/worktrees/subagent-*`
5. `~/.gemini/antigravity-cli/cache/last_conversations.json`

### 분석 결과

현재 `agy` CLI는 AgentManager에서 ConPTY로 실행하는 v1 경로가 존재하지만,
Codex/Claude처럼 안정적인 JSON event stream이 확인된 상태는 아니다.

Antigravity 쪽은 passive file observation을 우선한다.

- parent transcript에서 `INVOKE_SUBAGENT` 또는 유사 step을 찾아 child `conversationId`, `logAbsoluteUri`, `workspaceUris`를 추출한다.
- child session의 brain directory를 추가로 감시한다.
- messages directory는 background task/subagent 완료 통지 후보로 본다.
- tasks log는 stdout/stderr tailing 용도로 본다.
- worktree path가 있으면 diff 상태를 별도 계산할 수 있다.

### 구현 방향

`AgyNativeWorkObserver`는 `last_conversations.json` 또는 `SessionViewModel.ResumeSessionId`/conversation id에서 brain path를 찾는다.

이후:

- transcript JSONL tailing
- messages directory watcher
- task log watcher
- child conversation watcher 생성
- worktree diff metadata 수집

순서로 구현한다.

### 미확인 / 주의

- `.gemini/antigravity/brain` 구조는 내부 캐시로 보이며 버전 변경 위험이 있다.
- transcript schema가 공식 stable API인지 확정하지 않는다.
- file watcher는 `FileShare.ReadWrite | FileShare.Delete`로 열어야 한다.
- 상태는 대부분 추정이다. UI에서 `inferred` 또는 낮은 confidence로 표시한다.
- 현재 `agy`는 attach 가능한 interactive child session을 제공한다고 보지 않는다.

## Claude 위치

Claude는 이번 구현 우선순위에서 뒤로 미룬다. 단, 설계상 Codex와 매우 비슷한 hook-first 전략이 가능하다.

보류 사유:

- 현재 Claude quota가 부족하다.
- Antigravity 내 Claude 모델이 생성한 분석 문서는 검증 후보로만 본다.
- 로컬에서 Claude Code 2.1.177, cache directory, 일부 `agent-*.jsonl` 존재는 확인됐지만,
  AgentManager 프로젝트에서 실제 SubagentStart/Stop hook 재현은 아직 하지 않았다.

추후 Claude spike:

1. 임시 hook settings 생성
2. SubagentStart/SubagentStop hook이 JSON spool 파일을 쓰는지 확인
3. `agent_transcript_path` 실제 파일 확인
4. `claude agents --json` 실제 schema 저장

## 공통 구현 단계

### Phase A: Core model

- `ObservedWorkItem`
- `ObservedState`
- `WorkItemKind`
- `ObservationSource`
- `ObservationConfidence`
- `INativeWorkObserver`
- `NativeWorkObservationService`

### Phase B: Codex hook spool

- hook event JSON schema 정의
- hook spool directory watcher
- Codex hook event -> `ObservedWorkItem` mapper
- app-server notification과 session id 병합

### Phase C: Antigravity passive observer

- last conversation lookup
- brain path resolver
- transcript tailer
- messages/tasks watcher
- subagent/worktree discovery

### Phase D: UI

- session detail에 Native Workers/Subagents section 추가
- running/waiting/completed/failed badge
- source/confidence 표시
- transcript/log/artifact path open action

### Phase E: AgentManager worker tool

Native observation이 먼저 안정된 뒤 진행한다.

- `agentmanager.start_worker`
- `agentmanager.get_worker_status`
- `agentmanager.wait_worker`
- `agentmanager.get_worker_report`
- `agentmanager.cancel_worker`

## 현재 결정

- 지금은 **Codex + Antigravity**에 집중한다.
- Codex는 hook-first.
- Antigravity는 passive file observation-first.
- Claude는 hook 구조 검증 후 같은 observer interface에 붙인다.
- `claude agents --json` 또는 vendor session list는 discovery 보조 수단이지 상태 truth source로 쓰지 않는다.

## 2026-06-14 구현 시작 상태

추가된 core 타입:

- `ObservedWorkItem`
- `ObservedState`
- `WorkItemKind`
- `ObservationSource`
- `ObservationConfidence`
- `NativeWorkObservationTarget`
- `INativeWorkObserver`
- `NativeHookEvent`

추가된 observer:

- `HookSpoolNativeWorkObserver`
  - Codex/Claude hook script가 JSON 파일을 쓰는 spool directory를 감시한다.
  - `SubagentStart`/`SubagentStop` JSON을 `ObservedWorkItem`으로 변환한다.
  - vendor parent session id와 AgentManager parent session id를 분리한다.
- `AgyNativeWorkObserver`
  - `last_conversations.json`에서 conversation id를 찾는다.
  - `.gemini/antigravity/brain/<conversation>/.system_generated/logs/transcript.jsonl`을 읽는다.
  - `INVOKE_SUBAGENT` 계열 line에서 child conversation/workspace/log 정보를 추출한다.
  - `.system_generated/messages/*.json`을 통해 완료/실패 상태를 best-effort로 갱신한다.
  - cache schema가 private이므로 confidence는 기본 `Medium` 이하로 둔다.

추가된 smoke:

- `dotnet run --project src/AgentManager.Smoke -- --native-observer-check`
- `dotnet run --project src/AgentManager.Smoke -- --agy-observer-check`
- `dotnet run --project src/AgentManager.Smoke -- --codex-hook-args-check`

검증 결과:

- `dotnet build` PASS
- `--native-observer-check` PASS
- `--agy-observer-check` PASS
- `--codex-hook-args-check` PASS

## 2026-06-14 Codex hook injection 연결

추가된 옵션:

- `SessionOptions.NativeHookSpoolDirectory`
- `SessionOptions.NativeHookCommand`
- `SessionOptions.BypassHookTrust`

변경된 실행 경로:

- `AgentSession`은 `NativeHookSpoolDirectory`가 있으면 디렉터리를 생성하고
  child process 환경 변수 `AGENTMANAGER_HOOK_SPOOL`에 주입한다.
- `CodexAdapter`는 `NativeHookCommand`가 있으면 다음 inline config를 `codex exec --json`에 추가한다.
  - `hooks.SubagentStart`
  - `hooks.SubagentStop`
  - `--dangerously-bypass-hook-trust`
- `AppViewModel.RunTurnAsync`는 Codex(`gx`) 세션에 한해 `%AppData%/AgentManager/native-hooks/<sessionId>` spool path와
  `NativeHookCommandFactory.WindowsPowerShellSpoolWriter()`를 주입한다.

아직 남은 일:

- 실제 Codex quota/환경에서 subagent를 유도해 hook 파일 생성 실측.
- Codex app-server 경로(`RequireApproval=true`)에도 hook config를 안전하게 주입할 수 있는지 별도 확인.

## 2026-06-14 UI 연결 시작

추가된 UI/ViewModel:

- `SessionViewModel.NativeWorkItems`
- `NativeWorkItemViewModel`
- 세션 상세 status strip 아래 Native workers/subagents strip

연결된 lifecycle:

- Codex(`gx`) 세션 시작 시:
  - 기존 hook spool JSON 파일을 정리한다.
  - `HookSpoolNativeWorkObserver`를 시작한다.
  - hook event가 들어오면 `NativeWorkItems`에 upsert한다.
  - 세션 종료 시 observer를 dispose한다.
- Antigravity/agy 세션은 `SessionStarted`에서 conversation id가 확정된 뒤 `AgyNativeWorkObserver`를 시작한다.

아직 남은 일:

- 실제 Codex quota/환경에서 subagent를 유도해 hook 파일 생성 실측.
- 실제 agy cache schema로 `AgyNativeWorkObserver` 파서 보정.
- Codex app-server 경로(`RequireApproval=true`)에도 hook config를 안전하게 주입할 수 있는지 별도 확인.
- Native worker strip의 path open/copy actions.
