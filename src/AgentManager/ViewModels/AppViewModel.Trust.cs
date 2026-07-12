using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using AgentManager.Core.Engines;

namespace AgentManager.ViewModels;

/// <summary>
/// Pre-run TRUST gate for CUSTOM engines. Before the FIRST run of a custom engine (arbitrary exe + argument list the
/// user configured), an approval modal lists the exact executable and each argv entry and requires explicit approval.
/// Approvals are remembered per engine id keyed by a fingerprint of exe+args (see <see cref="EngineTrustStore"/>), so
/// any later edit to exe or args forces a re-prompt. The gate awaits a <see cref="TaskCompletionSource{TResult}"/>
/// exactly like the existing permission round-trip (HandlePermissionAsync).
/// </summary>
public sealed partial class AppViewModel
{
    private readonly EngineTrustStore _trustStore = EngineTrustStore.Load();

    // ----- modal state (single app-global overlay) -----
    private bool _showTrustPrompt;
    public bool ShowTrustPrompt
    {
        get => _showTrustPrompt;
        private set { if (Set(ref _showTrustPrompt, value)) OnChanged(nameof(IsModalActive)); }
    }

    private string _trustEngineName = "";
    public string TrustEngineName { get => _trustEngineName; private set => Set(ref _trustEngineName, value); }

    private string _trustExe = "";
    public string TrustExe { get => _trustExe; private set => Set(ref _trustExe, value); }

    /// <summary>The launch argument LIST — each entry rendered on its own row (reinforces argv-list, not a shell string).</summary>
    public ObservableCollection<string> TrustArgs { get; } = [];

    public bool TrustHasArgs => TrustArgs.Count > 0;

    private TaskCompletionSource<bool>? _trustTcs;
    private (string EngineId, string Fp)? _pendingTrust;

    public RelayCommand TrustApproveCommand { get; private set; } = null!;
    public RelayCommand TrustDenyCommand { get; private set; } = null!;

    private void InitTrustCommands()
    {
        TrustApproveCommand = new RelayCommand(_ =>
        {
            if (_pendingTrust is { } p) _trustStore.Trust(p.EngineId, p.Fp); // persist approval (fingerprint of exe+args)
            _pendingTrust = null;
            ShowTrustPrompt = false;
            _trustTcs?.TrySetResult(true);
            _trustTcs = null;
        });
        TrustDenyCommand = new RelayCommand(_ =>
        {
            _pendingTrust = null;
            ShowTrustPrompt = false;
            _trustTcs?.TrySetResult(false);
            _trustTcs = null;
        });
    }

    /// <summary>Gate the FIRST run of a custom engine on explicit approval. Returns true immediately when the exact
    /// exe+args are already trusted; otherwise pops the modal and awaits the user's Approve/Deny. A second custom
    /// engine trying to prompt while one is already up is denied (single overlay) — its run aborts and can retry.</summary>
    private Task<bool> EnsureCustomEngineTrustedAsync(SessionViewModel s, string exe, IReadOnlyList<string> args)
    {
        var fp = EngineTrustStore.Fingerprint(exe, args);
        if (_trustStore.IsTrusted(s.AgentId, fp)) return Task.FromResult(true);
        return Application.Current.Dispatcher.Invoke(() =>
        {
            if (_showTrustPrompt) return Task.FromResult(false); // another trust prompt is already up → caller retries
            _trustTcs = new TaskCompletionSource<bool>();
            TrustEngineName = s.AgentName;
            TrustExe = exe;
            TrustArgs.Clear();
            foreach (var a in args) TrustArgs.Add(a);
            OnChanged(nameof(TrustHasArgs));
            _pendingTrust = (s.AgentId, fp);
            ShowTrustPrompt = true;
            AttentionRequested?.Invoke("approval", s);
            return _trustTcs.Task;
        });
    }
}
