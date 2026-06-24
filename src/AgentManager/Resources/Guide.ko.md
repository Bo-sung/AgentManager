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

## 번역 엔진 (Ollama)
한국어↔영어 번역은 로컬 LLM **Ollama**로 동작합니다. 설치·실행돼 있어야 번역 ON이 가능합니다.
- **설치**: [ollama.com/download ↗](https://ollama.com/download)
- **번역 모델 받기**: 터미널에서 `ollama pull exaone3.5:7.8b` (권장) — 또는 원하는 모델
- **실행**: Ollama 앱 실행 또는 `ollama serve` (설정 → 번역·언어의 **[실행]** 버튼으로도 가능)
- **연결**: 설정 → 번역·언어 → 엔드포인트(`http://localhost:11434`) 확인 → **설치 모델 조회**로 모델 선택
- Ollama가 꺼져 있거나 미설치면 **번역 ON이 비활성화**되고, 입력/출력이 번역 없이 그대로 전달됩니다.

## 참고
- **번역**: 로컬 LLM(Ollama) 번역은 선택 기능입니다. 설정 → 번역·언어에서 엔드포인트·모델을 지정하세요.
- **사용량**: 표시되는 사용량은 공식 수치가 아닌 대략적 추정치입니다. 정확한 잔여량은 각 서비스 공식 페이지에서 확인하세요.
