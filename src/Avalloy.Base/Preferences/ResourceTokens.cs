using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avalloy.Preferences;

/// <summary>
/// Typed JSON → Avalonia resource parsers used by <see cref="ResourceCascade"/>.
/// Each <c>TryApply…</c> writes into the dictionary on success and returns
/// <c>true</c>; on a type/parse mismatch it leaves the dictionary alone and
/// returns <c>false</c>. Apps that need a one-off conversion outside the
/// cascade engine can call these directly.
/// </summary>
public static class ResourceTokens
{
    /// <summary>Hex string ("#RRGGBB" / "#AARRGGBB") or Avalonia color name
    /// → <see cref="SolidColorBrush"/>.</summary>
    public static bool TryApplyColor(IResourceDictionary res, string key, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        var raw = value.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!Color.TryParse(raw, out var color)) return false;
        res[key] = new SolidColorBrush(color);
        return true;
    }

    /// <summary>Comma-separated family fallback chain → <see cref="FontFamily"/>.</summary>
    public static bool TryApplyFontFamily(IResourceDictionary res, string key, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        var family = value.GetString();
        if (string.IsNullOrWhiteSpace(family)) return false;
        res[key] = new FontFamily(family);
        return true;
    }

    /// <summary>Either a weight name ("SemiBold") or a numeric weight (600) →
    /// <see cref="FontWeight"/>.</summary>
    public static bool TryApplyFontWeight(IResourceDictionary res, string key, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString();
            if (Enum.TryParse<FontWeight>(name, ignoreCase: true, out var fw))
            {
                res[key] = fw;
                return true;
            }
            return false;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
        {
            res[key] = (FontWeight)n;
            return true;
        }
        return false;
    }

    /// <summary>Positive number → <c>double</c>. Used for both font sizes and
    /// layout sizes (row heights, paddings, …). Non-positive values are
    /// rejected so a "0" in JSON can't accidentally collapse a UI element.</summary>
    public static bool TryApplyDouble(IResourceDictionary res, string key, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) return false;
        if (!value.TryGetDouble(out var d) || d <= 0) return false;
        res[key] = d;
        return true;
    }

    /// <summary>Avalonia <see cref="Thickness"/> from a JSON string
    /// ("2", "2,4", "1,2,3,4") or a JSON number (uniform thickness).
    /// Negative values are accepted — themes sometimes use negative margins
    /// to claw back layout space — but NaN / infinity are rejected.</summary>
    public static bool TryApplyThickness(IResourceDictionary res, string key, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)
            && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            res[key] = new Thickness(d);
            return true;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (TryParseThickness(raw, out var t))
            {
                res[key] = t;
                return true;
            }
        }
        return false;
    }

    /// <summary>Avalonia <see cref="CornerRadius"/> from a JSON string
    /// ("4", "4,4,0,0") or a JSON number (uniform radius). Negative values
    /// are rejected — Avalonia's CornerRadius clamps to non-negative anyway
    /// and a negative input almost always indicates a typo.</summary>
    public static bool TryApplyCornerRadius(IResourceDictionary res, string key, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d)
            && !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0)
        {
            res[key] = new CornerRadius(d);
            return true;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (TryParseCornerRadius(raw, out var cr))
            {
                res[key] = cr;
                return true;
            }
        }
        return false;
    }

    private static bool TryParseCornerRadius(string raw, out CornerRadius result)
    {
        result = default;
        var parts = raw.Split(',');
        var nums = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out nums[i])
                || double.IsNaN(nums[i]) || double.IsInfinity(nums[i]) || nums[i] < 0)
            {
                return false;
            }
        }
        switch (nums.Length)
        {
            case 1: result = new CornerRadius(nums[0]); return true;
            case 4: result = new CornerRadius(nums[0], nums[1], nums[2], nums[3]); return true;
            default: return false;
        }
    }

    /// <summary>Parse a Thickness from "u", "h,v", or "l,t,r,b". Whitespace is
    /// trimmed; commas are the separator (matches XAML's parser). Avalonia's
    /// <see cref="Thickness"/> doesn't ship a public TryParse so we roll our
    /// own — keeps the JSON syntax for thickness identical to what theme
    /// authors already write in XAML markup.</summary>
    private static bool TryParseThickness(string raw, out Thickness result)
    {
        result = default;
        var parts = raw.Split(',');
        var nums = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out nums[i])
                || double.IsNaN(nums[i]) || double.IsInfinity(nums[i]))
            {
                return false;
            }
        }
        switch (nums.Length)
        {
            case 1: result = new Thickness(nums[0]); return true;
            case 2: result = new Thickness(nums[0], nums[1]); return true;
            case 4: result = new Thickness(nums[0], nums[1], nums[2], nums[3]); return true;
            default: return false;
        }
    }

    /// <summary>Linearly interpolates between two colors. <paramref name="t"/>
    /// is clamped to [0,1]. Handy when an app's user-overrides callback derives
    /// a tint / shade family from a single accent color.</summary>
    public static Color LerpColor(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * t),
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }
}
