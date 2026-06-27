using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AgentManager.ViewModels;

/// <summary>One selectable option. <see cref="Marker"/> = "1".."9"/"A".. shortcut, <see cref="Label"/> =
/// display text, <see cref="Text"/> = what is sent when chosen. <see cref="IsSelected"/> drives the
/// multi-select checkbox state.</summary>
public sealed class ChoiceOption(string marker, string label, string text) : ObservableObject
{
    public string Marker { get; } = marker;
    public string Label { get; } = label;
    public string Text { get; } = text;
    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>A single question: prompt + options, single- or multi-select. <see cref="Answer"/> holds
/// the captured response once the page is answered (so the pager can revisit it).</summary>
public sealed class ChoiceItem : ObservableObject
{
    public string? Question { get; init; }
    public bool Multi { get; init; }
    public ObservableCollection<ChoiceOption> Options { get; } = [];
    public string? Answer { get; set; }

    public int SelectedCount => Options.Count(o => o.IsSelected);
    public bool HasSelection => Options.Any(o => o.IsSelected);
    /// <summary>Raise when an option's IsSelected flips (multi-select) so the footer updates.</summary>
    public void RaiseSelection() { OnChanged(nameof(SelectedCount)); OnChanged(nameof(HasSelection)); }
}

/// <summary>A choice flow: one or more questions navigated with a pager. Heuristic A/B/C detection is a
/// single single-select item; the ask-user skill can push several, each single- or multi-select.
/// <see cref="Structured"/> flows come from the skill and own the panel (heuristic won't overwrite).</summary>
public sealed class ChoiceFlow : ObservableObject
{
    public required IReadOnlyList<ChoiceItem> Items { get; init; }
    public bool Structured { get; init; }

    private int _page;
    public int Page
    {
        get => _page;
        set
        {
            if (!Set(ref _page, value)) return;
            OnChanged(nameof(Current)); OnChanged(nameof(Multi)); OnChanged(nameof(Header));
            OnChanged(nameof(HasPager)); OnChanged(nameof(PagerLabel));
            OnChanged(nameof(HasPrev)); OnChanged(nameof(HasNext)); OnChanged(nameof(IsLast));
        }
    }

    public ChoiceItem Current => Items[_page];
    public bool Multi => Current.Multi;
    public string Header => string.IsNullOrWhiteSpace(Current.Question) ? AgentManager.App.L("L.PickOne") : Current.Question!;
    public bool HasPager => Items.Count > 1;
    public string PagerLabel => $"{_page + 1}/{Items.Count}";
    public bool HasPrev => _page > 0;
    public bool HasNext => _page < Items.Count - 1;
    public bool IsLast => _page >= Items.Count - 1;

    /// <summary>Combine every answered question into the message sent to the agent. One question →
    /// just the answer; several → "question: answer" lines.</summary>
    public string BuildMessage()
    {
        if (Items.Count == 1) return Items[0].Answer ?? "";
        return string.Join("\n", Items.Select(i =>
        {
            var q = string.IsNullOrWhiteSpace(i.Question) ? "?" : i.Question!.Trim();
            return $"{q}: {i.Answer ?? "-"}";
        }));
    }
}
