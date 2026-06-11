using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using AgentManager.Persistence;

namespace AgentManager.Views;

public partial class ActivityHistoryWindow : Window, INotifyPropertyChanged
{
    private readonly ObservableCollection<HistoryRow> _rows = [];
    private string _filterText = "";
    private string _summaryText = "0 sessions · 0 projects";
    private string _filterSummaryText = "0 shown";

    public ActivityHistoryWindow()
    {
        InitializeComponent();
        RowsView = CollectionViewSource.GetDefaultView(_rows);
        RowsView.Filter = FilterRow;
        DataContext = this;
        LoadRows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView RowsView { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
            RowsView.Refresh();
            UpdateFilterSummary();
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set
        {
            if (_summaryText == value) return;
            _summaryText = value;
            OnPropertyChanged();
        }
    }

    public string FilterSummaryText
    {
        get => _filterSummaryText;
        private set
        {
            if (_filterSummaryText == value) return;
            _filterSummaryText = value;
            OnPropertyChanged();
        }
    }

    private void LoadRows()
    {
        var state = AppStateStore.Load();
        var sessions = state?.Sessions ?? [];
        var projects = state?.Projects ?? [];

        _rows.Clear();
        foreach (var row in sessions
                     .OrderByDescending(s => s.StartedAt)
                     .Select(HistoryRow.FromDto))
        {
            _rows.Add(row);
        }

        SummaryText = $"{sessions.Count} sessions · {projects.Count} projects";
        RowsView.Refresh();
        UpdateFilterSummary();
    }

    private bool FilterRow(object item)
    {
        if (item is not HistoryRow row) return false;
        if (string.IsNullOrWhiteSpace(FilterText)) return true;

        var filter = FilterText.Trim();
        return Contains(row.Title, filter)
               || Contains(row.Project, filter)
               || Contains(row.Status, filter);
    }

    private static bool Contains(string value, string filter)
        => value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private void UpdateFilterSummary()
    {
        var shown = RowsView.Cast<object>().Count();
        FilterSummaryText = string.IsNullOrWhiteSpace(FilterText)
            ? $"{shown} shown"
            : $"{shown} shown after filter";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadRows();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record HistoryRow(
        string Badge,
        string Title,
        string Project,
        string Branch,
        string Status,
        string StartedText,
        string TokensText,
        string CostText,
        string BlocksText,
        bool IsArchived)
    {
        public static HistoryRow FromDto(SessionDto session)
        {
            var title = string.IsNullOrWhiteSpace(session.Title) ? "(untitled)" : session.Title;
            var project = string.IsNullOrWhiteSpace(session.Project) ? "(no project)" : session.Project;
            var branch = string.IsNullOrWhiteSpace(session.Branch) ? "(no branch)" : session.Branch;
            var status = (session.Status ?? "").Trim().ToLowerInvariant();
            var badge = BadgeFor(session.AgentId);

            return new HistoryRow(
                badge,
                title,
                project,
                branch,
                status,
                session.StartedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture),
                $"{session.TokensIn:n0}/{session.TokensOut:n0}",
                session.CostUsd.ToString("$0.000", CultureInfo.InvariantCulture),
                $"{session.Transcript.Count:n0} blocks",
                session.IsArchived);
        }

        private static string BadgeFor(string agentId)
        {
            var value = (agentId ?? "").Trim();
            return value.ToLowerInvariant() switch
            {
                "cc" or "claude" or "claude-code" => "CC",
                "gx" or "codex" => "GX",
                _ when value.Length >= 2 => value[..2].ToUpperInvariant(),
                _ when value.Length == 1 => value.ToUpperInvariant(),
                _ => "--",
            };
        }
    }
}
