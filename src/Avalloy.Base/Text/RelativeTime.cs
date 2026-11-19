using System;

namespace Avalloy.Text;

/// <summary>Shared "N min ago" formatter for recency timestamps. One implementation so every surface
/// reads identically, instead of screens keeping private copies of the same coarse-bucket phrasing.</summary>
public static class RelativeTime
{
    /// <summary>Format a past instant as a coarse "ago" phrase, or "" for null / a future/zero delta (never-touched
    /// rows carry a null timestamp, and callers gate visibility on the empty string).</summary>
    public static string Format(DateTimeOffset? at)
    {
        if (at is not { } t) return "";
        var d = DateTimeOffset.Now - t;
        if (d < TimeSpan.Zero) return "";
        if (d.TotalHours < 1) { var m = Math.Max(1, (int)d.TotalMinutes); return $"{m} min ago"; }
        if (d.TotalHours < 24) { var h = (int)d.TotalHours; return h == 1 ? "1 hour ago" : $"{h} hours ago"; }
        var days = (int)d.TotalDays;
        if (days == 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        if (days < 30) { var w = days / 7; return w == 1 ? "1 week ago" : $"{w} weeks ago"; }
        if (days < 365) { var mo = days / 30; return mo == 1 ? "1 month ago" : $"{mo} months ago"; }
        var y = days / 365; return y == 1 ? "1 year ago" : $"{y} years ago";
    }
}
