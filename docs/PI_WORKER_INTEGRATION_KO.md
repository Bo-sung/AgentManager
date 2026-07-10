# AgentManager ↔ Pi Worker 통합 설계 (AM 측)

> 상대 문서: harness의 실행 계약 `H:\Git\Bosung_PI\pi-worker-harness\docs\AGENTMANAGER_PI_WORKER_INTEGRATION_KO.md`.
> 이 문서는 그 계약을 소비하는 **AgentManager 측 구현**을 기록한다. 진행 로그/인계는 [HANDOFF_AGENTMANAGER_PI_WORKER_KO.md](HANDOFF_AGENTMANAGER_PI_WORKER_KO.md).

## 0. 고정 의존성 (Fixed Pi Worker Harness)
```
Package:            @agentmanager/pi-worker-harness
Version:            0.1.0
Commit:             6e49dbd0f6b2858dc4d946311843ebc2ba6dde10  (branch develop, tag 없음)
Supported Pi:       0.80.3 (pinned)
Runtime entrypoint: dist/cli/index.js  (bin: pi-worker)
Node:               ≥ 22.19
```
harness는 **커밋 완료된 고정 외부 런타임**이다. 이 통합은 위 버전/커밋을 대상으로 하며, harness는 이 저장소에서 수정하지 않는다(별도 저장소). 로컬 개발 경로 `H:\Git\Bosung_PI\pi-worker-harness`는 개발 참고용이며, 공개 설치 계약은 package/version/entrypoint 기준이다.

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

### 4.1 Worker Delegation Policy (AgentManager 공통 — Pi 전용 아님)
이 정책은 Pi뿐 아니라 **모든 Worker 역할 세션**(cc·gx·agy·pi)에 적용되는 AgentManager 공통 정책이다.
```
Main / General 세션 : Task spool 사용 가능(AGENTMANAGER_TASK_SPOOL) · Worker 생성 가능
Worker 세션         : Task spool 미제공 · 다른 Worker 생성 불가 · AGENTMANAGER_DELEGATION_DEPTH=0
```
- 구현: `TurnPlanner.BuildOptions`가 `!r.Worker`일 때만 `AGENTMANAGER_TASK_SPOOL` 주입. `AppViewModel.Run`은 worker엔 `WatchSessionTaskSpool` 미설정.
- 이유: 무제한 재귀 위임 방지 · 비용/동시성 예측 가능 · `WorkerTaskStore` 경쟁 감소 · 보고 경로 단순화 · 엔진 native subagent와 AM Worker 혼합 방지 · Worker 작업 범위 통제.
- cc/gx/agy Adapter 자체는 재설계하지 않음 — 정책은 **role 레벨**이며 엔진 실행구조는 불변. (비-pi 워커도 이제 하위 위임 불가 — 위 공통 정책의 결과.)

## 5. 세션 루트 / discovery (`CliSessionDiscovery`)
- `PiSessionsRoot(worker)`: General=`~/.pi/agent/sessions`, Worker=`~/.pi-worker/agent/sessions`(+`PIWORKER_HOME`). `.pi`/`.pi-worker` 하드코딩을 한 곳에.
- resume: 라이브 `EngineSessionId`(get_state로 확보) → `--session`. **역할 무관**하게 이미 동작(경로 지식 불필요).
- transcript resync: pi 세션이면 `DiscoverPi(cwd, worker: s.IsWorker)`로 역할별 루트 스캔.

## 6. RPC 완료 상태 머신 (`PiAdapter`)
근거: pi 0.80.3 `dist/core/agent-session.d.ts`/`.js`.
- `agent_end`는 **시도마다** 발생, `willRetry: boolean` 동반(`_willRetryAfterAgentEnd` = retry enabled ∧ attempt<maxRetries ∧ last-assistant-가-retryable-error).
- **완료 = `agent_end.willRetry == false`**. willRetry는 maxRetries로 bounded → 항상 종료.
- **`willRetry` 필드 누락 시**: backward compatibility를 위해 `false`(= 완료)로 간주한다. 만약 미래 pi 버전이 이 필드를 없애면 프로토콜 변화 신호다 — 현재 `PiAdapter`는 순수 파서(로거 미주입)라 별도 debug/warning 로그는 두지 않았다(로깅 프레임워크 신설 회피). 필드 유무는 `AssertPiCompletionStateMachine`가 커버하며, 프로토콜 드리프트 탐지가 필요해지면 이 지점에 경고 로그를 추가한다(후속).
- 성공 assistant `message_end`면 `_turnErrored=false` 리셋 → 회복된 auto-retry는 성공으로 보고.
- abort/cleanup: `AgentSession`이 취소·완료 시 `proc.Kill(entireProcessTree:true)`. 하네스가 자식 pi를 `shell:true`(node→cmd.exe→node, detached 아님)로 띄우므로 트리 kill이 자식까지 도달 → orphan 없음.

