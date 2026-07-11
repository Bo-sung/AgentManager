import { homedir } from "node:os";
import { join } from "node:path";
/**
 * Config isolation for pi-worker.
 *
 * Official Pi centralizes almost all user-level resources (settings, auth,
 * sessions, models, trust, extensions, skills, prompts, themes, keybindings,
 * debug log) behind a single function `getAgentDir()` in
 * packages/coding-agent/src/config.ts, which is controlled by the
 * `PI_CODING_AGENT_DIR` environment variable (see
 * docs/pi-main-analysis/03_CONFIG_RESOURCE_MAP_KO.md §2-3).
 *
 * pi-worker does NOT fork Pi or change its package.json `piConfig`, so the
 * env var names are fixed as `PI_CODING_AGENT_DIR` / `PI_CODING_AGENT_SESSION_DIR`
 * regardless of the launcher's own name.
 *
 * Known leaks that this isolation CANNOT close (documented, not silently
 * ignored — see docs/CONFIG_ISOLATION_KO.md):
 *   1. packages/tui/src/tui.ts hardcodes ~/.pi/agent/pi-debug.log and
 *      ~/.pi/agent/pi-crash.log regardless of PI_CODING_AGENT_DIR.
 *   2. ~/.agents/skills (cross-harness skill auto-discovery root) only
 *      depends on $HOME, never on PI_CODING_AGENT_DIR.
 */
export const DEFAULT_WORKER_CONFIG_DIR_NAME = ".pi-worker";
export const ENV_WORKER_HOME = "PIWORKER_HOME";
export const ENV_WORKER_SESSION_DIR = "PIWORKER_SESSION_DIR";
/** Official Pi's own override env vars (fixed names, not derived from our brand). */
export const ENV_PI_AGENT_DIR = "PI_CODING_AGENT_DIR";
export const ENV_PI_SESSION_DIR = "PI_CODING_AGENT_SESSION_DIR";
/**
 * Resolve isolated paths for this pi-worker invocation.
 * Override with PIWORKER_HOME / PIWORKER_SESSION_DIR for tests or
 * multi-worker-identity setups (e.g. per-project worker homes).
 */
export function resolveWorkerPaths(env = process.env) {
    const workerHome = env[ENV_WORKER_HOME]?.trim() || join(homedir(), DEFAULT_WORKER_CONFIG_DIR_NAME);
    const agentDir = join(workerHome, "agent");
    const sessionDir = env[ENV_WORKER_SESSION_DIR]?.trim() || join(agentDir, "sessions");
    return { workerHome, agentDir, sessionDir };
}
/** Build the environment overlay to inject into the spawned official `pi` process. */
export function buildIsolationEnv(paths) {
    return {
        [ENV_PI_AGENT_DIR]: paths.agentDir,
        [ENV_PI_SESSION_DIR]: paths.sessionDir,
    };
}
