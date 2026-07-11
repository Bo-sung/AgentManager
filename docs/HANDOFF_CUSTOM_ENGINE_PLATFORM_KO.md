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
- D. AgentManager bridge JSONL adapter.
- E. Custom Engine settings GUI + trust/security.
- F. Hermes preset (E2E blocked — 미설치). G. ZCode probe doc (미설치).
- H. Migration/regression tests + docs + publish verify.

## Completed
- (없음 — 착수)

## In Progress
- Phase A.

## Decisions (ADR 요약)
- pi-worker 번들: harness의 `dist/ + worker-package/ + package.json`만 vendor(런타임 deps 0 → node_modules/src/tests 불필요). 위치 `engines/pi-worker/`(source commit + MIT 기록). publish 시 `runtimes/pi-worker/`로 복사. → 상세 ADR: `docs/PI_WORKER_BUNDLING_KO.md`.
- 런타임 탐지 우선순위: (1) 사용자 override → (2) 번들 `runtimes/pi-worker/dist/cli/index.js` → (3) legacy `PiWorkerPath` → (4) 글로벌 npm(최하위 legacy) → (5) EngineUnavailable.

## Next Action
- harness `dist/`+`worker-package/`+`package.json`를 `engines/pi-worker/`로 복사 + NOTICE 기록.

## Do Not
- 개발 PC 절대경로(H:\...) 런타임 사용 금지. harness 코드 수정 금지. 프로젝트 파일에서 engine manifest 자동 실행 금지. shell string 실행 금지(ArgumentList). 미확인 CLI 기능을 지원한다고 문서화 금지. push/merge/release 사용자 승인 없이 금지.

## Known Blockers
- hermes/zcode 미설치 → Hermes/ZCode 라이브 E2E/probe 불가(문서로 인계).
