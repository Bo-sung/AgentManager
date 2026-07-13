# AgentManager

**여러 코딩 에이전트(Claude Code · Codex · Antigravity · Pi)를 한 곳에서 구동·격리·승인·리뷰하고, 로컬 LLM 번역으로 토큰을 아끼는 Windows 데스크톱 관제 플랫폼**

`WPF · .NET 10 · Windows` · v1.21.16

---

AgentManager는 IDE가 아니라 **에이전트 전용 관제 평면(control plane)** 입니다. 코딩 에이전트 CLI를 프로젝트 단위로 실행하고, 세션마다 독립된 git worktree로 작업 공간을 격리하며, 변경 사항을 우측 Review pane에서 diff로 검토한 뒤 Merge/Discard 합니다. 클라우드 에이전트에게는 토큰 소비가 큰 한글 대신 **로컬 LLM이 번역한 영어**를 보내고, 영어 응답은 다시 한글로 되돌려 보여 줍니다 — 사용 편의와 토큰 비용 절감을 동시에 잡습니다.

> 핵심 원칙: **단발 실행 + resume**(상주 프로세스 아님) · **worktree 격리 기본** · **3-pane**(좌 nav · 중 console · 우 Review) · **번역 1급 시민** · **전체 트랜스크립트 JSON 영속**.

---

## 지원 엔진

| ID | 이름 | CLI | 구동 방식 | 모델(예) |
|----|------|-----|-----------|----------|
| `cc` | **Claude Code** | `claude` | stream-json (단발 + `--resume`) | opus · sonnet · haiku · **fable** · opusplan · sonnet[1m] · opus[1m] |
| `gx` | **Codex** | `codex` | `exec --json` / 승인 시 app-server | gpt-5.5 · gpt-5.4 · gpt-5.4-mini |
| `agy` | **Antigravity** (badge `AG`) | `agy` | **구독**=TTY 전용 ConPTY(텍스트) · **API**=Antigravity SDK(Python 브리지, 구조화) — 설정에서 모드 전환 | default · gemini-3.5-flash · gemini-3.1-pro · claude-* · gpt-oss-120b |
| `pi` | **Pi** (pi.dev) | `pi` (node) | `--mode rpc` (JSONL, thin-proxy) | `pi --list-models`로 동적 조회 (멀티 provider: Anthropic·OpenAI·Google·zai 등) |

> Google 계열은 `agy` 엔진으로 일원화되었습니다(구형 standalone Gemini CLI 어댑터는 제거). **Pi**는 여러 provider를 하나로 묶는 멀티 provider 에이전트 — provider 추가·인증은 pi가 자체 관리(`~/.pi`)하고 앱은 호출·표시만 합니다. 각 엔진은 사이드바·New Agent에서 식별색으로 구분됩니다.

> **Pi Worker (선택)** — Pi 엔진의 **Worker 역할** 세션은 공식 `pi` 대신 격리 런처 **pi-worker**(`@agentmanager/pi-worker-harness` 0.1.0, 공식 Pi `0.80.3` 래핑)로 실행됩니다. General/Main Pi는 그대로 `pi`를 씁니다(같은 Pi 엔진 — 별도 엔진 아님, 실행 파일만 분기).
> - **설치**: `pi-worker-harness`를 빌드(`npm install && npm run build`) 후 전역 등록(`npm install -g <pack>` 또는 `npm link`)하거나, 빌드 산출물 `dist/cli/index.js` 경로를 직접 지정.
> - **경로 지정**: Settings의 **PiWorkerPath**(또는 `settings.json`의 `PiWorkerPath`)에 `pi-worker` 런처/`dist/cli/index.js` 경로. **빈 값이면 자동 탐지**(전역 설치 위치).
> - **격리**: Worker 인증·세션은 `~/.pi-worker` 기준(공식 `~/.pi` 설정은 **변경되지 않음**). Worker는 **delegation depth 0**(하위 워커 생성·task-spool 없음).
> - 자세한 내용: [`docs/PI_WORKER_INTEGRATION_KO.md`](docs/PI_WORKER_INTEGRATION_KO.md).

