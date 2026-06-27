using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentManager.Core;

/// <summary>
/// Writes a shared SKILL.md (the Agent Skills open standard) into each engine's skills
/// directory, so cc/gx/agy/pi all pick up the same user skill. Engines use the same format
/// and differ only in the install location.
/// </summary>
public static partial class SkillInjector
{
    /// <summary>Default per-engine user-skills directory. Editable in settings (engines vary).</summary>
    public static Dictionary<string, string> DefaultDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new Dictionary<string, string>
        {
            ["cc"]  = Path.Combine(home, ".claude", "skills"),
            ["gx"]  = Path.Combine(home, ".codex", "skills"),
            ["agy"] = Path.Combine(home, ".gemini", "antigravity-cli", "skills"),
            ["pi"]  = Path.Combine(home, ".pi", "agent", "skills"),
        };
    }

    /// <summary>Write the skill into one engine's dir as <c>&lt;dir&gt;/&lt;name&gt;/SKILL.md</c>.
    /// Returns null on success, otherwise a short error message.</summary>
    public static string? InjectTo(string dir, string content)
    {
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(content)) return "empty";
        try
        {
            var target = Path.Combine(ExpandHome(dir.Trim()), SkillName(content));
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "SKILL.md"), content.Replace("\r\n", "\n"), new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Inject into every engine that has a non-empty dir. Returns engineId → null(ok) | error.</summary>
    public static Dictionary<string, string?> Inject(string content, IReadOnlyDictionary<string, string> dirs)
    {
        var results = new Dictionary<string, string?>();
        foreach (var (engine, dir) in dirs)
            if (!string.IsNullOrWhiteSpace(dir))
                results[engine] = InjectTo(dir, content);
        return results;
    }

    /// <summary>Skill folder name = the frontmatter <c>name:</c> (sanitized), else "worker-prompt".</summary>
    static string SkillName(string content)
    {
        var m = NameRx().Match(content);
        var raw = m.Success ? m.Groups[1].Value.Trim() : "worker-prompt";
        raw = Regex.Replace(raw, @"[^A-Za-z0-9._-]", "-").Trim('-');
        return raw.Length == 0 ? "worker-prompt" : raw;
    }

    static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.Length <= 2 ? home : Path.Combine(home, path[2..]);
        }
        return path;
    }

    [GeneratedRegex(@"(?m)^name:\s*(.+)$")]
    private static partial Regex NameRx();

    /// <summary>Default skill content — worker task authoring (editable in settings).
    /// Registers tasks into the AgentManager backlog via a spool dir instead of pasting
    /// copyable blocks, so there are no fenced-block / copy issues.</summary>
    public const string WorkerPromptDefault =
"""
---
name: worker-prompt
description: Use when planning or drafting prompts to hand to worker sub-agents (e.g. "write worker prompts", "split this into parallel worker tasks", "give me prompts for the workers"). Registers each task into the AgentManager worker backlog.
---

# Worker task authoring

When asked to plan work for worker sub-agents, DO NOT paste prompts into the chat.
Instead REGISTER each task as a file in the spool directory, then reply with only a
short confirmation. This avoids copy/paste and lets the user review + assign in the UI.

## Where to write
- The spool directory is in the env var `AGENTMANAGER_TASK_SPOOL`. If it is set, write there.
- If it is NOT set (running outside AgentManager), create and write to `./.am/worker-tasks/`.

## How to write
For each task, write ONE JSON file to the spool dir (e.g. `1.json`, `2.json`). Shape:
{ "title": "<short label>", "prompt": "<the FULL, self-contained delegation prompt>", "engine": "<cc|gx|agy|pi, or omit>" }
- `prompt` is a single JSON string — escape newlines as \n. Put the ENTIRE prompt there, never a summary.
- One file per worker task. Do not give overlapping file ownership to parallel tasks.

## Each task's "prompt" MUST contain
1. Role / phase - what this worker builds, and the repo path.
2. Read first - exact context files (paths) + the source files whose public APIs it must read.
3. Task - concrete deliverables: exact file paths, signatures, behavior, naming.
4. Constraints - what NOT to touch, code style, allowed libraries.
5. Verify before finishing - exact build/test command + expected result, and what to run.
6. Report contract - "reply with a TERSE summary (<=8 lines); do NOT paste full file contents."

## After writing
Reply with ONLY a short confirmation: how many tasks you registered, and their titles
(one per line). Do NOT paste the task prompts back into the chat.

## Don'ts
- Don't paste task prompts into chat — they go to the spool.
- Don't assume APIs - make each worker read them.
- Don't give two parallel tasks ownership of the same file.
""";

    /// <summary>Second skill — structured "ask the user to choose". Writes a JSON file to a spool
    /// dir that AgentManager renders as a clickable choice panel (so models other than the host
    /// get the same structured choice UI). Injected alongside <see cref="WorkerPromptDefault"/>.</summary>
    public const string AskUserDefault =
"""
---
name: ask-user
description: Use whenever you would otherwise ask the user to pick between options (e.g. "which approach?", "A or B?", "what should I do next?"). Renders a clickable choice panel in AgentManager instead of listing options as plain text.
---

# Ask the user to choose

When you want the user to choose between options, DO NOT list "A) … B) …" in the chat.
Instead WRITE ONE JSON file to the spool dir and STOP your turn — AgentManager shows a
clickable panel and sends the user's choice back as your next message.

## Where to write
- The spool dir is in the env var `AGENTMANAGER_ASK_SPOOL`. If it is set, write there.
- If it is NOT set (running outside AgentManager), fall back to plain "A) … B) …" text.

## How to write
Write ONE JSON file named `ask.json` to the spool dir.

Single question:
{ "question": "<one short question>", "options": ["<option 1>", "<option 2>", ...], "multi": false }
- `question`: a single short line shown as the panel header.
- `options`: 2–9 short option labels (the text the user sees and picks).
- `multi` (optional): true = the user can check several options and submit them together;
  false/omitted = single pick.

Several questions in a row (a wizard — the panel pages through them, then sends all answers):
{ "questions": [
    { "question": "<q1>", "options": ["a", "b"] },
    { "question": "<q2>", "options": ["x", "y", "z"], "multi": true }
] }

## After writing
Reply with ONLY a one-line note (e.g. "선택지를 띄웠습니다.") and STOP. Wait for the user's
choice — it arrives as your next message. Do NOT also paste the options as text.
""";
}
