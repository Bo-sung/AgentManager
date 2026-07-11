#!/usr/bin/env node
import { resolveWorkerPaths } from "../config/paths.js";
import { runDoctor } from "../diagnostics/doctor.js";
import { launchOfficialPi } from "../launcher/spawn.js";
import { resolvePiCommand } from "../launcher/resolve-pi.js";
const HARNESS_VERSION = "0.1.0";
async function main() {
    const argv = process.argv.slice(2);
    if (argv[0] === "doctor" || argv[0] === "--diagnostics") {
        const code = await runDoctor();
        process.exit(code);
    }
    if (argv.length === 1 && (argv[0] === "--version" || argv[0] === "-v")) {
        const resolved = resolvePiCommand();
        console.log(`pi-worker ${HARNESS_VERSION} (wraps official pi ${resolved.version ?? "unknown"})`);
        process.exit(0);
    }
    if (argv.length === 1 && (argv[0] === "--help" || argv[0] === "-h")) {
        printHelp();
        // Also surface official pi's own --help beneath ours, since we pass through
        // most of its flags verbatim and the user needs to see them.
        const result = await launchOfficialPi(["--help"]);
        process.exit(result.exitCode);
    }
    // Banner only for interactive-ish invocations (never in RPC mode, never in
    // --print mode) — avoid corrupting machine-readable output streams.
    const isRpcMode = argv.includes("--mode") && argv[argv.indexOf("--mode") + 1] === "rpc";
    const isPrintMode = argv.includes("--print") || argv.includes("-p");
    if (!isRpcMode && !isPrintMode && process.stdout.isTTY) {
        printBanner();
    }
    try {
        const result = await launchOfficialPi(argv);
        process.exit(result.exitCode);
    }
    catch (err) {
        console.error(`[pi-worker] ${err.message}`);
        process.exit(1);
    }
}
function printBanner() {
    const paths = resolveWorkerPaths();
    const depth = process.env.AGENTMANAGER_DELEGATION_DEPTH ?? "0";
    console.error("Pi Worker Harness");
    console.error("Role: worker");
    console.error(`Delegation depth: ${depth}`);
    console.error(`Config: ${paths.agentDir}`);
    console.error("");
}
function printHelp() {
    console.log(`pi-worker ${HARNESS_VERSION}

Runs the official Pi coding agent under an isolated Worker role for AgentManager.
Does not fork or modify Pi itself — see docs/ARCHITECTURE_KO.md.

Usage:
  pi-worker [pi-args...]        Run official pi with worker isolation (interactive)
  pi-worker --mode rpc          Run official pi RPC mode with worker isolation (AgentManager path)
  pi-worker doctor              Print diagnostics (config paths, versions, env)
  pi-worker --version           Print pi-worker + wrapped pi version
  pi-worker --help              Print this help, then official pi's --help

All other arguments are passed through verbatim to the official pi CLI.
Environment:
  PIWORKER_HOME              Override worker config root (default ~/.pi-worker)
  PIWORKER_SESSION_DIR       Override worker session dir
  PIWORKER_PI_BIN            Explicit path to the official pi executable
  PIWORKER_NO_GUARD=1        Skip injecting the worker-guard extension
  PIWORKER_NO_SKILL_PACKAGE=1  Skip injecting the worker-task skill
  AGENTMANAGER_ROLE, AGENTMANAGER_SESSION_ID, AGENTMANAGER_TASK_ID,
  AGENTMANAGER_PROJECT_ID, AGENTMANAGER_DELEGATION_DEPTH
                              Forwarded to the worker-guard extension as-is
`);
}
main().catch((err) => {
    console.error(`[pi-worker] fatal: ${err.stack ?? err}`);
    process.exit(1);
});