> **추론 강도(effort)** — 컴포저/New Agent에서 엔진별 effort 선택: cc `low~max` + **`ultracode`**(xhigh 지원 모델 한정 — Opus·Sonnet·Fable). ultracode는 `--effort` 값이 아니라 `--settings {"ultracode":true}`로 전달되어 **동적 워크플로우**(다중 서브에이전트 조율)를 구동하며, 스트림상 2턴(런치→리포트)을 **1턴으로 접어** 최종 답만 완료로 표시하고 워크플로우 서브에이전트는 **네이티브 작업자**에 관측됩니다. gx `none~xhigh` · pi `off~xhigh` · agy는 모델 label에 내장.

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
- **탭형 사이드 패인** — 우측 영역을 **Diff / 네이티브 작업자 / 보고 수신함** 탭으로 분리(한 뷰가 다른 뷰를 밀어내지 않음)
- 생성·수정된 파일 목록 + 인라인 **Git Diff**(추가/삭제 색상), 실행 중 라이브 갱신
- **Merge ▸ main**(커밋+머지) · **Commit only** · **Discard**(`git reset --hard` + `git clean`)
- **Diff 피드백** — diff에 인라인 코멘트를 적어 에이전트에게 후속 수정 지시
- 펼침/접힘 `.22s` 슬라이드 애니메이션

### 권한/안전 모드 (engine-aware) & 승인 broker
- 컴포저의 **권한/안전 모드 칩** — 엔진 네이티브 모드를 그대로 노출하고 **색=위험도**로 현재 모드를 한눈에:
  - **cc** `Plan / Default(ask) / Bypass` (`--permission-mode`) · **gx** `Read-only / Workspace-write / Full access` (`--sandbox`)
  - **agy·pi** = 잠금 정적 배지(agy는 항상 권한 스킵, pi는 권한 개념 없음)
- `Default(ask)`에서 도구 권한 요청을 가로채 **Approve / Deny** — Claude=stream-json 승인(Stage 1), Codex=app-server JSON-RPC 승인(Stage 2)

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

### 워커 태스크 큐 (Worker Task Queue)
- **스킬 → 백로그 자동 유입** — 세션이 워커-프롬프트 스킬로 작업을 쪼개면 spool(`AGENTMANAGER_TASK_SPOOL`)에 기록되어 Orchestrator **백로그**로 자동 수집. 스풀은 **세션별** `<cwd>/.am/worker-tasks/<sessionId>/`(env=watch 동일)이라 cwd를 공유하는 세션끼리도 보고 origin이 섞이지 않음. PTY 엔진(agy)에도 env를 주입해 동일 동작.
- **Core 소유 도메인** — 백로그·**워커별 큐**·수명주기(backlog→assigned→running→done/failed)를 `WorkerTaskStore`(Core, 토큰0 테스트)가 소유. UI는 관측·시각화만.
- **할당 → 큐 → 실행** — 백로그에서 워커 선택(또는 **`+ 새 워커`로 유휴 워커 즉시 생성** — 첫 실작업이 첫 깨끗한 턴) → 워커별 큐 → **`큐 실행`**(순차 자동-진행, 워커 동시성 cap 준수) · ↑↓ 재정렬 · 완료 작업은 **`완료 기록`** 토글로 숨김.
- **작업 보고 + 복사** — 워커 작업의 최종 응답을 보고로 캡처해 **오리진 세션의 "보고 수신함" 탭**으로 라우팅. 카드별 **복사** · **전체 복사** · 체크박스 **선택 복사**로 클립보드에 빼내 세션에 수동 전달.

