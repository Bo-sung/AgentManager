# AgentManager ↔ Pi Worker 통합 설계 (AM 측)

> 상대 문서: harness의 실행 계약 `H:\Git\Bosung_PI\pi-worker-harness\docs\AGENTMANAGER_PI_WORKER_INTEGRATION_KO.md`.
> 이 문서는 그 계약을 소비하는 **AgentManager 측 구현**을 기록한다. 진행 로그/인계는 [HANDOFF_AGENTMANAGER_PI_WORKER_KO.md](HANDOFF_AGENTMANAGER_PI_WORKER_KO.md).
> 기준 버전: pi `0.80.3`(pinned), harness `0.1.0`, Node ≥22.19.

## 1. 원칙
- 기존 공식 `pi`는 **General/Main** 그대로. **Worker 역할**일 때만 `pi-worker`(하네스)로 실행.
- `pi-worker`는 **별도 엔진 아님**. 기존 `pi` `EngineDef`/`PiAdapter` 하나를 공유하고, 세션 `Role`로 실행 파일만 분기.
- 범용 Profile 시스템 신설 안 함. Pi 코어/`pi-upstream`/cc·gx·agy 실행 구조 수정 안 함.

## 2. 실행 파일 해석 (역할별)
`SessionViewModel.Role == SessionRole.Worker`(`Workers/WorkerTypes.cs`)가 트리거.

```
pi (General/Main/Plain)  →  EngineRegistry.ResolvePi()      → ~/AppData/Roaming/npm/.../pi-coding-agent/dist/cli.js
pi (Worker)              →  EngineRegistry.ResolvePiWorker() → ~/AppData/Roaming/npm/.../@agentmanager/pi-worker-harness/dist/cli/index.js
```

- `EngineRegistry.ResolveExe(id, …, piWorker, piWorkerPath)`: `id=="pi" && piWorker`면 `ResolveOverride(piWorkerPath) ?? ResolvePiWorker()`.
- 우선순위: 설정 `PiWorkerPath` 오버라이드 → npm-global 하네스 자동탐지. 못 찾으면 typed error(`EngineUnavailable`) → 프론트 지역화.
- 자동탐지는 `npm i -g <tarball>`/`npm link` 설치 위치를 본다(하네스는 private 패키지라 registry에 없음). 미설치면 사용자가 `PiWorkerPath`로 `dist/cli/index.js`를 지정.

## 3. 프로세스 기동 (`PiAdapter.BuildStartInfo`)
- exe가 `.js`로 끝나면 `node <path> --mode rpc …`(pi cli.js·pi-worker index.js 공용).
- 아니면(실제 실행파일/shim 오버라이드) `<path> --mode rpc …` 직접 실행.
- 공식 pi 플래그 pass-through: `--model <id>`, `--thinking <level>`, `--session <id>`. provider 키 등은 `options.ExtraEnvironment`로 주입.

## 4. Worker 환경변수 (하네스 worker-guard가 읽음)
`TurnPlanner.BuildOptions`가 Worker 역할 세션에 주입:

| env | 값 |
|---|---|
| `AGENTMANAGER_ROLE` | `worker` |
| `AGENTMANAGER_SESSION_ID` | 세션 id |
| `AGENTMANAGER_PROJECT_ID` | 프로젝트 id |
| `AGENTMANAGER_DELEGATION_DEPTH` | `0` |
| `AGENTMANAGER_TASK_ID` | (있으면) |
| `PIWORKER_HOME` | (오버라이드 시) |

- Worker엔 `AGENTMANAGER_TASK_SPOOL`을 **주지 않음**(delegation depth 0 강제). 비-worker(Main/Plain)만 task-spool을 받는다.

## 5. 세션 루트 / discovery (`CliSessionDiscovery`)
- `PiSessionsRoot(worker)`: General=`~/.pi/agent/sessions`, Worker=`~/.pi-worker/agent/sessions`(+`PIWORKER_HOME`). `.pi`/`.pi-worker` 하드코딩을 한 곳에.
- resume: 라이브 `EngineSessionId`(get_state로 확보) → `--session`. **역할 무관**하게 이미 동작(경로 지식 불필요).
- transcript resync: pi 세션이면 `DiscoverPi(cwd, worker: s.IsWorker)`로 역할별 루트 스캔.

