import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { buildIsolationEnv, resolveWorkerPaths } from "../config/paths.js";
import { PiNotFoundError, resolvePiCommand } from "./resolve-pi.js";
const __dirname = dirname(fileURLToPath(import.meta.url));
/** worker-package lives next to the installed harness (source tree or npm package). */
export function workerPackageDir() {
    // dist/launcher/spawn.js -> ../../worker-package  (mirrors src/launcher/spawn.ts -> ../../worker-package)
    return join(__dirname, "..", "..", "worker-package");
}
export function workerExtensionPath() {
    return join(workerPackageDir(), "extensions", "worker-guard.ts");
}
export const ENV_ROLE = "AGENTMANAGER_ROLE";
export const ENV_DELEGATION_DEPTH = "AGENTMANAGER_DELEGATION_DEPTH";
export const ENV_NO_GUARD = "PIWORKER_NO_GUARD";
export const ENV_NO_SKILL_PACKAGE = "PIWORKER_NO_SKILL_PACKAGE";
/**
 * Build the final argv passed to the official `pi` binary.
 *
 * Unknown/unrecognized user args are passed through verbatim and untouched —
 * pi-worker does not maintain its own allowlist of official Pi flags
 * (docs/pi-main-analysis instructs pass-through over blocking).
 */
export function buildPiArgs(userArgs, env = process.env) {
    const args = [...userArgs];
    const guardDisabled = env[ENV_NO_GUARD] === "1" || userArgs.includes("--no-extensions");
    if (!guardDisabled) {
        args.push("--extension", workerExtensionPath());
    }
    if (env[ENV_NO_SKILL_PACKAGE] !== "1" && !userArgs.includes("--no-skills")) {
        args.push("--skill", join(workerPackageDir(), "skills", "worker-task"));
    }
    return args;
}
/** Env overlay applied on top of the inherited process env before spawning official pi. */
export function buildWorkerEnv(env = process.env) {
    const paths = resolveWorkerPaths(env);
    const overlay = {
        ...buildIsolationEnv(paths),
        [ENV_ROLE]: env[ENV_ROLE]?.trim() || "worker",
        [ENV_DELEGATION_DEPTH]: env[ENV_DELEGATION_DEPTH]?.trim() || "0",
    };
    return { ...env, ...overlay };
}
/**
 * Windows cmd.exe argument quoting. Node's `shell: true` on Windows passes
 * args through cmd.exe, which requires manual escaping — passing an array
 * to spawn() with shell:true does NOT escape args for you (Node emits
 * DEP0190 for exactly this reason), so an unescaped arg containing cmd.exe
 * metacharacters (&, |, etc.) from a caller (e.g. a Task Contract prompt)
 * could otherwise break out of its argument position.
 */
function quoteForWindowsShell(arg) {
    if (arg === "")
        return '""';
    if (!/[\s"&|<>^%]/.test(arg))
        return arg;
    // Escape embedded double quotes by doubling them, per cmd.exe quoting rules,
    // then wrap the whole argument in quotes.
    return `"${arg.replace(/"/g, '""')}"`;
}
/**
 * Spawn the official pi process with worker isolation applied, forwarding
 * stdio 1:1 (so RPC JSONL, interactive TTY, and print-mode output are never
 * touched by pi-worker). Resolves once the child exits, with its exit code.
 */
export async function launchOfficialPi(userArgs) {
    const resolved = resolvePiCommand(process.env);
    const finalArgs = buildPiArgs(userArgs, process.env);
    const finalEnv = buildWorkerEnv(process.env);
    const isWindows = process.platform === "win32";
    // Node emits DEP0190 whenever an args array is combined with shell:true,
    // regardless of quoting, because it cannot verify the array was escaped.
    // Building one pre-quoted command line and passing it with an empty args
    // array avoids that (documented Node workaround) while keeping the actual
    // escaping logic (and therefore the injection fix) identical.
    const windowsCommandLine = [resolved.command, ...finalArgs].map(quoteForWindowsShell).join(" ");
    return new Promise((resolve, reject) => {
        const child = isWindows
            ? spawn(windowsCommandLine, [], { stdio: "inherit", env: finalEnv, shell: true })
            : spawn(resolved.command, finalArgs, { stdio: "inherit", env: finalEnv, shell: false });
        child.on("error", (err) => {
            if (err.code === "ENOENT") {
                reject(new PiNotFoundError(resolved.command));
            }
            else {
                reject(err);
            }
        });
        child.on("exit", (code, signal) => {
            if (signal) {
                // Mirror common shell convention: 128 + signal number is not resolvable
                // portably here, so surface a nonzero code and let the caller print the signal.
                resolve({ exitCode: 1 });
                return;
            }
            resolve({ exitCode: code ?? 1 });
        });
    });
}
