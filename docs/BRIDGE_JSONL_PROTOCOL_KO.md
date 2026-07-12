# AgentManager Bridge JSONL 프로토콜 (v1)

`adapterKind: "agentmanager-bridge-jsonl"` — 서드파티 CLI가 AgentManager에
**도구 호출 / thinking / 토큰 사용량**까지 온전히 노출할 수 있는, 줄 단위 JSON(JSONL)
전송 규격이다. 단순한 `one-shot-text`(모든 stdout을 평문 델타로 흘리고 프로세스 종료로 완료를
추정)보다 풍부하다.

- 구현 어댑터: `src/AgentManager.Core/Agents/BridgeJsonlAdapter.cs`
- 팩토리 등록: `AdapterFactory.CreateCustom` — `agentmanager-bridge-jsonl`(정식) 또는
  `bridge-jsonl`(별칭) 둘 다 허용
- 프로토콜 버전: **1**

---

## 1. 전송(Transport)

- AgentManager가 매니페스트의 `launch.exe` + `launch.args`로 자식 프로세스를 **턴 작업
  디렉터리(cwd)** 에서 실행한다.
- 인코딩은 **BOM 없는 UTF-8**. CLI는 **stdout에 한 줄당 JSON 객체 하나**를 출력한다.
- 각 줄은 반드시 문자열 필드 `type`을 가진다.
- **알 수 없는 `type`** → `RawUnknown`으로 안전하게 무시(전방 호환). 새 이벤트를 추가해도
  구버전 AgentManager가 죽지 않는다.
- **공백 줄 / 파싱 실패 줄**은 조용히 스킵된다.
- **stderr**의 각 줄은 AgentSession의 stderr 펌프를 통해 `EngineError`로 표면화된다.

---

## 2. 프롬프트 전달 — 두 가지 모드 (매니페스트 args로 자동 선택)

### (A) ARGS 모드 — 기본/권장
`launch.args`에 `{prompt}` 플레이스홀더가 **있으면** ARGS 모드. 프롬프트를 argv로 넘기고,
시작 직후 **stdin을 닫는다**(codex류 stdin 대기 hang 방지).

지원 플레이스홀더(각 arg 내부에서 치환):

| 플레이스홀더 | 값 |
|---|---|
| `{prompt}` | 사용자 프롬프트(이미 영어로 번역된 상태) |
| `{model}`  | 선택 모델, 없으면 빈 문자열 |
| `{cwd}`    | 턴 작업 디렉터리 |
| `{sessionId}` | 재개 세션 id. 신규 세션이면 빈 문자열 — 이때 CLI가 자체 id를 만들어 `session_started`로 보고 |

예: `"launch": { "exe": "mybridge", "args": ["chat", "{prompt}", "--model", "{model}", "--session", "{sessionId}"] }`

### (B) STDIN 모드 — 영속/서버형 CLI
`launch.args`에 `{prompt}`가 **없으면** STDIN 모드. **stdin을 열어 둔 채** 시작 라인 하나를 쓴다:

```json
{"type":"start","prompt":"...","model":"...","cwd":"...","sessionId":"..."}
```

턴이 끝나도 살아 있는 서버형 CLI를 위해, `turn_completed` 직후 AgentManager가 프로세스를
정리한다(`KillAfterTurnCompleted`, pi 어댑터와 동일).

> 주의: 모드는 `{prompt}` 유무로 **암묵 자동 선택**된다. STDIN 모드를 의도했다면 args에
> `{prompt}`를 넣지 말 것(넣으면 조용히 ARGS 모드가 된다).

---

## 3. 이벤트: CLI → AgentManager (stdout)

| `type` | 필드 | 매핑(NormalizedEvent) | 비고 |
|---|---|---|---|
| `session_started` | `sessionId`, `model?`, `cwd?`, `toolCount?` | `SessionStarted` | **첫 줄로 보내야 함(SHOULD)** |
| `assistant_delta` | `text` | `AssistantDelta` | 스트리밍 조각. 즉시 표시, **번역 안 됨** |
| `assistant_text` | `text` | `AssistantText` | 최종 전체 메시지. **EN→KO 번역 대상**, 누적 델타를 대체 |
| `thinking` | `text` | `Thinking` | **블록당 1회** 방출 권장(토큰마다 X — 줄 폭주 방지) |
| `tool_started` | `id`, `name`, `input?` | `ToolUseStarted(id,name,InputJson)` | `input`은 JSON 객체(원문) 또는 문자열(`{"text":..}`로 래핑). 없으면 `"{}"` |
| `tool_result` | `id`, `content`, `isError?` | `ToolResult` | `content`는 문자열 또는 `[{type:"text",text}]` 배열(평탄화) |
| `token_usage` | `input`, `output`, `cacheRead?`, `cacheWrite?`, `reasoning?` | `TokenUsage` | 진행 중 러닝 사용량 |
| `error` | `message` | `EngineError` | |
| `turn_completed` | `text?`, `isError?`, `costUsd?`, `numTurns?`, `usage?{input,output,...}` | `TurnCompleted` | **필수 종료 줄.** `usage`가 있으면 그것이 턴 합계 권위값 |
| (그 외) | — | `RawUnknown(type, 원문)` | 무시(전방 호환) |

