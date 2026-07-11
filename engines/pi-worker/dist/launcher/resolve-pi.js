import { execFileSync } from "node:child_process";
export const ENV_PI_BIN_OVERRIDE = "PIWORKER_PI_BIN";
/** Pi version this harness was validated against (docs/PI_VERSION_SUPPORT_KO.md). */
export const PINNED_SUPPORTED_PI_VERSION = "0.80.3";
/**
 * Resolve the official Pi CLI to spawn.
 *
 * Priority:
 *   1. PIWORKER_PI_BIN env var (explicit path, for tests / non-standard installs)
 *   2. "pi" resolved via PATH (the normal case: `npm install -g @earendil-works/pi-coding-agent`
 *      puts pi/pi.cmd/pi.ps1 shims on PATH)
 *
 * This never touches or reinstalls the official pi package — it only locates it.
 */
export function resolvePiCommand(env = process.env) {
    const override = env[ENV_PI_BIN_OVERRIDE]?.trim();
    const command = override || "pi";
    const version = queryPiVersion(command);
    return {
        command,
        version,
        versionDrift: version !== undefined && version !== PINNED_SUPPORTED_PI_VERSION,
    };
}
function queryPiVersion(command) {
    try {
        // See spawn.ts quoteForWindowsShell: combining shell:true with an args
        // array triggers Node's DEP0190 regardless of content, so pass one
        // pre-built command line on Windows instead of a separate args array.
        const isWindows = process.platform === "win32";
        const out = isWindows
            ? execFileSync(`${command} --version`, { shell: true, stdio: ["ignore", "pipe", "ignore"], timeout: 10_000 })
            : execFileSync(command, ["--version"], { stdio: ["ignore", "pipe", "ignore"], timeout: 10_000 });
        return out.toString().trim() || undefined;
    }
    catch {
        return undefined;
    }
}
export class PiNotFoundError extends Error {
    constructor(command) {
        super(`Official Pi CLI ("${command}") was not found on PATH. ` +
            `pi-worker requires the official @earendil-works/pi-coding-agent package to already be installed ` +
            `(e.g. "npm install -g @earendil-works/pi-coding-agent"). ` +
            `Set ${ENV_PI_BIN_OVERRIDE} to point at an explicit pi executable if it is installed in a non-standard location.`);
        this.name = "PiNotFoundError";
    }
}
