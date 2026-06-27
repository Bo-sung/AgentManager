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

    /// <summary>Delete a session's agent branch (call after its worktree is removed, else git refuses).
    /// Safe delete only (<c>git branch -d</c>): a branch carrying unmerged commits is left intact, so
    /// nothing is lost — without this, merged session branches pile up in the repo forever.
    /// Returns true only when the branch was actually deleted.</summary>
    public static async Task<bool> RemoveBranchAsync(string repoPath, string branch)
    {
        if (string.IsNullOrWhiteSpace(branch)) return false;
        try { return (await RunAsync(repoPath, "branch", "-d", branch)).code == 0; }
        catch { return false; }
    }

    /// <summary>Drop all changes in the worktree (tracked + untracked) back to HEAD. Worktree is kept.</summary>
    public static async Task<(bool ok, string message)> DiscardAsync(string worktreePath)
    {
        var reset = await RunAsync(worktreePath, "reset", "--hard", "HEAD");
        if (reset.code != 0) return (false, reset.stderr.Trim());
        await RunAsync(worktreePath, "clean", "-fdx");
        return (true, "변경을 폐기했습니다");
    }

    /// <summary>Commit the worktree's changes onto its agent branch only (no merge) —
    /// keeps the work safe/reviewable without touching the project's main branch.</summary>
    public static async Task<(bool ok, string message)> CommitAsync(string worktreePath, string commitMessage)
    {
        await RunAsync(worktreePath, "add", "-A");
        var status = await RunAsync(worktreePath, "status", "--porcelain");
        if (string.IsNullOrWhiteSpace(status.stdout))
            return (false, "커밋할 변경이 없습니다");
        var commit = await RunAsync(worktreePath, "commit", "-m", commitMessage);
        return commit.code == 0
            ? (true, "에이전트 브랜치에 커밋했습니다 (머지 안 함)")
            : (false, "commit 실패: " + (commit.stdout + commit.stderr).Trim());
    }

    /// <summary>
    /// Commit the worktree's changes to its branch and merge that branch into the project's
    /// current branch. On failure (dirty main tree / conflict) the merge is aborted and the
    /// commit remains on the agent branch (nothing lost).
    /// </summary>
    public static async Task<(bool ok, string message)> MergeAsync(string repoPath, string branch, string commitMessage, string worktreePath)
    {
        await RunAsync(worktreePath, "add", "-A");
        var status = await RunAsync(worktreePath, "status", "--porcelain");
        if (string.IsNullOrWhiteSpace(status.stdout))
            return (false, "머지할 변경이 없습니다");

        var commit = await RunAsync(worktreePath, "commit", "-m", commitMessage);
        if (commit.code != 0)
            return (false, "commit 실패: " + (commit.stdout + commit.stderr).Trim());

        var merge = await RunAsync(repoPath, "merge", "--no-ff", branch, "-m", $"Merge agent branch {branch}");
        if (merge.code != 0)
        {
            await RunAsync(repoPath, "merge", "--abort");
            return (false, "머지 실패(메인 작업트리 dirty 또는 충돌). 변경은 브랜치 '" + branch + "'에 커밋됨: " + (merge.stdout + merge.stderr).Trim());
        }
        return (true, $"'{branch}' → 메인 머지 완료");
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

    /// <summary>Unified diff for the worktree (optionally one file), including untracked files.</summary>
    public static async Task<string> GetDiffAsync(string worktreePath, string? file = null)
    {
        var args = file is null ? new[] { "diff", "HEAD" } : ["diff", "HEAD", "--", file];
        var r = await RunAsync(worktreePath, args);
        var diff = r.stdout;

        if (file is not null)
        {
            if (!string.IsNullOrWhiteSpace(diff) || !await IsUntrackedAsync(worktreePath, file))
                return diff;
            return await BuildUntrackedDiffAsync(worktreePath, file);
        }

        var untracked = await RunAsync(worktreePath, "ls-files", "--others", "--exclude-standard");
        foreach (var path in untracked.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            diff += await BuildUntrackedDiffAsync(worktreePath, path);

        return diff;
    }

    private static async Task<bool> IsUntrackedAsync(string worktreePath, string file)
    {
        var r = await RunAsync(worktreePath, "ls-files", "--others", "--exclude-standard", "--", file);
        return r.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => string.Equals(p, file, StringComparison.Ordinal));
    }

    private static async Task<string> BuildUntrackedDiffAsync(string worktreePath, string file)
    {
        var root = System.IO.Path.GetFullPath(worktreePath);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, file.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return "";

        var bytes = await File.ReadAllBytesAsync(full);
        var sb = new StringBuilder();
        sb.AppendLine($"diff --git a/{file} b/{file}");
        sb.AppendLine("new file mode 100644");
        sb.AppendLine("index 0000000..0000000");
        sb.AppendLine("--- /dev/null");
        sb.AppendLine($"+++ b/{file}");

        if (bytes.Contains((byte)0))
        {
            sb.AppendLine($"Binary files /dev/null and b/{file} differ");
            return sb.ToString();
        }

        var text = Utf8.GetString(bytes).Replace("\r\n", "\n");
        var lines = text.Split('\n');
        sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
        foreach (var line in lines.Take(4000))
            sb.Append('+').AppendLine(line);
        if (lines.Length > 4000)
            sb.AppendLine("+... diff truncated ...");
        return sb.ToString();
    }
}
