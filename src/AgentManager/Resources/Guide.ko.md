# 엔진 설치 & 세팅 가이드

각 코딩 에이전트 CLI를 설치하고 AgentManager에 연결하는 방법입니다. 설치는 공식 문서를 따른 뒤, **설정 → 런타임**에서 연결하세요.

## 연결 공통 절차
1. **설정 → 런타임**에서 해당 엔진 카드를 엽니다.
2. **[탐지]** 버튼으로 CLI 경로를 자동 인식합니다 (또는 경로를 직접 입력).
3. **[로그인]** 으로 구독/계정 인증 — 또는 **API 키** 모드로 키를 입력합니다.
4. **기본 모델**을 선택합니다.

## Claude Code
- **설치**: `npm i -g @anthropic-ai/claude-code`
- **공식 가이드**: [docs.claude.com ↗](https://docs.claude.com/en/docs/claude-code/overview)
- **연결**: 런타임 → Claude Code → 탐지/경로 → 로그인(구독) 또는 API 키 → 모델

## Codex
- **설치**: `npm i -g @openai/codex` (독립 설치 권장) — 또는 VS Code `openai.chatgpt` 확장
- **공식 가이드**: [github.com/openai/codex ↗](https://github.com/openai/codex)
- **연결**: 런타임 → Codex → 탐지(독립 설치 우선) → 로그인 또는 API 키 → 모델

## Antigravity
- **설치**: Antigravity 설치 후 `agy` CLI 사용
- **공식 가이드**: [antigravity.google ↗](https://antigravity.google)
- **연결**: 런타임 → Antigravity → 탐지 → 로그인 → 모델(default)
- 현재 **무료 프리뷰** — 사용량(쿼터) 정보는 제공되지 않습니다.

## 참고
- **번역**: 로컬 LLM(Ollama) 번역은 선택 기능입니다. 설정 → 번역·언어에서 엔드포인트·모델을 지정하세요.
- **사용량**: 표시되는 사용량은 공식 수치가 아닌 대략적 추정치입니다. 정확한 잔여량은 각 서비스 공식 페이지에서 확인하세요.
