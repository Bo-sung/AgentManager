# QA 시트 — `feature/cc-capabilities`

> 대상 브랜치: `feature/cc-capabilities` (기준: `develop`/`master` = v1.18.1)
> 범위: cc(Claude Code) 네이티브 역량 노출 — **Fable/best/opusplan 모델**, **ultracode effort**, **ultracode 워크플로우 2턴 생명주기 수정**, `am` CLI `--effort`.
> 성격: **검증 전용(verify-only).** 코드 수정 금지 — 결함은 기록만.
> 각 항목: `PASS / FAIL / N/A` + **근거**(명령 출력·파일:라인·화면 관찰) 기입.

브랜치 커밋:
- `ccb7ac9` feat(cc): Fable/opusplan 모델 + ultracode effort
- `d570a35` test(cc): smoke 커버리지 (ultracode→--settings, auto-omit, fable)
- `352538c` fix(cc): ultracode 워크플로우 = 1턴 완료

변경 파일: `EngineRegistry.cs` · `SessionViewModel.cs` · `AppViewModel.cs` · `Strings.Ko/En.xaml` · `SessionView.xaml` · `ClaudeAdapter.cs` · `AgentManager.Cli/Program.cs` · `AgentManager.Smoke/Program.cs`

---

## 0. 전제 (Preconditions)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| P1 | 올바른 브랜치 | `git rev-parse --abbrev-ref HEAD` | `feature/cc-capabilities` | |
| P2 | HEAD 확인 | `git log --oneline -3` | `352538c` … `ccb7ac9` 포함 | |
| P3 | 워킹트리 인지 | `git status --porcelain` | (README/docs/screenshots 외에) 예기치 않은 수정 없음 | |

---

## A. 정적/빌드 (토큰 0)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| A1 | Core 빌드 | `dotnet build src/AgentManager.Core/AgentManager.Core.csproj` | **에러 0** (총 error 수로 확인, 필터 grep 금지) | |
| A2 | WPF 빌드 | `dotnet build src/AgentManager/AgentManager.csproj` | **에러 0** (MSB 포함 전체 카운트) | |
| A3 | CLI 빌드 | `dotnet build src/AgentManager.Cli/AgentManager.Cli.csproj` | 에러 0 | |
| A4 | Smoke 실행 | `dotnet run --project src/AgentManager.Smoke` | 마지막 줄 `smoke OK`, `ultracode workflow lifecycle asserts OK` 포함 | |
| A5 | i18n 키 패리티 | `Strings.Ko.xaml`/`Strings.En.xaml`의 `x:Key` 정렬 비교 | ko=en 동일, **중복 키 0** (`uniq -d`) | |
| A6 | 신규 i18n 키 | `L.EffortUltracodeNote` grep | ko/en 각 1회, 값 존재 | |

---

## B. 모델 피커 (fable/best/opusplan)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| B1 | cc 모델 목록 | `EngineRegistry.cs`의 cc `Models` 배열 | `sonnet, opus, opusplan, haiku, fable, best, sonnet[1m], opus[1m]` 포함 | |
| B2 | 드롭다운 반영 | `AppViewModel.Settings.cs` `DropdownModelsFor("cc")` 로직 | 별칭 목록 그대로 노출(+ 커스텀 append) | |
| B3 | 커스텀 모델 추가 | Settings ▸ "주로 쓰는 모델" ▸ 커스텀 추가 (`ModelChecklist.AddCustom`, `ModelChecklist.cs:66`) | 풀네임(`claude-opus-4-8`) 추가 시 드롭다운에 노출. (New Agent 모델 콤보는 `IsEditable` 아님 → 인라인 타이핑 불가, 설정 경로를 거침) | |
| B4 | (GUI) 육안 | New Agent ▸ cc ▸ 모델 드롭다운 | fable·best·opusplan 노출 | |

---

## C. Effort 모델 게이팅 (ultracode)

핵심 함수: `SessionViewModel.CcSupportsUltracode(model)` / `CcEffortOptions(model)`.

| # | 모델 | 기대 effort 목록 | 결과 |
|---|------|------------------|------|
| C1 | `opus` / `sonnet` / `fable` / `best` / `opusplan` | `default,low,medium,high,xhigh,max,` **`ultracode`** | |
| C2 | `haiku` (또는 이름에 haiku) | ultracode **없음** (`default..max`) | |
| C3 | `opus-4-6` / `sonnet-4-6` 포함 풀네임 | ultracode **없음** | |
| C4 | 빈값/`default` 별칭 | ultracode 포함(true) | |
| C5 | New Agent 피커도 동일 | `AppViewModel.NewAgentEffortOptions` = `CcEffortOptions(NewAgentModel)` | 세션 피커와 동일 게이팅 | |
| C6 | 모델 바꾸면 목록 갱신 | `Model`/`NewAgentModel` 세터가 effort 목록 재발행 | haiku↔opus 전환 시 ultracode 즉시 나타남/사라짐 | |