- `assistant_delta`만 보내고 `assistant_text`를 생략하는 CLI도 동작하지만, 그 출력은
  **번역되지 않는다**(pi 델타와 같은 한계). 번역이 필요하면 턴 마지막에 `assistant_text`로
  최종 메시지를 한 번 보낼 것.
- `session_started` 없이 먼저 스트리밍하면, 어댑터가 최소 `SessionStarted`(engineId만)를
  합성한다. 이후 진짜 `session_started`가 오면 그 id가 UI에 반영된다.

---

## 4. 제어: AgentManager → CLI (stdin, 선택)

v1에서 stdin으로 가는 것은 **STDIN 모드의 `start` 줄 하나뿐**이다. 권한/승인 왕복은 v1에
없다(`Capabilities.Permissions = false`). 나중에 와이어 포맷을 깨지 않고
`permission_request` / `permission_response` 쌍 + `BuildPermissionResponse`로 확장 가능.

---

## 5. 완료 · 취소 · 안전장치

- `turn_completed`가 정상 턴 경계다. 중복 `turn_completed`는 무시된다(중복 완료 가드).
- 프로세스가 `turn_completed` 없이 종료되면 AgentSession이 `TurnCompleted`를 합성한다
  (exit code ≠ 0 → error). 조용히 죽거나 멈춘 브리지가 UI를 영원히 매달지 않는다.
- 취소는 CancellationToken이 프로세스 트리를 종료한다(stdin 모드와 무관).

---

## 6. 능력(Capabilities) — v1

`new AgentCapabilities(Permissions:false, Thinking:true, Sessions:true, Images:false, TokenUsage:true, Quota:false)`

- `Images:false` — v1 어댑터는 base64 이미지를 주입하지 않는다. 이미지가 필요한 CLI는
  프롬프트/args에서 스스로 읽어야 한다.

---

## 7. 예시 트랜스크립트 (ARGS 모드)

CLI가 stdout에 순서대로 출력:

```json
{"type":"session_started","sessionId":"5f3a","model":"gpt-x","cwd":"D:/proj","toolCount":12}
{"type":"assistant_delta","text":"Reading the file"}
{"type":"thinking","text":"The user wants the config value."}
{"type":"tool_started","id":"c1","name":"shell","input":{"cmd":"cat config.json"}}
{"type":"tool_result","id":"c1","content":[{"type":"text","text":"{\"port\":8080}"}],"isError":false}
{"type":"assistant_text","text":"The port is 8080."}
{"type":"token_usage","input":320,"output":48}
{"type":"turn_completed","text":"The port is 8080.","isError":false,"costUsd":0.004,"numTurns":1,"usage":{"input":320,"output":48}}
```

---

## 8. 버전 · 전방 호환

- protocolVersion 1. 새 이벤트 `type`을 추가해도 구버전은 `RawUnknown`으로 무시하므로 안전.
- 기존 이벤트에 필드를 **추가**하는 것은 호환된다(어댑터는 모르는 필드를 무시). 기존 필드의
  의미/타입 변경은 breaking이므로 새 `type`이나 새 protocolVersion으로 처리할 것.

---

## 9. 로컬 검증(라이브 E2E)

실제 브리지 CLI가 아직 없으므로(Hermes/ZCode처럼 외부 바이너리 대기), 검증은
`AgentManager.Smoke`의 `AssertBridgeAdapter`(파싱/모드/치환/디스패치 단위 검사)로 한다.
라이브 확인은: `session_started → assistant_delta → tool_started → tool_result →
assistant_text → turn_completed`를 그대로 뱉는 node/python 한 줄 스크립트를 만들고,
커스텀 엔진 매니페스트(`adapterKind: "agentmanager-bridge-jsonl"`,
`launch.exe: "node"`, `launch.args: ["<script>", "{prompt}"]`)로 등록해 한 턴 돌려
스트리밍 텍스트 + 도구 카드 + 최종 메시지 + 깔끔한 턴 종료(hang 없음)를 확인하면 된다.
