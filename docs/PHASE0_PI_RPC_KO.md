# Phase 0 실측 — pi (pi.dev) RPC 모드

> 2026-06-25, pi **0.74.2** (`@earendil-works/pi-coding-agent`, npm 전역), Windows.
> 측정: `node dist/cli.js --mode rpc --no-session` 에 stdin으로 prompt 1줄 주입 → stdout 이벤트 캡처(라이브 PASS, envelope·에러경로 검증).
> 권위 스펙: 패키지 동봉 `docs/rpc.md` + 타입 `dist/modes/rpc/rpc-types.d.ts` (버전 일치). 성공 경로(text 스트리밍)는 동봉 문서 기준.

## 1. 구동 / 프레이밍
- 진입점: **`node <pkg>/dist/cli.js --mode rpc`** — pi는 네이티브 exe가 아니라 **node 스크립트**다. (cc/gx는 exe, pi는 node 필요: engines `node>=20.6`.)
  - 설치 경로(실측): `%APPDATA%\npm\node_modules\@earendil-works\pi-coding-agent\dist\cli.js`. 전역 npm 패턴은 gx와 유사.
- cwd: 프로세스 `WorkingDirectory`로 지정(pi에 `-C` 류 플래그 없음 — cwd에서 동작).
- 프레이밍: **엄격 JSONL, LF(`\n`) 단일 구분자.** 입력의 `\r`은 strip. (우리 C# 라인 리더는 `\n` 분할이라 호환.)
- stdin: 명령(JSON 1줄). stdout: 응답(`type:"response"`) + 이벤트(JSONL). stderr: 로그(분리 필수 — 안 비우면 파이프 데드락).

## 2. 핸드셰이크 없음 (실측)
codex app-server와 달리 **초기화 왕복이 없다.** spawn 직후 바로 prompt를 보낸다:
```
--> {"type":"prompt","message":"Reply with exactly: OK"}
<-- {"type":"response","command":"prompt","success":true}
<-- {"type":"agent_start"}
<-- {"type":"turn_start"}
<-- {"type":"message_start","message":{"role":"user","content":[{"type":"text","text":"..."}],"timestamp":...}}
<-- {"type":"message_end","message":{...user...}}
<-- {"type":"message_start","message":{"role":"assistant","content":[...],"provider":"anthropic","model":"claude-opus-4-7","usage":{...},"stopReason":"...","timestamp":...}}
   ... (성공 시 message_update/text_delta 스트리밍) ...
<-- {"type":"message_end","message":{...assistant...}}
<-- {"type":"turn_end","message":{...assistant...},"toolResults":[...]}
<-- {"type":"agent_end","messages":[...]}
```
- `prompt`에 `images?: [{type:"image",data:"<base64>",mimeType:"image/png"}]` 지원.
- **stdin을 agent_end까지 열어둬야 한다** — 닫으면 비동기 턴 완료 전에 프로세스가 종료된다(실측: `< file`로 즉시 EOF 주면 user 메시지 echo까지만 나오고 종료).

## 3. 우리가 쓸 명령 (stdin)
| 명령 | 용도 |
|---|---|
| `{"type":"prompt","message":...,"images":[...]}` | 턴 시작 |
| `{"type":"abort"}` | 현재 작업 중단 |
| `{"type":"set_model","provider":...,"modelId":...}` | 모델 전환(또는 spawn 시 `--model`) |
| `{"type":"set_thinking_level","level":"off|minimal|low|medium|high|xhigh"}` | 추론 강도 |
| `{"type":"get_state"}` | `sessionId`/`sessionFile`/모델/스트리밍 상태 조회 |
| `{"type":"get_session_stats"}` | 토큰/cost/contextUsage 집계 |
- 모든 명령에 선택적 `id` → 같은 `id`로 `response` 회신(상관관계).

## 4. 이벤트 (stdout) → NormalizedEvent 매핑(안)
| pi 이벤트 | NormalizedEvent |
|---|---|
| `agent_start` / `turn_start` | (내부 경계, 무시 가능) |
| `message_update` + `assistantMessageEvent.type=text_delta` | **AssistantDelta**(delta) |
| `message_update` + `thinking_delta` | **Thinking** |
| `message_update` + `toolcall_end`(`toolCall`) / `tool_execution_start` | **ToolUseStarted**(toolCallId, toolName, args) |
| `tool_execution_end` | **ToolResult**(toolCallId, content, isError) |
| `message_end`(assistant) | **AssistantText**(최종 텍스트) + **TokenUsage**(usage) |
| `agent_end` | **TurnCompleted** |
| assistant `stopReason:"error"` + `errorMessage` (또는 `response{success:false}`, `extension_error`) | **EngineError** + TurnCompleted(isError) |
| `auto_retry_*`, `compaction_*` | (선택: 상태 표시) |

`assistantMessageEvent` 델타 타입: `start|text_start|text_delta|text_end|thinking_start|thinking_delta|thinking_end|toolcall_start|toolcall_delta|toolcall_end|done|error`.

### 메시지/usage 형태 (라이브 실측)
```json
{"role":"assistant","content":[{"type":"text","text":"..."},{"type":"thinking","thinking":"..."},{"type":"toolCall","id":"call_..","name":"bash","arguments":{...}}],
 "provider":"anthropic","model":"claude-opus-4-7","api":"anthropic-messages",
 "usage":{"input":N,"output":N,"cacheRead":N,"cacheWrite":N,"totalTokens":N,"cost":{"input":..,"output":..,"total":..}},
 "stopReason":"stop|length|toolUse|error|aborted","timestamp":...,"errorMessage":"...(에러 시)"}
```
- `ToolResultMessage`: `{role:"toolResult",toolCallId,toolName,content:[{type:"text",text}],isError,timestamp}`.
- TokenUsage 매핑: input→Input, output→Output, cacheRead→CacheRead, cacheWrite→(Claude식 4번째 슬롯). cost는 별도.

## 5. 인증 / provider / model (실측)
- `--api-key`는 **기본값이 env vars**. provider별 env: `ANTHROPIC_API_KEY`, `ANTHROPIC_OAUTH_TOKEN`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, `OPENROUTER_API_KEY`, `GROQ_API_KEY`, `DEEPSEEK_API_KEY`, `MISTRAL_API_KEY`, `XAI_API_KEY`, `AZURE_OPENAI_*`, … (전체는 `pi --help`).
  → **cc/gx식 "DPAPI 저장 → ExtraEnvironment 주입"이 그대로 먹힌다.** 다만 멀티 provider라 키는 provider 종속.
- OAuth/구독: pi가 `/login`으로 자체 관리(앱은 상태만) — 결정 문서대로.
- **model 기본값은 `~/.pi` 설정에서 온다**(실측: default가 `anthropic/claude-opus-4-7`로 잡힘). `--provider google`만 줘도 모델은 안 바뀜 → **adapter는 `--model "provider/id[:thinking]"`로 명시 전달해야 결정적.**

## 6. 함정 (실측)
1. **에러도 구조화 이벤트**로 온다: 실패 시 assistant 메시지 `stopReason:"error"` + `errorMessage`(예: `400 ... "You're out of extra usage..."`)가 오고, **turn_end/agent_end는 정상 발생**. → EngineError 후 TurnCompleted(isError)로 매핑, 턴은 정상 종료 처리.
2. **stdin 조기 종료 금지**: agent_end 전에 stdin 닫으면 프로세스가 턴 미완료로 종료. → C# 어댑터: `CloseStdinAfterStart=false`로 stdin 유지, `agent_end` 수신 후 종료(`KillAfterTurnCompleted=true`).
3. **stdin 인코딩**: BOM 없는 UTF-8 + LF. (PowerShell 파이프가 BOM을 붙여 parse 실패한 사례 — 어댑터는 `Utf8NoBom`으로 쓰면 됨, cc/gx와 동일.)
4. **spawn = node 스크립트**: FileName=`node`, args=`[cli.js, --mode, rpc, ...]`. node가 PATH에 있어야 함(pi 설치 전제).
5. `--provider`만으론 `~/.pi`의 default 모델을 못 덮는다(위 5항).

## 7. 통합 설계 메모 (PiAdapter)
- 베이스: **`StdioJsonAdapter`** (JSONL). `ParseRoot`에서 위 이벤트 매핑.
- `BuildStartInfo`: `AdapterJson.NewStdioStartInfo("node", cwd)` + args `cli.js --mode rpc [--model provider/id[:thinking]] [--session <id>] [--no-session?]`. cli.js 경로는 EngineRegistry에서 npm 전역 탐지.
- `CloseStdinAfterStart=false`, `KillAfterTurnCompleted=true`.
- `InitialStdinLines`: prompt 명령 1줄(`{"type":"prompt",...,"images":[...]}`). 필요 시 set_thinking_level/set_model 선행.
- **세션 id 확보**: pi는 별도 session-started 이벤트가 없다. resume용 id가 필요하면 spawn 후 `get_state`를 stdin으로 보내고 응답의 `sessionId`를 읽어 `SessionStarted` emit — app-server처럼 "파싱 중 stdin writeback"(EngineWriteback) 수단 사용. 또는 `--session-dir`로 우리가 관리.
- 승인: `extension_ui_request`(confirm/select) ↔ `extension_ui_response`. **v1은 Capabilities.Permissions=false**, dialog 요청은 자동 cancel/기본값 처리.
- Capabilities(실측 확정): Thinking ✅ · Sessions ✅ · Images ✅ · TokenUsage ✅ · Permissions ❌(v1) · Quota ❌(native 없음; cost는 get_session_stats).

> 참고: 라이브 검증 시 사용한 Anthropic 키가 크레딧 소진("out of extra usage") 상태라 성공 텍스트 턴은 실측 못 했다. envelope·에러경로·메시지/usage 형태는 라이브 PASS, 성공 경로 델타는 동봉 `docs/rpc.md` 기준(버전 일치라 신뢰).
