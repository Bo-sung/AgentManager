# Settings 백엔드 작업 계획 (원본에만 있는 설정)

> **진행 상태 (업데이트):** 아래 항목 대부분이 구현 완료됨.
> - ✅ Permissions 전역 승인 정책(ask/safe/yolo) — 새 세션 시드 + 영속
> - ✅ Orchestration: worktree 기준 경로 / 시작 시 마지막 세션 / 목록 실시간 활동 표시
> - ✅ Per-engine 기본 모델 (Runtimes 카드 모델 피커)
> - ✅ Appearance: 강조색 라이브 적용(5 프리셋) / 밀도(UI 스케일) / 텔레메트리(로컬 opt-in)
> - ⏳ 남음: **CLI 로그인 트리거**(엔진별 자체 로그인 플로우 기동) — 현재는 "CLI 관리" 안내로 정직 표시.
> - ⏳ 남음: auto-start의 "running 에이전트 재개"는 안전상 "마지막 세션 열기"로 구현(재실행 X).


> Phase 4에서 Settings를 풀 중앙 페인(TOC + 카드)으로 재배치하면서, 원본
> (`design/am-settings.jsx`)에는 있지만 현재 백엔드가 없는 항목들은 UI 셸 +
> `PLANNED` 배지로 정직하게 표시해 두었다(가짜 위젯 금지). 각 항목의 실제 구현 계획.

## 01 Runtimes
- **Per-engine 기본 모델** — 원본은 런타임별 Default model 피커 보유. 현재 모델 목록은
  `EngineRegistry`에 있으나 "기본값"을 영속하지 않음.
  - 백엔드: `AppSettingsDto`에 `Dictionary<string,string> DefaultModels`(engineId→model) 추가
    → `CreateSession`/NewAgent 기본값으로 사용. Settings에 ComboBox(=EngineRegistry.Models) 바인딩.
- **Authentication (Subscription / API key) + 계정 연동** — 원본은 OAuth 구독연동 + usage 바.
  공개 API 없음(각 CLI가 자체 인증 관리). 현실 구현:
  - 엔진별 **"CLI 로그인"** 버튼 → 해당 CLI의 로그인 플로우를 터미널/프로세스로 기동
    (`claude` 로그인, `codex login`, `gemini`, `agy`).
  - 인증 상태/플랜은 CLI가 노출하는 범위에서만 읽어 표시. usage 바는 토큰 누적(이미 보유)으로 대체 가능.

## 02 Translation (이미 동작)
- 현재만 있는 섹션. 유지(Ollama endpoint/model/기본 번역 토글).

## 03 Orchestration
- **Auto-start on launch** — 앱 시작 시 직전 실행 세션 재개.
  - 백엔드: `AppSettingsDto.AutoStart` + App 초기화에서 running 상태였던 세션 resume.
- **Stream activity logs** — 사이드바/카드의 라이브 tool·토큰 표시 on/off.
  - 백엔드: `AppSettingsDto.StreamLogs` + 사이드바 activity 라인/Orchestrator 카드 갱신 게이트.
- **Worktree base directory** — 격리 worktree 기준 경로.
  - 백엔드: `AppSettingsDto.WorktreeBase` + `GitWorktree` 경로 생성에 반영.

## 04 Permissions (전역 기본값)
- 세션별 제어(RequireApproval/Sandbox)는 컴포저에서 **이미 동작**. 전역 기본값만 미구현.
- **Approval policy** (ask / auto-safe / auto-all), **allow destructive**, **network access**,
  **sandboxed filesystem**.
  - 백엔드: `AppSettingsDto`에 4개 필드 추가 → 새 세션 spawn 시 기본값으로 주입.
    auto-all은 위험 → 경고 색(이미 디자인에 warn 세그) 유지.

## 05 Appearance
- 현재 동작: **테마(light/dark)**, **언어(KO/EN)** — 재시작 적용(리소스 스왑).
- **Accent color** — 원본은 라이브 스와치. 현재 색은 `StaticResource`(재시작 적용).
  - 백엔드: 강조색 계열 브러시를 런타임 오버라이드 가능하게(앱 리소스 딕셔너리 키 교체 또는
    `DynamicResource` 전환) → 즉시 적용 + 영속(`AppSettingsDto.Accent`).
- **Density** (compact/comfortable) — 간격 스케일.
  - 백엔드: 간격/패딩을 리소스화한 스케일 토큰 도입 후 토글.
- **Anonymous telemetry** — 로컬 우선 제품이므로 의미상 로컬 opt-in 로그 토글.
  - 백엔드: `AppSettingsDto.Telemetry`(기본 off) + 로컬 로그 기록 여부만 제어. 외부 전송 없음.

## 06 Project (이미 동작)
- 현재만 있는 섹션. 유지(MCP config 경로, 추가 폴더).

---
### 우선순위 제안
1. **Permissions 전역 기본값** + **Orchestration(worktree base/auto-start)** — 실사용 영향 큼, 백엔드 단순.
2. **Per-engine 기본 모델** — 작고 유용.
3. **Accent live / Density / Telemetry** — 외관, 리소스 구조 변경 필요.
4. **CLI 로그인 트리거** — 엔진별 프로세스 기동 검증 필요.
