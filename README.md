# AgentManager

**여러 코딩 에이전트(Claude Code · Codex · Antigravity)를 한 곳에서 구동·격리·승인·리뷰하고, 로컬 LLM 번역으로 토큰을 아끼는 Windows 데스크톱 관제 플랫폼**

`WPF · .NET 10 · Windows` · v1.3.0

---

AgentManager는 IDE가 아니라 **에이전트 전용 관제 평면(control plane)** 입니다. 코딩 에이전트 CLI를 프로젝트 단위로 실행하고, 세션마다 독립된 git worktree로 작업 공간을 격리하며, 변경 사항을 우측 Review pane에서 diff로 검토한 뒤 Merge/Discard 합니다. 클라우드 에이전트에게는 토큰 소비가 큰 한글 대신 **로컬 LLM이 번역한 영어**를 보내고, 영어 응답은 다시 한글로 되돌려 보여 줍니다 — 사용 편의와 토큰 비용 절감을 동시에 잡습니다.

> 핵심 원칙: **단발 실행 + resume**(상주 프로세스 아님) · **worktree 격리 기본** · **3-pane**(좌 nav · 중 console · 우 Review) · **번역 1급 시민** · **전체 트랜스크립트 JSON 영속**.

---

## 지원 엔진

| ID | 이름 | CLI | 구동 방식 | 모델(예) |
|----|------|-----|-----------|----------|
| `cc` | **Claude Code** | `claude` | stream-json (단발 + `--resume`) | claude-sonnet-4-6 · claude-opus-4-8 · claude-haiku-4-5 · sonnet[1m] |
| `gx` | **Codex** | `codex` | `exec --json` / 승인 시 app-server | gpt-5.5 · gpt-5.4 · gpt-5.4-mini |
| `agy` | **Antigravity** (badge `AG`) | `agy` | TTY 전용 → ConPTY 구동 (텍스트 v1) | default · gemini-3.5-flash · gemini-3.1-pro · claude-* · gpt-oss-120b |

> Google 계열은 `agy` 엔진으로 일원화되었습니다(구형 standalone Gemini CLI 어댑터는 제거). 각 엔진은 사이드바·New Agent에서 식별색으로 구분됩니다.

---

## 주요 기능

### 세션 & 멀티 에이전트
- **멀티 세션 병렬 구동** — 독립 프로세스 다중 실행 + 실시간 상태(실행중/대기/완료/실패/에러)
- **세션 수명주기** — 생성·중지(Stop)·이름변경·아카이브 토글·삭제, **멀티턴 resume**(세션 ID 기억 후 대화 이어가기), **Fork**(세션 분기)
- **사이드바 그룹화**(Active / Project / Archived) + **검색·필터**(Title/Branch/Project 즉시 필터)
- **동시 실행 개수 제한**(concurrency cap)

### 프로젝트 & worktree 격리
- 사이드바 **PROJECTS** 목록에서 전 프로젝트 표시·전환, 우클릭 Rename/Remove(관련 세션 정리)
- Browse로 폴더 선택(미존재 경로 자동 생성), **멀티폴더**(Settings의 EXTRA FOLDERS → Claude `--add-dir` / Codex writable_roots)
- 세션마다 독립 **git worktree** 생성·마운트로 작업 공간 격리

### Review pane (우측 변경/Diff)
- 생성·수정된 파일 목록 + 인라인 **Git Diff**(추가/삭제 색상), 실행 중 라이브 갱신
- **Merge ▸ main**(커밋+머지) · **Commit only** · **Discard**(`git reset --hard` + `git clean`)
- **Diff 피드백** — diff에 인라인 코멘트를 적어 에이전트에게 후속 수정 지시
- 펼침/접힘 `.22s` 슬라이드 애니메이션

### 승인 broker
- 에이전트의 도구 권한 요청을 가로채 **Approve / Deny** 제공 — 세션의 `APPROVAL` 토글 하나로 통일
- Claude = stream-json 승인(Stage 1), Codex = app-server JSON-RPC 승인(Stage 2)
- 샌드박스 모드(read-only / workspace-write / danger) 선택

