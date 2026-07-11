using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AgentManager.Core.Engines;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    public bool IsModelManagerView => CurrentView == MainViewKind.ModelManager;

    /// <summary>Per-engine sections shown in the model-manager sub-page (built from engines/*.json on open).</summary>
    public ObservableCollection<EngineModelsVm> ModelManagerEngines { get; } = [];

    private RelayCommand? _showModelManagerCommand;
    public RelayCommand ShowModelManagerCommand => _showModelManagerCommand ??= new RelayCommand(_ => OpenModelManager());
    private RelayCommand? _closeModelManagerCommand;
    /// <summary>Back to Settings (the manager is a Settings sub-page).</summary>
    public RelayCommand CloseModelManagerCommand => _closeModelManagerCommand ??= new RelayCommand(_ => CurrentView = MainViewKind.Settings);

    private void OpenModelManager()
    {
        RebuildModelManager();
        CurrentView = MainViewKind.ModelManager;
    }

    private void RebuildModelManager()
    {
        ModelManagerEngines.Clear();
        if (_engineConfig is null) return;
        foreach (var id in new[] { "cc", "gx", "agy", "pi" })
        {
            if (_engineConfig.Get(id) is not { } c) continue;
            var section = new EngineModelsVm(c.Id, c.Name, c.HasEfforts, draft => AddManagedModels(c.Id, draft));
            foreach (var m in c.ModelList) section.Models.Add(BuildManagedRow(c, m));
            ModelManagerEngines.Add(section);
        }
    }

    private ManagedModelVm BuildManagedRow(EngineConfig c, EngineModelConfig m)
    {
        var efforts = (m.Efforts ?? c.DefaultEfforts ?? []).ToArray();
        return new ManagedModelVm(
            m.Id,
            m.Preferred,
            string.Equals(m.Id, c.DefaultModel, StringComparison.OrdinalIgnoreCase),
            m.DefaultEffort ?? "",
            efforts,
            c.HasEfforts,
            onPreferred: r => MutateModel(c.Id, r.Id, mm => mm with { Preferred = r.Preferred }),
            onDefault: r => SetManagedDefault(c.Id, r.Id),
            onEffort: r => MutateModel(c.Id, r.Id, mm => mm with { DefaultEffort = string.IsNullOrWhiteSpace(r.DefaultEffort) ? null : r.DefaultEffort }),
            onRemove: r => RemoveManagedModel(c.Id, r.Id));
    }

    /// <summary>Replace one model in an engine (preferred flag / default effort) and persist. No section rebuild —
    /// the edited row already reflects the change.</summary>
    private void MutateModel(string engineId, string modelId, Func<EngineModelConfig, EngineModelConfig> mutate)
    {
        if (_engineConfig?.Get(engineId) is not { } c) return;
        var models = c.ModelList.Select(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase) ? mutate(m) : m).ToList();
        _engineConfig.Upsert(c with { Models = models });
        AfterModelManagerChange(engineId);
    }

    private void SetManagedDefault(string engineId, string modelId)
    {
        if (_engineConfig?.Get(engineId) is not { } c) return;
        _engineConfig.Upsert(c with { DefaultModel = modelId });
        RebuildModelManager(); // refresh IsDefault across the section (radio behavior)
        AfterModelManagerChange(engineId);
    }

    private void RemoveManagedModel(string engineId, string modelId)
    {
        if (_engineConfig?.Get(engineId) is not { } c) return;
        var models = c.ModelList.Where(m => !string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)).ToList();
        var def = string.Equals(c.DefaultModel, modelId, StringComparison.OrdinalIgnoreCase) ? null : c.DefaultModel;
        _engineConfig.Upsert(c with { Models = models, DefaultModel = def });
        RebuildModelManager();
        AfterModelManagerChange(engineId);
    }

    /// <summary>Bulk-add: split the draft on newline/comma, add each new id (skipping duplicates) and persist.</summary>
    private void AddManagedModels(string engineId, string draft)
    {
        if (_engineConfig?.Get(engineId) is not { } c) return;
        var ids = (draft ?? "").Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0) return;
        var models = c.ModelList.ToList();
        var known = new HashSet<string>(models.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var id in ids) if (known.Add(id)) { models.Add(new EngineModelConfig(id)); added = true; }
        if (!added) return;
        _engineConfig.Upsert(c with { Models = models });
        RebuildModelManager();
        AfterModelManagerChange(engineId);
    }

    /// <summary>After a manager edit persists to engines/*.json, refresh the runtime working copies (preferred set +
    /// composer/New-Agent model menus) so the change shows everywhere immediately.</summary>
    private void AfterModelManagerChange(string engineId)
    {
        SyncPreferredFromStore();
        NotifySessionModelsChanged(engineId);
        OnChanged(nameof(NewAgentModels));
    }
}
