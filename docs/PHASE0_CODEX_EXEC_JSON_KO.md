# Phase 0 스파이크 — Codex `exec --json` 이벤트 매핑 (실측)

> 목적: Codex CLI 출력 스키마를 **정규화 이벤트**로 매핑.
> 캡처: `codex-cli 0.137.0-alpha.4` (ChatGPT 확장 번들 바이너리) / 2026-06.

## 실행 파일 위치 (이 머신)
```
C:\Users\sbss0\.vscode\extensions\openai.chatgpt-<ver>-win32-x64\bin\windows-x86_64\codex.exe
```
- 사용자의 "Codex" = **OpenAI ChatGPT VS Code 확장**에 번들된 codex CLI. 직접 spawn 가능.
- 인증: `CODEX_HOME`(`~/.codex`) 공유 → 확장의 ChatGPT 로그인 재사용됨(별도 로그인 불필요).

## 캡처 방법 + ★중요 함정
```
codex exec --json --dangerously-bypass-approvals-and-sandbox -C <dir> "<prompt>"
```
- **★ stdin을 반드시 닫아야 함(EOF)**. 프롬프트를 인자로 줘도, stdin이 파이프로 열려 있으면
  `"Reading additional input from stdin..."` 상태로 **무한 대기**한다.
  → 어댑터 spawn 시 **stdin 즉시 종료**(또는 입력 다 보낸 뒤 end) 필수.
- 출력은 **JSONL**(한 줄 = JSON 1개).

## 관측 이벤트 시퀀스
| # | type | 내용 |
|---|---|---|
| 0 | `thread.started` | `{ thread_id }` — 세션 시작 |
| 1 | `turn.started` | `{}` — 턴 시작 |
| 2 | `item.started` | `item:{ id, type:"command_execution", command, status:"in_progress" }` |
| 3 | `item.completed` | `item:{ id, type:"command_execution", aggregated_output, exit_code:0, status:"completed" }` |
| 4 | `item.completed` | `item:{ id, type:"agent_message", text }` — 어시스턴트 답변 |
| 5 | `turn.completed` | `usage:{ input_tokens, cached_input_tokens, output_tokens, reasoning_output_tokens }` |

### item 타입 (관측)
- **`command_execution`**: `{ id, type, command, aggregated_output, exit_code, status }`
  → Claude와 달리 **툴 호출+결과가 한 item에 통합**(started=명령, completed=출력/exit_code).
- **`agent_message`**: `{ id, type, text }` → 최종 텍스트. (이번엔 `item.completed`만 관측; started 없음)
- (미관측) reasoning/파일변경(patch)/MCP 등 다른 item 타입은 작업에 따라 등장 예상.

## ★ 매핑표: Codex 이벤트 → 정규화 이벤트
| Codex | 정규화 이벤트 | 비고 |
|---|---|---|
| `thread.started` | `SessionStarted(sessionId = thread_id)` | |
| `turn.started` | (턴 경계, 내부) | |
| `item.started` (`command_execution`) | `ToolUseStarted(id, name="shell", input=command)` | |
| `item.completed` (`command_execution`) | `ToolResult(id, content=aggregated_output, isError = exit_code!=0)` | 툴+결과 통합 |
| `item.completed` (`agent_message`) | `AssistantText(text)` | **번역 EN→KO 대상** |
| `turn.completed` | `TokenUsage(in, out, cached, reasoning)` + `TurnCompleted(usage)` | |
| (approval) | `PermissionRequest(...)` | **미관측 → 별도 스파이크** |

## ★ Claude vs Codex — 어댑터 설계 차이
| 항목 | Claude (stream-json) | Codex (exec --json) |
|---|---|---|
| 세션 시작 | `system/init` (session_id) | `thread.started` (thread_id) |
| 어시스턴트 텍스트 | `assistant` content `text` 블록 | `item.completed` `agent_message.text` |
| 사고(thinking) | `assistant` `thinking` 블록 | (별도 item 미관측; usage의 reasoning_output_tokens) |
| 툴 호출/결과 | **분리**: `tool_use`(assistant) + `tool_result`(user) | **통합**: `command_execution` item (started/completed) |
| 서브에이전트 식별 | `parent_tool_use_id` | (미확인 — item 계층/parent 필드 확인 필요) |
| 토큰 usage | assistant.usage + result.usage (캐시 5m/1h 분리) | `turn.completed.usage` (cached/reasoning 포함) |
| 종료 | `result` | `turn.completed` |
| 할당량 | `rate_limit_event` (utilization 등) | (미관측 — exec --json엔 없을 수 있음) |
| stdin | 양방향 stream-json(권한 왕복) | exec는 단방향 + **stdin EOF 필수** |
| 권한/승인 | `control_request can_use_tool` ↔ `control_response` | `-s/--sandbox` 모드 + 승인(프로토콜 미관측) |
| 입력 방식 | stdin에 user-message JSON | 프롬프트 인자 또는 stdin, `-i` 이미지 |
| 세션 재개 | `--resume <id>` | `exec resume` / `--last` |

→ **정규화 이벤트 계층이 두 엔진 차이를 흡수**한다. 어댑터는 각자 다른 스키마를 같은 이벤트로 변환.
→ 공통 원칙: **미지 type/item은 안전 무시(전방호환)**, **번역은 AssistantText/서브에이전트 ToolResult에만**.

## 미완 / 다음
- [ ] **Codex 승인(approval) 흐름** 캡처 — 비-bypass + sandbox에서 승인 요청이 어떤 이벤트로 오는지
      (또는 `app-server`/`mcp-server` 프로토콜이 양방향 권한에 더 적합한지 검토)
- [ ] 서브에이전트/파일변경(patch)/reasoning item 타입 확인
- [ ] `app-server`(experimental) vs `exec --json` 중 매니저에 적합한 모드 결정
      (멀티턴·권한·중단이 필요하면 app-server, 단발 작업이면 exec)
- [ ] 통합 → `IAgentAdapter.ParseChunk` 구현 기준 확정 (Claude/Codex 둘 다)
