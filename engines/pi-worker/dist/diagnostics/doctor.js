import { existsSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { resolveWorkerPaths } from "../config/paths.js";
import { PINNED_SUPPORTED_PI_VERSION, resolvePiCommand } from "../launcher/resolve-pi.js";
import { workerExtensionPath, workerPackageDir } from "../launcher/spawn.js";
/**
 * Diagnostics command. Never prints credential contents — only presence/absence
 * of files and resolved paths, per the security-review instruction in the
 * task order (no secrets in test/diagnostic logs).
 */
export async function runDoctor() {
    const checks = [];
    const resolved = resolvePiCommand();
    checks.push({
        label: "Official pi resolvable",
        ok: resolved.version !== undefined,
        detail: resolved.version ? `pi ${resolved.version} (command: ${resolved.command})` : `NOT FOUND (command tried: ${resolved.command})`,
    });
    checks.push({
        label: "Pi version matches pinned support",
        ok: !resolved.versionDrift,
        detail: resolved.version
            ? `installed=${resolved.version} pinned=${PINNED_SUPPORTED_PI_VERSION}${resolved.versionDrift ? " (DRIFT — see docs/PI_VERSION_SUPPORT_KO.md)" : ""}`
            : "skipped (pi not found)",
    });
    const paths = resolveWorkerPaths();
    checks.push({ label: "Worker config root", ok: true, detail: paths.workerHome });
    checks.push({ label: "Worker agent dir (PI_CODING_AGENT_DIR)", ok: true, detail: paths.agentDir });
    checks.push({ label: "Worker session dir (PI_CODING_AGENT_SESSION_DIR)", ok: true, detail: paths.sessionDir });
    const officialAgentDir = join(homedir(), ".pi", "agent");
    const collision = paths.agentDir === officialAgentDir;
    checks.push({
        label: "Isolated from official ~/.pi/agent",
        ok: !collision,
        detail: collision ? `COLLISION: worker agent dir resolves to ${officialAgentDir}` : `official=${officialAgentDir}`,
    });
    const extPath = workerExtensionPath();
    checks.push({ label: "worker-guard extension present", ok: existsSync(extPath), detail: extPath });
    const skillDir = join(workerPackageDir(), "skills", "worker-task");
    checks.push({ label: "worker-task skill present", ok: existsSync(skillDir), detail: skillDir });
    const sharedSkillsRoot = join(homedir(), ".agents", "skills");
    checks.push({
        label: "Shared ~/.agents/skills root (known non-isolable leak)",
        ok: true,
        detail: existsSync(sharedSkillsRoot)
            ? `exists at ${sharedSkillsRoot} — contents are visible to BOTH official pi and pi-worker (see docs/CONFIG_ISOLATION_KO.md)`
            : `does not exist (${sharedSkillsRoot})`,
    });
    checks.push({ label: "Node version", ok: true, detail: process.version });
    const amEnvVars = [
        "AGENTMANAGER_ROLE",
        "AGENTMANAGER_SESSION_ID",
        "AGENTMANAGER_TASK_ID",
        "AGENTMANAGER_PROJECT_ID",
        "AGENTMANAGER_DELEGATION_DEPTH",
    ];
    for (const key of amEnvVars) {
        const value = process.env[key];
        checks.push({ label: `env ${key}`, ok: true, detail: value !== undefined ? value : "(unset — default applied by pi-worker if applicable)" });
    }
    console.log("Pi Worker Harness — doctor\n");
    let allOk = true;
    for (const c of checks) {
        const mark = c.ok ? "OK  " : "FAIL";
        if (!c.ok)
            allOk = false;
        console.log(`[${mark}] ${c.label}: ${c.detail}`);
    }
    console.log("");
    console.log(allOk ? "All checks passed." : "Some checks failed — see FAIL lines above.");
    return allOk ? 0 : 1;
}
