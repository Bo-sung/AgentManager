# AgentManager Pi Worker Handoff

## Last Updated
- 2026-07-11 (KST) — 작성 에이전트: Claude (Opus 4.8)
- 상태: **핵심 Pi Worker 통합 완료 + 최종 정리(문서 정합성·정책 확정·머지 준비)**. 빌드 green, 전체 스모크 green, 실제 pi-worker 라이브 E2E(로컬 모델, 무료) 통과, orphan 0. **push/머지 안 함**(사용자 승인 대기).

## Repository State
### Base (분기 시점)
- Base branch: `master` (= `develop`, 둘 다 `c64660f`)
- Base HEAD: `c64660f` — release: v1.19.5
### Current
- Path: `J:\prj\AgentManager`
- Current branch: `feature/pi-worker-integration`
- Current HEAD: `ae1b5e5`(정리 커밋 추가 시 갱신됨 — 아래 Commits 섹션이 SSOT)
- Working tree: clean(정리 작업 중 커밋마다 확인)
- Push status: **미push**
- Merge status: **미merge** (대상: `develop`, 이후 release→`master`)

## Fixed Pi Worker Harness Dependency (고정)
```
Package:            @agentmanager/pi-worker-harness
Version:            0.1.0
Commit:             6e49dbd0f6b2858dc4d946311843ebc2ba6dde10   (H:\Git\Bosung_PI\pi-worker-harness)
Branch:            develop   (tag 없음)
Supported Pi:       0.80.3 (pinned; package.json pinnedSupportedPiVersion)
Runtime entrypoint: dist/cli/index.js  (bin: pi-worker)
```
- harness는 **커밋 완료된 고정 외부 런타임**(이전 인계의 "커밋 0개/uncommitted" 기술은 폐기 — 이제 6e49dbd로 고정). 이번 작업에서 harness는 **읽기/버전확인만**, 수정 금지.
- 외부 참고: `H:\Git\Bosung_PI\pi-upstream` — Pi 원본, 읽기 전용, 수정 금지.

## Original Goal
기존 공식 `pi`는 Main/General 그대로 두고, Worker 역할일 때만 `pi-worker`(harness)로 실행하도록 AgentManager를 연동한다.
- `pi-worker`를 **별도 엔진으로 등록하지 않음**. 기존 `pi` 엔진 하나 유지, 세션 Role(Worker)로 실행 파일만 분기.
- 범용 Profile 시스템 신설 금지. Pi 코어/`pi-upstream` 수정 금지. cc/gx/agy 실행 구조 재설계 금지.
- 공통 Worker queue·Task Contract·worktree·report routing 유지.

## 확정 아키텍처 (핵심 사실, 실측 기반)
- **실행 계약**(harness `docs/AGENTMANAGER_PI_WORKER_INTEGRATION_KO.md`):
  `pi-worker --mode rpc` + 공식 pi 플래그 pass-through(`--model`, `--thinking`, `--session`). stdin/stdout은 공식 Pi RPC JSONL 그대로(pi-worker는 가공 안 함).
- **배포 형태(Step 1 실측)**: harness `package.json` bin = `pi-worker → dist/cli/index.js`. 아직 `npm i -g`/`npm link` 안 됨 → PATH에 `pi-worker` 없음. **`node <harness>/dist/cli/index.js`로 직접 구동 가능**(검증 완료).
  - `node dist/cli/index.js --version` → `pi-worker 0.1.0 (wraps official pi 0.80.3)`
  - `node dist/cli/index.js doctor` → All checks passed. Worker config root `C:\Users\sbss0\.pi-worker`, `PI_CODING_AGENT_DIR=~/.pi-worker/agent`, `PI_CODING_AGENT_SESSION_DIR=~/.pi-worker/agent/sessions`, `~/.pi/agent`와 격리 확인. worker-guard/worker-task skill present. 알려진 leak: `~/.agents/skills`는 pi/pi-worker 공유.
