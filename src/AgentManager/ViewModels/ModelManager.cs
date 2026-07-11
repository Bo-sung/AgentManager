using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AgentManager.ViewModels;

/// <summary>One model row in the model manager — id + preferred/default toggles + per-model default effort.
/// Edits persist live to engines/&lt;id&gt;.json via the callbacks (owned by AppViewModel).</summary>
public sealed class ManagedModelVm : ObservableObject
{
    private readonly Action<ManagedModelVm> _onPreferred;
    private readonly Action<ManagedModelVm> _onDefault;
    private readonly Action<ManagedModelVm> _onEffort;
    private readonly Action<ManagedModelVm> _onRemove;

    public ManagedModelVm(string id, bool preferred, bool isDefault, string defaultEffort,
        IReadOnlyList<string> effortOptions, bool hasEfforts,
        Action<ManagedModelVm> onPreferred, Action<ManagedModelVm> onDefault,
        Action<ManagedModelVm> onEffort, Action<ManagedModelVm> onRemove)
    {
        Id = id;
        _preferred = preferred;
        _isDefault = isDefault;
        _defaultEffort = defaultEffort;
        EffortOptions = effortOptions;
        HasEfforts = hasEfforts;
        _onPreferred = onPreferred;
        _onDefault = onDefault;
        _onEffort = onEffort;
        _onRemove = onRemove;
    }

    public string Id { get; }
    public IReadOnlyList<string> EffortOptions { get; }
    public bool HasEfforts { get; }

    private bool _preferred;
    public bool Preferred { get => _preferred; set { if (Set(ref _preferred, value)) _onPreferred(this); } }

    private bool _isDefault;
    public bool IsDefault { get => _isDefault; set { if (Set(ref _isDefault, value) && value) _onDefault(this); } }

    private string _defaultEffort;
    public string DefaultEffort { get => _defaultEffort; set { if (Set(ref _defaultEffort, value)) _onEffort(this); } }

    private RelayCommand? _removeCommand;
    public RelayCommand RemoveCommand => _removeCommand ??= new RelayCommand(_ => _onRemove(this));
}

/// <summary>One engine's section in the model manager — its models + a bulk "add models" box
/// (newline/comma-separated ids).</summary>
public sealed class EngineModelsVm : ObservableObject
{
    private readonly Action<string> _onAdd;

    public EngineModelsVm(string engineId, string engineName, bool hasEfforts, Action<string> onAdd)
    {
        EngineId = engineId;
        EngineName = engineName;
        HasEfforts = hasEfforts;
        _onAdd = onAdd;
    }

    public string EngineId { get; }
    public string EngineName { get; }
    public bool HasEfforts { get; }
    public ObservableCollection<ManagedModelVm> Models { get; } = [];

    private string _newModelsDraft = "";
    public string NewModelsDraft { get => _newModelsDraft; set => Set(ref _newModelsDraft, value); }

    private RelayCommand? _addCommand;
    public RelayCommand AddCommand => _addCommand ??= new RelayCommand(_ => { _onAdd(_newModelsDraft); NewModelsDraft = ""; });
}
