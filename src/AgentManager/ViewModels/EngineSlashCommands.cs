using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentManager.ViewModels;

/// <summary>Built-in slash commands per engine (gx/pi/agy), captured verbatim from each CLI's in-TUI
/// <c>/help</c>. cc uses file discovery (<c>.claude/commands</c>) instead. These surface in the composer
/// "/" popup for discovery + autocomplete; many are interactive-TUI features that, in AgentManager's
/// non-interactive runs, pass through as plain text rather than executing.</summary>
public static class EngineSlashCommands
{
    public static IReadOnlyList<(string Cmd, string Desc)> For(string engineId)
    {
        var all = engineId switch
        {
            "gx" => Codex,
            "agy" => Antigravity,
            "pi" => Pi,
            _ => Array.Empty<(string, string)>(),
        };
        // 노이즈 제거: 터미널/디스플레이/인증/종료 + AgentManager UI로 대체된 명령은 숨긴다(원본 배열은 보존).
        return all.Where(c => !Hidden.Contains(c.Item1)).ToArray();
    }

    /// <summary>비대화형 실행에선 의미 없거나 AgentManager UI로 이미 대체된 명령 — `/` 팝업에서 숨김.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        // 터미널/디스플레이/입력 설정
        "/vim", "/keymap", "/keybindings", "/theme", "/pets", "/statusline", "/title",
        "/personality", "/hotkeys", "/raw", "/ide",
        // 인증/종료/메타
        "/exit", "/quit", "/logout", "/login", "/feedback", "/credits", "/app",
        "/scoped-models", "/experimental", "/reload", "/changelog", "/sandbox-add-read-dir",
        "/ps", "/stop", "/trust",
        // AgentManager UI로 대체됨(@ 멘션·복사 버튼)
        "/mention", "/copy",
    };

    // Codex (codex) — from `/help` in the TUI. Deduped, original order.
    private static readonly (string, string)[] Codex =
    [
        ("/model", "choose what model and reasoning effort to use"),
        ("/ide", "include current selection, open files, and other context from your IDE"),
        ("/permissions", "choose what Codex is allowed to do"),
        ("/keymap", "remap TUI shortcuts"),
        ("/vim", "toggle Vim mode for the composer"),
        ("/sandbox-add-read-dir", "let sandbox read a directory: <absolute_path>"),
        ("/experimental", "toggle experimental features"),
        ("/approve", "approve one retry of a recent auto-review denial"),
        ("/memories", "configure memory use and generation"),
        ("/skills", "use skills to improve how Codex performs specific tasks"),
        ("/import", "import setup, this project, and recent chats from Claude Code"),
        ("/hooks", "view and manage lifecycle hooks"),
        ("/review", "review my current changes and find issues"),
        ("/rename", "rename the current thread"),
        ("/new", "start a new chat during a conversation"),
        ("/archive", "archive this session and exit"),
        ("/delete", "permanently delete this session and exit"),
        ("/resume", "resume a saved chat"),
        ("/fork", "fork the current chat"),
        ("/app", "continue this session in Codex Desktop"),
        ("/init", "create an AGENTS.md file with instructions for Codex"),
        ("/compact", "summarize conversation to prevent hitting the context limit"),
        ("/plan", "switch to Plan mode"),
        ("/goal", "set or view the goal for a long-running task"),
        ("/agent", "switch the active agent thread"),
        ("/side", "start a side conversation in an ephemeral fork"),
        ("/copy", "copy last response as markdown"),
        ("/raw", "toggle raw scrollback mode for copy-friendly terminal selection"),
        ("/diff", "show git diff (including untracked files)"),
        ("/mention", "mention a file"),
        ("/status", "show current session configuration and token usage"),
        ("/usage", "view account usage or use a usage limit reset"),
        ("/title", "configure which items appear in the terminal title"),
        ("/statusline", "configure which items appear in the status line"),
        ("/theme", "choose a syntax highlighting theme"),
        ("/pets", "choose or hide the terminal pet"),
        ("/mcp", "list configured MCP tools; use /mcp verbose for details"),
        ("/plugins", "browse plugins"),
        ("/logout", "log out of Codex"),
        ("/exit", "exit Codex"),
        ("/feedback", "send logs to maintainers"),
        ("/ps", "list background terminals"),
        ("/stop", "stop all background terminals"),
        ("/clear", "clear the terminal and start a new chat"),
        ("/personality", "choose a communication style for Codex"),
        ("/subagents", "switch the active agent thread"),
    ];

    // Antigravity (agy) — from `/help`. Built-ins only; injected skills (ask-user / worker-prompt /
    // antigravity-guide) are excluded since AgentManager manages those.
    private static readonly (string, string)[] Antigravity =
    [
        ("/add-dir", "Add a directory to the workspace"),
        ("/agents", "List available custom agents"),
        ("/artifact", "View and review artifacts"),
        ("/btw", "Ask a side question without interrupting the current task"),
        ("/changelog", "Show release notes and changes"),
        ("/clear", "Clear conversation and start a new one"),
        ("/config", "Open settings panel"),
        ("/context", "Visualize current context usage"),
        ("/copy", "Copy the last planner response to the clipboard"),
        ("/credits", "Show remaining G1 credits and purchase link"),
        ("/diff", "View uncommitted changes and per-turn diffs"),
        ("/exit", "Exit the CLI"),
        ("/fast", "Agent executes tasks directly — for simple, faster tasks"),
        ("/feedback", "Submit qualitative feedback to improve the agent"),
        ("/fork", "Branch the current conversation at this point"),
        ("/help", "Show available commands and keybindings"),
        ("/hooks", "Manage hook configurations for tool events"),
        ("/keybindings", "Set custom keybindings"),
        ("/logout", "Log out"),
        ("/mcp", "Manage MCP servers"),
        ("/model", "Set a model"),
        ("/open", "Open a file or view opened/edited files"),
        ("/permissions", "Manage tool permissions"),
        ("/planning", "Agent plans before executing — for complex/collaborative work"),
        ("/rename", "Rename the current conversation"),
        ("/resume", "Browse and resume past conversations"),
        ("/rewind", "Rewind conversation to a previous message"),
        ("/skills", "List available skills"),
        ("/statusline", "Toggle the statusline"),
        ("/tasks", "View background tasks"),
        ("/title", "Toggle custom terminal window title"),
        ("/usage", "View model quota usage"),
        ("/goal", "Run until the specified goal is completely finished"),
        ("/schedule", "Run an instruction on a recurring schedule or one-time timer"),
        ("/grill-me", "Interview me to align on a plan"),
        ("/teamwork-preview", "Invoke a team of agents to tackle large projects"),
        ("/learn", "Reflect on recent successes/corrections to capture reusable skills or rules"),
    ];

    // Pi (pi) — from `/help`. Built-ins only; `skill:*` entries are skills (AgentManager-managed),
    // excluded. "/"-prefixed for the composer "/" popup (pi shows them bare in its own menu).
    private static readonly (string, string)[] Pi =
    [
        ("/settings", "Open settings menu"),
        ("/model", "Select model (opens selector UI)"),
        ("/scoped-models", "Enable/disable models for Ctrl+P cycling"),
        ("/export", "Export session (HTML default, or a .html/.jsonl path)"),
        ("/import", "Import and resume a session from a JSONL file"),
        ("/share", "Share session as a secret GitHub gist"),
        ("/copy", "Copy last agent message to clipboard"),
        ("/name", "Set session display name"),
        ("/session", "Show session info and stats"),
        ("/changelog", "Show changelog entries"),
        ("/hotkeys", "Show all keyboard shortcuts"),
        ("/fork", "Create a new fork from a previous user message"),
        ("/clone", "Duplicate the current session at the current position"),
        ("/tree", "Navigate session tree (switch branches)"),
        ("/trust", "Save project trust decision for future sessions"),
        ("/login", "Configure provider authentication"),
        ("/logout", "Remove provider authentication"),
        ("/new", "Start a new session"),
        ("/compact", "Manually compact the session context"),
        ("/resume", "Resume a different session"),
        ("/reload", "Reload keybindings, extensions, skills, prompts, and themes"),
        ("/quit", "Quit pi"),
    ];
}