- **공식 pi 설치 위치**: `C:\Users\sbss0\AppData\Roaming\npm\node_modules\@earendil-works\pi-coding-agent\dist\cli.js` (Node v24.18.0).
- **AgentManager 실행 경로**:
  - Role 트리거: `SessionViewModel.Role == SessionRole.Worker` (`src/AgentManager.Core/Workers/WorkerTypes.cs`). 워커 생성: `AppViewModel.Delegation.cs: CreateWorkerSession` (Role=Worker 세팅).
  - 엔진 해석: `AppViewModel.Run.cs:277` → `TurnPlanner.ResolveEngine(EngineResolveRequest)` → `EngineRegistry.ResolveExe(id, ..., piPath)`. pi는 `ResolvePi()`가 `@earendil-works .../dist/cli.js` 반환 → `PiAdapter`가 `node cli.js --mode rpc`.
  - 옵션 조립: `AppViewModel.Run.cs:333` → `TurnPlanner.BuildOptions(TurnOptionsRequest)` → `SessionOptions.ExtraEnvironment`에 env 주입.

## 설계 결정 (Decisions)
1. **엔진 그대로, 경로만 분기**: `EngineRegistry.ResolveExe`에 worker 여부 + `piWorkerPath`를 넘겨, `id=="pi" && worker`이면 pi-worker(index.js) 경로를 반환. Main/Plain pi는 기존 그대로.
2. **pi-worker 경로 해석 우선순위**: (a) 설정 `PiWorkerPath` 오버라이드 → (b) PATH의 `pi-worker` → (c) npm-global `@agentmanager/pi-worker-harness/dist/cli/index.js` 자동탐지. 못 찾으면 typed error(EngineUnavailable, 프론트가 지역화).
3. **PiAdapter node-vs-direct 분기**: `executablePath`가 `.js`로 끝나면 `node <path>`, 아니면(예: `pi-worker` shim/.cmd) 직접 실행. → **어댑터 1개로 pi/pi-worker 공용**(별도 엔진 안 만듦 제약 충족).
4. **Worker env**: worker-role 세션에 `AGENTMANAGER_ROLE=worker`, `_SESSION_ID`, `_TASK_ID`(있으면), `_PROJECT_ID`, `_DELEGATION_DEPTH=0`, 필요 시 `PIWORKER_HOME` 주입. `TurnPlanner.BuildOptions`에서 조립.
5. **Skill/Spool 격리(Step 7)**: worker에는 Main 오케스트레이션 스킬(worker-prompt)·`AGENTMANAGER_TASK_SPOOL`을 **주지 않는다**(delegation depth 0 강제). harness가 worker-task 스킬을 이미 주입.
6. **Session discovery(Step 6/10)**: `CliSessionDiscovery.DiscoverPi`가 `~/.pi/agent/sessions` 하드코딩 → worker는 `~/.pi-worker/agent/sessions`도 탐색해야 함.
7. **RPC 완료 상태 머신(Step 9)**: 현재 `PiAdapter`가 `agent_end` 단일 이벤트에서 즉시 `TurnCompleted`→kill(`KillAfterTurnCompleted=true`), `willRetry` 미파싱. pi/pi-worker 공통 RPC라 위험 동일. **실측 이벤트 캡처 후** 근거 기반으로만 수정. 근거 부족 시 측정값+차단원인 기록.

### 폐기한 대안
- pi-worker를 별도 EngineDef/adapter로 등록 → 제약 위반(별도 엔진 금지), report/queue 재작업 유발. 폐기.
- 범용 Profile 시스템 → 제약 위반. 폐기.
- pi-worker를 `.cmd` shim으로만 구동 → Windows RedirectStdin arg-escaping 리스크. index.js+node를 1순위로. 폐기(단, 직접실행 경로는 fallback으로 유지).