### 로컬 LLM 번역 레이어 (차별점)
- **번역 언어 쌍 설정** — *번역 전 언어*(내가 입력·표시)와 *번역 후 언어*(엔진에 전달, 토큰 절감)를 각각 드롭다운에서 선택(기본 한국어→English, 11개 언어). 입력은 *전→후*, 출력은 *후→전*으로 자동 변환
- 블록별 **ORIGINAL** 토글로 원문 교차검증, "이미 번역됨" 스킵은 언어별 스크립트(한글/가나/한자/키릴/아랍/데바나가리) 감지
- 코드블록·인라인 코드·`@file` 참조 등은 **마스킹**하여 번역 손상 방지
- Ollama 기반(기본 권장 모델 `exaone3.5:7.8b`, 엔드포인트/모델 설정 가능)

### 워커 위임 (Worker Delegation)
- 대형 모델 **메인** 세션이 소형 모델 **워커**에게 잔작업을 위임하고 보고를 받는 반자동 흐름 — 토큰 비용 절감.
- 에이전트 응답의 **위임 버튼** → 워커 선택/생성 → 위임 프롬프트 전송. 워커는 **지속 풀**(프로젝트별, 사이드바 `WORKERS` 그룹)로 관리.
- 워커별 **고정 설정**(엔진·모델·번역 정책·행동 규칙 preamble) — 생성 시 고정. preamble은 위임 프롬프트 앞에 자동 부착되어 워커가 항상 `## Report`로 마무리.
- 완료 시 메인 트랜스크립트의 **DelegationCard**(보고 미리보기) → `보고 붙여넣기`. 다수는 **보고 수신함** + **합쳐 붙여넣기**(위임 순서 병합).
- **일괄 fan-out** — "유휴 워커 전체에 위임"으로 N개 워커 **동시 실행**(워커 전용 동시성 cap, 메인과 분리).
- **크로스 엔진** — 메인=Claude, 워커=Codex/Antigravity 등 자유 조합(엔진 무관 어댑터 라우팅).

### 관측 & 대시보드
- **Orchestrator 대시보드** — KPI(Active/Awaiting/Completed/Failed/Fleet) + Live/Recent 카드, spark 이퀄라이저, diff 바
- **Native worker 관측** — Claude 서브에이전트를 hook으로 실시간 표시(Running→Completed/Failed), `claude agents --json` 백그라운드 세션 폴러, 실패/rate-limit subagent transcript 추론
- **사용량(Usage)** — 세션/주간 % · 리셋까지 시간 · 신선도 라벨, Settings의 *Usage check* 카드에서 on-demand 확인
- **Activity History** — 저장된 app state를 직접 읽는 세션 횡단 이력(타임라인·일별 그룹·검색·필터, 토큰/비용/블록 수)
- **데스크톱 알림** — 비활성 창 작업표시줄 깜빡임 + 승인 요청 사운드

