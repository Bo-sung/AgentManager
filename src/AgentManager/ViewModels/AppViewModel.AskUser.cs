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
            List<ChoiceItem> items;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                items = ParseChoiceItems(doc.RootElement);
            }
            catch { return; } // partial/invalid write — leave file, a later event retries

            if (items.Count < 1) return;
            var s = _allSessions.FirstOrDefault(x => x.Id == sessionId);
            if (s is not null)
                s.ActiveChoice = new ChoiceFlow { Items = items, Structured = true };
            try { File.Delete(path); } catch { }
        }
        catch { }
    }

    /// <summary>Parse the ask spool JSON: a single question ({ question, options, multi }) or a
    /// multi-page flow ({ questions: [ {…}, … ] }).</summary>
    private static List<ChoiceItem> ParseChoiceItems(JsonElement root)
    {
        var items = new List<ChoiceItem>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in qs.EnumerateArray())
                if (BuildChoiceItem(q) is { } it) items.Add(it);
        }
        else if (BuildChoiceItem(root) is { } single) items.Add(single);
        return items;
    }

    private static ChoiceItem? BuildChoiceItem(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var question = el.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String ? q.GetString() : null;
        var multi = el.TryGetProperty("multi", out var m) &&
                    (m.ValueKind == JsonValueKind.True ||
                     (m.ValueKind == JsonValueKind.String && bool.TryParse(m.GetString(), out var b) && b));
        var opts = new List<string>();
        if (el.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array)
            foreach (var x in o.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { } str && str.Trim() is { Length: > 0 } t)
                    opts.Add(t);
        if (opts.Count < 1) return null;
        var item = new ChoiceItem { Question = string.IsNullOrWhiteSpace(question) ? null : question!.Trim(), Multi = multi };
        int n = 1;
        foreach (var op in opts.Take(9)) item.Options.Add(new ChoiceOption((n++).ToString(), op, op)); // marker 1..9
        return item;
    }
}
