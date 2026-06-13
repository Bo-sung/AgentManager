using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace AgentManager.ViewModels;

public sealed record ComposerSuggestionItem(string Label, string Value, string Type);

public partial class AppViewModel
{
    private bool _isComposerSuggestionOpen;
    public bool IsComposerSuggestionOpen
    {
        get => _isComposerSuggestionOpen;
        set => Set(ref _isComposerSuggestionOpen, value);
    }

    private string _composerSuggestionHeader = "";
    public string ComposerSuggestionHeader
    {
        get => _composerSuggestionHeader;
        set => Set(ref _composerSuggestionHeader, value);
    }

    public ObservableCollection<ComposerSuggestionItem> ComposerSuggestions { get; } = [];

    private ComposerSuggestionItem? _selectedComposerSuggestion;
    public ComposerSuggestionItem? SelectedComposerSuggestion
    {
        get => _selectedComposerSuggestion;
        set => Set(ref _selectedComposerSuggestion, value);
    }

    private int _suggestionStartTokenIndex = -1;
    private char _suggestionMode = '\0';

    public void TriggerComposerSuggestion(char mode, string query, int tokenStartIndex)
    {
        _suggestionMode = mode;
        _suggestionStartTokenIndex = tokenStartIndex;
        ComposerSuggestions.Clear();

        if (mode == '@')
        {
            ComposerSuggestionHeader = L("L.OrchMentionHeader");

            // 1. Sessions
            foreach (var s in Sessions)
            {
                if (string.IsNullOrWhiteSpace(query) || s.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    ComposerSuggestions.Add(new ComposerSuggestionItem(s.Title, s.Title, "Session"));
                }
            }

            // 2. Files
            string searchDir = "";
            if (ActiveSession != null && !string.IsNullOrWhiteSpace(ActiveSession.WorktreePath))
            {
                searchDir = ActiveSession.WorktreePath;
            }
            else if (ActiveProject != null && !string.IsNullOrWhiteSpace(ActiveProject.Path))
            {
                searchDir = ActiveProject.Path;
            }
            else if (!string.IsNullOrWhiteSpace(NewProjectPath))
            {
                searchDir = NewProjectPath;
            }

            if (!string.IsNullOrEmpty(searchDir) && Directory.Exists(searchDir))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(searchDir);
                    var files = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                                       .Where(f => !f.FullName.Contains(".git") && !f.FullName.Contains("bin") && !f.FullName.Contains("obj"))
                                       .Where(f => string.IsNullOrWhiteSpace(query) || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                                       .Take(20);
                    foreach (var file in files)
                    {
                        var relPath = Path.GetRelativePath(searchDir, file.FullName);
                        ComposerSuggestions.Add(new ComposerSuggestionItem(relPath, relPath, "File"));
                    }
                }
                catch
                {
                    // ignore IO exceptions
                }
            }
        }
        else if (mode == '/')
        {
            ComposerSuggestionHeader = L("L.OrchActionHeader");

            var actions = new List<ComposerSuggestionItem>
            {
                new ComposerSuggestionItem("/clear - " + L("L.ActionClearDesc"), "/clear", "Command"),
                new ComposerSuggestionItem("/review - " + L("L.ActionReviewDesc"), "/review", "Command"),
                new ComposerSuggestionItem("/settings - " + L("L.ActionSettingsDesc"), "/settings", "Command"),
                new ComposerSuggestionItem("/help - " + L("L.ActionHelpDesc"), "/help", "Command")
            };

            foreach (var act in actions)
            {
                if (string.IsNullOrWhiteSpace(query) || act.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    ComposerSuggestions.Add(act);
                }
            }
        }

        if (ComposerSuggestions.Count > 0)
        {
            IsComposerSuggestionOpen = true;
            if (SelectedComposerSuggestion == null || !ComposerSuggestions.Contains(SelectedComposerSuggestion))
            {
                SelectedComposerSuggestion = ComposerSuggestions[0];
            }
        }
        else
        {
            IsComposerSuggestionOpen = false;
        }
    }

    public void CloseComposerSuggestion()
    {
        IsComposerSuggestionOpen = false;
        ComposerSuggestions.Clear();
        SelectedComposerSuggestion = null;
        _suggestionStartTokenIndex = -1;
        _suggestionMode = '\0';
    }

    public void ApplySuggestion(TextBox tb)
    {
        if (SelectedComposerSuggestion == null || _suggestionStartTokenIndex == -1) return;

        var insertVal = SelectedComposerSuggestion.Value;
        if (_suggestionMode == '@' && SelectedComposerSuggestion.Type == "File")
        {
            string searchDir = ActiveSession?.WorktreePath ?? ActiveProject?.Path ?? "";
            if (!string.IsNullOrEmpty(searchDir))
            {
                var absPath = Path.Combine(searchDir, insertVal).Replace("\\", "/");
                insertVal = $"[{insertVal}](file:///{absPath})";
            }
        }

        var text = tb.Text ?? "";
        var caret = tb.CaretIndex;
        if (caret < _suggestionStartTokenIndex) caret = text.Length;

        var before = text.Substring(0, _suggestionStartTokenIndex);
        var after = text.Substring(caret);

        // slash command specific handlers
        if (_suggestionMode == '/' && insertVal.StartsWith("/"))
        {
            if (insertVal == "/clear")
            {
                if (ActiveSession != null) ActiveSession.Draft = "";
                CloseComposerSuggestion();
                tb.Focus();
                return;
            }
            if (insertVal == "/review")
            {
                ToggleReviewCommand.Execute(null);
                if (ActiveSession != null) ActiveSession.Draft = "";
                CloseComposerSuggestion();
                tb.Focus();
                return;
            }
            if (insertVal == "/settings")
            {
                ShowSettingsCommand.Execute(null);
                if (ActiveSession != null) ActiveSession.Draft = "";
                CloseComposerSuggestion();
                tb.Focus();
                return;
            }
            if (insertVal == "/help")
            {
                if (ActiveSession != null)
                {
                    ActiveSession.Draft = "Help: Use @ to mention files or sessions, and / to trigger slash actions.";
                    CloseComposerSuggestion();
                    tb.CaretIndex = ActiveSession.Draft.Length;
                }
                else
                {
                    CloseComposerSuggestion();
                }
                tb.Focus();
                return;
            }
        }

        tb.Text = before + insertVal + " " + after;
        tb.CaretIndex = _suggestionStartTokenIndex + insertVal.Length + 1;
        tb.Focus();

        CloseComposerSuggestion();
    }
}