## Completed
- **Step 1 (pi-worker 배포 형태 검증)** — 완료. `node dist/cli/index.js`로 `--version`/`doctor` green. 위 "확정 아키텍처" 참조.
- **Step 2–5 (launch binding + path config + env)** — 완료(빌드 green).
  - `SettingsService.PiWorkerPath` 신설 + persistence(`AppStateStore.AppSettingsDto.PiWorkerPath`, `AppViewModel.Persistence` load/save, `AppViewModel._piWorkerPath` 위임, `AppViewModel.Settings` editor/detect/pull/save).
  - `EngineRegistry.ResolveExe(..., piWorker, piWorkerPath)` + `ResolvePiWorker()`(npm-global `@agentmanager/pi-worker-harness/dist/cli/index.js` 자동탐지) + `DetectPiWorkerExe()`/`IsPiWorkerInstalled()`.
  - `PiAdapter.BuildStartInfo`: exe가 `.js`면 `node <path>`, 아니면 직접 실행(pi/pi-worker 공용, 별도 엔진 아님).
  - `TurnPlanner`: `EngineResolveRequest.Worker/PiWorkerPath`, `TurnOptionsRequest.Worker/SessionId/ProjectId/TaskId/PiWorkerHome`. `BuildOptions`가 worker에 `AGENTMANAGER_ROLE/SESSION_ID/PROJECT_ID/DELEGATION_DEPTH(+TASK_ID/PIWORKER_HOME)` 주입, **worker엔 `AGENTMANAGER_TASK_SPOOL` 미주입**(delegation depth 0).
  - `AppViewModel.Run.cs`: `EngineResolveRequest`/`TurnOptionsRequest`에 `Worker=s.IsWorker` 등 전달, worker엔 `WatchSessionTaskSpool` 미설정.
- **Step 8 (launch 특성화 테스트)** — 완료. `AgentManager.Smoke/Program.cs: AssertPiWorkerLaunch()` — node/direct 분기, 모델 pass-through, worker env 주입, TASK_SPOOL 격리, ResolveExe 역할별 해석을 검증. `dotnet run --project src/AgentManager.Smoke -c Release` → `pi/pi-worker launch + env asserts OK`, 전체 스모크 green.

- **Step 6 (session discovery) + Step 7/10 (skill 격리)** — 완료(빌드+스모크 green).
  - `CliSessionDiscovery.PiSessionsRoot(worker)` 신설 → `.pi`/`.pi-worker` 하드코딩 단일화. `DiscoverPi(projectPath, max, worker)` 역할 인자. Worker는 `~/.pi-worker/agent/sessions`(+`PIWORKER_HOME` 반영), General은 `~/.pi/agent/sessions`.
  - `AppViewModel.History.ResyncTranscriptAsync`: pi 세션이면 `DiscoverPi(cwd, worker: s.IsWorker)`로 역할별 루트 스캔(Worker transcript resync가 이제 동작).
  - **Resume는 이미 역할 무관하게 동작** — live `EngineSessionId`→`--session` 플래그(pi-worker가 자기 루트에서 resolve). 경로 지식 불필요.
  - **Skill 격리는 구성상 성립**: AM은 skill을 `~/.pi/agent/skills`(Main)에만 주입, 절대 worker 루트/`~/.agents`에 안 씀. pi-worker는 harness의 worker-task 스킬을 package로 로드. worker엔 `AGENTMANAGER_TASK_SPOOL` 미주입(Step 2-5)이라 delegation 스킬이 보여도 쓸 대상 없음. 스모크 가드 추가(`skill inject dir = ~/.pi`).
  - 알려진 한계: `~/.agents/skills`는 pi/pi-worker 공유(harness doctor가 명시). AM 기본값은 이 경로를 안 쓰므로 AM발 leak 없음. 사용자가 pi skill dir을 `~/.agents/skills`로 바꾸면 leak 가능 → 문서화.