> 검증 방법(택1): (a) 코드 리딩으로 로직 확인, (b) 작은 콘솔 하네스로 `CcEffortOptions("opus")` 등 호출, (c) GUI 육안.

---

## D. ultracode 전달 (ClaudeAdapter)

핵심: `ClaudeAdapter.BuildStartInfo` / `AddSettings`.

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| D1 | ultracode → --settings | Smoke `AssertSandboxAndModelArgs` 또는 어댑터 직접 호출: `ReasoningEffort="ultracode"` | 인자에 `--settings` + JSON에 `"ultracode":true`, **`--effort` 없음** | |
| D2 | auto → 플래그 생략 | `ReasoningEffort="auto"` | `--effort` **없음** | |
| D3 | default → 플래그 생략 | `ReasoningEffort="default"` | `--effort` 없음 | |
| D4 | 일반 값 전달 | `ReasoningEffort="max"` | `--effort max` 존재 | |
| D5 | hooks + ultracode 병합 | NativeHookCommand 설정 + ultracode | `--settings` JSON에 `ultracode` **와** `hooks` 둘 다 | |
| D6 | (라이브·선택) CLI 실측 | `claude --settings '{"ultracode":true}' --model opus -p "reply OK"` | exit 0, "unknown setting" 경고 없음 | |

---

## E. 워크플로우 2턴 생명주기 (핵심 수정)

ultracode는 **런치 턴(`system/task_started` + 중간 result "launched") → 리포트 턴(2번째 `system/init` + 진짜 답 + 최종 result)** 로 옴. 어댑터가 **1턴**으로 접어야 함.

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| E1 | 골든 파싱 | Smoke `AssertUltracodeWorkflowLifecycle` | `SessionStarted 1` + `TurnCompleted 1`, AssistantText 2(launch+final) | |
| E2 | 중간 result 억제 | `ClaudeAdapter` `_workflowActive` 로직 리딩 | task_started 후 첫 result는 TurnCompleted 미발생 | |
| E3 | 중복 init 억제 | `_sawInit` 로직 | 2번째 system/init은 SessionStarted 재발행 안 함 | |
| E4 | (라이브·최대 1회) 실 어댑터 | `am cc --model opus --effort ultracode "ultracode: 두 에이전트 alpha/beta 반환 후 'alpha beta'로 합쳐. 최소, 툴/파일 없이."` | **`[done]` 1회**, 최종 답 `alpha beta` 도착, 행(hang) 없음(수십 초 내) | |
| E5 | 일반 턴 무영향 | `am cc --model sonnet "echo 1"` 또는 Smoke 골든 | 정상 1턴 완료, 회귀 없음 | |

> ⚠️ E4는 **opus+ultracode 실 워크플로우 = 토큰 소모**. **최대 1회**만. 작업은 반드시 tiny(no tools/files).

---

## F. 네이티브 작업자 관측

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| F1 | 훅 관측 경로 | `NativeHookEvent.ToObservedWorkItem` / `HookSpoolNativeWorkObserver` 리딩 | SubagentStart/Stop → `NativeSubagent` 항목 생성 | |
| F2 | (GUI·선택) 워크플로우 서브에이전트 | opus+ultracode 세션 후 우측 "네이티브 작업자" 탭 | `workflow-subagent`(Hook/High/Completed) 항목 노출 | |

---

## G. 회귀 (Regression)

| # | 항목 | 기대 | 결과 |
|---|------|------|------|
| G1 | gx/agy/pi effort 목록 | 변경 없음(cc만 model-aware) | |
| G2 | cc 일반 effort(low~max) | `--effort <v>` 그대로 전달 | |
| G3 | translation 경로 | ultracode 응답도 번역 레이어 통과(코드상 ParseRoot→번역 순서 불변) | |
| G4 | `am` CLI 기존 인자 | `--cwd/--model/--approve/-` 정상 | |

---

## H. 종합 판정

| 영역 | PASS/FAIL | 비고 |
|------|-----------|------|
| A 빌드/스모크 | | |
| B 모델 | | |
| C effort 게이팅 | | |
| D ultracode 전달 | | |
| E 워크플로우 생명주기 | | |
| F 네이티브 관측 | | |
| G 회귀 | | |

**최종:** ☐ 머지 가능  ☐ 조건부(결함 N건)  ☐ 반려

**발견 결함 (있으면):** `파일:라인` — 증상 — 재현
