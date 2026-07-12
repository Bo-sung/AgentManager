# AgentManager Custom Engine Platform — Handoff (SSOT)

## Last Updated
- 2026-07-11 (KST) — Claude (Opus 4.8). 착수. 대규모 다단계 기능(지시서: Built-in Pi Worker 번들 + Custom CLI Engine 시스템).

## Repository State
- Base: `develop`=`master`=`origin`= `30c81b9` (v1.20.0). 동기화 확인, divergence 0.
- Work branch: `feature/custom-engine-platform` (30c81b9에서 분기). **push/merge/release 안 함**(사용자 승인 대기).
- 릴리즈 후보 버전: `v1.21.0`.

## Fixed Inputs
- pi-worker-harness: `@agentmanager/pi-worker-harness` 0.1.0, commit **`6e49dbd`**, **MIT**, **런타임 npm deps 0개**(plain node로 실행), bin=`dist/cli/index.js`, pinnedPi `0.80.3`. 소스: `H:\Git\Bosung_PI\pi-worker-harness`(읽기 전용, 이번 작업에서 수정 금지).
- 환경: hermes/zcode CLI **미설치**(로컬) → Hermes/ZCode **라이브 E2E/probe 불가** → 매니페스트/문서만, 실측 blocker 기록. node/npm/vpk 사용 가능.

## 목표(지시서 §0)
1. Pi Worker를 별도 Built-in Engine(`pi-worker`)으로 등록(같은 `PiAdapter` 공유, AdapterKind=pi-rpc). 2. harness를 설치본에 번들. 3. EngineDefinition/AdapterKind/RuntimeDefinition 분리. 4. Custom Engine(매니페스트 + GUI) 시스템. 5. OneShotTextAdapter + Bridge JSONL Adapter. 6. Hermes preset. 7. ZCode probe. 8. v1.20.0 backward compat. (Worker queue/WorkerTaskStore/report routing/worktree/세션 구조는 재작성 안 함.)

## Phases (commit 단위, 지시서 §17)
- **A. Bundle Pi Worker + built-in engine** — 진행 예정 먼저.
- B. EngineDefinition/AdapterKind/RuntimeDefinition refactor + AdapterFactory.
- C. Custom engine manifest loader + OneShotTextAdapter.
- D. AgentManager bridge JSONL adapter. ✅ (adapter+spec+smoke 완료 — 라이브 E2E는 실제 CLI 대기)
- E. Custom Engine settings GUI + trust/security.
- F. Hermes preset (E2E blocked — 미설치). G. ZCode probe doc (미설치).
- H. Migration/regression tests + docs + publish verify.

## Completed
- **Phase A — Pi Worker 런타임 번들(commit `d95d1bb`)**: harness 런타임을 `engines/pi-worker/`로 vendor, publish 출력 `runtimes/pi-worker/`로 번들, `EngineRegistry.BundledPiWorker()`/`ResolvePiWorker()` 번들-우선 해석. **글로벌 npm 설치 없이 Pi Worker 동작**(핵심 목표 달성). 실측: build green · bin/single-file publish에 runtime 포함 · entrypoint `--version` ok · 스모크 14 groups green. ADR `docs/PI_WORKER_BUNDLING_KO.md`.
  - 아직 pi-worker는 별도 engine **id**로 분리되지 않음(Worker-role pi가 번들 런타임을 씀). 별도 id 등록은 Phase B 리팩터 위에서 하는 게 깔끔.

## In Progress
- (없음) — Phase B 이후는 대규모. 아래 "Remaining/설계 노트" 참조.

