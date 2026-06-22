namespace AgentManager.ViewModels;

/// <summary>
/// 선택/내비게이션 커맨드 — 이전에는 View code-behind 클릭 핸들러가 VM을 직접 변이했다.
/// MouseClick attached behavior와 함께 써서 code-behind 없이 바인딩한다.
/// 생성자에서 InitNavCommands() 한 줄로 초기화한다(다른 커맨드들과 동일 패턴).
/// </summary>
public sealed partial class AppViewModel
{
    /// <summary>New Agent 엔진 옵션 선택.</summary>
    public RelayCommand SelectEngineCommand { get; private set; } = null!;
    /// <summary>Agents 메뉴: 엔진을 미리 고른 채 New Agent 폼 열기.</summary>
    public RelayCommand NewAgentForEngineCommand { get; private set; } = null!;
    /// <summary>History 행 열기.</summary>
    public RelayCommand OpenHistoryRowCommand { get; private set; } = null!;
    /// <summary>Orchestrator 카드 diff: 세션 활성화 + 리뷰 패널 + 세션 뷰 전환.</summary>
    public RelayCommand OpenSessionReviewCommand { get; private set; } = null!;
    /// <summary>Settings 세그먼트 선택. 파라미터 "group:value" (group = policy|accent|density).</summary>
    public RelayCommand SettingsSegCommand { get; private set; } = null!;
    /// <summary>Runtimes 인증 모드 세그. 파라미터 "engine:mode" (engine = cc|gx).</summary>
    public RelayCommand AuthModeCommand { get; private set; } = null!;
    /// <summary>Runtimes: 엔진 CLI 로그인.</summary>
    public RelayCommand SignInCommand { get; private set; } = null!;
    /// <summary>Appearance: 테마 선택(파라미터 = 테마 id).</summary>
    public RelayCommand ThemeSelectCommand { get; private set; } = null!;

    private void InitNavCommands()
    {
        SelectEngineCommand = new RelayCommand(p => { if (p is EngineDef def) NewAgentSelectedEngine = def; });
        NewAgentForEngineCommand = new RelayCommand(p =>
        {
            if (p is string id && Engines.FirstOrDefault(en => en.Id == id) is { } engine)
                NewAgentSelectedEngine = engine;
            ShowNewAgent = true;
        });
        OpenHistoryRowCommand = new RelayCommand(p => { if (p is HistoryRowViewModel row) OpenHistoryRow(row); });
        OpenSessionReviewCommand = new RelayCommand(p =>
        {
            if (p is not SessionViewModel session) return;
            ActiveSession = session;
            IsReviewOpen = true;
            CurrentView = MainViewKind.Session;
        });
        SettingsSegCommand = new RelayCommand(p =>
        {
            if (p is not string tag) return;
            var i = tag.IndexOf(':');
            if (i < 0) return;
            var (group, value) = (tag[..i], tag[(i + 1)..]);
            switch (group)
            {
                case "policy": SettingsApprovalPolicy = value; break;
                case "accent": SettingsAccent = value; break;
                case "density": SettingsDensity = value; break;
            }
        });
        AuthModeCommand = new RelayCommand(p =>
        {
            if (p is not string tag) return;
            var parts = tag.Split(':');
            if (parts.Length != 2) return;
            if (parts[0] == "cc") SettingsAuthCc = parts[1];
            else if (parts[0] == "gx") SettingsAuthGx = parts[1];
        });
        SignInCommand = new RelayCommand(p => { if (p is string id) SignIn(id); });
        ThemeSelectCommand = new RelayCommand(p => { if (p is string id) SettingsTheme = id; });
    }
}
