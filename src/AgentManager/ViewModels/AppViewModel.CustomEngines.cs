using System;
using System.Collections.ObjectModel;
using System.Linq;
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
                    onEnabledChanged: SetCustomEngineEnabled,
                    onExeChanged: SetCustomEngineExe,
                    onRemove: RemoveCustomEngine,
                    onManageModels: _ => OpenModelManager()));
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
        RefreshEngines();
        RebuildCustomEngines(); // command context ⇒ safe to rebuild the collection
    }
}