## Remaining — 설계 노트 (다음 에이전트/세션용)
- **Phase B (engine model refactor)**: `EngineDef` → `EngineDefinition`(identity) + `AdapterKind`(protocol) + `EngineRuntimeDefinition` + `Capabilities` + `AllowedRoles` + `Source`. `AdapterFactory.Create(adapterKind, engine)`로 id-switch 축소. 내장 adapterKind: claude-stream-json/codex-json/codex-app-server/agy-pty/pi-rpc. **주의**: `AgentId`(engine id)가 UI·persistence·worker 생성 전반에 박혀 있어 광범위·회귀 위험. 점진적으로.
- **Phase C (custom manifest + one-shot adapter)**: `%LOCALAPPDATA%/AgentManager/engines/*.json`(schemaVersion 1, §5.1). `OneShotTextAdapter`는 stdout **전체**를 최종 AssistantText로, **process-exit**로 완료 — **현 `AgentSession`은 라인별 JSONL 파서라 EOF/exit 완료 훅이 없음**. `IAgentAdapter`에 stream-end/exit 훅 추가 or one-shot 전용 실행 경로 필요(공유 세션 루프 변경 = 회귀 주의). model source: static/free-form/command(단순 1줄=1모델 or JSON array만).
- **Phase D (bridge JSONL)** ✅ 완료: `BridgeJsonlAdapter : StdioJsonAdapter` + `AdapterFactory.CreateCustom`(`agentmanager-bridge-jsonl`, 별칭 `bridge-jsonl`) + `AssertBridgeAdapter` 스모크 + 규격 문서 `docs/BRIDGE_JSONL_PROTOCOL_KO.md`. 버전드 protocol(protocolVersion 1): ARGS 모드({prompt} argv, stdin close) / STDIN 모드(start 줄, KillAfterTurnCompleted). 이벤트 session_started/assistant_delta/assistant_text/thinking/tool_started/tool_result/token_usage/error/turn_completed → NormalizedEvent, malformed/unknown(RawUnknown)/crash(AgentSession exit 합성)/dup-completion 가드. 라이브 E2E는 실제 브리지 CLI 대기(미설치). v1 미지원: Permissions/Images.
- **Phase E (GUI + trust)**: Settings Engines 관리 화면. **XAML 회귀 주의**(중복 x:Key = 시작 크래시, 로컬라이즈 문자열). trust: 첫 실행 전 exe+args 표시·승인, ArgumentList only, shell string 금지, secret 마스킹, manifest 변경 시 trust 무효화.
- **Phase F/G (Hermes/ZCode)**: **CLI 미설치 → 라이브 E2E/probe 불가**. Hermes preset 매니페스트(`hermes -z {prompt}`, capabilities는 one-shot 정직하게) + `HERMES_ENGINE_INTEGRATION_KO`/`ZCODE_CAPABILITY_PROBE_KO`에 blocker + 설치 시 검증 절차 기록. ACP는 후속(공식 계약 확인 후).
- **Phase H**: 마이그레이션(legacy PiWorkerPath → engine override, 최소 1릴리즈 읽기 호환) · 회귀(cc/gx/agy/pi/pi-worker + 14 smoke) · 8개 문서 · publish 검증 · v1.21.0 후보(push/release 사용자 승인 후).

## Decisions (ADR 요약)
- pi-worker 번들: harness의 `dist/ + worker-package/ + package.json`만 vendor(런타임 deps 0 → node_modules/src/tests 불필요). 위치 `engines/pi-worker/`(source commit + MIT 기록). publish 시 `runtimes/pi-worker/`로 복사. → 상세 ADR: `docs/PI_WORKER_BUNDLING_KO.md`.
- 런타임 탐지 우선순위: (1) 사용자 override → (2) 번들 `runtimes/pi-worker/dist/cli/index.js` → (3) legacy `PiWorkerPath` → (4) 글로벌 npm(최하위 legacy) → (5) EngineUnavailable.

## Next Action
- harness `dist/`+`worker-package/`+`package.json`를 `engines/pi-worker/`로 복사 + NOTICE 기록.

## Do Not
- 개발 PC 절대경로(H:\...) 런타임 사용 금지. harness 코드 수정 금지. 프로젝트 파일에서 engine manifest 자동 실행 금지. shell string 실행 금지(ArgumentList). 미확인 CLI 기능을 지원한다고 문서화 금지. push/merge/release 사용자 승인 없이 금지.

## Known Blockers
- hermes/zcode 미설치 → Hermes/ZCode 라이브 E2E/probe 불가(문서로 인계).
