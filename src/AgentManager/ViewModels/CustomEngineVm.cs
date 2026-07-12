using System;

namespace AgentManager.ViewModels;

/// <summary>One custom engine's card in Settings → Runtimes (source=="custom" in engines/&lt;id&gt;.json). Shows the
/// identity + adapter kind + launch exe with enable/remove/manage-models actions. Edits persist to the engine's file
/// via callbacks owned by <see cref="AppViewModel"/>. Built-in engines keep their own hand-authored cards.</summary>
public sealed class CustomEngineVm : ObservableObject
{
    private readonly Action<CustomEngineVm> _onEnabledChanged;
    private readonly Action<CustomEngineVm> _onExeChanged;
    private readonly Action<CustomEngineVm> _onRemove;
    private readonly Action<CustomEngineVm> _onManageModels;
    private readonly Action<CustomEngineVm> _onQueryModels;

    public CustomEngineVm(string id, string name, string badge, string adapterKind, string launchExe,
        bool enabled, string modelSummary, bool hasModelsQuery,
        Action<CustomEngineVm> onEnabledChanged, Action<CustomEngineVm> onExeChanged,
        Action<CustomEngineVm> onRemove, Action<CustomEngineVm> onManageModels, Action<CustomEngineVm> onQueryModels)
    {
        Id = id;
        Name = name;
        Badge = badge;
        AdapterKind = adapterKind;
        _launchExe = launchExe;
        _enabled = enabled;
        _modelSummary = modelSummary;
        HasModelsQuery = hasModelsQuery;
        _onEnabledChanged = onEnabledChanged;
        _onExeChanged = onExeChanged;
        _onRemove = onRemove;
        _onManageModels = onManageModels;
        _onQueryModels = onQueryModels;
    }

    public string Id { get; }
    public string Name { get; }
    public string Badge { get; }
    public string AdapterKind { get; }
    private string _modelSummary;
    /// <summary>"N개 · id1, id2 …" summary; updated in place after a model query (no full card rebuild).</summary>
    public string ModelSummary { get => _modelSummary; set => Set(ref _modelSummary, value); }
    /// <summary>The engine defines a <c>modelsQuery</c> command ⇒ the "모델 조회" button is shown.</summary>
    public bool HasModelsQuery { get; }

    private string _modelsStatus = "";
    /// <summary>Transient status of the last model query (querying / N found / failed).</summary>
    public string ModelsStatus { get => _modelsStatus; set => Set(ref _modelsStatus, value); }

    private bool _enabled;
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) _onEnabledChanged(this); } }

    // TextBox.Text defaults to LostFocus, so this fires once the user leaves the field (not per keystroke).
    private string _launchExe;
    public string LaunchExe { get => _launchExe; set { if (Set(ref _launchExe, value)) _onExeChanged(this); } }

    private RelayCommand? _removeCommand;
    public RelayCommand RemoveCommand => _removeCommand ??= new RelayCommand(_ => _onRemove(this));
    private RelayCommand? _manageModelsCommand;
    public RelayCommand ManageModelsCommand => _manageModelsCommand ??= new RelayCommand(_ => _onManageModels(this));
    private RelayCommand? _queryModelsCommand;
    public RelayCommand QueryModelsCommand => _queryModelsCommand ??= new RelayCommand(_ => _onQueryModels(this));
}
