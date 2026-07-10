# AgentManager Pi Worker Handoff

## Last Updated
- 2026-07-10 (KST) — 작성 에이전트: Claude (Opus 4.8)
- 상태: **Step 1~15 전부 완료**. 빌드 green, 전체 스모크 green, 실제 pi-worker 라이브 E2E(로컬 모델, 무료) 통과, orphan 0.
- 브랜치: `feature/pi-worker-integration` (master에서 분기, GitFlow). **push/머지 안 함**(사용자 승인 대기).
- 커밋(6): e3670a5 launch binding · a011d1a session discovery · 37cbc7f willRetry 완료판정 · 2e59f87 extension_ui cancel · 58bed05 design doc · 2709ff3 live E2E harness.

## Repository State
- Path: `J:\prj\AgentManager`
- Branch: `master`  (착수 시점 clean)
- HEAD(착수): `c64660f` — release: v1.19.5
- 외부 참고 저장소:
  - `H:\Git\Bosung_PI\pi-worker-harness` — **완성된 Worker 런타임**. 단, git 브랜치 `feature/pi-worker-harness`에 **커밋 0개(전부 uncommitted working tree)**. dist/ 빌드 산출물 존재. AgentManager 작업과 **다른 커밋**으로 취급(섞지 말 것). 결함 확인 시에만 별도 수정.
  - `H:\Git\Bosung_PI\pi-upstream` — Pi 원본, **읽기 전용**.

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

## In Progress
- (없음) — Step 1~15 모두 완료. 남은 것은 선택적 후속(설정 XAML 행, graceful abort, ApiEnvVar("pi") 멀티 provider 키 주입).

## Remaining (우선순위 순)
1. Step 2–5 launch binding (진행 중)
2. Step 6 session discovery + Step 7/10 skill·spool 격리
3. Step 8 PiAdapter characterization test
4. Step 9 RPC 완료 상태 머신 + Step 11 abort/timeout/cleanup (실측 캡처 선행)
5. Step 12 extension_ui_request
6. Step 12–14 E2E + 기존 엔진 회귀 + 도그푸딩
7. Step 15 문서/테스트 보고 마무리

## Worker Tasks
- (아직 없음) AgentManager Pi Worker 통합이 안정되기 전까지 `pi-worker`는 `node dist/cli/index.js`로 직접 구동해 조사/캡처에 사용. 통합 안정 후 AgentManager 경유 위임으로 전환.

## Validation
- `node H:\Git\Bosung_PI\pi-worker-harness\dist\cli\index.js --version` → exit 0, `wraps official pi 0.80.3`.
- `node ... doctor` → exit 0, All checks passed.
- `dotnet build AgentManager.slnx -c Release` → 경고 0/오류 0 (Step 2–5 후 재확인 green).
- `dotnet run --project src/AgentManager.Smoke -c Release` → exit 0, `pi/pi-worker launch + env asserts OK` + 전체 스모크 green(기존 엔진 회귀 없음).

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
- `docs/HANDOFF_AGENTMANAGER_PI_WORKER_KO.md` — 본 인계 문서. (계속 갱신)

## Next Action (모두 선택적 후속 — 핵심 통합은 완료)
- (a) 사용자 승인 시 `feature/pi-worker-integration` → develop/master 머지 + README 갱신(master 머지 전 규칙).
- (b) 설정 화면에 pi-worker 경로 입력 **XAML 행** 추가(VM `SettingsPiWorkerPath`/`PiWorkerDetectLabel`/`DetectPiWorkerPath` 이미 존재 — 바인딩만). 로컬라이즈 문자열 중복 x:Key 주의.
- (c) 원격 provider 워커 실턴 지원: `CoreHelpers.ApiEnvVar("pi")` 확장 또는 워커 루트 인증 흐름(설계doc §10). 로컬 dgx-spark는 이미 동작.
- (d) 사용자 배포 편의를 위해 `pi-worker`를 `npm i -g`/`npm link` → `ResolvePiWorker()` 자동탐지가 잡힘(현재 미설치라 `PiWorkerPath` 지정 필요).

## Do Not Repeat
- Step 1(pi-worker 배포형태/버전/isolation) 재검증 불필요 — 위 실측값 사용.
- 저장소 상태 복구(clean, master, HEAD c64660f) 재조사 불필요.
- harness 내부 구조 재탐색 불필요 — bin=`dist/cli/index.js`, 계약 문서 = `docs/AGENTMANAGER_PI_WORKER_INTEGRATION_KO.md`.
- Step 2–5 launch binding 재구현 금지 — 위 파일들에 완료(빌드+스모크 green).
- pi/pi-worker 실행 분기는 `PiAdapter`(exe .js 여부)와 `EngineRegistry.ResolveExe`(role)에 이미 있음.

## 주의 — 공유 코드 동작 변경 (검토 지점)
- `TurnPlanner.BuildOptions`가 이제 **모든 Worker 역할 세션**(cc/gx/agy/pi 무관)에 `AGENTMANAGER_TASK_SPOOL`을 주지 않고, `AGENTMANAGER_ROLE/SESSION_ID/PROJECT_ID/DELEGATION_DEPTH`를 준다. `AppViewModel.Run`도 worker엔 `WatchSessionTaskSpool` 미설정.
  - 근거: 지시서 "Worker delegation depth=0, Worker에게 Main용 task spool·delegation Skill 미제공"(일반 워커 원칙).
  - 영향: 기존 cc/gx/agy 워커도 이제 하위 위임(task-spool)을 못 함. 엔진 실행 구조(adapter/launch)는 안 건드림 → "cc/gx/agy 재설계 금지"에는 저촉 안 된다고 판단. 다만 **비-pi 워커 동작 변경**이므로, pi 워커로만 한정하고 싶으면 `BuildOptions`의 `if (!r.Worker)` 게이트를 `if (!r.Worker || r.AgentId != "pi")`류로 좁히면 됨. (현재는 일반 적용.)

## Safety Notes
- **워커 루트 변경(사용자 승인함)**: 라이브 E2E를 위해 `~/.pi/agent/models.json` → `~/.pi-worker/agent/models.json` 복사(= 워커에 dgx-spark 로컬 provider + 그 apiKey 설정). 워커가 이제 dgx-spark로 실제 응답 가능. 원치 않으면 `~/.pi-worker/agent/models.json` 삭제(기존 백업은 `.bak.*`가 있으면 그것). `~/.pi`는 **불변**.
- **provider 키**: `dgx-spark`는 사용자 자가 서버(`http://8eh1ndy0u.iptime.org:8083/v1`, llama-swap)라 무료. models.json에 그 서버용 apiKey 포함(자기 서버 키).
- 사용자 승인 없이 push/release/커밋 강제 금지. 커밋은 논리 단위로만, 기존 변경과 섞지 않기(현재는 clean이라 충돌 없음).
- `~/.pi`(공식 pi 설정) 절대 변경 금지 — worker는 `~/.pi-worker`만 사용.
- `pi-upstream` 읽기 전용, Pi 코어 수정 금지.
- harness 커밋은 AgentManager와 분리(다른 저장소).
- 커밋 메시지에 Co-Authored-By/모델명/AI 생성 문구/로봇 이모지 넣지 않기(지시서 17절).
