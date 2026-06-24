# Engine install & setup guide

How to install each coding-agent CLI and connect it to AgentManager. Install via the official docs, then wire it up under **Settings → Runtimes**.

## Common connect steps
1. Open the engine card under **Settings → Runtimes**.
2. Use **[Detect]** to auto-find the CLI path (or enter the path manually).
3. Sign in with **[Sign in]** for subscription/account auth — or switch to **API key** mode and paste a key.
4. Pick the **default model**.

## Claude Code
- **Install**: `npm i -g @anthropic-ai/claude-code`
- **Official guide**: [docs.claude.com ↗](https://docs.claude.com/en/docs/claude-code/overview)
- **Connect**: Runtimes → Claude Code → Detect/path → Sign in (subscription) or API key → model

## Codex
- **Install**: `npm i -g @openai/codex` (standalone recommended) — or the VS Code `openai.chatgpt` extension
- **Official guide**: [github.com/openai/codex ↗](https://github.com/openai/codex)
- **Connect**: Runtimes → Codex → Detect (standalone preferred) → Sign in or API key → model

## Antigravity
- **Install**: install Antigravity, then use the `agy` CLI
- **Official guide**: [antigravity.google ↗](https://antigravity.google)
- **Connect**: Runtimes → Antigravity → Detect → Sign in → model (default)
- Currently a **free preview** — no usage/quota info is exposed.

## Notes
- **Translation**: local-LLM (Ollama) translation is optional. Set the endpoint/model under Settings → Translation · Language.
- **Usage**: the usage shown is approximate, not official. Check each provider's official page for exact remaining quota.