## 7. extension_ui_request — Worker headless UX 정책 (`PiAdapter`)
근거: pi 0.80.3 `dist/modes/rpc/rpc-types.d.ts` + `rpc-mode.js`. **이것은 임시 우회가 아니라 현재 Worker UX 정책이다**: Pi Worker는 headless 실행 역할이며, Worker Extension은 사용자에게 직접 질문할 수 없다 — 대화형 요청은 안전 기본값으로 취소/거부된다.
- blocking(`select`/`confirm`/`input`/`editor`)은 응답 없으면 턴이 무기한 hang → 수신 즉시 `{type:extension_ui_response,id,cancelled:true}`를 stdin writeback → 확장이 안전 기본값(undefined/false=deny)으로 resolve.
- 즉시 취소라 pending이 안 남음 → abort/exit 정리 불필요.
- fire-and-forget(`notify`/`setStatus`/`setWidget`/`setTitle`/`set_editor_text`)은 무시.
- Main/General Pi의 대화형 Extension UI 지원은 별개 주제다(이번 범위 아님, 전체 WPF Extension UI 미구현).

## 8. 설정 (`SettingsService.PiWorkerPath`)
- 빈 값 = 자동탐지. 기존 설정과 backward compatible(신규 필드, 기본 "").
- 설정 UI VM: `SettingsPiWorkerPath` + `PiWorkerDetectLabel` + `DetectPiWorkerPath()`. (WPF XAML 행은 후속 — 현재는 settings.json/자동탐지로 동작.)

## 9. 테스트 (`AgentManager.Smoke`, 토큰 0)
`dotnet run --project src/AgentManager.Smoke -c Release`:
- `AssertPiWorkerLaunch` — node/direct 분기, 모델 pass-through, worker env 주입, TASK_SPOOL 격리, ResolveExe 역할 해석, 세션 루트 역할별, skill 주입 dir(~/.pi) 격리.
- `AssertPiCompletionStateMachine` — willRetry:true 미완료 / false 완료 / 회복 retry 성공 / 비재시도 오류 errored / willRetry 필드 없음 완료.
- `AssertPiExtensionUi` — blocking cancel writeback / fire-and-forget 무시.
- **라이브 E2E(opt-in, 무료 로컬 모델)**: `AM_PIWORKER_PATH=… AM_PIWORKER_MODEL=dgx-spark/qwen3-30b-a3b dotnet run --project src/AgentManager.Smoke -- --pi-worker-live`. 실제 `PiAdapter+AgentSession`이 실제 `pi-worker` 프로세스+모델 구동 → SessionStarted/TurnCompleted(isError=false)/assistant text 검증. 실측 통과(text="OK", orphan 0).
- **검증용 로컬 Publish + GUI 시작(2026-07-11)**: `dotnet publish …AgentManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts\publish\pi-worker-gui-e2e`(exit 0). Published `AgentManager.exe` 실행 → WPF 창 ~2s 생성·무크래시·자동세션 없음·graceful close·orphan 0. **상호작용 위임 GUI E2E는 앱 상태(state.json) 변경 수반 + 픽셀 자동화 신뢰성 문제로 에이전트 미수행** → HANDOFF의 사용자 체크리스트로 인계(런타임은 위 헤드리스 E2E가 커버).

## 10. 알려진 한계 / 후속
- 라이브 E2E는 로컬 dgx-spark 모델로 통과. 다른 provider(zai/anthropic 등) 실턴은 워커 루트 인증 또는 env 키 주입 필요(§10 아래).
- `~/.agents/skills`는 pi/pi-worker 공유(하네스 doctor 명시). AM 기본값은 이 경로를 안 써서 AM발 leak 없음. 사용자가 pi skill dir을 그리로 바꾸면 leak 가능.
- 설정 화면 pi-worker 경로 **XAML UI 행**은 미추가(VM/자동탐지/settings.json으로 동작). 후속 UI 작업.
- graceful `{"type":"abort"}`는 미구현(하드 트리 kill로 충분 — pi가 세션 증분 저장). 선택적 후속.
- 비-pi(cc/gx/agy) 워커도 이제 task-spool 미수령(일반 워커 정책). pi-only로 좁히려면 HANDOFF "주의" 참조.
- **워커 provider 인증**: pi-worker는 격리 루트 `~/.pi-worker`를 쓰므로 공식 `pi`의 `~/.pi` 인증이 안 넘어감. 현재 `CoreHelpers.ApiEnvVar("pi")`는 null이라 AM이 pi에 provider 키를 env로 주입하지 않는다(cc/gx/agy만 주입). 따라서 워커가 원격 provider(zai/anthropic 등)로 실턴하려면 (a) 워커 루트에 인증 설정을 넣거나(예: models.json/auth.json), (b) `ApiEnvFor("pi")`가 선택 모델의 provider에 맞는 키(ZAI_API_KEY 등)를 주입하도록 확장해야 함. 로컬 dgx-spark는 models.json에 endpoint+key가 있어 별도 주입 없이 동작(라이브 E2E로 확인). — **후속 작업 후보**.