- **Step 9 (RPC 완료 상태 머신) + Step 11(abort/timeout/cleanup)** — 완료(빌드+스모크 green). **근거: 버전 정확 소스**(pi 0.80.3 `dist/core/agent-session.d.ts` + `.js`), 라이브 retry 캡처가 아님(retry는 provider 일시 오류가 있어야 관찰 가능 — 재현 불안정. 소스가 결정적).
  - **완료 판정**: `agent_end.willRetry`(= `_willRetryAfterAgentEnd` = retry enabled ∧ attempt<maxRetries ∧ last-assistant-가-retryable-error). `agent_end`는 **시도마다 1회** 발생. `willRetry:true`면 auto-retry가 이어지므로 완료 아님 → **`willRetry:false`일 때만 `TurnCompleted`**. willRetry는 maxRetries로 bounded → 항상 종료.
  - `PiAdapter`: `agent_end`에서 willRetry:true면 `break`(완료 안 함, 프로세스 유지). willRetry 필드 없으면 false로 간주(방어). 성공 assistant message_end면 `_turnErrored=false` 리셋(회복된 retry가 오류로 보고되지 않게).
  - **abort/cleanup**: `AgentSession`이 취소 및 TurnCompleted 시 `proc.Kill(entireProcessTree:true)`. 하네스 spawn(`shell:true`, tree=node→cmd.exe→node, detached 아님)이라 트리 kill이 자식 official pi까지 도달 → **orphan 없음**(E2E에서 재확인 예정). graceful `{"type":"abort"}`는 선택적 후속(pi가 세션을 증분 저장하므로 hard kill로도 안전).
  - **timeout**: 일반 턴 타임아웃 없음. 무기한 대기 위험은 blocking `extension_ui_request` → Step 12에서 즉시 deny/cancel로 차단.
  - 테스트: `AssertPiCompletionStateMachine()` — willRetry:true 미완료, willRetry:false 완료, 회복 retry=성공, 비재시도 오류=errored, willRetry 필드 없음=완료.

- **Step 12 (extension_ui_request)** — 완료(빌드+스모크 green). 스키마: pi 0.80.3 `rpc-types.d.ts` + `rpc-mode.js`(pendingExtensionRequests).
  - Blocking(select/confirm/input/editor)은 응답 없으면 턴 무기한 hang → `PiAdapter`가 **즉시 `{type:extension_ui_response,id,cancelled:true}`를 stdin writeback**(EngineWriteback, UI엔 노출 안 됨). 확장은 안전 기본값(undefined/false=deny)으로 resolve.
  - 수신 즉시 취소 → pending이 절대 안 남음 → abort/exit 시 정리할 pending 없음(무해).
  - fire-and-forget(notify/setStatus/setWidget/setTitle/set_editor_text)은 무시. WPF 재설계 없음.
  - 테스트: `AssertPiExtensionUi()`.

- **Step 12-14 (E2E + 회귀 + 도그푸딩)** — **완료(무료 로컬 모델로 실제 턴 통과)**.
  - **실제 C# 파이프라인 라이브 E2E 통과**: `AgentManager.Smoke -- --pi-worker-live`가 실제 `PiAdapter+AgentSession`으로 실제 `pi-worker` 프로세스 + 실제 모델(dgx-spark/qwen3-30b-a3b, 사용자 자가 서버 = 무료)을 구동. 결과: `sessionId` 캡처, `TurnCompleted isError=False`, assistant text `"OK"`, 15s. worker env ROLE=worker + TASK_SPOOL 미주입 확인.
  - **완료 판정 라이브 검증**: 실제 이벤트열 `response,response,agent_start,turn_start,message_start,message_end,message_start,message_update×N,message_end,turn_end,agent_end(willRetry=false)` → willRetry:false 완료 로직이 실제와 일치.
  - **프로세스 정리 라이브 검증**: RunAsync 종료 후 orphan node/cmd/pi 프로세스 0(`entireProcessTree` kill이 자식 official pi까지 도달).
  - **회귀**: 전체 스모크(codex app-server/antigravity/claude/quick-reply/approval broker + 모든 pi/pi-worker) 동시 green(RUN_EXIT=0). General pi(non-worker)는 경로/스풀 불변.
  - **GUI 도그푸딩**: AgentManager GUI에서 pi 엔진 Worker 세션으로의 위임 도그푸딩은 사용자 몫(2번째 AM 인스턴스 금지 원칙). pi-worker 런타임 자체는 위 라이브 E2E로 반복 도그푸딩됨.

## Commits (feature/pi-worker-integration, SSOT)
`c64660f`(base master/develop) 이후 (구현 7 + 최종정리 2, 최신 목록은 `git log --oneline c64660f..HEAD`):
```
e3670a5 feat(pi): launch pi-worker for Worker-role pi sessions
a011d1a feat(pi): role-aware pi session discovery for worker resync
37cbc7f fix(pi): complete turn only on agent_end willRetry:false
2e59f87 feat(pi): cancel blocking extension_ui_request to prevent turn hang
58bed05 docs(pi): AM-side Pi Worker integration design + handoff update
2709ff3 test(pi): opt-in live pi-worker E2E harness + record results
ae1b5e5 docs(pi): finalize handoff — all steps complete, follow-ups listed
d67ef2c docs(pi): pin fixed harness dep, finalize common worker policy, reconcile handoff
1dfbc54 docs(pi): README Pi Worker install/enablement + record final validation
```
(이 목록을 갱신하는 최종 doc 커밋 1건이 뒤따름.)

