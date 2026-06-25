# Changelog

AgentManager 버전별 변경 사항. (최신순) · 버전은 `vX.Y.Z` 태그와 1:1 대응.

## 1.8.0
4번째 엔진 **Pi(pi.dev)** 추가 + 엔진별 모델 큐레이션 + 테마 확장.
- **Pi 엔진**: pi.dev를 thin-proxy(RPC 모드)로 통합 — 멀티 provider(Anthropic/OpenAI/Google/zai 등) 하나의 엔진으로 사용. 공식 로고/색, RPC 이벤트→정규화 매핑(thinking/text/tool/usage), resume. provider·인증은 pi가 자체 관리(`~/.pi`), 앱은 호출+표시만.
- **동적 모델 카탈로그**: `pi --list-models`로 실모델 목록 조회(설정 "조회") + 연동 provider 표시. (실측: docs/PHASE0_PI_RPC_KO.md)
- **"주로 쓰는 모델" 체크리스트**(전 엔진, 접이식): 체크한 모델만 New Agent 피커·설정 드롭다운에 노출. 선택은 settings.json에 영속.
- **테마**: Claude Dark · Codex Light · Antigravity Light 추가(총 13종). 엔진 식별색을 **테마·강조색과 완전 독립**으로 고정(아이콘·텍스트·컴포저 외곽·알약). 타이틀바/메뉴바가 테마를 추종(라이트에서 안 보이던 문제 fix). agy 컴포저 입력 하이라이트 무지개(Google 4색). 라이트 3종 텍스트 대비 강화.
- 내부 리팩터: 엔진 어댑터 공통화(StdioJsonAdapter·AdapterJson), Shell.Open, JsonFile 스토어 IO(동작무변, Smoke 검증).

## 1.7.2
- **설정 런타임 카드**에도 공식 엔진 아이콘 적용(CC/GX/AG 텍스트 배지 → 로고).
- **엔진 아이콘 색 고정**: 강조색/테마를 바꿔도 각 모델 아이콘 색 유지(Claude 주황·Codex 보라). 이전엔 Claude가 강조색에 묶여 같이 변하던 버그 수정.
- **Antigravity 아이콘** 공식 CI 무지개 그라데이션(파랑→초록→빨강→주황)으로 변경.

## 1.7.1
- **공식 엔진 아이콘**: Claude(공식 심볼) · Codex(OpenAI 마크) · Antigravity(공식 "A" 마크의 단색 재현) 적용.
- README "로드맵" 섹션 → "변경 이력"(CHANGELOG 요약/링크)으로 교체.

## 1.7.0
엔진 가용성("사용 불가") 처리 + 번역 Ollama 연동 강화.
- **엔진 설치 게이팅**: New Agent 피커에서 미설치 엔진은 회색 + "미설치" 배지 + 선택 불가, 옆에 공식 설치 가이드 링크.
- **설치 & 세팅 가이드 모달**: 설정 → 런타임 "가이드" 버튼 → Markdown 렌더(테마 매칭). 엔진별 설치·연결 + Ollama 안내.
- **한도 소진 처리**: 구독 한도 도달 시 (opt-in) 저장된 API 키로 자동 전환, 끄면 해당 엔진 회색("한도 초과"). 판정 = 사용량 100% 또는 실제 rate-limit 실패.
- **번역 Ollama 연동**: 번역 ON은 Ollama 실행 중일 때만 적용(실행 시 핑 게이팅). 설정에 Ollama 상태(실행/꺼짐/미설치) + [실행](`ollama serve`)/[설치 가이드]. 꺼짐 시 번역 토글 옆 ⚠(클릭→설정) + 켜기 시도는 OFF로 되돌림.
- **번역 토글 이동**: 세션 헤더 → 컴포저(모델/effort 옆), New Agent 폼에도 번역 토글 추가.
- **CLI 세션 삭제 영속**: 삭제한 CLI 기록이 재시작 후 재발견으로 되살아나지 않음(dismiss 셋).
- Fixes: 중복 문자열 키 기동 크래시, ⚠ 아이콘 렌더, Ollama `localhost`→IPv4 탐지, editable ComboBox 선택(드롭다운).

## 1.6.0
사용량 표시 개선.
- 사용량 체크를 **엔진별**로 표시(활성 1개 → 전 엔진), **퍼센트 막대**(Claude 세션/주간, Codex 사용%, Ok/Warn/Err 색).
- "공식 수치가 아닌 대략적 추정치" 안내 문구. Antigravity는 무료 프리뷰(N/A).

## 1.5.1
- 사용량 체크 크래시 가드 — 전역 예외 핸들러 + 자식 CLI 오류 대화상자(WER) 억제.

## 1.5.0
- 브랜드 테마 3종(Claude · Codex · Antigravity) + 커스텀 강조색(hex).
- 엔진 경로 수동 설정 + 탐지 버튼(독립 설치 우선, Codex npm 우선).
- 번역·언어 설정 통합 + 번역 모델 드롭다운/설치 모델 조회.
- 창 최대화 상태 복원 fix.

## 1.4.0
- UI 줌(Ctrl+휠) — 본문/모달 독립 배율, 활성 영역만 조정.

## 1.3.0
- 워커 위임(메인↔워커 핸드오프) — 지속 풀 · 위임 모달 · 보고 수신함 · 일괄 fan-out · 크로스 엔진.

## 1.2.0
- 언어 설정 드롭다운화 + 설정 가능한 번역 언어 쌍(번역 전/후, 11개 언어).

## 1.1.0
- IDE 테마 프리셋 + 라이브 전환, VS Code식 settings.json(분리·라이브 리로드), 엔진 명칭/설정 재편, API 키 인증(DPAPI).

## 1.0.0
- 첫 릴리즈: 멀티 에이전트(Claude Code · Codex · Antigravity) 구동 · worktree 격리 · Review pane · 승인 broker · 로컬 LLM 번역 · 트랜스크립트 영속.
