# QA 시트 — 전체 앱 (헤드리스 / 릴리즈 검수)

> 대상: `feature/cc-capabilities` (릴리즈 후보). 기준선: v1.18.1.
> 범위: **앱 전 표면 중 헤드리스로 검증 가능한 것** — 빌드·스모크·i18n·엔진 어댑터·Core 오케스트레이션·영속성·번역·관측·worktree·승인·스케줄·CLI·보안.
> **제외(별도 비주얼 QA):** 렌더링/피커/테마/리뷰 UI/드래그앤드롭/줌/알림/ask-user 패널 등 GUI 육안 항목.
> 성격: **검증 전용.** 코드/설정 수정·커밋 금지. 결함은 `파일:라인` + 증상 + 재현으로 기록.
> 각 항목: `PASS / FAIL / N/A(사유)` + **근거**.

프로젝트: `AgentManager.Core` · `AgentManager`(WPF) · `AgentManager.Cli`(am) · `AgentManager.Smoke`.
> 데몬 계열(`AgentManager.Protocol`/`Daemon`/`Client`)은 파킹된 `feature/headless-core`에만 존재하고 **이 브랜치·기준선(v1.18.1)엔 없음** — 섹션 11a/11d/12b는 파일이 있으면 검증, 없으면 N/A(회귀 아님).
> cc 역량(Fable/opusplan/ultracode)은 `docs/QA_cc_capabilities.md`에서 이미 심층 QA됨 — 여기선 빌드+스모크 green만 재확인하고, **나머지 표면에 폭을 집중**한다.

---

## 0. 전제

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| P1 | 브랜치 | `git rev-parse --abbrev-ref HEAD` | `feature/cc-capabilities` | |
| P2 | 트리 | `git status --porcelain` | 추적 소스에 예기치 않은 수정 없음(README/docs 무관) | |
| P3 | 엔진 가용성 메모 | `claude/codex/agy/pi` PATH 확인 | 미설치 엔진의 **라이브** 항목은 N/A 처리(코드로 대체) | |

---

## 1. 빌드 (전 프로젝트, 토큰 0)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 1a | 솔루션 전체 | `dotnet build AgentManager.slnx -c Release` | **에러 0** — 요약의 "n Error(s)" 신뢰(`error CS`만 grep 금지: MSB4025 등 놓침) | |
| 1b | 경고 스캔 | 위 출력의 Warning 수 | 신규 브랜치發 경고 급증 없음(기존 수준) | |

---

## 2. 스모크 (자동 검증 백본, 토큰 0)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 2a | 전체 스모크 | `dotnet run --project src/AgentManager.Smoke` | 마지막 줄 `smoke OK`, 중간에 아래 전 항목 OK 라인 | |

2a가 커버하는 것(각 "… OK" 라인 존재 확인):
- `sandbox/model/mcp/add-dir arg asserts OK` (엔진별 인자 매트릭스)
- `permission response asserts OK` · `codex app-server adapter asserts OK` · `antigravity sdk adapter asserts OK`
- `quick-reply parser asserts OK` · `markdown fence-split asserts OK`
- `GitWorktree end-to-end OK (create/changes/diff/discard/commit-only/merge/branch-switch/cleanup)`
- `ProjectStore debounce/flush asserts OK` · `TranscriptProjector golden asserts OK` · `RunRegistry asserts OK` · `ApprovalBroker asserts OK`
- `ultracode workflow lifecycle asserts OK`
- Claude/Codex/Pi stream-json 파싱 데모 섹션(예외 없이 통과)

> 하나라도 누락/실패면 FAIL + 해당 라인 인용.

---

## 3. i18n (전체 문자열, 토큰 0)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 3a | 키 패리티 | `Strings.Ko.xaml`/`Strings.En.xaml`의 `x:Key` 목록 정렬 비교 | ko = en (개수·키 완전 일치) | |
| 3b | 중복 키 | 각 파일 `x:Key` `sort | uniq -d` | **0** (중복 x:Key는 시작 시 크래시) | |
| 3c | 하드코딩 문자열 스캔(샘플) | 뷰/뷰모델에서 사용자 노출 리터럴 grep(샘플) | L.* 리소스 사용, 노출 하드코딩 없음(중대한 것) | |

---

## 4. 엔진 어댑터 (코드 + 라이브·선택)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 4a | cc 인자/파싱 | 2a의 Claude 항목 + `ClaudeAdapter.cs` 리딩 | stream-json 입출력, permission-mode/model/effort/resume/mcp/add-dir 매핑 정상 | |
| 4b | gx 인자/파싱 | 2a Codex 항목 + `CodexAdapter`/`CodexAppServerAdapter` | exec --json + app-server 승인 경로 | |
| 4c | agy | 2a Antigravity + `AgyAdapter`/ConPTY | ConPTY 텍스트 경로, resume | |
| 4d | pi | 2a Pi + `PiAdapter` | RPC(JSONL), `pi --list-models` 파싱(`ClaudeAgentsProbe`와 별개) | |
| 4e | 라이브(선택·최소) | 설치·인증된 엔진 1개로 `am <engine> "echo 1"` 1회 | 1턴 정상 `[done]` | |

---