## Merge Readiness
- **머지 준비 상태**: 코드/문서/검증 완료. **push/merge 미수행**(사용자 승인 대기).
- **권장 대상**: `feature/pi-worker-integration` → `develop`(현재 `develop`==`master`==c64660f). GitFlow상 이후 release 검증 → `master`.
- **머지 전 남은 필수**: (1) GUI 수동 E2E 1회(위 절차) · (2) master 머지 직전 README 최신 확인(프로젝트 관례). build/smoke/live-E2E 회귀는 green.
- **충돌 위험**: base(c64660f) 이후 feature만 진행 — develop/master가 그 자리에 있으면 fast-forward 가능.
- **비저장소 변경**: `~/.pi-worker/agent/models.json`(dgx-spark, E2E용, 저장소 밖). 원치 않으면 삭제 가능. `~/.pi` 불변.

## In Progress
- 없음. (핵심 통합 완료. 진행 중인 코드 작업 없음.)

## Remaining
핵심 Pi Worker 통합 작업 없음.

### Merge 전 필수
1. GUI 수동 E2E 검증 (아래 "GUI Verification" 참조 — 절차 기록됨)
2. 최종 전체 회귀 테스트 (build + smoke + 가능 시 live E2E)
3. 문서 정합성 확인 (본 문서 + PI_WORKER_INTEGRATION_KO.md + README)
4. README 설치 안내 갱신
5. 사용자 승인 후 merge (`feature/pi-worker-integration` → `develop`)

### 선택적 후속 (핵심 완료와 분리)
1. Settings XAML의 Pi Worker 경로 입력 행 (VM/자동탐지/settings.json으로 이미 동작)
2. 원격 provider 인증 환경변수 지원 (`CoreHelpers.ApiEnvVar("pi")` 확장) — 로컬 dgx-spark는 동작
3. graceful RPC abort (`{"type":"abort"}`) — 현재 트리 kill로 안전
4. Linux/macOS 검증 (현재 Windows 1차 대상)

## Validation (2026-07-11 최종)
| command | exit | result |
|---|---|---|
| `dotnet build AgentManager.slnx -c Release` | 0 | 경고 0 / 오류 0 |
| `dotnet run --project src/AgentManager.Smoke -c Release` | 0 | 전체 스모크 green — General Pi/Pi Worker(launch·env·session root·skill 격리)·willRetry 상태머신·extension UI cancel + cc/gx/agy(codex app-server/antigravity/claude)·quick-reply·approval broker 모두 통과(회귀 없음) |
| `… --pi-worker-live` (AM_PIWORKER_PATH=harness index.js, MODEL=dgx-spark/qwen3-30b-a3b) | 0 | 실제 PiAdapter+AgentSession+pi-worker: sessionId 캡처, TurnCompleted isError=false, text="OK", 18s |
| orphan check (Win32_Process) | — | pi-worker/pi 잔여 프로세스 0 |
| `~/.pi/agent/auth.json` mtime | — | Jul 7(작업 전) 그대로 — **~/.pi 불변**. 세션은 `~/.pi-worker`에만 생성(get_state 실측) |
- harness 확인: `node …/dist/cli/index.js --version` → `wraps official pi 0.80.3`; `doctor` → All checks passed(이전 실측, 고정 커밋 6e49dbd).

## Known Failures
- (없음) 현재까지 재현되는 실패 없음.

