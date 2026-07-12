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
using AgentManager.Core.Usage;
using AgentManager.Core;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    // ----- translation + quota -----
    public bool TranslationEnabled
    {
        get => _settings.TranslationEnabled;
        set { if (_settings.TranslationEnabled != value) { _settings.TranslationEnabled = value; OnChanged(nameof(TranslationEnabled)); } }
    }
    private string _quotaText = "";
    public string QuotaText
    {
        get => _quotaText;
        set
        {
            if (Set(ref _quotaText, value))
                OnChanged(nameof(UsageStatusText));
        }
    }
    /// <summary>카드 fallback 텍스트 — 확인 중이거나(행 없음) 데이터 전무일 때만 보인다(UsageFallbackVisible).</summary>
    public string UsageStatusText => _checkingUsage ? L("L.UsageChecking") : L("L.UsageNoData");
    public System.Windows.Visibility UsageFallbackVisible =>
        UsageRows.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>사용량 카드: 활성화된 엔진별 행(이름 + 막대 + 잔여 메모). cc/gx는 실측 또는 "미확인",
    /// agy는 API가 없어 "무료 프리뷰" — 향후 agy가 쿼터를 방출하면 _usageService에 잡혀 자동으로 막대까지 표시된다.</summary>
    public ObservableCollection<UsageRowVm> UsageRows { get; } = [];

    private void RebuildUsageRows()
    {
        UsageRows.Clear();
        if (!_checkingUsage)
            foreach (var id in new[] { "cc", "gx", "agy" })
            {
                if (_disabledEngines.Contains(id)) continue;
                var name = EngineDefFor(id).Name;
                if (_usageService.TryGet(id, out var snap) && snap.Utilization >= 0)
                {
                    var stale = snap.ResetsAtUnix > 0 && snap.ResetsAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (stale) { UsageRows.Add(new UsageRowVm { Name = name, Note = L("L.UsageStaleNote") + " · " + AgeText(snap.CapturedUtc) }); continue; }

                    var bars = new List<UsageBar>();
                    if (snap.WeekUtilization >= 0)
                    {
                        bars.Add(MakeBar(L("L.UsageBarSession"), snap.Utilization));
                        bars.Add(MakeBar(L("L.UsageBarWeek"), snap.WeekUtilization));
                    }
                    else bars.Add(MakeBar("", snap.Utilization));

                    var note = new List<string>();
                    if (ResetText(snap.ResetsAtUnix) is { } r) note.Add(L("L.UsageResetIn", r));
                    note.Add(AgeText(snap.CapturedUtc));
                    UsageRows.Add(new UsageRowVm { Name = name, Note = string.Join(" · ", note), Bars = bars });
                }
                else if (id == "agy") UsageRows.Add(new UsageRowVm { Name = name, Note = L("L.UsageFreePreviewNote") });
                else UsageRows.Add(new UsageRowVm { Name = name, Note = L("L.UsageNeedsCheckNote") });
            }
        OnChanged(nameof(UsageFallbackVisible));
        OnChanged(nameof(UsageStatusText));
    }

    private static UsageBar MakeBar(string label, double util)
    {
        var p = (int)Math.Round(Math.Clamp(util, 0, 1) * 100);
        return new UsageBar { Label = label, Percent = p, Level = p >= 90 ? "crit" : p >= 70 ? "warn" : "ok" };
    }

    // ----- 사용량(rate-limit) -----
    // 엔진별 마지막 스냅샷. Utilization/WeekUtilization = 0~1(사용 비율), -1 = 미상.
    //   cc: 세션/주간 % 는 /usage 명령 텍스트에서만 나온다(rate_limit_event엔 리셋 시각만 있음).
    //   gx: app-server account/rateLimits/updated 의 usedPercent 가 실 사용량.
    private readonly UsageService _usageService = new();
    private bool _checkingUsage;
    public bool CheckingUsage
    {
        get => _checkingUsage;
        set { if (Set(ref _checkingUsage, value)) { System.Windows.Input.CommandManager.InvalidateRequerySuggested(); RebuildUsageRows(); } }
    }

    /// <summary>실행 중 패시브 캡처. 실 사용량(util>=0, gx)이면 갱신, cc의 리셋전용 이벤트(util&lt;0)면
    /// 리셋 시각만 갱신하고 기존 %·캡처시각은 유지한다.</summary>
    private void RecordUsage(string engineId, QuotaUpdate q)
    {
        // 사용량 표시 기능은 제거됐지만, passive 스냅샷은 유지한다 — MarkRateLimited(리셋시각 기반
        // 소진 감지 / auto-API-fallback)가 _usageService의 리셋시각을 읽기 때문. 표시 갱신만 뺀다.
        _usageService.Record(engineId, q);
        // RefreshQuotaText(); — 사용량 표시 기능 제거(2026-07)
    }

    /// <summary>footer 표시 갱신: 활성 세션 엔진 우선, 없으면 가장 최근 갱신된 엔진의 잔여량.</summary>
    private void RefreshQuotaText()
    {
        if (_checkingUsage) return; // 확인 중에는 "확인 중…" 텍스트 유지
        // footer: 단일(활성/최근) 엔진만 컴팩트하게. 카드(UsageStatusText)는 엔진별 멀티라인.
        var (displayEngineId, snap) = _usageService.PickDisplay(ActiveSession?.AgentId);
        QuotaText = snap is null ? "" : FormatUsageLine(displayEngineId!, EngineDefFor(displayEngineId!).Name, snap);
        RebuildUsageRows();
    }

    /// <summary>한 엔진의 사용량 한 줄 포맷. footer(단일)·카드(엔진별) 양쪽에서 공용.</summary>
    private static string FormatUsageLine(string engineId, string engineName, UsageSnapshot snap)
    {
        var hasPercent = snap.Utilization >= 0;

        // %를 아는데 리셋이 지났으면 윈도우가 갱신돼 값이 무효 → 재확인 안내
        if (hasPercent && snap.ResetsAtUnix > 0 && snap.ResetsAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return L("L.UsageStale", engineName) + " · " + AgeText(snap.CapturedUtc);

        var parts = new List<string>();
        if (!hasPercent)
            parts.Add(L("L.UsageNeedsCheck", engineName));                                  // cc: /usage 미실행 → 미상
        else if (snap.WeekUtilization >= 0)
            parts.Add(L("L.UsageDual", engineName, Pct(snap.Utilization), Pct(snap.WeekUtilization))); // cc: 세션·주간
        else
            parts.Add(L("L.UsageSingleUsed", engineName, Pct(snap.Utilization)));           // gx: 단일

        var reset = ResetText(snap.ResetsAtUnix);
        if (reset is not null) parts.Add(L("L.UsageResetIn", reset));
        if (hasPercent) parts.Add(AgeText(snap.CapturedUtc));

        return string.Join(" · ", parts);
    }

    private static string Pct(double u) => ((int)Math.Round(Math.Clamp(u, 0, 1) * 100)) + "%";

    /// <summary>스냅샷 캡처 후 경과: 방금 / N분 전 / N시간 전. (footer 신선도 라벨)</summary>
    private static string AgeText(DateTime capturedUtc)
    {
        var mins = (int)Math.Max(0, (DateTime.UtcNow - capturedUtc).TotalMinutes);
        if (mins < 1) return L("L.UsageJustNow");
        if (mins < 60) return L("L.UsageMinAgo", mins);
        return L("L.UsageHrAgo", mins / 60);
    }

    /// <summary>리셋까지 남은 시간을 "3h 12m"/"45m"로. 정보 없으면 null.</summary>
    private static string? ResetText(long resetsAtUnix)
    {
        if (resetsAtUnix <= 0) return null;
        var until = DateTimeOffset.FromUnixTimeSeconds(resetsAtUnix) - DateTimeOffset.UtcNow;
        if (until <= TimeSpan.Zero) return null;
        return until.TotalHours >= 1 ? $"{(int)until.TotalHours}h {until.Minutes}m" : $"{until.Minutes}m";
    }

#if false // 사용량 체크 능동 조회 제거(2026-07): 엔진별 공식 사용량 API 부재 + cc /usage는 헤드리스 미지원(토큰만 소모).
          // 되살리려면 이 블록을 #if true 로 + XAML 버튼/카드 + AppViewModel.cs의 CheckUsageCommand 배선을 함께 복원.
    /// <summary>'지금 체크': cc/gx에 최소 요청을 보내 최신 잔여량을 받아온다(소량 토큰 소모).</summary>
    private async Task CheckUsageAsync()
    {
        if (_checkingUsage) return;
        CheckingUsage = true;
        QuotaText = L("L.UsageChecking");
        try
        {
            foreach (var id in new[] { "cc", "gx" })
            {
                if (_disabledEngines.Contains(id)) continue;
                try { await ProbeUsageAsync(id); } catch { /* 한 엔진 실패해도 계속 */ }
            }
        }
        finally { CheckingUsage = false; RefreshQuotaText(); SaveState(); }
    }

    private async Task ProbeUsageAsync(string id)
    {
        var requireApproval = id == "gx"; // gx 쿼터는 app-server(승인) 경로에서만 방출됨
        var adapter = EngineRegistry.CreateAdapter(id, requireApproval);
        var exe = EngineRegistry.ResolveExe(id, _claudePath, _codexPath);
        if (adapter is null || exe is null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var session = new AgentSession(adapter, exe, null, translationEnabled: false);
        long resetAt = 0; string rlType = "";
        session.EventReceived += ev =>
        {
            switch (ev)
            {
                case QuotaUpdate q when id == "gx" && q.Utilization >= 0:
                    // gx: app-server usedPercent = 실 사용량
                    Application.Current.Dispatcher.Invoke(() =>
                        _usageService.Set(id, new UsageSnapshot(q.Utilization, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow)));
                    try { cts.Cancel(); } catch { }
                    break;
                case QuotaUpdate q:
                    // cc: rate_limit_event = 리셋 시각·타입만 (%는 아래 /usage 텍스트에서)
                    resetAt = q.ResetsAtUnix; rlType = q.RateLimitType;
                    break;
                case AssistantText at when id == "cc":
                    var (sUtil, wUtil) = ParseUsageText(at.Text);
                    if (sUtil >= 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            _usageService.Set(id, new UsageSnapshot(sUtil, resetAt, rlType.Length > 0 ? rlType : "five_hour", DateTime.UtcNow, wUtil)));
                        try { cts.Cancel(); } catch { }
                    }
                    break;
            }
        };
        var options = new SessionOptions
        {
            WorkingDirectory = WorkingDirectory,
            BypassPermissions = !requireApproval,
            ExtraEnvironment = ApiEnvFor(id),
            Model = DefaultModelFor(id) is { Length: > 0 } m ? m : null,
        };
        // cc는 /usage(구독 세션·주간 사용량) 명령, gx는 최소 턴으로 쿼터 유도.
        var prompt = id == "cc" ? "/usage" : "ok";
        try { await Task.Run(() => session.RunAsync(options, prompt, cts.Token), cts.Token); }
        catch (OperationCanceledException) { /* 정상: 받고 취소했거나 타임아웃 */ }
        catch { }
    }

    /// <summary>/usage 응답 텍스트에서 세션·주간 사용 비율(0~1)을 추출. 못 찾으면 -1.</summary>
    private static (double session, double week) ParseUsageText(string text) => CoreHelpers.ParseUsageText(text);
#endif // 사용량 체크 능동 조회 제거

}

/// <summary>사용량 카드의 엔진 1행: 이름 + 우측 메모 + 0~2개의 막대.</summary>
public sealed class UsageRowVm
{
    public string Name { get; init; } = "";
    public string Note { get; init; } = "";
    public IReadOnlyList<UsageBar> Bars { get; init; } = [];
}

/// <summary>사용량 막대 1개. FillStar/RestStar로 컨버터 없이 비율 폭을 표현(Grid star).</summary>
public sealed class UsageBar
{
    public string Label { get; init; } = "";
    public int Percent { get; init; }
    public string Level { get; init; } = "ok"; // ok | warn | crit → 막대 색(Ok/Warn/Err)
    public System.Windows.GridLength FillStar => new(Percent, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength RestStar => new(System.Math.Max(0, 100 - Percent), System.Windows.GridUnitType.Star);
}
