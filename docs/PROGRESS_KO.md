# AgentManager — 진행 상태 (living tracker)

> 단일 진실 소스. 한 기능 끝낼 때마다 갱신. 상세 계획은 FEATURES_KO.md, 매핑은 PHASE0_*.

## ✅ 완료
| 항목 | 커밋 |
|---|---|
| 클린룸 스펙 + WPF 스캐폴드 | 1183276 |
| Phase 0: Claude stream-json 매핑(실측) | 075c124 |
| Phase 0: Codex exec --json 매핑(실측) | 0589a35 |
| Core 골격: 정규화이벤트 + Claude/Codex 어댑터 + 번역기 + 세션 + 오프라인 스모크 | 729537b |
| WPF UI: 오케스트레이터 디자인 포팅 + Core 연동(실엔진 실행) | 2ff9b50 |
| 한글 mojibake 수정(stdio UTF-8) | d325938 |
| 확정 기능셋 문서화(리서치 반영) | cf4a2d9 |
| **M1-① Stop(중지)** | ae26f06 |
| **P0구조-① Worktree 격리** (Core GitWorktree + 세션별 worktree cwd) | 2314af2 |
| **P0구조-② 우측 Review pane** (변경파일 목록 + diff 뷰) | f82a194 |
| **P0구조-③ Project 개념** (프로젝트 등록/선택 + 세션 소속 + project cwd) | f82a194 |
| **P0구조-④ JSON 영속성** (project/session/transcript 저장·복원) | f82a194 |
| **P0구조-⑤ 3-pane 보강** (사이드바 Active/Project 그룹화 + 접이식 Review pane 토글 + 활성 하이라이트) | a20fc6a |
| **P0구조-⑥ Review actions** (Merge ▸ main = 커밋+머지 / Discard = reset+clean) | e1d95c4 |
| **M1-⑦ 멀티턴(resume)** (SessionStarted id 저장 + 다음 턴 Claude/Codex resume 인자) | 76ab03e |
| **M2-① 번역 토글/원본보기** (세션별 TR ON/OFF + 번역 응답 ORIGINAL 표시) | 76ab03e |
| **M3-① 마크다운 렌더링** (assistant 응답 heading/list/code fence/inline code/bold 표시) | 76ab03e |
| **M1-⑧ 실행 상태 가시화** (RUNNING 바 + 경과 시간 + 마지막 출력 신호 + 무응답 경고) | 76ab03e |
| **M3-② 설정 패널** (provider 경로 + Ollama endpoint/model + 새 세션 번역 기본값 저장) | 76ab03e |
| **M1-⑨ 비용/토큰 정산 + 모델 연결 (로직)** (TurnCompleted.Usage 보정, CostUsd 누적·영속, Total 집계 속성, SessionOptions.Model) | e48fab7 |
| **A-① 세션 수명주기 (로직)** (Delete=중지+worktree제거, Archive 토글+ArchivedSessions, Rename, 영속성) | 9b77dc2 |

| **검증 패스** (Smoke: sandbox/model 인자 매트릭스 + GitWorktree e2e · 실 2턴 resume="47" 성공, session_id 유지) | ec1aab6 |
| **승인 broker Stage 1 (Claude, 로직)** — PermissionHandler 왕복, control_response, ApprovalBlock, RequireApproval(기본 off). 실 왕복 검증(Smoke --live-approval) | 253060e |
| **Artifacts 라이트 (로직)** — TodoWrite→태스크리스트, 테스트 러너 감지→pass/fail, 턴 종료→Summary. 세션별 영속 | 37154aa |
| **MCP 패스스루 (로직)** — Project.McpConfigPath(영속) → claude `--mcp-config`(파일 존재 시만) | 37154aa |
| **승인 UI (Approve/Deny 블록)** + UI 배치 위임 프롬프트 | fa54913 |
| **UI 일괄 패스 (Codex 세션 수행, 검증 완료)** — 집계 표시, 컨텍스트 메뉴, 아카이브 그룹, 승인/샌드박스 토글, Commit/피드백, cap 설정, Artifacts 패널, MCP 경로 필드 | ee8e271~dfc2f34 |
| **UI 폴리시 패스 (Gemini)** — 단축키/복사/창상태/내보내기/빈상태·툴팁 | 5483ae4~0b446e8 |

