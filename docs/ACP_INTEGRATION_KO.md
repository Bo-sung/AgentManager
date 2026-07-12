# ACP (Agent Client Protocol) 어댑터

`adapterKind:"acp"` — Zed의 **Agent Client Protocol**(개행 구분 JSON-RPC 2.0 over stdio)을 말하는 커스텀 엔진 어댑터. ACP 서버를 제공하는 어떤 CLI든 하나의 어댑터로 붙는다. 실측 통합: **opencode**(`opencode acp`, v1.17.18), **hermes**(`hermes-acp`, v0.18.2) — 둘 다 protocolVersion 1.

## 동작 (핸드셰이크)
`CodexAppServerAdapter`와 동일한 stateful handshake — `EngineWriteback`으로 순차 진행:
1. `initialize`(clientCapabilities.fs=false → 에이전트가 자기 파일 도구 사용, tool_call로 보임) → 응답(agentCapabilities/authMethods)
2. `session/new`(cwd, mcpServers) → 응답의 `sessionId` 확보 → SessionStarted
3. `session/prompt`(sessionId, prompt=[{type:text}]) → `session/update` 알림 스트림 →
   - `agent_message_chunk` → 어시스턴트 텍스트
   - `agent_thought_chunk` → thinking
   - `tool_call` / `tool_call_update` → 도구 시작/결과
4. `session/prompt` 응답 `{stopReason, usage}` → 턴 완료(토큰 usage 포함)
5. 에이전트→클라 `session/request_permission` → 권한 승인 UI(옵션 kind로 allow/reject optionId 선택). 그 외 에이전트 요청은 JSON-RPC 에러로 응답(무한 대기 방지).

첫 실행 시 다른 커스텀 엔진과 동일하게 **trust 프롬프트**(exe+args 승인)를 통과한다.

## 엔진 추가 (설정 → Runtimes → ＋ 커스텀 엔진 추가, 또는 engines/&lt;id&gt;.json 직접 저장)

**opencode** (`opencode auth login`으로 provider 설정 필요):
```json
{
  "id": "opencode", "name": "OpenCode", "badge": "OC",
  "source": "custom", "adapterKind": "acp",
  "launch": {
    "exe": "C:/Users/<you>/AppData/Roaming/npm/node_modules/opencode-ai/bin/opencode.exe",
    "args": ["acp"]
  },
  "defaultModel": "opencode/big-pickle",
  "models": [{ "id": "opencode/big-pickle" }],
  "allowedRoles": ["Plain", "Main", "Worker"]
}
```

**hermes** (Nous 계열; `hermes-setup`으로 provider 설정. **첫 실행 콜드스타트가 느림** — lazy pip install):
```json
{
  "id": "hermes", "name": "Hermes", "badge": "HM",
  "source": "custom", "adapterKind": "acp",
  "launch": {
    "exe": "C:/Users/<you>/AppData/Local/hermes/hermes-agent/venv/Scripts/hermes-acp.exe",
    "args": []
  },
  "allowedRoles": ["Plain", "Main", "Worker"]
}
```
> 폼으로 넣을 때: **실행 경로**=위 exe, **args**=`acp`(opencode) 또는 비움(hermes), **adapterKind**=`acp`.

## 한계 / 후속
- **모델 선택**: v1은 에이전트 기본 모델을 사용(ACP는 에이전트별 configOptions로 모델을 바꿈 — 추후 노출). 컴포저 모델 필드는 표시용.
- **fs=false**: 파일 편집은 에이전트 자체 도구로 수행(에디터-소유 파일 모델 미지원). 필요 시 fs/read·write 핸들러 추후.
- **이미지**: 텍스트 프롬프트만(후속).
- **zcode**: Electron 데스크톱앱 + 자체 `zcode_protocol`(ACP 아님) — stdio ACP 모드를 못 찾아 이 어댑터로는 미통합. zcode가 ACP나 헤드리스 stdio 모드를 노출하면 같은 방식으로 추가 가능.
