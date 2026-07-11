# Vendored: @agentmanager/pi-worker-harness

This directory is a **vendored runtime copy** of the pi-worker harness, bundled into
AgentManager so the Pi Worker engine works without a global npm install.

- Package: `@agentmanager/pi-worker-harness`
- Version: `0.1.0`
- Source commit: `6e49dbd0f6b2858dc4d946311843ebc2ba6dde10`
- Upstream repo: `H:\Git\Bosung_PI\pi-worker-harness` (feature/pi-worker-harness → develop)
- License: MIT
- Wraps official Pi: `0.80.3` (pinned)
- Runtime entrypoint: `dist/cli/index.js` (bin `pi-worker`)

Only the runtime files are vendored: `dist/` (built JS), `worker-package/`
(extensions/skills/prompts), and `package.json`. The harness has **no runtime npm
dependencies**, so it runs on plain Node with no `node_modules`. Source (`src/`),
tests, and dev tooling are intentionally NOT vendored.

**Do not edit these files here** — fix upstream in the harness repo and re-vendor,
updating the source commit above.