> 기능(Core/VM) 우선 + View 일괄 + 멀티세션 위임 방식으로 **로직·UI 패스 모두 종료**.

| **IDE 핸드오프 + 이미지 첨부 + UI 수정(스크롤바/콤보/컴포저 모델선택)** | 08aecaf~244d2d0 |
| **알림(작업표시줄 깜빡임+승인 사운드) + Thinking 블록 + Provider 탐지 표시** | f726b26 |
| **Diff 색상 + 마크다운 링크/테이블 + Release 패키징 (Codex)** | 90e8040, 0edbf6f, c79bc7b |

| **README 재작성 (Gemini, 검증·보정 완료)** | c4995af, 2d88507 |

| **실행 중 Review 라이브 갱신** (ToolResult마다 0.8s 디바운스 새로고침, 선택 파일 유지) + **Smoke --e2e** (헤드리스 풀 파이프라인: 한글→번역→Claude→worktree 파일→diff→merge, PASS) | de295df |
| **라이브 갱신 보강** (실행 중 주기 갱신 + quiet 모드로 깜빡임 제거) · **Review pane 기본 열림+영속** | 40341f7, f405039 |
| **원본 디자인 리소스 추출** (아이콘 34종 Geometry + IconView 컨트롤 + AddBg/DelBg/Run/radii 토큰, 글리프 대체 일괄 적용) | b0487bf |
| **트랜스크립트 휠 스크롤 수정 + 본문 선택/복사 + 탭바 정렬** (내부 스크롤러 휠 중계, 맨아래 근처일 때만 자동 추적, SelectableText) | bf707ca |
| **프로젝트 폴더 생성** (Browse 다이얼로그 + 미존재 경로 자동 생성) | d4c5df0 |
| **사이드바 PROJECTS 목록** (전 프로젝트 표시/전환 — 기존엔 활성 프로젝트만 보여 신규 추가 시 이전 프로젝트 실종) | 4da7dca |
| **CLI HISTORY** (외부 claude/codex 세션 발견→가져오기→resume + 과거 대화 트랜스크립트 복원, Smoke --cli-history 실측 검증) | 75db923, b154081 |
| **CLI HISTORY 가져오기 성능 개선** (대형 트랜스크립트 청크 단위 비동기 삽입 + UI 가상화로 프리징 해결) | 08d7d99 |
| **Task A: 프로젝트 우클릭 메뉴** (이름 변경 및 프로젝트 제거, 관련 세션 동시 취소/제거) | 2daaa88 |
| **Task B: 세션 검색 및 필터링** (사이드바 검색 박스, Title/Branch/Project 대소문자 무구분 필터) | 5081146 |
| **Task C: CLI HISTORY 재스캔** (사이드바 헤더에 새로고침 아이콘 배치, 클릭 시 동적 재탐색) | 1a83a9f |
| **리뷰 보정** (재스캔 헤더 상시 표시 — 빈 목록일 때 버튼 실종 모순 해소) | 87090cc, 23f8d5b |
| **멀티폴더 project** — SessionOptions.AdditionalDirectories → claude `--add-dir` / codex writable_roots, Settings EXTRA FOLDERS UI, Smoke 어서션 PASS | 44dd3a6, 9d9c419 |
| **UI 마무리** — 모델피커(spark+chevron)/Worktree필(branch)/Export(file) 벡터 아이콘, File/View/Help 메뉴 실동작(New Agent·New Project·Settings·Exit / Review 토글·최대화 / About·docs) | 9d9c419 |
| **Task A: Activity History 창** — 저장된 app state를 직접 읽는 세션 횡단 이력 창, 검색 필터, 토큰/비용/블록 수 표시 | bc6c14e |
| **Task B: docs refresh** — README 주요 기능, FEATURES_KO 기능표, PROGRESS_KO 진행 로그 갱신 | (this) |

| **승인 Stage 2 스파이크** — codex app-server 실측(스키마 덤프 + Smoke --appserver-probe 실 왕복 PASS: initialize→thread/start→turn/start→commandExecution/requestApproval→accept→파일생성→turn/completed). 함정 포함 문서화 (PHASE0_CODEX_APPSERVER_KO.md) | 113c8c9 |

