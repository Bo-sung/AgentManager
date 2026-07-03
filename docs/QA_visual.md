# QA 시트 — 비주얼(GUI, computer-use)

> 대상: `feature/cc-capabilities` 브랜치 빌드의 **GUI 육안 검증**.
> 실행 주체: **standalone Claude Code 세션 + computer-use** (Sonnet 권장; Bash/PowerShell + computer-use MCP 필요).
> 헤드리스 QA(`QA_full_app.md`)에서 이관된 GUI 항목만. 코드/CLI 검증은 거기서 완료됨.
> 성격: **검증 전용.** 앱 조작은 하되 코드 수정·커밋 금지. 각 항목 `PASS/FAIL` + **관찰(스크린샷 근거)**.

## ⚠️ 환경 규칙 (엄수 — 안 지키면 상태 손상)
1. **단일 인스턴스만.** 설치본(`%LocalAppData%\AgentManager\current\agentmanager.exe`)과 브랜치 빌드가 **동시에 뜨면 `state.json` 공유로 꼬임**. 시작 전 실행 중인 AgentManager 전부 닫고, **브랜치 빌드 하나만** 띄운다.
2. **QA 세션 ≠ 피검 AM.** 이 세션 자체가 AM 안에서 도는 세션이면 안 됨(자기 자신 조작=재진입). 반드시 **외부 터미널의 standalone**.
3. 브랜치 빌드 exe는 설치본과 **경로가 다르므로**(`...\bin\Release\net10.0-windows\agentmanager.exe`), computer-use `request_access`는 **프로세스 basename `agentmanager.exe`** 로 요청(그 프로세스가 떠 있을 때만 매칭됨).

## S. 세팅 (검사 전)

| # | 항목 | 방법 | 기대 | 결과 |
|---|------|------|------|------|
| S1 | 기존 인스턴스 종료 | PowerShell `Get-Process AgentManager \| Stop-Process -Force` | 실행 중 없음 | |
| S2 | Release 빌드 | `dotnet build src/AgentManager/AgentManager.csproj -c Release` | 에러 0 | |
| S3 | 브랜치 빌드 실행 | `Start-Process "…\src\AgentManager\bin\Release\net10.0-windows\AgentManager.exe"` | 창 표시 | |
| S4 | 접근 승인 | `request_access(["agentmanager.exe"])` → 사용자 승인 | granted, `windowLocations`로 모니터 확인 | |
| S5 | 창 포착 | `screenshot`(필요시 `switch_display`) | Orchestrator 대시보드 보임 | |

## V. 검사 (스크린샷 근거 기입)

| # | 조작 | 기대 관찰 | 결과 |
|---|------|-----------|------|
| V1 | `+ New Agent` 클릭 | "Spawn an agent" 모달, **Claude Code** 런타임 선택 | |
| V2 | 모델 드롭다운 열기 | 목록에 **opus · opusplan · haiku · fable · best** 노출 | |
| V3 | `opus` 선택 → effort 드롭다운 열기 | `default,low,medium,high,xhigh,max,` **`ultracode`** 노출 | |
| V4 | 모델을 `haiku`로 변경 → effort 드롭다운 | **ultracode 사라짐**(게이팅) — `default..max`만 | |
| V5 | 다시 `opus` + effort 팝업(세션 컴포저 쪽이면 툴팁) | ultracode 주의 문구(`ultracode = xhigh + 워크플로우 · 토큰↑`) 노출 | |
| V6 | opus + ultracode로 **Launch agent** | 세션 생성, 컴포저에 `opus`·`ultracode`·`Bypass` 칩 | |
| V7 | 컴포저에 소형 워크플로우 지시 전송: *"ultracode: 두 에이전트가 alpha, beta 반환 후 'alpha beta'로 합쳐. 최소, 툴/파일 없이."* → Enter | 트랜스크립트: `RUN Workflow…done` → "워크플로우 시작됨…" → **최종 답 `alpha beta`**, **세션 완료 1회**(조기완료·중복메시지·더블완료 **없음**) | |
| V8 | 우측 **네이티브 작업자** 탭 클릭 | `workflow-subagent`(Hook · Completed) 항목 노출(에이전트 2개) | |
| V9 | 번역 확인 | 응답이 한글(번역 ON일 때), 메시지의 `ORIGINAL/원문` 토글로 영어 원문 확인 | |
| V10 | 전반 안정성 | 상기 조작 중 크래시/오류 팝업 없음, 테마 정상 렌더 | |

> V6~V8은 **opus+ultracode 실 워크플로우 = 토큰 소모**. 워크플로우는 반드시 tiny(no tools/files). 1회만.

## 정리

| 항목 | PASS/FAIL | 관찰 |
|------|-----------|------|
| V1~V5 피커/게이팅 | | |
| V6~V7 워크플로우 1턴 완료 | | |
| V8 네이티브 관측 | | |
| V9 번역 | | |
| V10 안정성 | | |

**최종:** ☐ 비주얼 OK ☐ 결함 N건
**발견:** 화면/동작 이상 (스크린샷 첨부 권장)
**종료 처리:** 검사 후 브랜치 빌드 인스턴스를 닫아도 됨(테스트 세션은 state에 남으므로 정리 원하면 삭제).
