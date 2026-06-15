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
    public string QuotaText { get => _quotaText; set => Set(ref _quotaText, value); }

    // ----- 잔여 사용량(rate-limit) -----
    // 엔진별 마지막 스냅샷(인메모리). cc/gx만 쿼터를 방출 — ag/agy는 없음.
    public sealed record UsageSnapshot(double Utilization, long ResetsAtUnix, string RateLimitType, DateTime CapturedUtc);
    private readonly Dictionary<string, UsageSnapshot> _usage = new();
    private bool _checkingUsage;
    public bool CheckingUsage
    {
        get => _checkingUsage;
        set { if (Set(ref _checkingUsage, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    private void RecordUsage(string engineId, QuotaUpdate q)
    {
        _usage[engineId] = new UsageSnapshot(q.Utilization, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow);
        RefreshQuotaText();
    }

    /// <summary>footer 표시 갱신: 활성 세션 엔진 우선, 없으면 가장 최근 갱신된 엔진의 잔여량.</summary>
    private void RefreshQuotaText()
    {
        if (_checkingUsage) return; // 확인 중에는 "확인 중…" 텍스트 유지
        UsageSnapshot? snap = null;
        var id = ActiveSession?.AgentId;
        if (id is not null) _usage.TryGetValue(id, out snap);
        if (snap is null && _usage.Count > 0)
        {
            foreach (var v in _usage.Values)
                if (snap is null || v.CapturedUtc > snap.CapturedUtc) snap = v;
        }
        if (snap is null) { QuotaText = ""; return; }
        var remain = Math.Clamp(1 - snap.Utilization, 0, 1).ToString("P0");
        var reset = ResetText(snap.ResetsAtUnix);
        QuotaText = reset is null ? L("L.UsageRemaining", remain) : L("L.UsageRemainingReset", remain, reset);
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
        finally { CheckingUsage = false; RefreshQuotaText(); }
    }

    private async Task ProbeUsageAsync(string id)
    {
        var requireApproval = id == "gx"; // gx 쿼터는 app-server(승인) 경로에서만 방출됨
        var adapter = EngineRegistry.CreateAdapter(id, requireApproval);
        var exe = EngineRegistry.ResolveExe(id, _claudePath, _codexPath);
        if (adapter is null || exe is null) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var session = new AgentSession(adapter, exe, null, translationEnabled: false);
        session.EventReceived += ev =>
        {
            if (ev is QuotaUpdate q)
            {
                // 쿼터 수신: 저장하고 즉시 턴 종료(프로브 목적 달성)
                Application.Current.Dispatcher.Invoke(() =>
                    _usage[id] = new UsageSnapshot(q.Utilization, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow));
                try { cts.Cancel(); } catch { }
            }
        };
        var options = new SessionOptions
        {
            WorkingDirectory = WorkingDirectory,
            BypassPermissions = !requireApproval,
            ExtraEnvironment = ApiEnvFor(id),
            Model = DefaultModelFor(id) is { Length: > 0 } m ? m : null,
        };
        try { await Task.Run(() => session.RunAsync(options, "ok", cts.Token), cts.Token); }
        catch (OperationCanceledException) { /* 정상: 쿼터 받고 취소했거나 타임아웃 */ }
        catch { }
    }

}
