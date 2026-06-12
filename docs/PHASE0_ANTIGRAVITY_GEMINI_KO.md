# Phase 0 실측 — Antigravity / Gemini CLI

## ⚠ 추가 실측 (2026-06-12): 신규 `agy` CLI v1.0.7 — 당분간 채택 보류
- 설치: `irm https://antigravity.google/cli/install.ps1 | iex` → `%LOCALAPPDATA%\agy\bin\agy.exe` (gemini-cli 리브랜드가 아닌 **별도 Go 바이너리**, 내부 gRPC 언어 서버 구동).
- 표면: `-p/--print`(비대화형, **순수 텍스트 출력 — JSON/이벤트 없음**), `--conversation <id>`/`--continue`(resume),
  `--dangerously-skip-permissions`, `--add-dir`(반복), `--sandbox`, `models`.
- 모델 (실측): Gemini 3.5 Flash (Low/Medium/High) · Gemini 3.1 Pro (Low/High) · **Claude Sonnet/Opus 4.6 (Thinking)** · GPT-OSS 120B — 추론 강도가 모델 표시명에 내장.
- **보류 사유**: ① 구조화 출력이 없어 도구 이벤트/usage/스트리밍/승인 가시성이 전부 사라짐(텍스트 전용 축소 어댑터가 됨).
  ② 대화형 터미널에서는 인증이 동작하지만 **다른 프로세스가 자식으로 띄우면 "not logged into Antigravity"** (토큰 전달 안 됨 — 관제 앱 치명적).
- 결정: **ag 엔진은 gemini-cli(stream-json) 경로 유지**. agy가 구조화 출력을 제공하고 자식 프로세스 인증이 풀리면 재평가.
  exe 해석기는 `agy.exe`를 자동 선택하지 않음(이름이 달라 충돌 없음).


> 2026-06-12, `@google/gemini-cli` **0.42.0** (npm 글로벌), Windows.
> Antigravity CLI 전환(6/18) 전 — 현 gemini CLI 표면으로 어댑터를 만들고,
> exe 해석을 antigravity 우선 → gemini 폴백으로 두어 전환 시 자동 승계한다.

## 1. 호출 표면 (비대화형)
```
gemini -p "<prompt>" -o stream-json --skip-trust [-y | --approval-mode <m>] [-m <model>]
       [--resume <uuid>] [--include-directories <dir>]...
```
- `-o stream-json`: stdout JSONL.
- `--skip-trust` **필수**: 없으면 신뢰 폴더 게이트로 즉시 종료(approval-mode도 default로 강제 오버라이드됨). 함정 #1.
- 승인 매핑: `--approval-mode` default(프롬프트)/auto_edit/yolo/plan. 헤드리스에는 대화형 승인 프로토콜이 없어
  default는 사실상 막힘 → Sandbox 매핑: ReadOnly→plan, WorkspaceWrite→auto_edit, DangerFullAccess→`-y`(yolo).
- `--resume <uuid>`: **세션 UUID 직접 지원** (도움말엔 latest/index만 적혀 있으나 실측으로 uuid 동작 확인 — 2턴 "47" 기억 성공, init의 session_id 동일).
- `--include-directories`: 멀티폴더 — AdditionalDirectories 그대로 매핑.
- stdin은 prompt에 덧붙는 입력 — 시작 후 닫아야 함 (codex exec과 동일).

## 2. stream-json 이벤트 (실측)
```
{"type":"init","session_id":"<uuid>","model":"gemini-3-flash-preview", ...}
{"type":"message","role":"user","content":"..."}
{"type":"tool_use","tool_name":"run_shell_command","tool_id":"...","parameters":{...}}
{"type":"tool_result","tool_id":"...","status":"success","output":"..."}        (output 없을 수 있음)
{"type":"message","role":"assistant","content":"...","delta":true}              (조각 — 누적 필요)
{"type":"result","status":"success","stats":{"total_tokens":...,"input_tokens":...,"output_tokens":...,"cached":...,"duration_ms":...,"tool_calls":...}}
{"type":"result","status":"error","error":{"type":"unknown","message":"[API Error: ...]"}}
```

## 3. NormalizedEvent 매핑
| gemini | NormalizedEvent |
|---|---|
| init | SessionStarted(session_id, model) |
| message role=assistant (delta 누적 → 경계에서 flush) | AssistantText |
| tool_use | ToolUseStarted(tool_id, tool_name, parameters) |
| tool_result | ToolResult(tool_id, output, status!=success) |
| result.stats | TokenUsage(input, output, cacheRead=cached) |
| result | TurnCompleted(IsError = status==error) (+error.message → EngineError) |
| message role=user | 무시 (에코) |

## 4. 모델 (실측)
- `gemini-3-flash-preview` (기본) ✓
- `gemini-3-pro-preview` ✓ 유효 (테스트 시점 계정 쿼터 소진 — capacity 에러로 확인)
- `gemini-2.5-flash` ✓
- 추론 강도 플래그 없음 → effort 피커 비노출.

## 5. 함정
1. `--skip-trust` 없으면 비대화형 즉시 실패 + yolo가 default로 강제 다운그레이드.
2. assistant 메시지가 `delta:true` 조각으로 옴 — 그대로 흘리면 응답이 줄 단위로 쪼개진 블록이 됨. 누적 후 경계(tool_use/result)에서 flush.
3. `update_topic` 같은 내부 도구 호출이 섞임 — 일반 도구로 표시해도 무해하나 노이즈.
4. stderr에 "YOLO mode is enabled..." 안내 출력 — 에러 아님 (UI 필터 후보).
5. exe가 npm 셸 스크립트(`gemini.ps1`/`gemini.cmd`) — Process.Start는 `.cmd` 경로로.
