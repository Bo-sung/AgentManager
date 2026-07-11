/**
 * Worker Guard Extension (Pi Worker Harness)
 *
 * Applies the AgentManager "Worker" role to an official, unmodified Pi
 * session via the out-of-tree Extension API. This file is injected by
 * `pi-worker` via `--extension`; it is never copied into a user's own
 * ~/.pi/agent/extensions.
 *
 * Responsibilities (see docs/WORKER_HARNESS_KO.md):
 *   1. Announce the worker role + delegation depth in the system prompt.
 *   2. Structurally block delegation-shaped tool calls (best-effort, not a
 *      security sandbox — see docs/SECURITY_LIMITATIONS_KO.md).
 *   3. Warn (bash) / block (edit, write) on writes clearly outside the
 *      current working directory.
 *   4. Inject the Worker final-report format into the system prompt.
 */

import type {
	BashToolCallEvent,
	BeforeAgentStartEvent,
	CustomToolCallEvent,
	EditToolCallEvent,
	ExtensionAPI,
	ToolCallEvent,
	WriteToolCallEvent,
} from "@earendil-works/pi-coding-agent";
import { isAbsolute, relative, resolve } from "node:path";

const ENV_ROLE = "AGENTMANAGER_ROLE";
const ENV_SESSION_ID = "AGENTMANAGER_SESSION_ID";
const ENV_TASK_ID = "AGENTMANAGER_TASK_ID";
const ENV_PROJECT_ID = "AGENTMANAGER_PROJECT_ID";
const ENV_DELEGATION_DEPTH = "AGENTMANAGER_DELEGATION_DEPTH";

/**
 * Tool names considered "delegation-shaped" — i.e. their purpose is to
 * create new AgentManager tasks, spawn subagents, or write to a task
 * spool. Worker sessions must not create more work than they were given.
 * This is an allowlist of *known* names from the analysis (pi's own
 * example subagent extension, plus generic orchestration-tool naming
 * conventions) — it is deliberately conservative, not exhaustive.
 */
const BLOCKED_TOOL_NAME_PATTERNS = [
	/^spawn_?subagent$/i,
	/^subagent$/i,
	/^delegate/i,
	/^create_?task$/i,
	/^spawn_?worker$/i,
	/^dispatch_?task$/i,
	/^task_?spool/i,
	/^agentmanager_/i,
];

/** Recursive self-invocation patterns in bash commands — best-effort text match only. */
const BLOCKED_BASH_PATTERNS = [/\bpi-worker\b.*--mode\s+rpc/i, /\bpi-main\b.*--mode\s+rpc/i, /\bpi\b.*--mode\s+rpc.*&/i];

export default function workerGuardExtension(pi: ExtensionAPI) {
	const role = process.env[ENV_ROLE] || "worker";
	const sessionId = process.env[ENV_SESSION_ID];
	const taskId = process.env[ENV_TASK_ID];
	const projectId = process.env[ENV_PROJECT_ID];
	const depth = Number.parseInt(process.env[ENV_DELEGATION_DEPTH] || "0", 10) || 0;

	pi.on("before_agent_start", async (event: BeforeAgentStartEvent) => {
		return { systemPrompt: `${event.systemPrompt}\n\n${buildWorkerDirectives({ role, sessionId, taskId, projectId, depth })}` };
	});

	pi.on("tool_call", async (event: ToolCallEvent) => {
		if (event.type !== "tool_call") return undefined;

		if (isCustomDelegationTool(event)) {
			return {
				block: true,
				reason:
					"Blocked by pi-worker's worker-guard extension: this tool call looks like an AgentManager delegation/orchestration action, which Worker sessions are not allowed to perform. Report the need for follow-up work in the final report instead.",
			};
		}

		if (event.toolName === "bash") {
			const bashEvent = event as BashToolCallEvent;
			const command = bashEvent.input?.command ?? "";
			if (BLOCKED_BASH_PATTERNS.some((re) => re.test(command))) {
				return {
					block: true,
					reason:
						"Blocked by pi-worker's worker-guard extension: this command appears to recursively spawn another pi/pi-worker/pi-main RPC process, which is not permitted inside a Worker session.",
				};
			}
			return undefined;
		}

		if (event.toolName === "write" || event.toolName === "edit") {
			const pathEvent = event as WriteToolCallEvent | EditToolCallEvent;
			const targetPath = pathEvent.input?.path;
			if (typeof targetPath === "string" && isOutsideCwd(targetPath)) {
				return {
					block: true,
					reason: `Blocked by pi-worker's worker-guard extension: "${targetPath}" resolves outside the current working directory. Worker sessions must stay within their assigned working directory or worktree. This is a scope guard, not a full OS sandbox — see docs/SECURITY_LIMITATIONS_KO.md.`,
				};
			}
		}

		return undefined;
	});
}

function isCustomDelegationTool(event: ToolCallEvent): event is CustomToolCallEvent {
	if (event.toolName === "bash" || event.toolName === "read" || event.toolName === "edit" || event.toolName === "write") {
		return false;
	}
	if (event.toolName === "grep" || event.toolName === "find" || event.toolName === "ls") {
		return false;
	}
	return BLOCKED_TOOL_NAME_PATTERNS.some((re) => re.test(event.toolName));
}

function isOutsideCwd(targetPath: string): boolean {
	const cwd = process.cwd();
	const absoluteTarget = isAbsolute(targetPath) ? targetPath : resolve(cwd, targetPath);
	const rel = relative(cwd, absoluteTarget);
	return rel.startsWith("..") || isAbsolute(rel);
}

function buildWorkerDirectives(ctx: {
	role: string;
	sessionId?: string;
	taskId?: string;
	projectId?: string;
	depth: number;
}): string {
	return `## AgentManager Worker Directives

You are running as an AgentManager **${ctx.role}** session${ctx.taskId ? ` for task \`${ctx.taskId}\`` : ""}${
		ctx.projectId ? ` in project \`${ctx.projectId}\`` : ""
	}${ctx.sessionId ? ` (session \`${ctx.sessionId}\`)` : ""}. Delegation depth: ${ctx.depth}.

Follow these rules for this session:
- Perform only the single assigned Task Contract. Do not redesign the overall project plan.
- Stay within the allowed scope and file boundaries. Work only in the current working directory or the assigned worktree.
- If a change is needed outside your assigned scope, do NOT make it — report it instead.
- Investigate, modify code, run builds/tests, and review diffs as needed to complete the task.
- Verify completion against real results (actual command output), never assume success.
- Never claim a test passed unless you actually ran it and it passed.
- Do not spawn other Workers or subagents, create new AgentManager tasks, or write to any task spool.
- Do not make irreversible decisions on the user's behalf (e.g. force-push, delete branches, drop data) without explicit instruction.
- Your final response MUST use this report format:

## Result
Complete / Partial / Failed

## Summary
<what you did>

## Files Changed
- <path> — <reason>

## Validation
- command: <...>
- working directory: <...>
- exit code: <...>
- result: <...>

## Deviations
<anything that diverged from the Task Contract, or was left undone>

## Risks
<remaining risks>

## Follow-up
<work that should happen next, if any — described only, never queued or spawned by you>
`;
}
