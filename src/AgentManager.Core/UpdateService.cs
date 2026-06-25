using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentManager.Core;

/// <summary>
/// Checks GitHub for a newer tagged version. UI-agnostic: just the network/version
/// comparison and locating the local git checkout. Applying the update (git pull +
/// rebuild + relaunch) is done by a separate updater process (scripts/update.ps1).
/// </summary>
public sealed class UpdateService
{
    public const string Owner = "Bo-sung";
    public const string Repo = "AgentManager";
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";
    public static string ChangelogUrl => $"{RepoUrl}/blob/master/CHANGELOG.md";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // GitHub requires a User-Agent on every request.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AgentManager-UpdateCheck");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Query the repo tags, pick the highest semver, compare to <paramref name="current"/>.</summary>
    public async Task<UpdateInfo> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/tags?per_page=50";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);

            Version? latest = null;
            string latestTag = "";
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("name", out var nameEl)) continue;
                var v = ParseTag(nameEl.GetString());
                if (v != null && (latest == null || v > latest))
                {
                    latest = v;
                    latestTag = nameEl.GetString()!;
                }
            }

            if (latest == null) return UpdateInfo.Failed("no tags found");
            return new UpdateInfo(Normalize(current), latest, latestTag, latest > Normalize(current));
        }
        catch (Exception ex)
        {
            return UpdateInfo.Failed(ex.Message);
        }
    }

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static Version? ParseTag(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var s = name.Trim().TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Walk up from <paramref name="startDir"/> to find the git checkout root (a folder with .git). Null if not running from a clone.</summary>
    public static string? FindRepoRoot(string? startDir)
    {
        try
        {
            var dir = startDir;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return null;
    }
}

/// <summary>Result of an update check. <see cref="Available"/> is true only when a strictly newer tag exists.</summary>
public sealed record UpdateInfo(Version Current, Version? Latest, string LatestTag, bool Available, string? Error = null)
{
    public static UpdateInfo Failed(string err) => new(new Version(0, 0, 0), null, "", false, err);
}
