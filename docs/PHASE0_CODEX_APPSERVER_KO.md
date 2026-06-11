# Phase 0 실측 — codex app-server (승인 Stage 2)

> 2026-06-12, codex-cli **0.137.0-alpha.4** (VS Code 확장 동봉 exe), Windows.
> 측정 도구: `AgentManager.Smoke --appserver-probe` (실 왕복, PASS).
> 스키마 덤프: `codex app-server generate-json-schema --out <dir>` (v1/v2 JSON Schema 자동 생성 — 버전 업데이트 시 재생성해서 diff 확인).

## 1. 전송/프레이밍
- `codex app-server` (기본 `--listen stdio://`) — **개행 구분 JSON-RPC** (LSP Content-Length 아님).
- stdin: 클라이언트 요청/알림/응답. stdout: 서버 응답/알림/**서버→클라 요청**(승인). stderr: tracing 로그.

## 2. 핸드셰이크 (실측)
```
--> {"id":1,"method":"initialize","params":{"clientInfo":{"name":"AgentManager","title":"AgentManager","version":"0.1.0"}}}
<-- {"id":1,"result":{"userAgent":...}}
--> {"method":"initialized"}
--> {"id":2,"method":"thread/start","params":{"cwd":"...","approvalPolicy":"untrusted","sandbox":"danger-full-access"}}
<-- {"id":2,"result":{"thread":{"id":"019eb74c-...","sessionId":...}}}        ← threadId 확보
--> {"id":3,"method":"turn/start","params":{"threadId":"...","input":[{"type":"text","text":"..."}]}}
<-- {"id":3,"result":{"turn":{"id":"...","status":"inProgress"}}}
```
- `thread/resume` { threadId } 로 기존 스레드 재개 (exec resume과 같은 저장소 공유).
- turn/start 오버라이드: `model`, `cwd`, `approvalPolicy`, `sandboxPolicy`(객체형 — workspaceWrite는 `writableRoots:[]` 지원), `effort`, `summary`.

## 3. 승인 (서버→클라 요청, 실측)
```
<-- {"method":"item/commandExecution/requestApproval","id":0,"params":{"threadId":...,"turnId":...,"itemId":"call_...","startedAtMs":...}}
--> {"id":0,"result":{"decision":"accept"}}
<-- {"method":"serverRequest/resolved","params":{"threadId":...,"requestId":0}}
```
- 서버 요청 메서드: `item/commandExecution/requestApproval` · `item/fileChange/requestApproval` · `item/permissions/requestApproval` · `item/tool/requestUserInput` · `mcpServer/elicitation/request` (+ v1 레거시 `execCommandApproval`/`applyPatchApproval`).
- decision 값: `accept` | `acceptForSession` | `decline`(턴 계속) | `cancel`(턴 중단) | `acceptWithExecpolicyAmendment{...}` | `applyNetworkPolicyAmendment{...}`.
- 승인 대기 중 `thread/status/changed` activeFlags=["waitingOnApproval"] → UI 상태 표시에 사용 가능.

## 4. 주요 알림 → NormalizedEvent 매핑(안)
| app-server 알림 | NormalizedEvent |
|---|---|
| `thread/started` (thread.id) | SessionStarted |
| `item/started` type=commandExecution/fileChange/mcpToolCall | ToolUseStarted |
| `item/completed` (위 타입) | ToolResult |
| `item/agentMessage/delta` · `item/completed` type=agentMessage | AssistantText (delta 스트리밍) |
| `item/reasoning/*` | Thinking |
| `item/commandExecution/requestApproval` 등 서버 요청 | PermissionRequest (RequestId = JSON-RPC id) |
| `thread/tokenUsage/updated` | TokenUsage |
| `turn/completed` | TurnCompleted |
| `account/rateLimits/updated` | QuotaUpdate |
| `error` | EngineError |

## 5. 함정 (실측으로 확인)
1. **Windows 샌드박스 spawn 실패**: sandbox=`workspace-write`로 thread/start 시 모든 명령이
   `windows sandbox: spawn setup refresh` IO 오류로 실패 (승인 accept 후에도). `windowsSandbox/setupCompleted`
   알림이 있는 것으로 보아 별도 셋업 필요. → **Stage 2 모델: sandbox=danger-full-access + approvalPolicy=untrusted**
   (샌드박스 대신 승인 게이트 — Claude Stage 1과 동일 정책)으로 우회. 이 조합 실측 PASS.
2. `approvalPolicy=untrusted`에서 에이전트가 권한 에스컬레이션을 요청하면 router가 거부함
   ("you should not ask for escalated permissions...") — 정책 조합 주의.
3. 서버 요청의 JSON-RPC `id`는 0부터 시작하는 서버 측 시퀀스 — 클라이언트 요청 id와 충돌하지 않게 응답만 미러링.
4. `initialize` 후 `initialized` 알림을 보내야 함 (LSP 패턴).

## 6. 통합 설계 메모 (다음 단계)
- 현 `IAgentAdapter`는 "정적 초기 stdin + 라인 파싱" 모델 — app-server는 **응답을 봐야 다음 요청을 보내는** 상태형 핸드셰이크라
  어댑터가 파싱 중 "stdin으로 보낼 라인"을 일으킬 수단 필요. 안: NormalizedEvent에 내부용 `EngineWriteback(line)` 추가
  (AgentSession이 수신 시 stdin에 기록; UI로는 전달 안 함).
- 승인: 기존 PermissionRequest/PermissionDecision 흐름 재사용 — `BuildPermissionResponse` →
  `{"id":<requestId>,"result":{"decision":"accept"|"decline"}}`.
- 세션 id: thread.id (exec rollout과 동일 uuid 공간) → 기존 CLI HISTORY/resume과 호환.
- 어댑터 선택: 세션 옵션 `RequireApproval=true`인 codex 세션만 app-server 경로, 아니면 기존 exec --json 유지(위험 최소화).
