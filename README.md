# AgentManager

**Standalone Multi-Agent Development Control Plane + 로컬 LLM 한↔영 번역 레이어 (WPF/.NET 10, Windows)**

WPF(.NET 10) 기반의 Windows 데스크톱 애플리케이션으로, 코딩 에이전트(Claude Code, Codex 등)를 프로젝트 단위로 구동하고 격리·승인·변경 사항(Diff/Merge)을 통합 관리하는 에이전트 전용 관제 플랫폼입니다. 로컬 LLM을 통한 1급 한↔영 번역 레이어를 기본 탑재하여, 한글 입력 시 토큰 소비가 큰 클라우드 에이전트에게는 영어로 변환하여 전달하고 에이전트의 영어 응답은 다시 한글로 자동 번역하여 보여줌으로써 사용자 편의성과 토큰 비용 절감을 동시에 달성합니다.

---

## 🛠️ 주요 기능

현재 실제로 구현되어 정상 동작하는 기능들입니다:

*   **멀티 세션 병렬 관제 (Claude Code/Codex)**
    *   독립된 에이전트 프로세스 다중 구동 및 실시간 상태 관제 (실행중, 대기중, 완료, 실패, 에러)
    *   사이드바 그룹화 (Active/Project/Archived) 및 이름 변경, 아카이브 토글, 삭제 등 세션 수명주기 관리
    *   **세션 검색·필터**: Title/Branch/Project 기준으로 사이드바 세션 목록을 즉시 필터링
    *   **Activity History 창**: 저장된 app state를 직접 읽어 전체 세션 이력, 토큰, 비용, 트랜스크립트 블록 수를 읽기 전용으로 조회
    *   에이전트 구동/응답 시 작업표시줄 깜빡임 알림 및 승인 요청 시 사운드 알림 지원
    *   에이전트의 생각을 실시간으로 들여다볼 수 있는 Thinking 블록 UI 및 Markdown 렌더링 (헤더, 코드 펜스, 인라인 코드, 볼드, 클릭 가능한 링크, 테이블)
*   **PROJECTS 및 프로젝트 스코프 관리**
    *   사이드바 PROJECTS 목록에서 모든 프로젝트를 표시하고 클릭 한 번으로 활성 프로젝트 전환
    *   프로젝트 우클릭 메뉴로 Rename/Remove 지원, 제거 시 관련 세션 취소·정리
    *   Browse로 프로젝트 폴더를 고르고, 존재하지 않는 경로는 자동 생성
    *   **멀티폴더 project**: Settings의 EXTRA FOLDERS를 Claude `--add-dir` / Codex writable_roots로 전달하여 추가 작업 루트를 함께 허용
*   **세션별 git worktree 격리**
    *   프로젝트 디렉토리 내에 세션마다 독립된 임시 `git worktree`를 생성 및 마운트하여 안전하게 코드 작업 공간을 격리
*   **CLI HISTORY**
    *   AgentManager 밖에서 열린 `claude`/`codex` 세션을 발견하고 클릭 한 번으로 가져오기·resume
    *   과거 대화 트랜스크립트를 복원하며, 대형 기록은 청크 단위 비동기 삽입과 UI 가상화로 프리징을 줄임
    *   사이드바에서 재스캔하여 외부 세션 목록을 다시 탐색
*   **Review pane (우측 변경/diff/Merge·Commit·Discard)**
    *   우측의 접이식 Review pane을 통해 세션 내 생성/수정된 파일 목록 및 인라인 Git Diff 실시간 확인
    *   실행 중인 세션의 변경 사항을 라이브로 갱신하고, 선택한 파일의 diff를 유지
    *   **Merge ▸ main**: 에이전트의 변경 사항을 메인 브랜치에 자동 커밋 및 머지
    *   **Commit only**: 변경 사항을 현재 브랜치에 커밋만 수행
    *   **Discard**: 임시 worktree의 모든 변경 사항을 폐기 (`git reset --hard` 및 `git clean`)
    *   **Diff 피드백**: 변경된 diff 내용에 대해 인라인 피드백을 적어 에이전트에게 수정 작업을 연속 지시 가능