## 5. Core 오케스트레이션 서비스 (WPF-프리)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 5a | TurnPlanner | 2a arg 매트릭스 + 코드 | ResolveEngine/BuildOptions 정상 | |
| 5b | TranscriptProjector | 2a golden | 이벤트→중립 델타 리듀서 정합 | |
| 5c | RunRegistry | 2a | 취소 토큰/수명주기 | |
| 5d | ApprovalBroker | 2a + permission response | Stage1/Stage2 왕복 | |
| 5e | UsageService/SettingsService/ProjectStore | 코드 리딩 + 2a ProjectStore | 저장 코디네이션·설정 로드 | |

---

## 6. 영속성

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 6a | JSON 라운드트립 | 2a ProjectStore + `Persistence/*Store.cs` 리딩 | 원자적 쓰기(temp→rename)·재시도·코얼레싱·세션 격리 | |
| 6b | 프로젝트-로컬 상태 | `<project>/.am/project.json` 설계 확인 | 프로젝트 따라감, 전역 state.json은 목록/설정만 | |
| 6c | 크기 캡 읽기(보안) | `JsonFile.ReadCapped`(16MB) 등 | 대용량 파일 DoS 가드 존재 | |

---

## 7. 번역 레이어

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 7a | TranslatorBase 전략 | `Translation/TranslatorBase.cs` | skip·마스킹(코드/@file)·프롬프트 프레이밍 공유 | |
| 7b | 3 provider | Ollama/Agent/OpenAiCompat 어댑터 + `TranslatorFactory` | Create/Available/IsAvailableAsync 정상 | |
| 7c | 엔드포인트 정책 | `TranslationEndpointPolicy` | IsLoopback/IsRemote 경고(**차단 아님** — LAN 평문 허용) | |
| 7d | 키 저장 | 커스텀 provider 키 DPAPI 암호화(평문 금지) | 코드상 DPAPI 경로 | |
| 7e | 라이브(선택) | Ollama 켜져 있으면 KO→EN 1회 | 번역 반환·마스킹 유지 | |

---

## 8. 관측 (Native work)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 8a | 훅 이벤트 매핑 | `NativeHookEvent.ToObservedWorkItem` | SubagentStart/Stop→NativeSubagent(High) | |
| 8b | 훅 스풀 관측기 | `HookSpoolNativeWorkObserver` | 스풀 파일→항목, 크기 캡 가드 | |
| 8c | agents 폴러 | `ClaudeAgentsProbe.Parse` 픽스처 | `claude agents --json` 파싱 정상 | |
| 8d | 실패 추론 | `SubagentTranscriptInspector` | rate-limit/에러 서브에이전트→Failed 보정 | |

---

## 9. GitWorktree / 승인 / 스케줄

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 9a | worktree e2e | 2a GitWorktree 라인 | create~cleanup 전 단계 OK | |
| 9b | 승인 Stage1(cc) | 2a permission response + (선택)`--live-approval` | stdio control_response 왕복 | |
| 9c | 승인 Stage2(gx) | 2a app-server + (선택)`--appserver-probe`/`--live-stage2` | requestApproval→accept | |
| 9d | 스케줄 | (선택) `dotnet run --project src/AgentManager.Smoke -- --sched-check` 존재 시 | Trigger 파싱·Store·TimerScheduler | |

> 9b~9d의 라이브 플래그는 인증/토큰 필요 — 없으면 N/A(코드+오프라인 assert로 대체).

---

## 10. CLI (`am`)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 10a | 인자 파싱 | `AgentManager.Cli/Program.cs` | `--cwd/--model/--effort/--approve/-` 정상 | |
| 10b | 미설치 엔진 처리 | `am <미설치>` | 명확한 에러 메시지, 크래시 없음 | |
| 10c | 라이브(선택) | 4e와 공유 | 1턴 정상 | |

---

## 11. 보안 자세 (스팟)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| 11a | 데몬 파이프 ACL | `DaemonServer.cs` | `PipeOptions.CurrentUserOnly` | |
| 11b | 명령 주입 하드닝 | `AgyAdapter`/NativeHook 인용, `Win32Args.Quote`/`SafeBranchName` | 인자 인용/새니타이즈 | |
| 11c | 크기 캡 읽기 | 6c와 공유 | 스풀/state 16MB 캡 | |
| 11d | 샌드박스 fail-safe | `SessionHost.cs`(데몬) | 실패 시 WorkspaceWrite로 안전측 | |

---

## 12. 회귀 (기준선 v1.18.1)

| # | 항목 | 기대 | 결과 |
|---|------|------|------|
| 12a | `git diff --stat 06234dd...HEAD` | 변경이 cc/CLI/Smoke/i18n/문서에 국한, 무관 영역 미변경 | |
| 12b | 데몬(파킹) 빌드 | Protocol/Daemon/Client 여전히 빌드됨(1a에 포함) | |
| 12c | 전 엔진 파싱 데모 | 2a에서 cc/gx/pi 파싱 예외 없음 | |

---

## 13. 종합 판정

| 영역 | PASS/FAIL | 비고 |
|------|-----------|------|
| 1 빌드 | | |
| 2 스모크 | | |
| 3 i18n | | |
| 4 어댑터 | | |
| 5 Core 서비스 | | |
| 6 영속성 | | |
| 7 번역 | | |
| 8 관측 | | |
| 9 worktree/승인/스케줄 | | |
| 10 CLI | | |
| 11 보안 | | |
| 12 회귀 | | |

**최종:** ☐ 릴리즈 가능 ☐ 조건부(결함 N건) ☐ 반려
**발견 결함:** `파일:라인` — 증상 — 재현 (없으면 "없음")
**비주얼 QA 필요 항목(참고):** GUI 육안이 남은 표면 나열(다음 단계로 이관)