### 관측 & 대시보드
- **Orchestrator 대시보드** — KPI(Active/Awaiting/Completed/Failed/Fleet) + Live/Recent 카드, spark 이퀄라이저, diff 바
- **Native worker 관측** — Claude 서브에이전트를 hook으로 실시간 표시(Running→Completed/Failed), `claude agents --json` 백그라운드 세션 폴러, 실패/rate-limit subagent transcript 추론
- **한도(rate-limit) 인지** — 실행 중 감지한 rate-limit 리셋 시각을 저장해 **소진 엔진 표시 + 한도 도달 시 API 자동전환**(opt-in)에 사용. *(온디맨드 사용량 % 조회 카드는 v1.21.9에서 제거 — 엔진별 공식 헤드리스 사용량 API가 없어 정확치 못하고 조회 때마다 토큰만 소모)*
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
- **설정 파일 3분할** — 공통/전역값은 **`settings.json`**, 엔진별 값은 **`engines\<id>.json`**(엔진 1개=파일 1개), 런타임 상태(사용량·한도)는 `state.json`. 모두 손편집 + **라이브 리로드**(앱 내 변경과 양방향 동기화). 첫 실행 시 기존 `settings.json`+`models.json`에서 엔진별 파일로 **자동 마이그레이션**. API key는 DPAPI 암호화로 보관(평문 금지)
- **모델 관리(서브페이지)** — 설정 → `모델 관리`에서 엔진별 모델을 **여러 개 한번에 추가**(불릿/줄바꿈/콤마) · 삭제 · 기본 모델 지정. `models.json` 단일 파일이 차지하던 자리를 엔진별 파일로 분리해 설정 뷰가 가벼워짐
- **커스텀 엔진** — `engines\<id>.json`에 `adapterKind`+`launch`(exe/args 템플릿)를 적으면 New Agent 피커·모델 관리에 **커스텀 엔진**으로 노출·실행. 어댑터 팩토리가 엔진 id가 아니라 `adapterKind`로 프로토콜을 분기(빌트인 cc/gx/agy/pi와 동일 경로). 지원 종류: **`acp`**(Zed Agent Client Protocol — **opencode·hermes** 통합, [docs/ACP_INTEGRATION_KO.md](docs/ACP_INTEGRATION_KO.md)) · **`agentmanager-bridge-jsonl`**(풍부한 JSONL 프로토콜) · **`one-shot-text`**(단발 CLI). **`modelsQuery`**로 설치 모델 자동 조회, **`icon`**(내장 글리프 또는 SVG path)+**`color`**(hex)로 브랜드 아이콘/색 지정 — 설정 → 엔진 추가 폼에서 코드 없이 생성. 첫 실행 시 exe+args **trust 프롬프트**
- **Runtimes** — 엔진 4종 모두 **수동 CLI 경로 + 탐지(Detect) 버튼** · enable/disable · **인증(구독/API key)** · CLI Sign in. 탐지 우선순위는 **독립 설치 우선**(Claude `~/.local/bin`, Codex npm 전역) → 확장 번들 폴백. **미설치 엔진은 New Agent에서 회색+선택 불가**, **"가이드"**(설치·세팅 Markdown 모달), **한도 도달 시 API 자동전환**(opt-in) 토글
- **번역 · 언어** — UI 언어 + **번역 전/후 언어**(11개 언어쌍) + Ollama. **번역 모델 드롭다운 + "설치 모델 조회"**. **Ollama 상태(실행/꺼짐/미설치) + [실행]**(`ollama serve`); 번역 ON은 Ollama 실행 시에만 적용, 꺼짐 시 토글 옆 ⚠ · 새 세션 번역 기본값
- **Orchestration** — worktree base · auto-start · stream-logs · 동시 실행 cap
- **Permissions** — 승인 정책(ask / safe / yolo) · **익명 텔레메트리**(opt-in, 로컬 전용·외부 전송 없음)
- **Appearance** — **테마 프리셋 13종**(Dark · Light · Gray · Visual Studio · VS Code · Monokai · Nord + 브랜드 **Claude · Claude Dark · Codex · Codex Light · Antigravity · Antigravity Light**) **실시간 전환** · accent 8색 프리셋 **+ 커스텀 hex**(라이브) · density 스케일
- **Project** — 현재 **활성 프로젝트 전용** 설정: EXTRA FOLDERS · MCP config 경로

### 편의 / 디테일
- **첨부**(클립보드 Ctrl+V / 파일 선택 / **드래그앤드롭**) — 이미지(실제 썸네일·네이티브) + 마크다운/텍스트/코드 **문서**(인라인) · **오피스/PDF**(`.pdf .docx .xlsx .pptx`)는 `<cwd>/.am/attachments/`로 복사 후 경로 참조 → 에이전트가 자기 툴로 읽음(agy는 경고+폴백) · 모델 피커 · `@` 멘션 · Win+H 받아쓰기
- **슬래시 명령** — `/`는 엔진 몫: cc는 커스텀 명령(`.claude/commands`) 발견·자동완성, gx/agy/pi는 각 CLI 빌트인 카탈로그 노출(선택 시 전달). 앱 액션(clear/review/settings/help)은 `>` 접두. *비대화형 실행상 cc만 실제 확장, 그 외는 발견+텍스트 패스스루.*
- **트랜스크립트 복사** — 본문 대부분(유저/어시스턴트/툴 출력/표/코드블록/위임 리포트)을 드래그 선택·복사 · "전체 복사/내보내기"는 맨 위에 Engine·Model·Reasoning·추출시각 메타 헤더 포함
- **선택지 패널 (구조화 ask-user + 휴리스틱)** — `ask-user` 스킬을 전 엔진에 주입: 모델이 스풀에 `{question, options, multi}`(또는 `questions[]` 위저드)를 쓰면 클릭형 선택지로 렌더. **멀티셀렉트**(체크박스+제출)·**페이지네이션**(‹ N/M)·인라인 "기타" 자유입력 지원. 엔진 브랜드색 + ↑↓·마커(1-9/A-Z)·Esc 키보드. 모델이 그냥 `A)/B)`·`1./2.` 텍스트로 끝내도 휴리스틱이 폴백으로 감지 · **Enter 전송 / Shift+Enter 줄바꿈**
- **메시지 재번역(↻)** — 번역이 이상하거나 안 됐을 때 해당 어시스턴트 메시지만 다시 번역
- **스킬 주입** — 설정에서 공용 `SKILL.md`(Agent Skills 오픈 표준)를 편집하면 저장 시 각 엔진(cc·gx·agy·pi) 스킬 폴더에 기록 · 트랜스크립트 **코드블록 복사 버튼**
- **IDE 핸드오프(Open IDE)** — 활성 worktree를 VS Code로(미설치 시 탐색기 폴백) · 프로젝트 우클릭 → 폴더 열기(+경로 표시)
- **프로젝트-로컬 상태** — 각 프로젝트의 세션·워커 백로그가 `<project>/.am/project.json`에 저장돼 프로젝트 폴더를 따라감(공유 드라이브/다른 머신에서 열어도 동일). 전역 `state.json`은 프로젝트 목록·설정만. 워커 백로그는 Orchestrator의 🔄로 즉시 재스캔
- **UI 줌** — 크롬/파폭식 **Ctrl+휠** 확대·축소. **본문·모달 배율 독립**(설정에서 각각 드롭다운), Ctrl+휠/단축키는 활성 영역만 조정(LayoutTransform 리플로우라 선명)
- 창 크기/위치 기억 · 키보드 단축키 · About/단축키 모달
- **인앱 업데이트** — About 모달의 `업데이트 확인`이 GitHub 태그에서 최신 버전을 조회. 새 버전이 있으면 별도 업데이터 프로세스가 `git pull`(현재 브랜치 FF) → 재빌드 → 재실행(소스 체크아웃에서 실행 시)