*   **승인 broker (Claude)**
    *   에이전트가 파일 수정, 명령 실행 등 권한을 요청할 때 이를 차단하고 `Approve / Deny` 선택창 제공 (Stage 1 승인 중개)
    *   세션 옵션에서 `RequireApproval` 강제 여부 설정 및 샌드박스 모드(--sandbox 등) 선택 지원
*   **로컬 LLM 한↔영 번역 레이어**
    *   **입력 KO→EN**: 한글 입력을 영어로 번역하여 전송 (컨텍스트 토큰 누적 감소 효과)
    *   **출력 EN→KO**: 에이전트의 영어 응답 및 도구 실행 요약을 실시간 한글로 번역하여 출력
    *   **원본 보기 (ORIGINAL)**: 번역된 블록마다 원본 영어 텍스트를 즉시 토글하여 교차 검증 가능
    *   **마스킹**: 코드블록, 인라인 코드, 파일참조(`@file`) 등은 번역 시 플레이스홀더로 마스킹 처리하여 손상 방지
*   **Artifacts (태스크리스트/테스트/요약)**
    *   에이전트의 문서/태스크 쓰기(`TodoWrite`) 감지 시 태스크리스트(Todo) 자동 연동
    *   테스트 러너 실행 감지 및 pass/fail 여부 분석 표시
    *   턴 종료 시 작업 결과를 요약(Summary)하여 표시하고 세션별로 아티팩트를 보관/영속화
*   **사용자 편의성 및 디테일**
    *   **멀티턴 resume**: 이전 세션 ID를 기억하여 프로세스 재시작 시 대화를 이어서 진행
    *   **모델 선택**: 컴포저 영역에서 사용 가능한 모델 목록(Engine 옵션) 선택 및 매개변수 바인딩
    *   **이미지 첨부**: 클립보드 이미지 붙여넣기(Ctrl+V) 및 파일 선택(⊞)을 통한 이미지 첨부(base64 전송) 지원
    *   **대시보드 통계**: 세션별/전체 사용 토큰 수, 누적 사용 비용(USD), 할당량(Quota) 실시간 표시
    *   **IDE 핸드오프 (Open IDE)**: 클릭 한 번으로 활성 세션의 임시 worktree 폴더를 VS Code 등으로 바로 오픈 (미설치 시 탐색기 폴백)
    *   **상단 타이틀바 메뉴**: File/View/Help 메뉴에서 New Agent, New Project, Settings, Review 토글, About, docs 열기 지원
    *   **키보드 단축키**: `Ctrl+N` (새 에이전트), `Ctrl+R` (리뷰 패널 토글), `Escape` (새 창/폼 닫기)
    *   **본문 선택/복사**: 트랜스크립트 본문 텍스트를 직접 선택·복사하고, 에이전트 응답 헤더의 `⧉` 버튼으로 Markdown 원문 복사
    *   **창 상태 기억**: 종료 시점의 창 크기 및 위치 정보를 저장하여 재시작 시 원래대로 복원
*   **영속성**
    *   프로젝트 정보, 세션 기록, 대화 트랜스크립트 전체를 로컬 JSON 파일로 안전하게 영속화 및 자동 로드

---

## 📋 요구 사항

*   **OS**: Windows 10/11 이상
*   **SDK**: .NET 10 SDK 이상
*   **Claude CLI**: `claude` CLI가 시스템 경로에 설치 및 로그인(인증) 완료되어 있어야 합니다.
*   **Ollama**: 로컬 LLM 번역 구동을 위해 Ollama 서비스가 필요하며(localhost:11434), 번역 모델이 설치되어 있어야 합니다. (기본 권장 모델: `exaone3.5:7.8b`, 설정 패널에서 커스텀 모델 설정 가능)
*   **Codex CLI** (선택): `codex` CLI를 연동하여 사용할 경우 필요합니다.

