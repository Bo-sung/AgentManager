using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    private readonly List<HistoryRowViewModel> _historySource = [];
    private string _historyFilterText = "";
    public string HistoryFilterText
    {
        get => _historyFilterText;
        set
        {
            if (Set(ref _historyFilterText, value ?? ""))
                ApplyHistoryFilters();
        }
    }

    private string _historyAgentFilter = "all";
    public string HistoryAgentFilter
    {
        get => _historyAgentFilter;
        set
        {
            if (Set(ref _historyAgentFilter, string.IsNullOrWhiteSpace(value) ? "all" : value))
                ApplyHistoryFilters();
        }
    }

    private string _historyStatusFilter = "all";
    public string HistoryStatusFilter
    {
        get => _historyStatusFilter;
        set
        {
            if (Set(ref _historyStatusFilter, string.IsNullOrWhiteSpace(value) ? "all" : value))
                ApplyHistoryFilters();
        }
    }

    private string _historySummaryText = "";
    public string HistorySummaryText { get => _historySummaryText; private set => Set(ref _historySummaryText, value); }

    private string _historyFilterSummaryText = "";
    public string HistoryFilterSummaryText { get => _historyFilterSummaryText; private set => Set(ref _historyFilterSummaryText, value); }

    private void RebuildHistoryRows()
    {
        _historySource.Clear();
        foreach (var row in _allSessions
                     .OrderByDescending(s => s.StartedAt)
                     .Select(HistoryRowViewModel.FromSession))
        {
            _historySource.Add(row);
        }

        HistorySummaryText = L("L.SessionsProjectsSummary", _historySource.Count, Projects.Count);
        ApplyHistoryFilters();
    }

    private void ApplyHistoryFilters()
    {
        HistoryRows.Clear();
        foreach (var row in _historySource.Where(MatchesHistoryFilters))
            HistoryRows.Add(row);

        HistoryFilterSummaryText = string.IsNullOrWhiteSpace(HistoryFilterText)
            ? L("L.Shown", HistoryRows.Count)
            : L("L.ShownAfterFilter", HistoryRows.Count);
    }

    private bool MatchesHistoryFilters(HistoryRowViewModel row)
    {
        if (!row.Matches(HistoryFilterText)) return false;
        if (HistoryAgentFilter != "all" && row.AgentId != HistoryAgentFilter) return false;
        return HistoryStatusFilter switch
        {
            "all" => true,
            "active" => row.Status is "running" or "waiting",
            "done" => row.Status == "done",
            "waiting" => row.Status == "waiting",
            "error" => row.Status == "error",
            _ => true,
        };
    }

    public void OpenHistoryRow(HistoryRowViewModel row)
    {
        var session = _allSessions.FirstOrDefault(s => s.Id == row.SessionId);
        if (session is not null)
            ActiveSession = session;
    }

    private async Task LoadCliHistoryAsync(ProjectViewModel? project)
    {
        CliHistory.Clear();
        if (project is null) return;
        var path = project.Path;
        List<CliHistoryEntry> found;
        try { found = await Task.Run(() => CliSessionDiscovery.Discover(path)); }
        catch { return; }
        if (!ReferenceEquals(ActiveProject, project)) return; // 그 사이 프로젝트가 바뀜

        var known = _allSessions.Where(s => !string.IsNullOrEmpty(s.EngineSessionId))
            .Select(s => s.EngineSessionId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        CliHistory.Clear();
        // 가져온(known) 것 + 사용자가 삭제한(dismissed) 것은 재발견에서 제외 — 삭제가 재시작 후에도 유지되게.
        foreach (var e in found.Where(e => !known.Contains(e.SessionId) && !_dismissedCliSessions.Contains(e.SessionId)))
            CliHistory.Add(new CliHistoryItemViewModel(e));
    }

    /// <summary>CLI 기록을 AgentManager 세션으로 가져온다. resume은 원래 cwd에서만 이어지므로
    /// worktree 격리 없이 프로젝트 폴더에서 직접 작업한다.</summary>
    private void ImportCliSession(CliHistoryItemViewModel item)
    {
        var project = ActiveProject;
        if (project is null) return;

        var existing = _allSessions.FirstOrDefault(s =>
            string.Equals(s.EngineSessionId, item.Entry.SessionId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveSession = existing;
            CliHistory.Remove(item);
            return;
        }

        var engine = EngineRegistry.Get(item.Entry.EngineId);
        var s = new SessionViewModel(NewSessionId("s"), engine, item.Title, "(project dir)",
            project.Id, project.Name, project.Path, engine.Models[0])
        {
            TranslationEnabled = TranslationEnabled,
            EngineSessionId = item.Entry.SessionId,
            WorktreeAttempted = true, // resume가 세션을 못 찾게 되므로 worktree 생성 금지
            Activity = L("L.ImportedFromCliHistory"),
        };
        s.Transcript.Add(new WorkingBlock(
            L("L.CliImportMarker", engine.Name, item.Entry.SessionId[..Math.Min(8, item.Entry.SessionId.Length)], item.TimeLabel)));
        s.PropertyChanged += SessionStatusWatch;
        _allSessions.Insert(0, s);
        ActiveSession = s;
        RefreshProjectSessions(selectFirstIfMissing: false);
        CliHistory.Remove(item);
        RefreshCounts();
        RefreshProjectCounts();
        SaveState();
        _ = PopulateImportedTranscriptAsync(s, item);
    }

    /// <summary>가져온 CLI 세션의 과거 대화를 기록 파일에서 복원해 트랜스크립트 앞부분에 채운다.</summary>
    private async Task PopulateImportedTranscriptAsync(SessionViewModel s, CliHistoryItemViewModel item)
    {
        List<CliSessionDiscovery.CliTranscriptItem> history;
        try { history = await Task.Run(() => CliSessionDiscovery.LoadTranscript(item.Entry.EngineId, item.Entry.FilePath)); }
        catch { return; }
        if (history.Count == 0) return;

        // UI 스레드를 독점하지 않도록 청크로 나눠 삽입 (가상화 덕에 화면 밖 블록은 생성 비용 없음)
        var insertAt = 0;
        foreach (var chunk in history.Chunk(50))
        {
            if (!_allSessions.Contains(s)) return; // 그 사이 세션이 삭제됨
            foreach (var h in chunk)
            {
                TranscriptItem block = h.Role switch
                {
                    "user" => new UserBlock(h.Text),
                    "assistant" => new AgentTextBlock(h.Text),
                    "thinking" => new ThinkingBlock(h.Text),
                    _ => new ToolBlock("import-" + insertAt, KindOf(h.Name), h.Name) { Body = h.Text, Stat = "done" },
                };
                s.Transcript.Insert(insertAt++, block);
            }
            await Task.Delay(16); // 한 프레임 양보
        }
        SaveState();
    }

    /// <summary>Re-read this session's engine conversation file (after the user continued it in an external
    /// terminal via "open in terminal") and rebuild the transcript from it. cc/gx/pi only — those have
    /// discoverable session files keyed by EngineSessionId. The engine file is authoritative for the full
    /// conversation, so the transcript is replaced.</summary>
    private async Task ResyncTranscriptAsync(SessionViewModel? s)
    {
        if (s is null || string.IsNullOrEmpty(s.EngineSessionId)) return;
        var cwd = !string.IsNullOrWhiteSpace(s.WorktreePath) ? s.WorktreePath! : s.ProjectPath;
        List<CliSessionDiscovery.CliTranscriptItem> history;
        CliHistoryEntry? entry;
        try
        {
            var entries = await Task.Run(() => CliSessionDiscovery.Discover(cwd));
            // Prefer the MOST RECENT conversation for this cwd+engine, not the originally-recorded id: a
            // terminal `claude --resume` continues into a NEW session file (new id), so matching the old id
            // misses the terminal's new turns. The newest file for this cwd is what the terminal just wrote.
            entry = entries.Where(e => string.Equals(e.EngineId, s.AgentId, StringComparison.OrdinalIgnoreCase))
                           .OrderByDescending(e => e.LastWriteUtc).FirstOrDefault();
            if (entry is null) { s.Transcript.Add(new WorkingBlock(L("L.ResyncNotFound"))); return; }
            // Load the WHOLE conversation, then keep the recent tail: the terminal's new turns are at the
            // END, but LoadTranscript's default cap reads from the TOP, so a long conversation would show
            // its old start and cut off exactly the terminal work the user wants to see.
            var all = await Task.Run(() => CliSessionDiscovery.LoadTranscript(entry.EngineId, entry.FilePath, maxItems: int.MaxValue));
            history = all.Count > 400 ? all.GetRange(all.Count - 400, 400) : all;
        }
        catch (Exception ex) { s.Transcript.Add(new ErrorBlock(L("L.ResyncFailed"), ex.Message)); return; }
        if (history.Count == 0) { s.Transcript.Add(new WorkingBlock(L("L.ResyncNotFound"))); return; }

        s.Transcript.Clear();
        var i = 0;
        foreach (var h in history)
        {
            TranscriptItem block = h.Role switch
            {
                "user" => new UserBlock(h.Text),
                "assistant" => new AgentTextBlock(h.Text),
                "thinking" => new ThinkingBlock(h.Text),
                _ => new ToolBlock("resync-" + i, KindOf(h.Name), h.Name) { Body = h.Text, Stat = "done" },
            };
            s.Transcript.Add(block);
            i++;
        }
        // Follow the continuation: adopt the newest conversation id so future resume/resync chains forward.
        if (!string.Equals(entry.SessionId, s.EngineSessionId, StringComparison.OrdinalIgnoreCase))
            s.EngineSessionId = entry.SessionId;
        s.Transcript.Add(new WorkingBlock(L("L.ResyncDone")));
        SaveState();
    }

}
