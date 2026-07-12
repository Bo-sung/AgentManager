using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AgentManager.Controls;

/// <summary>
/// Engine id (cc/gx/agy/pi) + part param (main|dim|line) → the engine's brand brush
/// (CcBrand / CcBrandDim / CcBrandLine, …). Looks up the themed resource so engine accenting
/// (quick-reply container, badges, …) needs no per-engine DataTriggers. Falls back gracefully
/// when a variant is missing (e.g. pi has only the main brush).
/// </summary>
public sealed class EngineBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var prefix = (value as string) switch { "cc" => "Cc", "gx" => "Gx", "agy" => "Agy", "pi" => "Pi", _ => null };
        var part = (parameter as string ?? "main").ToLowerInvariant();
        if (prefix is not null)
        {
            var key = part switch { "dim" => prefix + "BrandDim", "line" => prefix + "BrandLine", _ => prefix + "Brand" };
            if (Find(key) is Brush b) return b;
        }
        // custom engine: use its manifest color (engines/<id>.json "color") for the main accent
        else if (value is string cid && part == "main" && EngineVisual.ColorFor(cid) is Brush cb) return cb;
        // fallback when the engine/variant resource is absent
        return Find(part switch { "dim" => "Bg3", "line" => "Line", _ => "Accent" }) ?? Brushes.Gray;
    }

    private static Brush? Find(string key) => Application.Current?.TryFindResource(key) as Brush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