### 트랜스크립트 렌더링
- 유저/에이전트/툴/에러/승인/**Thinking** 블록
- Markdown(헤딩·목록·코드펜스·인라인 코드·볼드·**클릭 가능 링크**·테이블)
- 본문 선택·복사 + 응답 헤더 `⧉`로 Markdown 원문 복사, 휠 스크롤 중계/자동 추적

### Artifacts
- `TodoWrite` → 태스크리스트 · 테스트 러너 감지 → pass/fail · 턴 종료 → Summary, 세션별 영속

### Scheduled Tasks
- cron 유사 트리거(간격/시각)로 세션 자동 spawn(`New schedule` 모달, `LastRunUtc` 영속). *(이벤트 기반 트리거는 v1 미지원)*

### CLI HISTORY
- AgentManager 밖에서 돌린 `claude`/`codex` 세션 발견 → 가져오기·resume, 과거 트랜스크립트 복원(대형 기록 청크 비동기 + UI 가상화), 사이드바 재스캔

### 설정 (중앙 Settings 페인 · VS Code식 settings.json)
- 설정은 별도 **`settings.json`** 으로 저장 — "Open settings.json"으로 손편집 + 외부 편집 **라이브 리로드**(앱 내 변경과 양방향 동기화). API key는 DPAPI 암호화로 보관(평문 금지)
- **Runtimes** — 엔진 경로 · enable/disable · **인증(구독/API key)** · CLI Sign in
- **Translation** — Ollama 엔드포인트/모델 · 새 세션 번역 기본값
- **Orchestration** — worktree base · auto-start · stream-logs · 동시 실행 cap
- **Permissions** — 승인 정책(ask / safe / yolo) · **익명 텔레메트리**(opt-in, 로컬 전용·외부 전송 없음)
- **Appearance** — **IDE 테마 프리셋 7종**(Dark · Light · Gray · Visual Studio · VS Code · Monokai · Nord) **실시간 전환** · accent 5색 프리셋(라이브) · density 스케일
- **Language** — **UI 언어**(한국어/English) · **번역 전/후 언어** 드롭다운(번역 언어 쌍)
- **Project** — EXTRA FOLDERS · MCP config 경로

### 편의 / 디테일
- 이미지 첨부(클립보드 Ctrl+V / 파일 선택) · 모델 피커 · `@` 멘션 · `/` 슬래시 액션 · Win+H 받아쓰기
- **IDE 핸드오프(Open IDE)** — 활성 worktree를 VS Code로(미설치 시 탐색기 폴백)
- **UI 줌** — 크롬/파폭식 **Ctrl+휠** 확대·축소. **본문·모달 배율 독립**(설정에서 각각 드롭다운), Ctrl+휠/단축키는 활성 영역만 조정(LayoutTransform 리플로우라 선명)
- 창 크기/위치 기억 · 키보드 단축키 · About/단축키 모달

### 영속성 & MCP
- 프로젝트·세션·**전체 트랜스크립트**를 로컬 JSON으로 영속·자동 로드
- MCP 패스스루(Project별 `--mcp-config`, 파일 존재 시)

### 디자인 충실도
- 원본 디자인의 토큰(색·radii)·**IBM Plex Sans/Mono** 번들 폰트, 애니메이션 재현(running pulse · 모달 fade/rise · spinner · blink · spark 이퀄라이저 · 호버 전환 · pane 슬라이드)

---

## 아키텍처

3계층 클린룸 설계 + WPF MVVM:

- **`AgentManager.Core`** — 엔진 어댑터(Claude/Codex/Antigravity), 정규화 이벤트, 번역기, 세션 실행, GitWorktree, 스케줄링, 관측(observation). UI 비의존.
- **`AgentManager`** (WPF) — MVVM. `AppViewModel`(partial 분할) + 컴포넌트 VM + Views/UserControls + Controls(behaviors·컨트롤). 클릭은 커맨드/attached behavior로 바인딩, VM은 View 타입 비의존.
- **`AgentManager.Smoke`** — 헤드리스 스모크(어댑터 파싱·인자 매트릭스·승인·GitWorktree e2e·관측), 토큰 0.

각 엔진 CLI의 출력은 **정규화 이벤트**로 변환되어 UI가 엔진 차이를 모르게 합니다. 프로토콜 실측은 `docs/PHASE0_*` 참조.

---

## 요구 사항

- **OS**: Windows 10 / 11
- **런타임**: **.NET 10 Desktop Runtime** (배포본 실행 시) — 소스 빌드 시 **.NET 10 SDK**
- **Claude CLI**: `claude` 가 PATH에 설치·로그인되어 있어야 함 (네이티브 관측 hook 사용)
- **Ollama** (번역용): `localhost:11434` 구동 + 번역 모델 설치(권장 `exaone3.5:7.8b`)
- **Codex CLI** / **agy CLI** (선택): 해당 엔진 사용 시

---

## 빌드 및 실행

```powershell
# 빌드
dotnet build AgentManager.slnx -c Release

# 실행 (소스에서)
dotnet run --project src/AgentManager/AgentManager.csproj -c Release

# 헤드리스 스모크 (토큰 0)
dotnet run --project src/AgentManager.Smoke
```

### 릴리즈 패키징

```powershell
# dist/ 에 단일 파일로 발행 (framework-dependent — 대상 PC에 .NET 10 Desktop Runtime 필요)
./scripts/publish.ps1
```

> 런타임 설치 없이 단독 실행 배포가 필요하면 self-contained 발행으로 전환할 수 있습니다(산출물 ~150MB).

---

## 빠른 사용법

1. **프로젝트 등록** — 사이드바에서 프로젝트 추가(+)로 로컬 소스 디렉터리 등록(미존재 경로 자동 생성)
2. **New Agent** — 프로젝트 선택 후 `+ New Agent`로 엔진·모델을 고르고 세션 생성(번역 `TR ON` 확인)
3. **대화** — 한글로 지시 → 영어로 번역되어 전송, 응답은 한글로 표시(`ORIGINAL`로 원문 확인)
4. **Review** — 에이전트가 코드를 바꾸면 우측 Review pane에 파일·diff 표시
5. **Merge / Discard** — 검토 후 `Merge ▸ main`으로 병합하거나 `Discard`로 폐기
6. **Open IDE** — 더 깊게 보려면 클릭 한 번으로 worktree를 VS Code로 열기

### 키보드 단축키
`Ctrl+N` 새 에이전트 · `Ctrl+1/2/3` Orchestrator/History/Scheduled · `Ctrl+R` Review 토글 · `Ctrl+,` Settings · `Ctrl+F` 세션 검색 · `Esc` 모달 닫기

---

## 데이터 위치

- 상태/세션/트랜스크립트: `%LocalAppData%\AgentManager\state.json`
- 설정: `%LocalAppData%\AgentManager\settings.json` (VS Code식 손편집 + 라이브 리로드)
- 창 상태: `%LocalAppData%\AgentManager\window.json`
- worktree: `%LocalAppData%\AgentManager\worktrees\...`
- 첨부 이미지: `%LocalAppData%\AgentManager\attachments\`
- API key: DPAPI(CurrentUser)로 암호화 저장 (평문 미저장)

---

## 문서

| 문서 | 내용 |
|------|------|
| [DESIGN_SPEC_KO.md](docs/DESIGN_SPEC_KO.md) | 3계층 아키텍처·정규화 이벤트·번역 프롬프트 명세 |
| [FEATURES_KO.md](docs/FEATURES_KO.md) | 기능 정의·우선순위·횡단 결정 |
| [FRONTEND_KO.md](docs/FRONTEND_KO.md) | 프론트엔드 렌더링 구조·영역 명칭·테마 시스템 위키 |
| [PROGRESS_KO.md](docs/PROGRESS_KO.md) | 구현 진행 상태·커밋 로그(단일 진실 소스) |
| [ROADMAP_KO.md](docs/ROADMAP_KO.md) | 기능 로드맵·마일스톤 |
| [SETTINGS_BACKEND_PLAN_KO.md](docs/SETTINGS_BACKEND_PLAN_KO.md) | 설정 백엔드 설계 |
| [PHASE0_CLAUDE_STREAMJSON_KO.md](docs/PHASE0_CLAUDE_STREAMJSON_KO.md) | Claude stream-json 실측·매핑 |
| [PHASE0_CODEX_EXEC_JSON_KO.md](docs/PHASE0_CODEX_EXEC_JSON_KO.md) | Codex exec --json 실측·매핑 |
| [PHASE0_CODEX_APPSERVER_KO.md](docs/PHASE0_CODEX_APPSERVER_KO.md) | Codex app-server 승인 실측 |
| [PHASE0_ANTIGRAVITY_GEMINI_KO.md](docs/PHASE0_ANTIGRAVITY_GEMINI_KO.md) | Antigravity/Gemini CLI 실측(phase-0 기록) |
| [NATIVE_WORK_OBSERVATION_KO.md](docs/NATIVE_WORK_OBSERVATION_KO.md) · [_CLAUDE_2026_06_15](docs/NATIVE_WORK_OBSERVATION_CLAUDE_2026_06_15.md) | 네이티브 작업 관측 설계·검증 |
| [UI_NAV_QA_SCRIPT.md](docs/UI_NAV_QA_SCRIPT.md) | UI 내비게이션 QA 스크립트 |

---

## 로드맵

- **선택 폴리시**: nav/사이드바 호버 전환 · 입력 focus 전환 등 미세 폴리시
- **P2 이후**: 풀 MCP · 멀티에이전트 파이프라인/Handoff · Browser QA · SSH/원격 · 컨테이너 · 팀 공유 · Extension SDK
- *(승인 Stage 2 · Scheduled Tasks · Antigravity(agy) 지원 · 네이티브 관측은 이미 제공됨)*

---

## 라이선스

이 프로젝트는 MIT 라이선스로 배포됩니다([LICENSE](LICENSE)). 번들된 **IBM Plex Sans/Mono**는 SIL Open Font License(OFL)를 따릅니다.
