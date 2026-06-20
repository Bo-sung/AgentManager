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
    // ----- translation + quota -----
    private bool _translationEnabled = true;
    public bool TranslationEnabled { get => _translationEnabled; set => Set(ref _translationEnabled, value); }
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
    public string UsageStatusText => string.IsNullOrWhiteSpace(QuotaText) ? L("L.UsageNoData") : QuotaText;

    // ----- 사용량(rate-limit) -----
    // 엔진별 마지막 스냅샷. Utilization/WeekUtilization = 0~1(사용 비율), -1 = 미상.
    //   cc: 세션/주간 % 는 /usage 명령 텍스트에서만 나온다(rate_limit_event엔 리셋 시각만 있음).
    //   gx: app-server account/rateLimits/updated 의 usedPercent 가 실 사용량.
    public sealed record UsageSnapshot(double Utilization, long ResetsAtUnix, string RateLimitType, DateTime CapturedUtc, double WeekUtilization = -1);
    private readonly Dictionary<string, UsageSnapshot> _usage = new();
    private bool _checkingUsage;
    public bool CheckingUsage
    {
        get => _checkingUsage;
        set { if (Set(ref _checkingUsage, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>실행 중 패시브 캡처. 실 사용량(util>=0, gx)이면 갱신, cc의 리셋전용 이벤트(util&lt;0)면
    /// 리셋 시각만 갱신하고 기존 %·캡처시각은 유지한다.</summary>
    private void RecordUsage(string engineId, QuotaUpdate q)
    {
        _usage.TryGetValue(engineId, out var prev);
        if (q.Utilization >= 0)
            _usage[engineId] = new UsageSnapshot(q.Utilization, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow, prev?.WeekUtilization ?? -1);
        else if (prev is not null)
            _usage[engineId] = prev with { ResetsAtUnix = q.ResetsAtUnix, RateLimitType = q.RateLimitType };
        else
            _usage[engineId] = new UsageSnapshot(-1, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow);
        RefreshQuotaText();
    }

    /// <summary>footer 표시 갱신: 활성 세션 엔진 우선, 없으면 가장 최근 갱신된 엔진의 잔여량.</summary>
    private void RefreshQuotaText()
    {
        if (_checkingUsage) return; // 확인 중에는 "확인 중…" 텍스트 유지
        UsageSnapshot? snap = null;
        var displayEngineId = ActiveSession?.AgentId;
        if (displayEngineId is not null && !_usage.TryGetValue(displayEngineId, out snap))
            displayEngineId = null;
        if (snap is null && _usage.Count > 0)
        {
            foreach (var pair in _usage)
            {
                if (snap is null || pair.Value.CapturedUtc > snap.CapturedUtc)
                {
                    displayEngineId = pair.Key;
                    snap = pair.Value;
                }
            }
        }
        if (snap is null) { QuotaText = ""; return; }

        var engineName = displayEngineId is null ? "" : EngineRegistry.Get(displayEngineId).Name;
        var hasPercent = snap.Utilization >= 0;

        // %를 아는데 리셋이 지났으면 윈도우가 갱신돼 값이 무효 → 재확인 안내
        if (hasPercent && snap.ResetsAtUnix > 0 && snap.ResetsAtUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            QuotaText = L("L.UsageStale", engineName) + " · " + AgeText(snap.CapturedUtc);
            return;
        }

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

        QuotaText = string.Join(" · ", parts);
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
                        _usage[id] = new UsageSnapshot(q.Utilization, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow));
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
                            _usage[id] = new UsageSnapshot(sUtil, resetAt, rlType.Length > 0 ? rlType : "five_hour", DateTime.UtcNow, wUtil));
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
    private static (double session, double week) ParseUsageText(string text)
    {
        double s = -1, w = -1;
        var ms = System.Text.RegularExpressions.Regex.Match(text, @"session[^0-9]*(\d+)\s*%\s*used",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (ms.Success) s = int.Parse(ms.Groups[1].Value) / 100.0;
        var mw = System.Text.RegularExpressions.Regex.Match(text, @"week[^0-9]*(\d+)\s*%\s*used",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mw.Success) w = int.Parse(mw.Groups[1].Value) / 100.0;
        return (s, w);
    }

}
