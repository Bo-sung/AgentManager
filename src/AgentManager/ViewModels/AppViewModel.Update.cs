using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AgentManager.Core;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    readonly UpdateService _updater = new();

    bool _updateBusy;
    string _updateStatusText = "";
    bool _isUpdateAvailable;

    public bool UpdateBusy
    {
        get => _updateBusy;
        private set { if (Set(ref _updateBusy, value)) { OnChanged(nameof(CanCheckUpdate)); OnChanged(nameof(CanApplyUpdate)); CommandManager.InvalidateRequerySuggested(); } }
    }

    public string UpdateStatusText { get => _updateStatusText; private set => Set(ref _updateStatusText, value); }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set { if (Set(ref _isUpdateAvailable, value)) { OnChanged(nameof(CanApplyUpdate)); CommandManager.InvalidateRequerySuggested(); } }
    }

    public bool CanCheckUpdate => !UpdateBusy;

    /// <summary>Self-update only works when running from the local git checkout (so it can pull + rebuild).</summary>
    public bool UpdaterAvailable => UpdateService.FindRepoRoot(AppContext.BaseDirectory) != null;

    public bool CanApplyUpdate => IsUpdateAvailable && !UpdateBusy && UpdaterAvailable;

    RelayCommand? _checkUpdateCommand;
    public RelayCommand CheckUpdateCommand => _checkUpdateCommand ??= new RelayCommand(_ => { _ = CheckUpdateAsync(); }, _ => CanCheckUpdate);

    RelayCommand? _applyUpdateCommand;
    public RelayCommand ApplyUpdateCommand => _applyUpdateCommand ??= new RelayCommand(_ => ApplyUpdate(), _ => CanApplyUpdate);

    RelayCommand? _openChangelogCommand;
    public RelayCommand OpenChangelogCommand => _openChangelogCommand ??= new RelayCommand(_ => Shell.Open(UpdateService.ChangelogUrl));

    async Task CheckUpdateAsync()
    {
        if (UpdateBusy) return;
        UpdateBusy = true;
        IsUpdateAvailable = false;
        UpdateStatusText = App.L("L.UpdateChecking");
        try
        {
            var cur = Assembly.GetEntryAssembly()?.GetName().Version
                      ?? Assembly.GetExecutingAssembly().GetName().Version
                      ?? new Version(1, 0, 0);
            var info = await _updater.CheckAsync(cur);

            if (info.Error != null)
            {
                UpdateStatusText = App.L("L.UpdateError");
            }
            else if (info.Available && info.Latest != null)
            {
                IsUpdateAvailable = true;
                UpdateStatusText = App.L("L.UpdateAvailable", info.Latest.ToString(3));
            }
            else
            {
                UpdateStatusText = App.L("L.UpToDate");
            }
        }
        catch
        {
            UpdateStatusText = App.L("L.UpdateError");
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    /// <summary>
    /// Launch the standalone updater (scripts/update.ps1) and shut the app down so its exe is
    /// unlocked. The updater waits for this PID to exit, then git-pulls master, rebuilds, and
    /// relaunches the same exe. Build outputs (dist/, bin/) are gitignored, so the pull never
    /// conflicts with the running binary.
    /// </summary>
    void ApplyUpdate()
    {
        var root = UpdateService.FindRepoRoot(AppContext.BaseDirectory);
        if (root == null) { UpdateStatusText = App.L("L.UpdateError"); return; }

        var script = Path.Combine(root, "scripts", "update.ps1");
        if (!File.Exists(script)) { UpdateStatusText = App.L("L.UpdateError"); return; }

        var exe = Environment.ProcessPath ?? "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,          // independent process + visible progress window
                WorkingDirectory = root,
            };
            psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(script);
            psi.ArgumentList.Add("-WaitPid"); psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("-Relaunch"); psi.ArgumentList.Add(exe);
            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch
        {
            UpdateStatusText = App.L("L.UpdateError");
        }
    }
}