| **승인 Stage 2 통합** — EngineWriteback + CodexAppServerAdapter (RequireApproval=true인 codex 세션 → app-server 경로, danger+untrusted 게이트, thread/resume 호환, KillAfterTurnCompleted). 오프라인 어서션 + --live-stage2 실제품 경로 PASS | e01f697 |
| **UI 언어 설정 (KO/EN)** — Strings.Ko/En 리소스 딕셔너리, 재시작 적용 설정 토글, MainWindow/Activity History/C# 표시 문자열 추출 | 214ed3e, b779288, (this) |

| **Antigravity/Gemini 어댑터** — gemini-cli 0.42 실측(PHASE0_ANTIGRAVITY_GEMINI_KO: --skip-trust 함정, delta 누적, uuid resume) → AntigravityAdapter + 엔진 활성화(파랑 식별색, effort 비노출, antigravity exe 우선→gemini 폴백). 오프라인+라이브 2턴 resume PASS, stderr 노이즈 필터 | (this) |

| **agy 엔진 (4번째)** — TTY 전용 agy CLI를 ConPTY로 구동 (IPtyTurnRunner + Core/Hosting/ConPtyHost), 캐시에서 conversation id 추출 → resume. 사용자 세션 라이브 2턴 PASS. 텍스트 전용 v1 (구조화 출력 추가 시 확장) | 53571a8 |

### 🎨 디자인 v2 전면 매칭 (design/ 원본 갱신 반영)
> 갱신된 원본(am-app/views/chat/sidebar/settings/components/data.jsx + AgentManager.html)을 기준으로 전 표면 매칭. 회귀 금지 자산(번역·멀티프로젝트·CLI HISTORY·i18n·4엔진) 매 단계 보존. 매 단계 빌드 0/0 + KO/EN 키 동일성 검증.

| **Phase 1 nav 골격** — CurrentView(Orchestrator/History/Scheduled/Settings/Session) 중앙 페인 전환 구조, 사이드바 nav 실동작화 | 5c679f6, 1a22756 |
| **Orchestrator 대시보드** — KPI 5종(Active/Awaiting/Completed/Failed/Fleet) + Live/Recent 카드 그리드 + 공유 OrchCard(상태닷·경과·토큰·indeterminate바·Open) + Spark 이퀄라이저 + diff 바(add/del, 백그라운드 스캔) + Diff 버튼 + HUD 코너틱 + full-width | 3bc0cba, 8507026, c24440d, b08ec02 |
| **메뉴바 재구성** — File/Edit/… 6개 → 원본 Agents/View/Help 3개 + 실제 단축키(Ctrl+N/1/2/3/R/,) | da82602 |
| **Settings 풀-페인** — 620px 모달 → 중앙 페인 TOC(Runtimes/Translation/Orchestration/Permissions/Appearance/Project) + 카드형, 옛 오버레이 폐기 | 5527419, 8a72a90 |
| **Settings 백엔드 전체 실동작** — 승인정책(ask/safe/yolo)·Orchestration(worktree base/auto-start/stream-logs)·per-engine 기본모델·엔진 enable/disable·CLI Sign in(터미널 로그인 위임)·Appearance(accent 라이브 5프리셋/density 스케일/telemetry) | e8eb16a, 4af21bd, ab9c1b6, 3d27750, 68ade27, 25680f3 |
| **History 인앱 뷰** — 별도 ActivityHistoryWindow 폐기 → ⌘2 중앙 페인(타임라인·일별 그룹·검색·에이전트/상태 필터) | 5c679f6, 1a1b9bf |
| **Scheduled Tasks** — Core/Scheduling(Trigger 파싱·Store·TimerScheduler) + New schedule 생성 모달 + JobDue→세션 spawn + 하드닝(LastRunUtc 선영속·lock밖 호출) + 스모크(--sched-check/--sched-create-check) | 42401e3, 6540f8f, 1d06c43 |
| **About/단축키 모달** — 신규 오버레이 + 버전/빌드/런타임 수 어셈블리 실값 바인딩 | c1b86c3, 8bc0dea |
| **컴포저 @mention/슬래시 + 마이크** — '@' 파일/세션 멘션·'/' 액션 자동완성 팝업, Win+H 받아쓰기 | 4af21bd, (composer) |
| **NewAgent 모달 정렬** — 런타임 선택 하이라이트(SelectedBrushConverter) + 모델 피커 + worktree 미리보기 필 | f1a0198 |
| **Accent 선택 링 + 클린업 + 점검** — 스와치 선택 링, subagent 브랜치/낡은 감사문서 정리, 하드코딩 'Failed' i18n·사이드바 설정 기어 | c766db1, 2eed05d, d81fd2a |
| **API key 인증** — Runtimes 카드(cc/gx) Subscription/API key 세그 + 키 필드, DPAPI(CurrentUser) 암호화 저장(crypt32 P/Invoke, 평문 금지), 실행 시 env 주입(SessionOptions.ExtraEnvironment → AgentSession; cc=ANTHROPIC_API_KEY/gx=OPENAI_API_KEY/ag 백엔드). DPAPI 라운드트립 OS 확인 | c6ee1d6 |
| **레거시 어댑터 정리** — 구형 standalone Gemini CLI / Antigravity 어댑터(AntigravityAdapter.cs) 제거, Google 계열을 agy 엔진으로 일원화 (등록 엔진 = cc·gx·agy) | 8da33b0 |

