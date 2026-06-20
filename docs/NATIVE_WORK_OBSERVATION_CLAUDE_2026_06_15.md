# Claude native subagent observation verification

> 2026-06-15 local verification. This supersedes the earlier Claude "deferred until quota returns"
> note in `docs/NATIVE_WORK_OBSERVATION_KO.md`.

## Verified

- Claude Code version: `2.1.177`.
- `SubagentStart` and `SubagentStop` hooks fire in real subagent runs.
- Inline launch-time `--settings '{...json...}'` hook injection works.
- No project `.claude/settings.local.json` write is required.
- Hook JSON fields match `NativeHookEvent.TryParse`.
- `SubagentStop` includes:
  - `agent_transcript_path`
  - `last_assistant_message`
  - `permission_mode`
  - `background_tasks`
  - `session_crons`
- Hook JSON does not include `turn_id` / `turnId`.
- Hook script failure with exit 1 + stderr did not fail the Claude main session.
- Subagent transcript path:
  `~/.claude/projects/<encoded>/<sessionId>/subagents/agent-<agentId>.jsonl`.
- Matching `.meta.json` contains `toolUseId`, which can join to `PreToolUse.tool_use_id`.
- `claude agents --json` does not list session-internal subagents as separate rows.
- Background sessions are separate and observable through:
  - `claude --bg`
  - `claude agents --json`
  - `claude logs <id>`
  - `claude stop <id>`

## Hook Examples

`SubagentStart`:

```json
{
  "session_id": "d92aa703-bc6e-430d-82f1-6b02847ecc5a",
  "transcript_path": "C:\\Users\\...\\d92aa703-....jsonl",
  "cwd": "C:\\Users\\...\\am-hooktest-proj",
  "agent_id": "a95692f20e63e3891",
  "agent_type": "Explore",
  "hook_event_name": "SubagentStart"
}
```

`SubagentStop`:

```json
{
  "session_id": "d92aa703-bc6e-430d-82f1-6b02847ecc5a",
  "transcript_path": "C:\\Users\\...\\d92aa703-....jsonl",
  "cwd": "C:\\Users\\...\\am-hooktest-proj",
  "permission_mode": "bypassPermissions",
  "agent_id": "a95692f20e63e3891",
  "agent_type": "Explore",
  "hook_event_name": "SubagentStop",
  "stop_hook_active": false,
  "agent_transcript_path": "C:\\Users\\...\\subagents\\agent-a95692f20e63e3891.jsonl",
  "last_assistant_message": "Based on my listing, here are all the files ...",
  "background_tasks": [],
  "session_crons": []
}
```

## Implementation Applied

- `ClaudeAdapter` injects `SubagentStart` / `SubagentStop` hooks through inline `--settings`.
- `AppViewModel.RunTurnAsync` enables hook spool observation for Claude (`cc`) sessions.
- `HookSpoolNativeWorkObserver` is reused for Claude with `EngineId="cc"`.
- The hook command now uses a generated PowerShell script file in the spool directory
  (`am-hook-spool.ps1`) instead of a long inline `-Command` string. The inline command
  form did not produce hook files in the AgentSession live path.
- `NativeHookEvent` preserves:
  - `permission_mode`
  - `background_tasks`
  - `session_crons`
- Added smoke:
  `dotnet run --project src/AgentManager.Smoke -- --claude-hook-args-check`.
- Added live smoke:
  `dotnet run --project src/AgentManager.Smoke -- --live-claude-native-observer`.

## Live Smoke Result

`--live-claude-native-observer` passes with:

- Claude main session completed without error.
- Claude spawned an `Agent`/Explore subagent.
- Hook spool file was written.
- `HookSpoolNativeWorkObserver` observed a completed Claude native subagent.

## WPF Visual Verification (2026-06-21)

Confirmed in the running WPF app that the Native workers strip renders observed
Claude subagents. Two passes:

- **Render pipeline (synthetic, no quota):** injected fake `ObservedWorkItem`s into
  `SessionViewModel.NativeWorkItems`. The strip showed the count and three cards with
  correct per-state colors — Running (accent), Completed (ok), Failed (err) — and the
  `Title` / `Status` / `Source (Hook / High)` / `LastMessage` layout.
- **Live end-to-end (real Claude run):** a cc session was prompted to delegate a
  read-only file count to an Explore subagent. The strip rendered the real subagent
  live as **Running → Completed** (driven by the `SubagentStart` / `SubagentStop`
  hook spool), with `Source = Hook / High`. Subagent reported "657 .cs files under
  src/"; turn cost ≈ $0.36.

## Remaining Work

- Decide whether to add a separate background-session poller for `claude agents --json`.
- Add optional transcript tailing for failed/rate-limited subagent inference.
