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

    /// <summary>Default skill content — worker-prompt authoring (editable in settings).</summary>
    public const string WorkerPromptDefault =
"""
---
name: worker-prompt
description: Use when drafting a prompt to hand to a worker / sub-agent (e.g. "write a worker prompt", "split this into parallel worker tasks", "give me prompts for the workers"). Produces a complete, self-contained, copyable delegation prompt.
---

# Worker prompt authoring

When asked to draft a prompt for a worker/sub-agent, output the ENTIRE prompt
verbatim inside ONE fenced code block so the user can copy it as-is. Never a summary.

## Format
- Wrap the whole prompt in a single fenced block. If the prompt itself contains
  ``` fences, use a LONGER outer fence (````text ... ````) so it stays one block.
- One block per worker. For multiple workers, a short "**-> Worker A (...)**" heading
  above each block.
- Never truncate or "...". The reader copies it directly into the worker.

## Each prompt MUST contain
1. Role / phase - what this worker builds, and the repo path.
2. Read first - exact context files (paths) + the source files whose public APIs it
   must read (instruct it to read, not guess).
3. Task - concrete deliverables: exact file paths, signatures, behavior, naming.
4. Constraints - what NOT to touch, code style, allowed libraries.
5. Verify before finishing - exact build/test command + expected result
   (e.g. "0 warnings, 0 errors"), and what to run.
6. Report contract - "reply with a TERSE summary (<=8 lines): confirm build,
   note key decisions/assumptions; DO NOT paste full file contents."

## Don'ts
- Don't paste full file contents back into the report.
- Don't assume APIs - make the worker read them.
- Don't give overlapping file ownership to parallel workers.
""";
}
