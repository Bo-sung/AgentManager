using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AgentManager.ViewModels;

/// <summary>"주로 쓰는 모델" 체크리스트 항목 — 모델 문자열 + 체크 여부.</summary>
public sealed class ModelChoice : ObservableObject
{
    private readonly Action<ModelChoice> _onToggle;

    public ModelChoice(string model, bool isChecked, Action<ModelChoice> onToggle)
    {
        Model = model;
        _isChecked = isChecked;
        _onToggle = onToggle;
    }

    public string Model { get; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) _onToggle(this); }
    }
}

/// <summary>한 엔진의 "주로 쓰는 모델" 체크리스트 — 전체 모델 목록 + 필터 + 선호 집합 연동.
/// 모든 엔진(cc/gx/agy/pi)이 동일하게 사용한다. (pi처럼 모델이 많은 엔진은 ShowFilter=true.)</summary>
public sealed class ModelChecklistVm : ObservableObject
{
    private readonly HashSet<string> _preferred;   // 엔진별 선호 집합(외부 소유 — AppViewModel)
    private readonly Action _onChanged;
    private readonly List<ModelChoice> _all = [];

    public ModelChecklistVm(string engineId, HashSet<string> preferred, bool showFilter, Action onChanged)
    {
        EngineId = engineId;
        _preferred = preferred;
        ShowFilter = showFilter;
        _onChanged = onChanged;
    }

    public string EngineId { get; }
    public bool ShowFilter { get; }
    public ObservableCollection<ModelChoice> Choices { get; } = [];

    // 접이식 UI 상태(영속 안 함) + 선택 개수(헤더 표시용).
    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public int CheckedCount => _preferred.Count;

    private string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) ApplyFilter(); } }

    /// <summary>전체 모델 목록을 설정(정적/동적). 선호 집합 기준으로 체크 상태를 반영한다.</summary>
    public void SetModels(IEnumerable<string> models)
    {
        _all.Clear();
        foreach (var m in models)
            _all.Add(new ModelChoice(m, _preferred.Contains(m), Toggle));
        ApplyFilter();
        OnChanged(nameof(CheckedCount));
    }

    private void ApplyFilter()
    {
        var f = (_filter ?? "").Trim();
        Choices.Clear();
        foreach (var c in _all)
            if (f.Length == 0 || c.Model.Contains(f, StringComparison.OrdinalIgnoreCase))
                Choices.Add(c);
    }

    private void Toggle(ModelChoice c)
    {
        if (c.IsChecked) _preferred.Add(c.Model); else _preferred.Remove(c.Model);
        OnChanged(nameof(CheckedCount));
        _onChanged();
    }
}