---

## 🚀 빌드 및 실행

레포지토리를 클론한 후, PowerShell 또는 터미널에서 아래 명령을 실행합니다.

```powershell
# 프로젝트 빌드
dotnet build AgentManager.slnx

# 프로그램 실행
dotnet run --project src/AgentManager/AgentManager.csproj
```

---

## 💡 빠른 사용법

1.  **프로젝트 등록**: 사이드바 좌측 하단의 프로젝트 추가(+) 버튼을 눌러 로컬 소스코드 디렉토리를 등록합니다.
2.  **New Agent**: 등록한 프로젝트를 선택하고 좌측 상단의 `+ New Agent` 버튼을 눌러 에이전트 세션을 만듭니다. (번역 토글 TR ON 상태 확인)
3.  **대화 진행**: 입력창에 한글로 명령을 내리면 영어로 번역되어 에이전트에게 전송되며, 에이전트의 응답은 한글로 자동 표시됩니다. (필요 시 `ORIGINAL` 버튼으로 원문 교차 검증)
4.  **Review pane 확인**: 에이전트가 코드를 수정하면 우측 Review pane에 변경된 파일 목록과 diff가 표시됩니다.
5.  **Merge / Discard**: 검증 후 코드가 마음에 들면 `Merge ▸ main`을 눌러 영구 병합하고, 마음에 들지 않으면 `Discard`를 눌러 변경 사항을 버립니다.
6.  **Open IDE**: 더 자세히 코드를 보거나 디버깅하고 싶다면 `Open IDE`를 클릭하여 즉시 VS Code로 작업 공간을 전환합니다.

---

## 📁 문서 목차

`docs/` 디렉토리에 포함된 주요 설계 및 분석 문서 목록입니다:

*   [DESIGN_SPEC_KO.md](docs/DESIGN_SPEC_KO.md): 클린룸 개발을 위한 3계층 아키텍처 및 정규화 이벤트 명세, 번역 프롬프트 프레이밍 등 전체 설계 문서
*   [FEATURES_KO.md](docs/FEATURES_KO.md): 제품 기능 정의, 횡단 아키텍처 결정 정보 및 우선순위별 상세 명세 목록
*   [PROGRESS_KO.md](docs/PROGRESS_KO.md): 기능 구현 상태 및 커밋 로그를 보여주는 진행 상태 기록 파일
*   [PHASE0_CLAUDE_STREAMJSON_KO.md](docs/PHASE0_CLAUDE_STREAMJSON_KO.md): Claude Code의 stream-json 포맷의 양방향 프로토콜 실측 정보 및 정규화 이벤트 매핑표
*   [PHASE0_CODEX_EXEC_JSON_KO.md](docs/PHASE0_CODEX_EXEC_JSON_KO.md): Codex CLI의 exec --json 프로토콜 실측 및 정규화 이벤트 매핑표
*   [UI_BATCH_PROMPT.md](docs/UI_BATCH_PROMPT.md): UI 일괄 패스 구현 가이드 프롬프트
*   [UI_POLISH_PROMPT_GEMINI.md](docs/UI_POLISH_PROMPT_GEMINI.md): Gemini UI 폴리시 패스 상세 가이드 프롬프트
*   [DELEGATE_CODEX_2.md](docs/DELEGATE_CODEX_2.md): Codex 위임 작업용 명세 및 진행 상태 문서
*   [DELEGATE_GEMINI_2.md](docs/DELEGATE_GEMINI_2.md): Gemini 위임 작업용 명세 및 진행 상태 문서

---

## 🗺️ 로드맵 (예정 기능)

*   **승인 Stage 2, Antigravity CLI 어댑터, 풀 MCP 연동, 백그라운드 Scheduled Tasks** 등이 순차적으로 고도화될 예정입니다.

---

## 📄 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하십시오.
