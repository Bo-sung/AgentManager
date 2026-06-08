using System.Diagnostics;
using System.Text;

namespace AgentManager.Core.Workspace;

public enum ChangeKind { Added, Modified, Deleted, Renamed, Untracked }

public sealed record FileChange(string Path, ChangeKind Kind, int Added, int Deleted);

public sealed record WorktreeInfo(string Path, string Branch, bool Isolated);

/// <summary>
/// Per-session git worktree isolation: each agent session works in its own worktree
/// branched from the project HEAD, so parallel agents never clobber each other or the
/// main working tree. Changes are reviewed/merged/discarded afterward.
/// </summary>
public static class GitWorktree
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static async Task<(int code, string stdout, string stderr)> RunAsync(string workdir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await outTask, await errTask);
    }

    public static async Task<bool> IsGitRepoAsync(string dir)
    {
        try { return (await RunAsync(dir, "rev-parse", "--is-inside-work-tree")).code == 0; }
        catch { return false; }
    }

    /// <summary>Create a worktree for a session branched from the repo's current HEAD.</summary>
    public static async Task<WorktreeInfo?> CreateAsync(string repoPath, string sessionId, string branch, string worktreesRoot)
    {
        if (!await IsGitRepoAsync(repoPath)) return null;
        var path = System.IO.Path.Combine(worktreesRoot, sessionId);
        // -B replaces a stale branch of the same name; worktree add checks out a new working copy.
        var r = await RunAsync(repoPath, "worktree", "add", "-B", branch, path, "HEAD");
        if (r.code != 0) return null;
        return new WorktreeInfo(path, branch, true);
    }

    public static async Task RemoveAsync(string repoPath, string worktreePath)
    {
        try { await RunAsync(repoPath, "worktree", "remove", "--force", worktreePath); } catch { }
    }

    /// <summary>Files changed in the worktree vs its HEAD (includes untracked).</summary>
    public static async Task<IReadOnlyList<FileChange>> GetChangedFilesAsync(string worktreePath)
    {
        var list = new List<FileChange>();
        var status = await RunAsync(worktreePath, "status", "--porcelain=v1", "--untracked-files=all");
        if (status.code != 0) return list;

        // line counts for tracked changes
        var numstat = await RunAsync(worktreePath, "diff", "--numstat", "HEAD");
        var counts = new Dictionary<string, (int add, int del)>();
        foreach (var line in numstat.stdout.Split('\n'))
        {
            var parts = line.Trim().Split('\t');
            if (parts.Length >= 3)
            {
                _ = int.TryParse(parts[0], out var a);
                _ = int.TryParse(parts[1], out var d);
                counts[parts[2]] = (a, d);
            }
        }

        foreach (var raw in status.stdout.Split('\n'))
        {
            if (raw.Length < 4) continue;
            var xy = raw[..2];
            var path = raw[3..].Trim();
            var kind = xy switch
            {
                "??" => ChangeKind.Untracked,
                var s when s.Contains('A') => ChangeKind.Added,
                var s when s.Contains('D') => ChangeKind.Deleted,
                var s when s.Contains('R') => ChangeKind.Renamed,
                _ => ChangeKind.Modified,
            };
            var (add, del) = counts.TryGetValue(path, out var c) ? c : (0, 0);
            list.Add(new FileChange(path, kind, add, del));
        }
        return list;
    }

    /// <summary>Unified diff for the worktree (optionally one file).</summary>
    public static async Task<string> GetDiffAsync(string worktreePath, string? file = null)
    {
        var args = file is null ? new[] { "diff", "HEAD" } : ["diff", "HEAD", "--", file];
        var r = await RunAsync(worktreePath, args);
        return r.stdout;
    }
}