### 🛠 세션 2026-06-21
| **Native worker 관측 시각 검증** — Claude subagent strip 합성 주입 + 라이브 런(Explore subagent Running→Completed, hook spool) 확인 | 2e4a390 |
| **Usage check 카드** — Settings▸Permissions 카드(상태줄+Check 버튼), UsageStatusText no-data 폴백 | 3559196 |
| **Native 관측 확장** — `claude agents --json` 백그라운드 세션 폴러(ProcessPoll) + 실패/rate-limit subagent transcript tailing(isApiErrorMessage→Failed). 스모크 --claude-agents-probe / --subagent-failure-check | dafb23f |
| **MVVM 리팩터** — VM↔View 분리(ApplySuggestion/RenameDraft/IDialogService/ShowAbout) · 로직 추출(TranscriptExporter·ImageAttachmentStore) · 클릭→커맨드(MouseClick behavior+nav 커맨드) | e91cb4a, 92225c8, a3c7ee0, 0893da9 |
| **디자인 충실도** — 원본 애니메이션 재현: pulse(StatusDot)·fade/rise(모달)·spin(Spinner)·blink(WorkingBlock)·border hover(BorderHover)·Review pane .22s(GridLengthAnimation) + **IBM Plex Sans/Mono 번들** | 9144878, d72ff8b, 6947773, 55a4cbe, 07c348a |
| **docs 정합성** — legacy 엔진 제거를 README/FEATURES/ROADMAP/DESIGN_SPEC/PROGRESS에 반영 | 7b0b210 |

> **agy v2 실측(2026-06-21)**: `--model gemini-3.5-flash` exit 0(포맷 유효) 확인, AgyAdapter는 Model≠"default"일 때 이미 `--model` 전달 → **agy 모델 선택 작동 중**. (`agy models` 카탈로그는 ConPTY 전용이라 헤드리스 캡처 불가 — 갱신 필요 시 실 터미널에서 `agy models`)

## 🔜 다음
1. **마이크로 폴리시(선택, 낮은 효용)**: nav/사이드바 background hover 전환(active 하이라이트와 Bg3 충돌 → 선택-인지 behavior 필요) · 입력창 focus border .15s 전환 · HudTicks 둥근 코너틱 · accent 라이브 frozen 폴백 실측
2. **observer 앱 레벨 폴러**: 현재 백그라운드 세션 폴러는 턴 범위 v1 → 항시 폴링 수요 생기면 앱 레벨로 승격
3. **뒤로 미룸(결정)**: 풀 MCP · agy 구조화 출력(추가 시 풀 이벤트 전환)

> 정리됨: API key 필드 마스킹(이미 구현)·"JK" 아바타(흔적 없음)·API key UI ag/agy 확장(agy는 계정 기반, API key 개념 없음)·agy --model(실측·작동 확인) — 모두 완료/무의미로 "다음"에서 제거.

## ⏸ 보류 / 후순위
- 멀티에이전트 파이프라인/Handoff → **P2** (결정됨)
- Browser QA · SSH/원격 · 컨테이너 · 팀공유 · Extension SDK → P2 이후
- ~~Antigravity 어댑터 → 전환(6/18)·표면 확정 후~~ → ✅ agy 엔진으로 출시 (8da33b0)

## 결정 로그 (요약)
- 세션 모델 = **단발+resume** / 승인 = **bypass 유지** / 파이프라인 = **P2**
- PTY ✗ (JSON 모드) · worktree 격리 기본 · 3-pane · Project 개념 · JSON 영속성 · 번역 1급
