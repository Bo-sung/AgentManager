using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using AgentManager.Core.Engines;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    /// <summary>Custom engines (source=="custom" in engines/*.json) rendered as their own cards in Settings → Runtimes.
    /// Rebuilt whenever the engine set changes (startup, manual reload, add/remove) so a hand-added/edited engine file
    /// appears without an app restart. Built-in engines keep their hand-authored cards; this only covers customs.</summary>
    public ObservableCollection<CustomEngineVm> CustomEngines { get; } = [];
    public bool HasCustomEngines => CustomEngines.Count > 0;

    /// <summary>Repopulate the custom-engine card list from the store. Safe to call from command/reload contexts
    /// (NOT from within a card's own property setter — that would mutate the collection mid-binding).</summary>
    private void RebuildCustomEngines()
    {
        CustomEngines.Clear();
        if (_engineConfig is not null)
            foreach (var c in _engineConfig.All.Where(e => string.Equals(e.Source, "custom", StringComparison.OrdinalIgnoreCase)))
                CustomEngines.Add(new CustomEngineVm(
                    c.Id,
                    c.Name,
                    string.IsNullOrEmpty(c.Badge) ? c.Id.ToUpperInvariant() : c.Badge,
                    string.IsNullOrWhiteSpace(c.AdapterKind) ? "—" : c.AdapterKind,
                    c.Launch?.Exe ?? "",
                    !_disabledEngines.Contains(c.Id),
                    ModelSummaryFor(c),
                    (c.ModelsQuery?.Count ?? 0) > 0,
                    onEnabledChanged: SetCustomEngineEnabled,
                    onExeChanged: SetCustomEngineExe,
                    onRemove: RemoveCustomEngine,
                    onManageModels: _ => OpenModelManager(),
                    onQueryModels: vm => _ = QueryCustomEngineModelsAsync(vm)));
        OnChanged(nameof(CustomEngines));
        OnChanged(nameof(HasCustomEngines));
    }

    private static string ModelSummaryFor(EngineConfig c)
    {
        var ids = c.ModelIds();
        if (ids.Count == 0) return "";
        var head = string.Join(", ", ids.Take(2));
        return ids.Count > 2 ? ids.Count + "개 · " + head + " …" : ids.Count + "개 · " + head;
    }

    /// <summary>Enable/disable a custom engine (mirror the built-in disabled-set + persist to its file). Only invalidates
    /// the picker — the card VM already reflects the new value, so the collection is NOT rebuilt (avoids reentrancy).</summary>
    private void SetCustomEngineEnabled(CustomEngineVm vm)
    {
        if (vm.Enabled) _disabledEngines.Remove(vm.Id); else _disabledEngines.Add(vm.Id);
        if (_engineConfig?.Get(vm.Id) is { } c) _engineConfig.Upsert(c with { Enabled = vm.Enabled });
        RefreshEngines();
    }

    /// <summary>Persist a custom engine's launch exe (its entry point). Installed-ness gates the picker on a non-empty
    /// exe, so refresh the picker; the card VM already holds the value so the list is not rebuilt.</summary>
    private void SetCustomEngineExe(CustomEngineVm vm)
    {
        if (_engineConfig?.Get(vm.Id) is { } c)
        {
            var exe = (vm.LaunchExe ?? "").Trim();
            var launch = (c.Launch ?? new EngineLaunchConfig()) with { Exe = exe };
            _engineConfig.Upsert(c with { Launch = launch });
        }
        RefreshEngines();
    }

    /// <summary>Delete a custom engine (its engines/&lt;id&gt;.json). Built-ins are never removed (they'd re-seed).</summary>
    private void RemoveCustomEngine(CustomEngineVm vm)
    {
        if (_engineConfig is null || !_engineConfig.Remove(vm.Id)) return; // Remove is a no-op for built-ins
        _disabledEngines.Remove(vm.Id);
        _skillDirs.Remove(vm.Id);
        _trustStore.Revoke(vm.Id); // forget any run approval (a re-added engine re-prompts anyway)
        RefreshEngines();
        RebuildCustomEngines(); // command context ⇒ safe to rebuild the collection
    }

    // ── "Add custom engine" inline form (Settings → Runtimes) ─────────────────────────────
    // Creates a fresh engines/<id>.json (Source="custom") from GUI fields — no hand-editing JSON.
    // The new engine appears as a card + in the New-Agent picker immediately; its first run passes
    // through the same trust gate as any other custom engine (nothing is auto-approved here).

    /// <summary>Adapter-kind choices for the dropdown. Mirrors <c>AdapterFactory.CreateCustom</c>/<c>Create</c>:
    /// the first two are custom-only kinds (need the launch args template); the rest reuse a built-in protocol
    /// (only actually run if the matching CLI/protocol is installed). <c>one-shot-text</c> is the safe generic default.</summary>
    public IReadOnlyList<string> CustomAdapterKinds { get; } =
        ["one-shot-text", "agentmanager-bridge-jsonl", "acp", "claude-stream-json", "codex-json", "codex-app-server", "agy-pty", "pi-rpc"];

    private bool _showAddEngineForm;
    /// <summary>Whether the collapsible add-engine form is expanded.</summary>
    public bool ShowAddEngineForm
    {
        get => _showAddEngineForm;
        set { if (Set(ref _showAddEngineForm, value)) OnChanged(nameof(ShowAddEngineButton)); }
    }
    /// <summary>Inverse of <see cref="ShowAddEngineForm"/> — the "＋ Add custom engine" launcher button shows only while the form is collapsed.</summary>
    public bool ShowAddEngineButton => !_showAddEngineForm;

    private string _newEngineId = "";
    public string NewEngineId { get => _newEngineId; set => Set(ref _newEngineId, value); }
    private string _newEngineName = "";
    public string NewEngineName { get => _newEngineName; set => Set(ref _newEngineName, value); }
    private string _newEngineBadge = "";
    public string NewEngineBadge { get => _newEngineBadge; set => Set(ref _newEngineBadge, value); }
    private string _newEngineExe = "";
    public string NewEngineExe { get => _newEngineExe; set => Set(ref _newEngineExe, value); }
    private string _newEngineArgs = "";
    /// <summary>One launch argument per line (spaces inside a line are preserved as part of that single arg).</summary>
    public string NewEngineArgs { get => _newEngineArgs; set => Set(ref _newEngineArgs, value); }
    private string _newEngineModels = "";
    /// <summary>Optional initial model ids, newline/comma separated.</summary>
    public string NewEngineModels { get => _newEngineModels; set => Set(ref _newEngineModels, value); }
    private string _newEngineAdapterKind = "one-shot-text";
    public string NewEngineAdapterKind { get => _newEngineAdapterKind; set => Set(ref _newEngineAdapterKind, value); }
    private string _newEngineModelsQuery = "";
    /// <summary>Optional args (one per line) for a command that lists the engine's models, one id per line
    /// (e.g. opencode <c>models</c>) — enables the "모델 조회" button. Empty = no auto-query.</summary>
    public string NewEngineModelsQuery { get => _newEngineModelsQuery; set => Set(ref _newEngineModelsQuery, value); }
    private string _newEngineIcon = "";
    /// <summary>Optional brand icon: a built-in glyph name (circle|square|hexagon|triangle|diamond|spark|bolt|bubble)
    /// or raw SVG path data ("M…"). Empty = default glyph.</summary>
    public string NewEngineIcon { get => _newEngineIcon; set => Set(ref _newEngineIcon, value); }
    private string _newEngineColor = "";
    /// <summary>Optional brand color as hex ("#RRGGBB"). Empty = accent fallback.</summary>
    public string NewEngineColor { get => _newEngineColor; set => Set(ref _newEngineColor, value); }

    private string _addEngineError = "";
    /// <summary>Inline validation message ("" = none).</summary>
    public string AddEngineError { get => _addEngineError; set => Set(ref _addEngineError, value); }
    public bool HasAddEngineError => !string.IsNullOrEmpty(_addEngineError);

    private RelayCommand? _showAddEngineFormCommand;
    public RelayCommand ShowAddEngineFormCommand => _showAddEngineFormCommand ??= new RelayCommand(_ =>
    {
        NewEngineId = ""; NewEngineName = ""; NewEngineBadge = ""; NewEngineExe = "";
        NewEngineArgs = ""; NewEngineModels = ""; NewEngineAdapterKind = "one-shot-text"; NewEngineModelsQuery = "";
        NewEngineIcon = ""; NewEngineColor = "";
        SetAddEngineError("");
        ShowAddEngineForm = true;
    });

    private RelayCommand? _cancelAddEngineFormCommand;
    public RelayCommand CancelAddEngineFormCommand => _cancelAddEngineFormCommand ??= new RelayCommand(_ =>
    {
        ShowAddEngineForm = false;
        SetAddEngineError("");
    });

    private RelayCommand? _addEngineCommand;
    public RelayCommand AddEngineCommand => _addEngineCommand ??= new RelayCommand(_ => AddCustomEngine());

    private void SetAddEngineError(string msg)
    {
        AddEngineError = msg;
        OnChanged(nameof(HasAddEngineError));
    }

    /// <summary>Validate the draft fields. On failure sets <see cref="AddEngineError"/> and returns false.</summary>
    private bool TryValidateNewEngine(out string id, out string name, out string badge, out string exe, out string kind)
    {
        id = (NewEngineId ?? "").Trim();
        name = (NewEngineName ?? "").Trim();
        badge = (NewEngineBadge ?? "").Trim();
        exe = (NewEngineExe ?? "").Trim();
        kind = (NewEngineAdapterKind ?? "").Trim();

        if (id.Length == 0) { SetAddEngineError(AgentManager.App.L("L.AddEngineErrIdEmpty")); return false; }
        // Strict charset so SafeFileName never remaps two distinct ids onto the same file (e.g. "a/b" vs "a_b").
        if (!Regex.IsMatch(id, "^[A-Za-z0-9._-]+$") || id.StartsWith('.') || id.EndsWith('.'))
        { SetAddEngineError(AgentManager.App.L("L.AddEngineErrId")); return false; }
        // Uniqueness — covers built-ins (cc/gx/agy/pi/pi-worker are seeded) AND existing customs. Upsert overwrites
        // silently, so this guard is load-bearing (prevents clobbering an existing engine's file).
        if (_engineConfig is null || _engineConfig.Contains(id)) { SetAddEngineError(AgentManager.App.L("L.AddEngineErrDup")); return false; }
        if (!CustomAdapterKinds.Contains(kind)) { SetAddEngineError(AgentManager.App.L("L.AddEngineErrAdapter")); return false; }
        if (exe.Length == 0) { SetAddEngineError(AgentManager.App.L("L.AddEngineErrExe")); return false; }

        if (name.Length == 0) name = id;
        if (badge.Length == 0) badge = id.ToUpperInvariant();
        return true;
    }

    /// <summary>Validate → build an EngineConfig → persist via Upsert → refresh picker + cards. On success collapses the form.</summary>
    private void AddCustomEngine()
    {
        if (!TryValidateNewEngine(out var id, out var name, out var badge, out var exe, out var kind)) return;

        // One arg per line — preserves spaces inside a single argument (e.g. a path). Args is a real list, not a shell string.
        var args = (NewEngineArgs ?? "")
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Same split as ModelManager.AddManagedModels: newline/comma separated, de-duplicated.
        var modelIds = (NewEngineModels ?? "").Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new List<EngineModelConfig>();
        foreach (var mid in modelIds) if (seen.Add(mid)) models.Add(new EngineModelConfig(mid));

        var modelsQuery = (NewEngineModelsQuery ?? "")
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var cfg = new EngineConfig(
            Id: id,
            Name: name,
            Badge: badge,
            Source: "custom",
            AdapterKind: kind,
            Enabled: true,
            Launch: new EngineLaunchConfig(exe, args.Count > 0 ? args : null),
            Models: models.Count > 0 ? models : null,
            AllowedRoles: ["Plain", "Main", "Worker"],
            ModelsQuery: modelsQuery.Count > 0 ? modelsQuery : null,
            Icon: (NewEngineIcon ?? "").Trim(),
            Color: (NewEngineColor ?? "").Trim());

        _engineConfig!.Upsert(cfg);   // appends to display order + writes engines/<id>.json atomically
        _disabledEngines.Remove(id);  // ensure enabled
        RefreshEngines();             // invalidate AllEngines cache + New-Agent picker
        RebuildCustomEngines();       // command context ⇒ safe to rebuild the card collection

        SetAddEngineError("");
        ShowAddEngineForm = false;
    }

    /// <summary>Run the engine's configured <c>modelsQuery</c> command (launch.exe + query args → one model id per line),
    /// fold the result into engines/&lt;id&gt;.json (UpdateModelsFromQuery preserves surviving efforts/preferred), and
    /// refresh the picker/model-manager. Feedback via the card's ModelsStatus; the card's model summary updates in place.</summary>
    private async System.Threading.Tasks.Task QueryCustomEngineModelsAsync(CustomEngineVm vm)
    {
        if (_engineConfig?.Get(vm.Id) is not { } c || (c.ModelsQuery?.Count ?? 0) == 0
            || c.Launch?.Exe is not { Length: > 0 } exe)
        { vm.ModelsStatus = AgentManager.App.L("L.NoModels"); return; }

        vm.ModelsStatus = AgentManager.App.L("L.Querying");
        IReadOnlyList<string> ids;
        try { ids = await AgentManager.Core.Agents.EngineRegistry.QueryModelsByCommandAsync(exe, c.ModelsQuery!); }
        catch { ids = []; }

        if (ids.Count == 0) { vm.ModelsStatus = AgentManager.App.L("L.QueryFailed"); return; }

        _engineConfig.UpdateModelsFromQuery(vm.Id, ids);
        RefreshEngines();          // picker + AllEngines
        RebuildModelManager();     // model-manager page rows
        NotifySessionModelsChanged(vm.Id);
        if (_engineConfig.Get(vm.Id) is { } updated) vm.ModelSummary = ModelSummaryFor(updated); // update card in place (no rebuild ⇒ status persists)
        vm.ModelsStatus = AgentManager.App.L("L.ModelsFound", ids.Count);
    }
}