## 6. RPC 완료 상태 머신 (`PiAdapter`)
근거: pi 0.80.3 `dist/core/agent-session.d.ts`/`.js`.
- `agent_end`는 **시도마다** 발생, `willRetry: boolean` 동반(`_willRetryAfterAgentEnd` = retry enabled ∧ attempt<maxRetries ∧ last-assistant-가-retryable-error).
- **완료 = `agent_end.willRetry == false`**(필드 없으면 false). willRetry는 maxRetries로 bounded → 항상 종료.
- 성공 assistant `message_end`면 `_turnErrored=false` 리셋 → 회복된 auto-retry는 성공으로 보고.
- abort/cleanup: `AgentSession`이 취소·완료 시 `proc.Kill(entireProcessTree:true)`. 하네스가 자식 pi를 `shell:true`(node→cmd.exe→node, detached 아님)로 띄우므로 트리 kill이 자식까지 도달 → orphan 없음.

## 7. extension_ui_request (`PiAdapter`)
근거: pi 0.80.3 `dist/modes/rpc/rpc-types.d.ts` + `rpc-mode.js`.
- blocking(`select`/`confirm`/`input`/`editor`)은 응답 없으면 턴이 무기한 hang → 수신 즉시 `{type:extension_ui_response,id,cancelled:true}`를 stdin writeback → 확장이 안전 기본값(undefined/false=deny)으로 resolve.
- 즉시 취소라 pending이 안 남음 → abort/exit 정리 불필요.
- fire-and-forget(`notify`/`setStatus`/`setWidget`/`setTitle`/`set_editor_text`)은 무시.

## 8. 설정 (`SettingsService.PiWorkerPath`)
- 빈 값 = 자동탐지. 기존 설정과 backward compatible(신규 필드, 기본 "").
- 설정 UI VM: `SettingsPiWorkerPath` + `PiWorkerDetectLabel` + `DetectPiWorkerPath()`. (WPF XAML 행은 후속 — 현재는 settings.json/자동탐지로 동작.)

## 9. 테스트 (`AgentManager.Smoke`, 토큰 0)
`dotnet run --project src/AgentManager.Smoke -c Release`:
- `AssertPiWorkerLaunch` — node/direct 분기, 모델 pass-through, worker env 주입, TASK_SPOOL 격리, ResolveExe 역할 해석, 세션 루트 역할별, skill 주입 dir(~/.pi) 격리.
- `AssertPiCompletionStateMachine` — willRetry:true 미완료 / false 완료 / 회복 retry 성공 / 비재시도 오류 errored / willRetry 필드 없음 완료.
- `AssertPiExtensionUi` — blocking cancel writeback / fire-and-forget 무시.
- **라이브 E2E(opt-in, 무료 로컬 모델)**: `AM_PIWORKER_PATH=… AM_PIWORKER_MODEL=dgx-spark/qwen3-30b-a3b dotnet run --project src/AgentManager.Smoke -- --pi-worker-live`. 실제 `PiAdapter+AgentSession`이 실제 `pi-worker` 프로세스+모델 구동 → SessionStarted/TurnCompleted(isError=false)/assistant text 검증. 실측 통과(15s, text="OK", orphan 0).

## 10. 알려진 한계 / 후속
- 라이브 E2E는 로컬 dgx-spark 모델로 통과. 다른 provider(zai/anthropic 등) 실턴은 워커 루트 인증 또는 env 키 주입 필요(§10 아래).
- `~/.agents/skills`는 pi/pi-worker 공유(하네스 doctor 명시). AM 기본값은 이 경로를 안 써서 AM발 leak 없음. 사용자가 pi skill dir을 그리로 바꾸면 leak 가능.
- 설정 화면 pi-worker 경로 **XAML UI 행**은 미추가(VM/자동탐지/settings.json으로 동작). 후속 UI 작업.
- graceful `{"type":"abort"}`는 미구현(하드 트리 kill로 충분 — pi가 세션 증분 저장). 선택적 후속.
- 비-pi(cc/gx/agy) 워커도 이제 task-spool 미수령(일반 워커 정책). pi-only로 좁히려면 HANDOFF "주의" 참조.
- **워커 provider 인증**: pi-worker는 격리 루트 `~/.pi-worker`를 쓰므로 공식 `pi`의 `~/.pi` 인증이 안 넘어감. 현재 `CoreHelpers.ApiEnvVar("pi")`는 null이라 AM이 pi에 provider 키를 env로 주입하지 않는다(cc/gx/agy만 주입). 따라서 워커가 원격 provider(zai/anthropic 등)로 실턴하려면 (a) 워커 루트에 인증 설정을 넣거나(예: models.json/auth.json), (b) `ApiEnvFor("pi")`가 선택 모델의 provider에 맞는 키(ZAI_API_KEY 등)를 주입하도록 확장해야 함. 로컬 dgx-spark는 models.json에 endpoint+key가 있어 별도 주입 없이 동작(라이브 E2E로 확인). — **후속 작업 후보**.
