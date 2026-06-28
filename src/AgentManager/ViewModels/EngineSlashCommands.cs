using System.Collections.Generic;

namespace AgentManager.ViewModels;

/// <summary>Built-in slash commands per engine (gx/pi/agy), captured verbatim from each CLI's in-TUI
/// <c>/help</c>. cc uses file discovery (<c>.claude/commands</c>) instead. These surface in the composer
/// "/" popup for discovery + autocomplete; many are interactive-TUI features that, in AgentManager's
/// non-interactive runs, pass through as plain text rather than executing.</summary>
public static class EngineSlashCommands
{
    public static IReadOnlyList<(string Cmd, string Desc)> For(string engineId) => engineId switch
    {
        "gx" => Codex,
        // "pi"  => Pi,   // 사용자 /help 목록 받으면 채움
        // "agy" => Antigravity,
        _ => System.Array.Empty<(string, string)>(),
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
}