## Files Changed
- `src/AgentManager.Core/Settings/SettingsService.cs` — `PiWorkerPath` 설정. (완료)
- `src/AgentManager.Core/Agents/EngineRegistry.cs` — `ResolveExe` worker 인자 + `ResolvePiWorker/DetectPiWorkerExe/IsPiWorkerInstalled`. (완료)
- `src/AgentManager.Core/Agents/PiAdapter.cs` — node/direct 분기. (완료)
- `src/AgentManager.Core/Orchestration/TurnPlanner.cs` — worker 역할·env·spool 격리. (완료)
- `src/AgentManager/Persistence/AppStateStore.cs` — DTO `PiWorkerPath`. (완료)
- `src/AgentManager/ViewModels/AppViewModel.cs` / `.Persistence.cs` / `.Settings.cs` — VM 위임·load/save·detect. (완료)
- `src/AgentManager/ViewModels/AppViewModel.Run.cs` — worker 전달 + task-spool 격리. (완료)
- `src/AgentManager.Smoke/Program.cs` — `AssertPiWorkerLaunch()` 특성화 테스트(launch+env+session root+skill 격리). (완료)
- `src/AgentManager.Core/Workspace/CliSessionDiscovery.cs` — `PiSessionsRoot(worker)` + `DiscoverPi(worker)`. (완료)
- `src/AgentManager/ViewModels/AppViewModel.History.cs` — resync 역할별 pi 루트. (완료)
- `src/AgentManager.Core/Agents/PiAdapter.cs` — willRetry 완료 판정 + extension_ui_request cancel + 헤더 doc. (완료)
- `src/AgentManager.Smoke/Program.cs` — 특성화 3종 + `--pi-worker-live` 하네스. (완료)
- `docs/PI_WORKER_INTEGRATION_KO.md` — AM 측 설계 기록(고정 harness dep 포함). (완료)
- `docs/HANDOFF_AGENTMANAGER_PI_WORKER_KO.md` — 본 인계 문서. (계속 갱신)
- `README.md` — Pi Worker 설치/활성화 안내. (정리 작업에서 추가)

## Next Action
1. (사용자) 위 "GUI Verification" 수동 절차 1회 실행 → 결과 기록.
2. (사용자 승인 후) `feature/pi-worker-integration` → `develop` 머지. GitFlow상 이후 release 검증 → `master`. master 머지 전 README 최신화 확인.
3. (선택) 위 "선택적 후속" 항목은 별도 이슈/브랜치로.

## Do Not Repeat (완료 — 재구현 금지)
- **Pi Worker launch binding** — `EngineRegistry.ResolveExe`(role) + `PiAdapter`(exe .js 여부 node/direct).
- **역할별 session discovery** — `CliSessionDiscovery.PiSessionsRoot(worker)` + resync.
- **Skill 및 spool 격리** — AM은 `~/.pi`에만 skill 주입, worker엔 TASK_SPOOL 미주입.
- **willRetry 완료 판정** — `agent_end.willRetry==false`일 때만 완료(pi 0.80.3 소스 근거).
- **extension_ui_request 즉시 cancel** — blocking은 `{cancelled:true}` writeback.
- **프로세스 트리 cleanup** — `Kill(entireProcessTree:true)`가 자식 pi까지.
- **로컬 모델 live E2E** — `--pi-worker-live`로 dgx-spark 통과(재실행은 회귀 확인용만).
- harness 내부 구조/배포형태 재탐색 불필요 — bin=`dist/cli/index.js`, 고정 커밋 6e49dbd, 계약=`docs/AGENTMANAGER_PI_WORKER_INTEGRATION_KO.md`.

## Worker Delegation Policy (확정 — AgentManager 공통)
Pi 전용이 아니라 **모든 Worker 역할 세션의 공통 정책**이다.
```
Main / General 세션
  → Task spool 사용 가능 (AGENTMANAGER_TASK_SPOOL)
  → Worker 생성 가능
Worker 세션 (cc · gx · agy · pi 무관)
  → Task spool 미제공
  → 다른 Worker 생성 불가
  → AGENTMANAGER_DELEGATION_DEPTH = 0
```
- 구현: `TurnPlanner.BuildOptions`가 `!r.Worker`일 때만 `AGENTMANAGER_TASK_SPOOL` 주입, worker엔 `AGENTMANAGER_ROLE/SESSION_ID/PROJECT_ID/DELEGATION_DEPTH=0` 주입. `AppViewModel.Run`이 worker엔 `WatchSessionTaskSpool` 미설정.
- 이유: 무제한 재귀 위임 방지, 비용·동시성 예측 가능, `WorkerTaskStore` 경쟁 감소, 보고 경로 단순화, 엔진 native subagent와 AM Worker 혼합 방지.
- cc/gx/agy Adapter 자체는 재설계하지 않음(정책은 role 레벨, 엔진 실행구조 불변).

