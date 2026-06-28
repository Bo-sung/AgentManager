using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

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
    private int _suggestionQueryLength;

    public void TriggerComposerSuggestion(char mode, string query, int tokenStartIndex)
    {
        _suggestionMode = mode;
        _suggestionStartTokenIndex = tokenStartIndex;
        _suggestionQueryLength = query?.Length ?? 0;
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
            // 엔진 슬래시 명령: cc는 커스텀 명령(.claude/commands)을 발견해 자동완성, 선택 시 그대로 전달.
            // 다른 엔진은 발견 목록 없음 — 미인식 /명령은 전송 시 텍스트로 엔진에 패스스루된다.
            ComposerSuggestionHeader = L("L.OrchSlashHeader");
            if (ActiveSession?.AgentId == "cc")
            {
                foreach (var cmd in CcSlashCommands(ActiveSession.ProjectPath))
                    if (string.IsNullOrWhiteSpace(query) || cmd.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                        ComposerSuggestions.Add(cmd);
            }
        }
        else if (mode == '>')
        {
            // 앱 액션(엔진 무관) — '/'를 엔진에 양보하고 '>' 접두로 분리.
            ComposerSuggestionHeader = L("L.OrchActionHeader");
            var actions = new List<ComposerSuggestionItem>
            {
                new ComposerSuggestionItem(">clear - " + L("L.ActionClearDesc"), ">clear", "Command"),
                new ComposerSuggestionItem(">review - " + L("L.ActionReviewDesc"), ">review", "Command"),
                new ComposerSuggestionItem(">settings - " + L("L.ActionSettingsDesc"), ">settings", "Command"),
                new ComposerSuggestionItem(">help - " + L("L.ActionHelpDesc"), ">help", "Command"),
            };
            foreach (var act in actions)
                if (string.IsNullOrWhiteSpace(query) || act.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                    ComposerSuggestions.Add(act);
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
        _suggestionQueryLength = 0;
    }

    /// <summary>@·/·&gt; 토큰 스캔(View에서 이관): caret 직전부터 공백 전까지 거슬러 올라가 트리거('@' 멘션 ·
    /// '/' 엔진 슬래시 명령 · '&gt;' 앱 액션)를 찾는다.</summary>
    public void UpdateComposerSuggestion(string text, int caret)
    {
        text ??= "";
        if (caret < 0 || caret > text.Length) return;

        int tokenStart = -1;
        char mode = '\0';
        for (int i = caret - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == ' ' || c == '\n' || c == '\r') break;
            if (c == '@' || c == '/' || c == '>') { tokenStart = i; mode = c; break; }
        }

        if (tokenStart != -1)
        {
            var query = text.Substring(tokenStart + 1, caret - (tokenStart + 1));
            TriggerComposerSuggestion(mode, query, tokenStart);
        }
        else
        {
            CloseComposerSuggestion();
        }
    }

    // ----- Claude Code 커스텀 슬래시 명령 발견 (.claude/commands) -----
    private string? _ccCmdProject;
    private List<ComposerSuggestionItem>? _ccCmds;
    private DateTime _ccCmdAt;

    /// <summary>cc 커스텀 슬래시 명령을 스캔: 프로젝트 <c>.claude/commands</c>(우선) + 사용자
    /// <c>~/.claude/commands</c>. 하위 폴더는 네임스페이스(<c>/ns:cmd</c>). 프로젝트별 캐시(5초 TTL —
    /// 빠른 타이핑엔 디스크 I/O 안 하고, 새로 만든 명령은 곧 반영).</summary>
    private List<ComposerSuggestionItem> CcSlashCommands(string? projectPath)
    {
        if (_ccCmds is not null && _ccCmdProject == projectPath && (DateTime.UtcNow - _ccCmdAt).TotalSeconds < 5)
            return _ccCmds;
        var list = new List<ComposerSuggestionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(projectPath)) roots.Add(Path.Combine(projectPath, ".claude", "commands"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "commands"));
        foreach (var dir in roots) // 프로젝트가 사용자보다 우선(먼저 추가 → seen 선점)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(dir, file);
                    var name = rel[..^3].Replace(Path.DirectorySeparatorChar, ':').Replace(Path.AltDirectorySeparatorChar, ':');
                    if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
                    var desc = ReadCommandDescription(file);
                    list.Add(new ComposerSuggestionItem(
                        string.IsNullOrWhiteSpace(desc) ? "/" + name : $"/{name} - {desc}", "/" + name, "Command"));
                }
            }
            catch { }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
        _ccCmds = list; _ccCmdProject = projectPath; _ccCmdAt = DateTime.UtcNow;
        return list;
    }

    private static string? ReadCommandDescription(string file)
    {
        try
        {
            foreach (var line in File.ReadLines(file).Take(12))
            {
                var t = line.Trim();
                if (t.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    return t["description:".Length..].Trim().Trim('"', '\'');
            }
        }
        catch { }
        return null;
    }

    /// <summary>선택된 서제스천을 ActiveSession.Draft에 적용하고 원하는 캐럿 위치를 반환. 적용 안 되면 -1.</summary>
    public int ApplySelectedSuggestion()
    {
        if (SelectedComposerSuggestion == null || _suggestionStartTokenIndex == -1 || ActiveSession is not { } session) return -1;

        var insertVal = SelectedComposerSuggestion.Value;
        if (_suggestionMode == '@' && SelectedComposerSuggestion.Type == "File")
        {
            string searchDir = session.WorktreePath ?? ActiveProject?.Path ?? "";
            if (!string.IsNullOrEmpty(searchDir))
            {
                var absPath = Path.Combine(searchDir, insertVal).Replace("\\", "/");
                insertVal = $"[{insertVal}](file:///{absPath})";
            }
        }

        var text = session.Draft ?? "";
        var caret = _suggestionStartTokenIndex + 1 + _suggestionQueryLength;
        if (caret > text.Length) caret = text.Length;

        var before = text[.._suggestionStartTokenIndex];
        var after = text[caret..];

        // 앱 액션('>')만 인앱에서 가로채 실행한다. '/'(엔진 슬래시 명령)는 그대로 드래프트에 삽입되어
        // 전송 시 엔진으로 전달된다(cc는 커스텀 명령을 확장).
        if (_suggestionMode == '>' && insertVal.StartsWith(">"))
        {
            if (insertVal == ">clear")
            {
                session.Draft = "";
                CloseComposerSuggestion();
                return 0;
            }
            if (insertVal == ">review")
            {
                ToggleReviewCommand.Execute(null);
                session.Draft = "";
                CloseComposerSuggestion();
                return 0;
            }
            if (insertVal == ">settings")
            {
                ShowSettingsCommand.Execute(null);
                session.Draft = "";
                CloseComposerSuggestion();
                return 0;
            }
            if (insertVal == ">help")
            {
                session.Draft = "Help: @ mentions files/sessions · / engine slash commands · > app actions.";
                var len = session.Draft.Length;
                CloseComposerSuggestion();
                return len;
            }
        }

        session.Draft = before + insertVal + " " + after;
        CloseComposerSuggestion();
        return _suggestionStartTokenIndex + insertVal.Length + 1;
    }
}
