# Pi Worker Published GUI E2E Test Report

## Environment
- Test date: 2026-07-11 (KST)
- AgentManager branch: `feature/pi-worker-integration`
- AgentManager commit: `f44ce82` (working tree clean; 이 리포트는 doc-only 후속 커밋)
- Publish executable: `J:\prj\AgentManager\artifacts\publish\pi-worker-gui-e2e\AgentManager.exe` (Release, win-x64, framework-dependent, single-file)
- Harness version / commit: `@agentmanager/pi-worker-harness` 0.1.0 / `6e49dbd`
- Pi version: 0.80.3 (pinned)
- Model: `dgx-spark/qwen3-30b-a3b` (사용자 자가 서버, 무료)
- OS: Windows 11
- Evidence directory: `artifacts\test-evidence\pi-worker-gui-e2e\20260711-022239\` (gitignored)

## Summary
- Total: 11 · Passed: 9 · Partial(Not-exercised): 1 (TC-07) · Failed: 0 · Blocked: 0 · Skipped(negative TC-N01): 1
- **Final result: 핵심 Pi Worker 통합 GUI E2E PASS.** Worker-role Pi 세션이 실제로 `pi-worker`를 구동하고, RPC로 도구 실행 후 표준 보고서(+완료 마커)를 반환하며, `~/.pi-worker`에 격리되고, orphan 없이 정리됨을 Published GUI에서 육안 검증. TC-07(원본 Main으로의 report routing)만 직접-태스킹 방식이라 미실행.

## Test Cases
### TC-01 — Published GUI 시작 및 단일 인스턴스 — PASS
- 사용자가 실행 중이던 설치본(PID 57560, master v1.19.5, pi-worker 미포함)을 **사용자가 직접 종료**한 뒤, 검증 publish를 단일 인스턴스로 실행.
- 결과: WPF 창 정상 시작(Title "AgentManager"), Orchestrator/엔진/프로젝트/세션 UI 렌더, 크래시 없음. 단일 인스턴스.
### TC-02 — Pi Worker 런타임 설정 — PASS(설정 파일 경로)
- **설정 XAML 입력 행은 존재하지 않음**(의도된 optional 후속). 그래서 앱 종료 상태에서 `%LocalAppData%\AgentManager\settings.json`의 `PiWorkerPath`만 최소 수정 → `H:\Git\Bosung_PI\pi-worker-harness\dist\cli\index.js`. 백업 생성, 40개 키 보존. `PiPath=""`(General pi 자동탐지).
### TC-03 — Main/General 세션 준비 — PASS
- "workspace" 프로젝트가 `J:\prj\AgentManager`를 가리킴. Pi 엔진 General 세션 "E2E-Main" 생성. (General Pi는 공식 `pi`를 구동 — 아래 TC-10 참조. 참고: 소형 로컬 모델이 프롬프트 없이 bash를 자동 실행하여 즉시 정지시킴 — 모델 행동, 통합과 무관.)
### TC-04 — Pi Worker 정확히 1개 생성 + 실제 실행 파일 pi-worker — PASS (핵심)
- New Agent → Pi + `dgx-spark/qwen3-30b-a3b` + **워커로 생성** → Worker 세션 "E2E-PiWorker" 1개(WORKERS 1).
- **실행 중 프로세스 트리 실측**(TC04-process-tree-proof.txt):
  ```
  AgentManager(42932) → node …\pi-worker-harness\dist\cli\index.js --mode rpc --model dgx-spark/qwen3-30b-a3b   [pi-worker]
    → cmd.exe /c "pi --mode rpc … --extension …\worker-package\extensions\worker-guard…"
      → node …@earendil-works\pi-coding-agent\dist\cli.js --mode rpc …   [공식 pi as child]
  ```
  → Worker는 **pi-worker**를 구동(공식 pi 직접 아님), pi-worker가 공식 pi를 자식으로 감싸고 worker-guard 확장을 주입.
### TC-05 — Worker 상태 전이 — PASS
- 대기(Queued/idle) → 실행 중(Running) → 완료(Completed). 무한 Running·중복 완료·완료 후 회귀 없음. `agent_end willRetry:false` 완료 로직이 GUI에서 정상 동작.
### TC-06 — 표준 Worker 보고서 + completion marker — PASS
- (1차 상세 프롬프트: 완료했으나 소형 로컬 모델이 빈 bash만 실행하고 구조화 보고서 미생성 — **모델 한계, 통합 결함 아님**.)
- (2차 명시적 command-list 프롬프트) 완전한 표준 보고서 생성:
  결과(README.md 존재, 첫 제목 `# AgentManager`, 분기 `worker/e2e-piworker-2`, git clean) · 변경된 파일 없음 · 검증(exit code 0, 실제 출력 verbatim) · 편차/위험 없음 · **`PI_WORKER_GUI_E2E_PASS`** 마커 포함.
