using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace AgentManager.ViewModels;

/// <summary>
/// Structured "ask the user to choose" — the ask-user skill writes a JSON file
/// ({ question, options }) to <c>&lt;cwd&gt;/.am/ask/&lt;sessionId&gt;/</c>; this watches that dir,
/// ingests the choice, and renders it in the session's quick-reply panel (with a real question
/// header + reliable options — beyond what the heuristic text parser can do). Single-select for now.
/// </summary>
public partial class AppViewModel
{
    private readonly HashSet<string> _watchedAskDirs = [];
    private readonly List<FileSystemWatcher> _askWatchers = [];

    /// <summary>Watch this session's <c>&lt;cwd&gt;/.am/ask/&lt;sessionId&gt;/</c> (session-scoped, like the
    /// task spool). Idempotent per directory.</summary>
    private void WatchSessionAskSpool(string cwd, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return;
        try
        {
            var dir = Path.Combine(cwd, ".am", "ask", sessionId);
            Directory.CreateDirectory(dir);
            if (!_watchedAskDirs.Add(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*.json")) ScheduleAskIngest(f, sessionId);
            var w = new FileSystemWatcher(dir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Created += (_, e) => ScheduleAskIngest(e.FullPath, sessionId);
            w.Changed += (_, e) => ScheduleAskIngest(e.FullPath, sessionId);
            w.Renamed += (_, e) => ScheduleAskIngest(e.FullPath, sessionId);
            _askWatchers.Add(w); // keep alive for the app's lifetime
        }
        catch { /* best-effort */ }
    }

    private void ScheduleAskIngest(string path, string sessionId) =>
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await System.Threading.Tasks.Task.Delay(150); // FS event may fire mid-write
            IngestAskFile(path, sessionId);
        });

    private void IngestAskFile(string path, string sessionId)
    {
        try
        {
            if (!File.Exists(path)) return;
            string? question;
            var options = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                question = root.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
                if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    foreach (var o in opts.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String && o.GetString() is { } str && str.Trim() is { Length: > 0 } t)
                            options.Add(t);
            }
            catch { return; } // partial/invalid write — leave file, a later event retries

            if (options.Count < 1) return;
            var s = _allSessions.FirstOrDefault(x => x.Id == sessionId);
            if (s is not null)
            {
                s.QuickReplies.Clear();
                int n = 1;
                foreach (var o in options.Take(8))
                    s.QuickReplies.Add(new Core.QuickReplyOption((n++).ToString(), o, o)); // marker=number, label=text=option
                s.ChoiceQuestion = string.IsNullOrWhiteSpace(question) ? null : question!.Trim();
            }
            try { File.Delete(path); } catch { }
        }
        catch { }
    }
}
