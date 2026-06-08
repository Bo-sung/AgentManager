# Phase 0 스파이크 — Claude `stream-json` 이벤트 매핑 (실측)

> 목적: Claude Code CLI의 출력 스키마를 **정규화 이벤트**로 매핑(언어 무관 설계도).
> 캡처 모델: `claude-sonnet-4-6` / 2026-06.

## 캡처 방법
```
claude -p "<prompt>" --output-format stream-json --verbose --dangerously-skip-permissions
```
출력은 **JSONL**(한 줄 = JSON 1개). 한 번의 1-툴 작업으로 관측한 이벤트 시퀀스:

| # | type | 내용 |
|---|---|---|
| 0 | `system` (subtype `init`) | 세션 시작: session_id, tools[], model, cwd, permissionMode … |
| 1 | `rate_limit_event` | **할당량 정보** (status, utilization, resetsAt, rateLimitType) |
| 2 | `assistant` | content=[`thinking`] |
| 3 | `assistant` | content=[`tool_use`: Bash] |
| 4 | `user` | content=[`tool_result`] + top-level `tool_use_result`(stdout/stderr) |
| 5 | `assistant` | content=[`text`] (최종 답변) |
| 6 | `system` (subtype `post_turn_summary`) | 턴 요약/상태 (status_category, needs_action) |
| 7 | `result` (subtype `success`) | 종료: usage, total_cost_usd, num_turns, stop_reason, duration_ms |

## 정확한 필드 (관측값)
```jsonc
// system/init
{ type:"system", subtype:"init", session_id, model, cwd, permissionMode,
  tools:[…37개], mcp_servers, agents, skills, plugins, slash_commands, apiKeySource }

// rate_limit_event  ← 매니저 할당량 대시보드 핵심
{ type:"rate_limit_event", session_id,
  rate_limit_info:{ status:"allowed_warning", resetsAt:<unixSec>,
                    rateLimitType:"seven_day", utilization:0.76, surpassedThreshold:0.75 } }

// assistant content blocks
{ type:"thinking", thinking:"…", signature:"…" }
{ type:"tool_use", id:"toolu_…", name:"Bash", input:{…}, caller:{type:"direct"} }
{ type:"text", text:"…" }
// assistant.message.usage
{ input_tokens, cache_creation_input_tokens, cache_read_input_tokens,
  cache_creation:{ ephemeral_5m_input_tokens, ephemeral_1h_input_tokens }, output_tokens }
// 공통: parent_tool_use_id (서브에이전트 식별!), session_id, uuid, request_id

// user / tool_result
{ type:"tool_result", tool_use_id:"toolu_…", content:"…", is_error:false }
// + top-level
tool_use_result:{ stdout, stderr, interrupted, isImage, noOutputExpected }

// result
{ type:"result", subtype:"success", is_error, num_turns, stop_reason, result:"<final text>",
  duration_ms, ttft_ms, total_cost_usd,
  usage:{ input_tokens, cache_creation_input_tokens, cache_read_input_tokens, output_tokens, … },
  modelUsage:{…}, permission_denials:[] }
```

## ★ 매핑표: Claude 이벤트 → 정규화 이벤트
| Claude | 조건 | 정규화 이벤트 | 비고 |
|---|---|---|---|
| `system`/`init` | — | `SessionStarted(sessionId, tools, model, cwd)` | |
| `rate_limit_event` | — | **`QuotaUpdate(utilization, resetsAt, type, status)`** | 신규 — 매니저 대시보드용 |
| `assistant` `text` | text.trim() | `AssistantText(text)` | **번역 EN→KO 대상** |
| `assistant` `thinking` | thinking.trim() | `Thinking(text)` | signature는 무시 |
| `assistant` `tool_use` | — | `ToolUseStarted(id, name, input)` | `caller`/`parent_tool_use_id`로 직접/서브 구분 |
| `user` `tool_result` | — | `ToolResult(toolUseId, content, isError, stdout/stderr)` | **서브에이전트(Task) 결과만 번역** |
| `assistant.message.usage` | 존재 시 | `TokenUsage(in, out, cacheRead, cacheCreated)` | 실시간 누적 |
| `system`/`post_turn_summary` | — | (내부 상태) 또는 무시 | needs_action 참고 |
| `result` | — | `TurnCompleted(result, usage, cost, numTurns, stopReason)` | stdin 종료 트리거 |
| (control_request) | 권한 모드일 때 | `PermissionRequest(id, payload)` | 본 캡처는 bypass라 미관측 → 별도 캡처 필요 |

## ★ 핵심 발견 (설계 반영 사항)
1. **`parent_tool_use_id`로 서브에이전트 정확 식별** — 익스텐션의 "직전 tool_use 추정" 휴리스틱보다 정확.
   서브에이전트(Task) 산하 콘텐츠는 `parent_tool_use_id != null`. **"Task 결과만 번역" 판정을 이걸로** 하면 견고.
2. **`tool_use.caller`** (`{type:"direct"}`) — 직접 호출 vs 에이전트 호출 구분 가능성. 서브에이전트 캡처로 확인 필요.
3. **`rate_limit_event` = 할당량 대시보드 금광** — `utilization`(0.76=76%), `resetsAt`, `rateLimitType`(seven_day) 실시간 제공.
   매니저에 **`QuotaUpdate` 정규화 이벤트 추가**하여 토큰/할당량 패널에 노출 권장.
4. **새 이벤트 `rate_limit_event`, `post_turn_summary`** — 구 파서가 모르는 타입. 어댑터는 **미지 타입을 안전하게 무시**해야 함(전방호환).
5. **usage 캐시 필드 분리** — `cache_creation_input_tokens` / `cache_read_input_tokens` + 5m/1h ephemeral. 토큰 분석 시 캐시 분리 표기.
6. **권한 흐름 미관측** — bypassPermissions로 캡처해서 `control_request`/`control_response`가 안 나옴.
   → **별도 스파이크 필요**: `--permission-prompt-tool stdio`(non-bypass)로 권한 왕복 캡처.

## 미완 / 다음
- [ ] 권한 왕복(`control_request`/응답) 캡처 (non-bypass)
- [ ] 서브에이전트(Task) 실제 호출해 `parent_tool_use_id`/`caller` 값 확인
- [ ] **Codex 측 동일 캡처** (`codex exec` 설치 후) → 같은 매핑표 작성
- [ ] 두 매핑표 통합 → `IAgentAdapter.ParseChunk` 구현 기준 확정
