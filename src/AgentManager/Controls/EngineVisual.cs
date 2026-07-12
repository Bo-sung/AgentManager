using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AgentManager.Core.Agents;

namespace AgentManager.Controls;

/// <summary>
/// Resolves a custom engine's brand VISUAL — its icon <see cref="Geometry"/> and color <see cref="Brush"/> — from
/// the engine manifest (engines/&lt;id&gt;.json <c>icon</c>/<c>color</c>). Built-in engines (cc/gx/agy/pi) keep their
/// hardcoded brand icon/color via the <c>EngineIcon</c>/<c>EngineIconByDef</c> style DataTriggers; this fills the gap
/// for user-defined engines so they are never a blank slot. <see cref="DefOf"/> is a static hook (set once by the
/// AppViewModel ctor, like HistoryRowViewModel.EngineResolver) so converters can look up an engine by id.
/// </summary>
public static class EngineVisual
{
    /// <summary>id → EngineDef (full set: built-in + custom). Assigned by AppViewModel to EngineDefFor.</summary>
    public static Func<string, EngineDef?>? DefOf;

    // Branded built-ins own their icon/color through the style triggers → return null so the trigger wins.
    private static readonly HashSet<string> BrandedBuiltins = new(StringComparer.OrdinalIgnoreCase) { "cc", "gx", "agy", "pi" };

    // A small curated set of FILLED glyphs (24x24, matching the brand-logo viewbox; IconView draws them with Filled=True).
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["circle"]   = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z",
        ["square"]   = "M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z",
        ["hexagon"]  = "M12 3l7.79 4.5v9L12 21l-7.79-4.5v-9z",
        ["triangle"] = "M12 3l9 16H3z",
        ["diamond"]  = "M12 2.5l6.5 9.5-6.5 9.5-6.5-9.5z",
        ["spark"]    = "M12 3l2 7 7 2-7 2-2 7-2-7-7-2 7-2z",
        ["bolt"]     = "M13 2L5 13h5l-1 9 10-12h-6z",
        ["bubble"]   = "M4 5h16v10h-9l-4.5 4.5V15H4z",
    };

    private static readonly Dictionary<string, Geometry> GeomCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.Ordinal);

    /// <summary>The names a user can put in a manifest's <c>icon</c> field (besides raw SVG path data).</summary>
    public static IReadOnlyCollection<string> GlyphNames => Glyphs.Keys;

    /// <summary>Geometry for a custom engine's <c>icon</c> spec: a known glyph name, else raw SVG path data, else the
    /// default glyph. Returns <c>null</c> for the branded built-ins so their style trigger supplies the brand logo.</summary>
    public static Geometry? IconFor(string engineId)
    {
        var spec = (DefOf?.Invoke(engineId)?.Icon ?? "").Trim();
        if (spec.Length == 0)
            return BrandedBuiltins.Contains(engineId) ? null : DefaultGlyph;   // pi-worker / custom-without-icon → never blank
        return ParseGeometry(spec) ?? DefaultGlyph;
    }

    /// <summary>Brush for a custom engine's <c>color</c> hex, or null (caller falls back to Accent/brand).</summary>
    public static Brush? ColorFor(string engineId) => ParseBrush(DefOf?.Invoke(engineId)?.Color ?? "");

    private static Geometry DefaultGlyph => ParseGeometry(Glyphs["circle"])!;

    private static Geometry? ParseGeometry(string spec)
    {
        if (Glyphs.TryGetValue(spec, out var glyphPath)) spec = glyphPath;   // name → path
        if (GeomCache.TryGetValue(spec, out var cached)) return cached;
        try
        {
            var g = Geometry.Parse(spec);
            g.Freeze();
            GeomCache[spec] = g;
            return g;
        }
        catch { return null; }   // not a known glyph and not valid path data
    }

    private static Brush? ParseBrush(string colorHex)
    {
        colorHex = (colorHex ?? "").Trim();
        if (colorHex.Length == 0) return null;
        if (BrushCache.TryGetValue(colorHex, out var cached)) return cached;
        try
        {
            if (new BrushConverter().ConvertFromString(colorHex) is Brush b)
            {
                b.Freeze();
                BrushCache[colorHex] = b;
                return b;
            }
        }
        catch { /* invalid hex → null */ }
        return null;
    }
}

/// <summary>Engine id → its icon <see cref="Geometry"/> (custom manifest glyph/SVG, or a default). Used as the
/// DEFAULT setter of the EngineIcon styles; built-in DataTriggers override it for cc/gx/agy/pi.</summary>
public sealed class EngineGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string id ? EngineVisual.IconFor(id) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