### 영속성 & MCP
- 프로젝트·세션·**전체 트랜스크립트**를 로컬 JSON으로 영속·자동 로드
- **견고한 저장** — 원자적 쓰기(temp→rename) + 일시적 파일잠금 재시도 · 변경 코얼레싱(디바운스 + 종료 시 flush, 오프스레드) · 세션별 결함 격리(한 세션이 깨져도 나머지 저장) · 저장 실패 시 비차단 경고 + `save-errors.log`
- MCP 패스스루(Project별 `--mcp-config`, 파일 존재 시)

### 디자인 충실도
- 원본 디자인의 토큰(색·radii)·**IBM Plex Sans/Mono** 번들 폰트, 애니메이션 재현(running pulse · 모달 fade/rise · spinner · blink · spark 이퀄라이저 · 호버 전환 · pane 슬라이드)

---

## 아키텍처

헤드리스 코어 + 씬(thin) 프런트엔드 설계:

- **`AgentManager.Core`** — 엔진 어댑터(Claude/Codex/Antigravity/Pi), 정규화 이벤트, 번역기, 세션 실행, GitWorktree, 스케줄링, 관측(observation)에 더해 **오케스트레이션 서비스**(설정·인증·상태 저장, 턴 셋업 `TurnPlanner`, 이벤트→트랜스크립트 리듀서 `TranscriptProjector`, 실행 레지스트리 `RunRegistry`, 승인 broker `ApprovalBroker`, 사용량 `UsageService`). **UI/WPF 완전 비의존**(`net10.0`).
- **`AgentManager`** (WPF, `net10.0-windows`) — MVVM. `AppViewModel`(partial 분할)이 Core 서비스에 위임하고 라이브 컬렉션을 UI로 투영. Views/UserControls + Controls(behaviors·컨트롤). VM은 View 타입 비의존.
- **`AgentManager.Cli`** (`am`, `net10.0`) — Core만 참조하는 **헤드리스 CLI 프런트엔드**. 같은 Core 서비스로 한 턴을 실행하고 트랜스크립트를 콘솔로 투영. `am <cc|gx|agy|pi> [--cwd dir] [--model m] [--approve] <prompt…>`. *Core가 진짜로 WPF 비의존임을 증명하는 두 번째 프런트엔드.*
- **`AgentManager.Smoke`** — 헤드리스 스모크(어댑터 파싱·인자 매트릭스·승인·GitWorktree e2e·관측 + Core 서비스 골든 테스트), 토큰 0.

각 엔진 CLI의 출력은 **정규화 이벤트**로 변환되고, `TranscriptProjector`가 이를 중립 델타로 환원해 **GUI는 WPF 블록으로, CLI는 콘솔 텍스트로** 각자 렌더합니다. 프로토콜 실측은 `docs/PHASE0_*`, 코어 추출 설계는 `docs/OVERHAUL_A_EXTRACTION_MAP.md` 참조.

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