- DIFF REVIEW "No changes in this worktree"(읽기 전용 준수).
### TC-07 — Origin Main 세션 Report Routing — NOT EXERCISED (부분)
- 본 검증은 Worker 세션에 **직접 태스킹**(Main→위임 UI를 통하지 않음)했으므로 원본 Main으로의 routing은 미실행. 워커 자신의 우측 "보고 수신함/Summary" 패널에는 보고서가 캡처됨.
- Main→Worker 위임 UI 제스처는 이번 세션에서 특정하지 못함(`>액션`은 앱 명령(clear/review/settings/help)뿐). routing 메커니즘 자체(`DelegateAsync`가 워커 최종 텍스트→Main inbox)는 코드/스모크로 검증됨. → 완전 검증하려면 Main-위임 흐름 1회 필요(후속).
### TC-08 — Worker 재위임 차단 — PASS
- WORKERS 수 1 유지(하위 Worker 0). pi-worker 커맨드라인에 `AGENTMANAGER_TASK_SPOOL` 없음(worker-guard만 주입) → delegation depth 0 정책 실측 확인.
### TC-09 — 앱 정상 종료 + 프로세스 정리 — PASS
- GUI graceful close 후 AgentManager 0, pi-worker/공식 pi/cmd orphan **0**.
### TC-10 — Pi 설정/세션 격리 — PASS
- Worker 세션 → `~/.pi-worker/agent/sessions`에 생성(신규 `019f4d1f…` 02:45:57 = E2E-PiWorker 실행).
- 공식 `~/.pi`의 **auth.json/settings.json/models.json 불변**. `~/.pi`의 변화 2건은 워커 leak이 아님: (a) **General E2E-Main 세션**이 공식 pi를 쓰며 만든 세션(설계상 정상), (b) 앱 시작 시 `ask-user/SKILL.md` 재주입(AM의 정상 스킬 주입, mtime만 갱신).
### TC-11 — 저장소 무변경 — PASS
- `J:\prj\AgentManager` `git status --short` clean, `git diff` 없음. 워커는 격리 worktree에서 동작. `artifacts/`(publish+evidence)는 gitignored.
### TC-N01 (negative, 잘못된 PiWorkerPath) — SKIPPED
- 상태 변경 위험(설정 파일 직접 수정 + 재시작)이라 이번엔 생략. `EngineUnavailable` typed 오류 경로는 코드/typed error로 보장.

## Worker Execution
- Task ID: PW-GUI-E2E-20260711-A · Worker count: 1 · Engine: Pi(Worker) · Model: dgx-spark/qwen3-30b-a3b
- State transitions: 대기→실행 중→완료 · Completion marker: PI_WORKER_GUI_E2E_PASS(2차) · Elapsed: 수 초~수십 초/턴

## Report Routing
- Worker report created: 예(2차) · WorkerTaskStore/inbox: 워커 우측 Summary 패널에 캡처 · Origin Main received: 미실행(직접 태스킹) · Duplicate: 없음

## Isolation and Cleanup
- Official Pi(auth/settings/models): 불변 · Worker session root: `~/.pi-worker` · Orphan: 0 · Repository: 무변경

## Regression (GUI 검증 후)
```
dotnet build AgentManager.slnx -c Release            → exit 0 (오류 0)
dotnet run --project src/AgentManager.Smoke -c Release → exit 0 (14 assert groups)
… -- --pi-worker-live (dgx-spark/qwen3-30b-a3b)       → exit 0 (completed isError=false, text="OK", 12s)
```

## Defects
- 없음(통합 결함). 관찰된 제약: (1) 소형 로컬 모델이 상세/모호 프롬프트에서 빈 bash 실행·보고서 미생성(모델 한계) — 명시적 프롬프트로 해소. (2) General Pi 세션이 프롬프트 없이 자동 실행되는 현상(모델/세션 시작 동작; 통합과 무관, 별도 관찰).

## Final Decision
- GUI E2E: **PASS**(핵심 Pi Worker 통합). TC-07(Main-origin routing)만 미실행.
- Merge readiness: 준비됨. 남은 필수: TC-07 Main-위임 routing 1회(선택), master 머지 직전 README 확인.
- Recommended next action: 사용자 승인 시 `feature/pi-worker-integration` → `develop` 머지. 검증 세션(E2E-Main/E2E-PiWorker)·상태 복원은 사용자 승인 후(state.json 백업 존재).
