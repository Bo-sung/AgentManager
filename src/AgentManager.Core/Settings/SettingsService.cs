namespace AgentManager.Core.Settings;

/// <summary>Headless machine-local app configuration (settings.json) owned by Core, so a CLI and the GUI
/// share ONE source of truth. Per-engine auth/rate-limit lives in <see cref="EngineAuthService"/>; this
/// holds the general config. The GUI ViewModel's backing fields become thin delegating properties over
/// this, so read/write sites are unchanged and there is no duplicate state. Grown incrementally by the
/// headless-core overhaul (step 2b).</summary>
public sealed class SettingsService
{
    // ----- engine executable path overrides (empty = auto-detect) -----
    public string ClaudePath { get; set; } = "";
    public string CodexPath { get; set; } = "";
    public string AgyPath { get; set; } = "";
    public string PiPath { get; set; } = "";
}