## GUI Verification (수동 E2E)
- **자동 실행 안 함**: 메모리 원칙 "Single AgentManager instance only" — 사용자 프로젝트에 2번째 AM 인스턴스를 띄우면 consume-once spool + 공유 state.json이 워커 백로그를 손상시킴. 그래서 **에이전트가 GUI를 띄우지 않음**. 동일 런타임 경로(`PiAdapter+AgentSession`+실제 pi-worker)는 헤드리스 `--pi-worker-live`로 이미 검증됨.
- **사용자 수동 절차** (한 번):
  1. AgentManager 실행(기존 사용자 인스턴스).
  2. 설정에서 `PiWorkerPath` = `H:\Git\Bosung_PI\pi-worker-harness\dist\cli\index.js` 지정(또는 harness를 `npm link`).
  3. Main/General 세션 생성 → Pi 엔진으로 Worker 세션 생성(위임).
  4. 위임 프롬프트(예: "이 저장소의 README 위치를 찾아 첫 제목만 보고. 파일 수정 금지."), 모델 `dgx-spark/qwen3-30b-a3b`.
  5. 확인: 실행 파일이 pi-worker인지 · 최종 Markdown 보고 반환 · `WorkerTaskStore`에 done+report · Main 세션에 결과 전달 · 종료 후 orphan 없음 · `~/.pi/agent` 불변 · 세션은 `~/.pi-worker`에만 생성.
  6. 결과를 본 문서 + PI_WORKER_INTEGRATION_KO.md에 기록(일시/AM commit/harness commit/모델/결과/시간/report routing/isolation/orphan).

## 공유 코드 동작 변경 (확정 — 위 "Worker Delegation Policy" 참조)
- `TurnPlanner.BuildOptions`가 **모든 Worker 역할 세션**(cc/gx/agy/pi 무관)에 `AGENTMANAGER_TASK_SPOOL`을 주지 않고 `AGENTMANAGER_ROLE/SESSION_ID/PROJECT_ID/DELEGATION_DEPTH=0`을 준다. `AppViewModel.Run`도 worker엔 `WatchSessionTaskSpool` 미설정. **코드=문서 일치 확인함**(`if (!r.Worker)` / `if (!s.IsWorker)`).
- 이것은 **AgentManager 공통 Worker 정책으로 확정**된 것이며 Pi 전용 임시 변경이 아니다. 엔진 어댑터(cc/gx/agy) 실행 구조는 불변(정책은 role 레벨). 만약 향후 특정 엔진만 예외 처리하려면 `BuildOptions`의 `!r.Worker` 게이트를 조정하면 되지만, 현재 결정은 **전 엔진 공통 적용**이다.

## Safety Notes
- **워커 루트 변경(사용자 승인함)**: 라이브 E2E를 위해 `~/.pi/agent/models.json` → `~/.pi-worker/agent/models.json` 복사(= 워커에 dgx-spark 로컬 provider + 그 apiKey 설정). 워커가 이제 dgx-spark로 실제 응답 가능. 원치 않으면 `~/.pi-worker/agent/models.json` 삭제(기존 백업은 `.bak.*`가 있으면 그것). `~/.pi`는 **불변**.
- **provider 키**: `dgx-spark`는 사용자 자가 서버(`http://8eh1ndy0u.iptime.org:8083/v1`, llama-swap)라 무료. models.json에 그 서버용 apiKey 포함(자기 서버 키).
- 사용자 승인 없이 push/release/커밋 강제 금지. 커밋은 논리 단위로만, 기존 변경과 섞지 않기(현재는 clean이라 충돌 없음).
- `~/.pi`(공식 pi 설정) 절대 변경 금지 — worker는 `~/.pi-worker`만 사용.
- `pi-upstream` 읽기 전용, Pi 코어 수정 금지.
- harness 커밋은 AgentManager와 분리(다른 저장소).
- 커밋 메시지에 Co-Authored-By/모델명/AI 생성 문구/로봇 이모지 넣지 않기(지시서 17절).
