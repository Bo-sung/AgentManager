using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AgentManager.Controls;

/// <summary>
/// Permission risk token (r0/r1/r2/r3/rn) + part param (main|dim|line|text) → a theme-independent
/// brush. The shared "risk gradient": color encodes risk level, not the mode name, so the same
/// danger reads the same color across engines. Hex from the permission-mode design
/// (cyan=safe · green=write · amber=elevated · red=full · gray=none).
/// </summary>
public sealed class RiskBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (string main, string dim, string line, string text)> Map = new()
    {
        ["r0"] = ("#46B5D8", "#2446B5D8", "#6B46B5D8", "#8FD4EA"),
        ["r1"] = ("#34D3A6", "#2434D3A6", "#6B34D3A6", "#73E3C2"),
        ["r2"] = ("#F5B531", "#26F5B531", "#73F5B531", "#FFD067"),
        ["r3"] = ("#FF5247", "#26FF5247", "#73FF5247", "#FF8076"),
        ["rn"] = ("#6B7A86", "#246B7A86", "#666B7A86", "#9AA8B2"),
    };

    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var risk = value as string ?? "rn";
        if (!Map.TryGetValue(risk, out var c)) c = Map["rn"];
        var part = (parameter as string ?? "main").ToLowerInvariant();
        var hex = part switch { "dim" => c.dim, "line" => c.line, "text" => c.text, _ => c.main };
        if (Cache.TryGetValue(hex, out var cached)) return cached;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