> **코드 서명(Authenticode)**: `scripts/release.ps1`은 환경 변수(`AM_SIGN_THUMBPRINT` 또는 `AM_SIGN_PFX`+`AM_SIGN_PFX_PASSWORD`)로 서명 정보를 받아 설치본(`Setup.exe`/`Update.exe`)에 서명합니다. 인증서·비밀번호는 커밋하지 않습니다(`*.pfx` gitignore). 발급·설치·서명 실행 방법은 [docs/RELEASE_SIGNING_KO.md](docs/RELEASE_SIGNING_KO.md) 참고. 환경 변수를 지정하지 않으면 기존처럼 서명 없이 패키징됩니다.

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

- 공통 설정: `%LocalAppData%\AgentManager\settings.json` (VS Code식 손편집 + 라이브 리로드) — 전 엔진 공통/전역 값만
- **엔진별 설정: `%LocalAppData%\AgentManager\engines\<id>.json`** — 엔진 1개 = 파일 1개. 모델·추론 effort·경로·인증·활성·스킬 폴더 등 그 엔진 전용값. **커스텀 엔진은 이 파일이 매니페스트 겸용**(`adapterKind`+`launch`). 설치/업데이트/시작 시 폴더 선생성, 설정에서 `엔진 설정 폴더 열기`
- 런타임 상태(사용량·한도 쿨다운·무시한 세션): `%LocalAppData%\AgentManager\state.json`
- 창 상태: `%LocalAppData%\AgentManager\window.json`
- worktree: `%LocalAppData%\AgentManager\worktrees\...`
- 첨부 이미지: `%LocalAppData%\AgentManager\attachments\`
- API key: DPAPI(CurrentUser)로 암호화 저장 (평문 미저장)

---

## 문서

| 문서 | 내용 |
|------|------|
| [CHANGELOG.md](CHANGELOG.md) | 버전별 변경 사항(태그 1:1) |
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

## 변경 이력

최근 버전 요약 — 전체는 [CHANGELOG.md](CHANGELOG.md) 참고 (`vX.Y.Z` 태그와 1:1).

- **1.21.16** — **워커 보고 스킬(완료 감지와 분리)** — 워커가 작업을 끝냈다고 판단하면 `AGENTMANAGER_REPORT_SPOOL`(`.am/report/<workerId>/`)에 `report.md`를 써서 보고를 **능동 전송**; 워처가 감지해 워커의 Running 태스크에 첨부+Done → origin(컨트롤타워) 수신함 즉시 반영. 보고가 **턴 완료 감지에서 분리**돼 이후 턴이 매달려도 보고는 이미 도착. 기존 완료-캡처는 폴백으로 유지(회귀 0). `ask-user` 스풀과 동일 패턴이라 모든 엔진 공용.
- **1.21.15** — **턴 무응답 타임아웃 설정화** — v1.21.14의 무활동 워치독(10분 고정)을 설정 → Orchestration의 **"턴 무응답 타임아웃(분)"** 드롭다운으로 조정 가능(0·3·5·10·15·30·60, 기본 10; **0=무제한**). `settings.json`의 `TurnTimeoutMinutes`로 영속.
- **1.21.14** — 버그 수정: **완료 이벤트 없이 매달리는 턴까지 무한 대기 해소**(특히 Claude Code 워커). v1.21.13의 grace 강제종료는 완료 이벤트(TurnCompleted)를 본 뒤에만 걸려, 엔진이 완료 이벤트 없이 매달리면 여전히 30분+ 고착됐음. **무활동 워치독**(출력 없이 10분→stall 강제종료+완료 합성, 매 출력마다 리셋해 긴 도구 호출은 보존)으로 **모든 고착을 최대 10분 상한**; 완료-후 고착 grace도 30s→6s 단축. 무한 대기·수동 중단 불필요.
- **1.21.13** — 버그 수정: **완료된 턴이 무한 "대기"로 고착되던 문제**(실행 중 PC 절전→복귀 등). keep-open 엔진(claude)은 턴 완료 후 stdin EOF로 종료하는데, 복귀 후 자식이 죽은 소켓에 매달려 안 나가면 stdout 읽기가 영원히 블록돼 세션이 "running"에 고착(수동 중단 필요)됐음. `TurnCompleted` 이후 유예 30초 내 미종료 시 강제 종료해 턴을 깔끔히 완료(status→done, 보고 캡처); 정상 종료 시 타이머 취소로 정상 턴은 무영향. (v1.21.12의 중단-시-보고-보존과 합쳐져 절전 시나리오 해소.)
- **1.21.12** — 버그 수정: **중지/에러로 끝난 워커 턴의 완성된 보고가 유실되던 문제**. 자동 러너가 워커 보고를 `worker.Status=="done"`일 때만 캡처해, 턴이 중지(idle)·에러(error)로 끝나면 다 만든 "## Report"를 버리고 태스크를 Failed로만 남겨 origin(컨트롤타워) 보고 수신함이 비었음. 이제 최종 응답을 만들었으면 종료 상태와 무관하게 보고를 캡처(상태는 Done/Failed 분류에만). *(깔끔히 done으로 끝난 보고가 안 뜨는 origin-링크 케이스는 별도 조사.)*
- **1.21.11** — 버그 수정: **모델 관리에서 추가한 모델이 설정 "기본 모델" 드롭다운에 즉시 안 뜨던 문제**. 갱신 경로가 둘로 갈려 모델 관리 경로(`AfterModelManagerChange`)가 설정 엔진 드롭다운 갱신을 빼먹은 탓 — 특히 CLI 목록 조회가 없어 모델을 모델 관리로만 넣는 **cc·gx(codex)**에서 두드러짐. 빌트인은 `RefreshEngineModels`에 위임하도록 통합해 추가/삭제/기본지정이 드롭다운·New Agent·컴포저에 즉시 반영.
- **1.21.10** — **커스텀 엔진 아이콘/색상** — 매니페스트에 `icon`(내장 글리프 circle·square·hexagon·triangle·diamond·spark·bolt·bubble 또는 SVG path 데이터) + `color`(hex)를 넣으면 New Agent 피커·세션 탭·히스토리·오케스트레이터·설정 카드 등 **모든 표시 지점에 브랜드 아이콘/색**으로 렌더(그전엔 빈 슬롯+badge 글자만). `EngineVisual` 컨버터가 EngineIcon 스타일 기본 Setter로 공급해 표시 지점 무수정 · 빌트인(cc/gx/agy/pi)은 기존 브랜드 유지 · 미지정 커스텀도 기본 글리프라 빈칸 없음. 엔진 추가 폼에 아이콘/색상 입력 추가.
- **1.21.9** — **사용량(rate-limit %) 체크 기능 제거** — 엔진별로 헤드리스에서 사용량을 정확히 조회할 공식 경로가 없음(cc `/usage`는 TUI 전용이라 `-p`에선 일반 프롬프트로 처리돼 토큰만 소모, gx/agy/ACP도 상시 조회 불가). 푸터·설정의 **"지금 체크"** 버튼 + 사용량 % 카드 + 능동 조회 제거. **코드는 보존**(XAML 주석 / C# `#if false`)해 향후 공식 API 등장 시 복원 용이. 실행 중 passive rate-limit 리셋 추적(소진 감지·auto-API-fallback)과 cost 표시(엔진 자가보고값 그대로 표시, 자체 계산 아님)는 그대로 유지.
- **1.21.8** — **커스텀 엔진 모델 자동 조회** — 매니페스트에 `modelsQuery`(모델 목록을 한 줄에 하나씩 출력하는 명령의 args, 예: opencode `models`)를 넣으면 설정 커스텀 엔진 카드에 **"설치 모델 조회"** 버튼이 생겨 실행→파싱→`engines/<id>.json` 갱신(빌트인 pi/agy 조회와 동일 UX). 엔진 추가 폼에도 조회 명령 입력 추가. GUI 실측: opencode `models` → 1→117개 자동 반영.
- **1.21.7** — **ACP(Agent Client Protocol) 어댑터** — Zed의 JSON-RPC/stdio 표준을 말하는 커스텀 엔진 종류(`adapterKind:"acp"`)로 **opencode·hermes 통합**(스트리밍 텍스트·thinking·도구/토큰 가시성, ANSI 없음). initialize→session/new→session/prompt 핸드셰이크 + session/update 매핑 + 권한 왕복. opencode `acp` end-to-end GUI 실측(핸드셰이크 연결→응답 스트림→완료). 상세 [docs/ACP_INTEGRATION_KO.md]. *(zcode는 자체 Electron/protocol이라 ACP stdio 모드 미발견 — 미통합.)*
- **1.21.6** — 핫픽스: **모델 0개 커스텀 엔진을 New Agent에서 실행 시 크래시**(`IndexOutOfRange` — `engine.Models[0]` 무가드 접근) 수정. 0모델 엔진은 유효(컴포저 모델 자유 입력)하므로 안전 폴백(`DefaultModelFor`, 빈 모델=엔진 기본)으로 교체. GUI 실측: 0모델 엔진 Launch→세션 정상 생성 + trust 프롬프트 정상.
- **1.21.5** — 커스텀 엔진 마감 + 보안 하드닝(병렬 설계→구현): **커스텀 엔진 추가 폼**(설정 Runtimes에서 JSON 손편집 없이 id/adapterKind/exe/args/모델 입력→생성) · **실행 전 trust 프롬프트**(커스텀 엔진 첫 실행 시 exe+args 승인, 지문 기억·변경 시 재승인) · **`agentmanager-bridge-jsonl` 어댑터**(one-shot-text보다 풍부한 JSONL 프로토콜 — 도구/사고/토큰 이벤트, 스펙 [docs/BRIDGE_JSONL_PROTOCOL_KO.md]) · 히스토리/예약 목록의 **커스텀 엔진 표시명이 cc로 보이던 버그 수정** · **Ollama egress 가드**(번역 엔드포인트가 loopback 아니면 경고+옵트인 전까지 차단 — 텍스트 무단 유출 방지) · **워커 스풀 크기 상한**(2000/파일당 200, 초과 시 reject-newest+로그) · **번역 활성화를 선택 provider 기준으로 게이팅**(Ollama 꺼져 있어도 agent/커스텀 provider면 번역 가능) · 릴리스 **코드서명 배선**([docs/RELEASE_SIGNING_KO.md]; 실제 서명은 인증서 확보 후). *(models.json 완전 제거는 업그레이드 마이그레이션 안전성 위해 연기.)*
- **1.21.4** — 커스텀 엔진 세션이 **재시작마다 cc로 리셋되던 버그 수정**(복원·포크·CLI복원·예약이 커스텀 엔진 id를 빌트인 전용 `EngineRegistry.Get`으로 되살려 cc+sonnet으로 폴백 → 다음 저장 때 손상; 커스텀 인지 `EngineDefFor`로 교체) · **[[PR #1](https://github.com/Bo-sung/AgentManager/pull/1)]** Codex 모델에 **GPT-5.6 Sol/Terra/Luna** 변형 추가 · **150% 배율에서 최대화 창이 작업영역을 넘던 고DPI 버그 수정**(`WM_GETMINMAXINFO`를 WindowChrome이 덮어쓰지 못하게 + 상태/DPI 전환 후 작업영역 재적용)
- **1.21.3** — v1.21.2 커스텀 엔진 후속 수정: **`설정 새로고침`이 `engines\*.json`도 재스캔**(손으로 추가/편집한 커스텀 엔진이 재시작 없이 피커·모델 관리·설정에 반영 — 이전엔 settings.json만 재로드) · 설정 Runtimes에 **커스텀 엔진 카드**(adapterKind·실행 경로·활성·삭제·모델 관리 링크, 데이터 주도) · 빌트인 카드의 중복 "주로 쓰는 모델" 체크리스트 제거(모델 관리로 일원화) · 커스텀 카드 adapterKind 바인딩 크래시 수정
- **1.21.2** — **엔진 설정 오버홀**: 설정 파일을 3분할(공통=`settings.json` / 엔진별=`engines\<id>.json` / 런타임=`state.json`) — 엔진 1개=파일 1개, 첫 실행 시 기존 `settings.json`+`models.json`에서 자동 마이그레이션 · 설치/업데이트/시작 시 `engines\` 폴더 선생성 + `엔진 설정 폴더 열기` 버튼 · **모델 관리 서브페이지**(엔진별 모델 다중 추가/삭제/기본값 지정) · **커스텀 엔진**(`engines\<id>.json`이 `adapterKind`+`launch` 매니페스트 겸용 → 피커·매니저 노출·실행, 어댑터 팩토리가 `adapterKind`로 프로토콜 분기 · `one-shot-text` 어댑터로 단발 CLI 지원). 상세 [docs/ENGINE_CONFIG_OVERHAUL_KO.md](docs/ENGINE_CONFIG_OVERHAUL_KO.md)
- **1.21.1** — 자기 업데이트가 스스로 막히던 버그 수정: 앱/`ollama serve` 자식이 설치 폴더(`current\`)를 cwd로 잡아 Velopack이 폴더를 교체 못 하던 문제(앱 cwd 이동 + 우리가 띄운 ollama만 정리, **외부 ollama 불가침**). ※ 이 버전부터 적용 — 기존에 막혔다면 재부팅 후 한 번만 업데이트
- **1.21.0** — 모델 카탈로그 파일 **`models.json`**(엔진별 모델 목록 + 모델별 추론 effort/기본값 — 설정파일 직접 편집이 필터/피커에 반영, 하드코딩 목록 대체) · 설정에 `models.json 열기`·`설정 새로고침` 버튼 · **settings.json 라이브 리로드 견고화 + 유실 버그 수정**(파싱 실패 시 기본값 덮어쓰기 방지). 상세 [docs/MODEL_CATALOG_KO.md](docs/MODEL_CATALOG_KO.md)
- **1.20.1** — Pi Worker 런타임을 설치본에 **번들**(글로벌 npm 설치 불필요) · Codex `gpt-5.6` 모델 · 컴포저 커스텀 모델 직접 입력 · agy 모델 조회
- **1.20.0** — Pi Worker: Pi 엔진의 **Worker 역할** 세션을 격리 런처 `pi-worker`(공식 Pi 0.80.3 래핑)로 실행(General/Main Pi는 그대로 공식 pi — 별도 엔진 아님, 실행 파일만 분기). 역할별 세션 discovery(`~/.pi-worker`) · Worker 공통 정책(delegation depth 0, task-spool 미제공) · `agent_end.willRetry==false` 완료 판정 · extension_ui_request 안전 취소 · 프로세스 트리 정리. `PiWorkerPath` 설정(빈 값=자동탐지). 상세 [docs/PI_WORKER_INTEGRATION_KO.md](docs/PI_WORKER_INTEGRATION_KO.md)
- **1.14.1** — Claude Code 에이전트 런타임(`.claude/worktrees/` · `settings.local.json`) gitignore
- **1.14.0** — Claude Design 동기화(오케스트레이터 워커 큐 + 위임 UI 재디자인) · 워커 보고 라우팅 수정(origin 유실) · 영속화 견고화(쓰기 재시도·코얼레싱·세션 격리·저장실패 경고) · 삭제 워커 작업 정리
- **1.13.3** — 죽은 cc 세션(resume 대상이 엔진에서 사라짐) 에러 블록에 "이 세션 삭제" 버튼 추가
- **1.13.2** — 보고 수신함: 전체 복사 → 전체 선택 토글, 선택 중엔 카드별 복사 버튼 숨김(일괄 모드 일관성)
- **1.13.1** — 세션 삭제 시 에이전트 브랜치 자동 정리(머지된 것만 삭제, 미머지 보존) — 브랜치 누적 버그 수정
- **1.13.0** — 워커 태스크 큐(스킬→백로그 자동 유입 · Core `WorkerTaskStore` · 워커별 큐 · 큐 실행/재정렬/완료 기록) · 탭형 사이드 패인(Diff/네이티브 작업자/보고 수신함) · 작업 보고 수신함 + 복사(카드별/전체/선택) · 네이티브 작업자 형제-세션 오인 필터
- **1.12.0** — 스킬 주입(설정에서 SKILL.md 편집 → 저장 시 cc·gx·agy·pi 스킬 폴더에 기록) · 마크다운 코드블록 복사 버튼
- **1.11.0** — 크래시 시 오류 로그 팝업 + 종료(전역 예외 핸들러)
- **1.10.0** — 문서 첨부(이미지 외) + 실제 썸네일 · 빠른-응답 버튼(A/B·1/2 선택지 자동 감지) · 메시지 재번역(↻) · Enter 전송/Shift+Enter 줄바꿈 · 번역 스킵·세션 열기 버그 수정
- **1.9.0** — 인앱 업데이트 확인(GitHub 태그) + 별도 업데이터 프로세스(git pull → 빌드 → 재실행) · About 단축키 구분선 정리
- **1.8.0** — Pi 엔진(pi.dev, 멀티 provider) · 엔진별 "주로 쓰는 모델" 체크리스트 · 테마 3종(Claude Dark·Codex Light·Antigravity Light) + 엔진색 전테마 고정
- **1.7.2** — 설정 카드 엔진 아이콘 · 아이콘 색 강조색/테마 독립 · Antigravity 무지개
- **1.7.1** — 공식 엔진 아이콘(Claude/Codex/Antigravity) · README 변경 이력 섹션
- **1.7.0** — 엔진 가용성 게이팅(미설치 회색·설치 가이드 모달) · 한도→API 자동전환 · 번역 Ollama 게이팅+상태/실행 · CLI 삭제 영속
- **1.6.0** — 사용량 엔진별 표시 + 퍼센트 막대(세션/주간) + 비공식 추정치 안내
- **1.5.1** — 사용량 체크 크래시 가드(전역 예외 핸들러 + 자식 오류 대화상자 억제)
- **1.5.0** — 브랜드 테마 3종 + 커스텀 강조색 · 엔진 경로 수동+탐지 · 번역/언어 통합
- **1.4.0** — UI 줌(Ctrl+휠, 본문/모달 독립 배율)
- **1.3.0** — 워커 위임(메인↔워커) · **1.2.0** 번역 언어쌍 · **1.1.0** 테마/settings.json · **1.0.0** 첫 릴리즈

---

## 라이선스

이 프로젝트는 MIT 라이선스로 배포됩니다([LICENSE](LICENSE)). 번들된 **IBM Plex Sans/Mono**는 SIL Open Font License(OFL)를 따릅니다.
