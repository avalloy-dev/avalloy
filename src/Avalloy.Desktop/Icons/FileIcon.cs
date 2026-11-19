using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Avalloy.Icons;

/// <summary>Full-colour file-type badge from the Material Icon Theme. The SVGs are vendored as geometry +
/// their own brand fills (a full-colour pack keeps its colours — NOT <c>currentColor</c>), keyed by file
/// extension and rendered through this one path. An unknown/empty extension renders nothing and takes no
/// space, so it can sit inline before a file path unconditionally.
/// Set <see cref="Ext"/> to a raw extension ("ts", "json", …); aliases (tsx, mjs, …) map to the same icon.</summary>
public sealed class FileIcon : Avalonia.Controls.Control, ICustomHitTest
{
    public static readonly StyledProperty<string?> ExtProperty =
        AvaloniaProperty.Register<FileIcon, string?>(nameof(Ext));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<FileIcon, double>(nameof(Size), 14);

    public string? Ext { get => GetValue(ExtProperty); set => SetValue(ExtProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    static FileIcon()
    {
        AffectsRender<FileIcon>(ExtProperty, SizeProperty);
        AffectsMeasure<FileIcon>(ExtProperty, SizeProperty);
    }

    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize)
        => Resolve(Ext) is null ? default : new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Resolve(Ext) is not { } def) return;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var scale = Size / def.Box;   // material viewBoxes vary (16 / 24 / 32 / 960); normalise to Size.
        var tx = w / 2 - def.Center.X * scale;
        var ty = h / 2 - def.Center.Y * scale;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(tx, ty)))
            foreach (var (brush, geo) in def.Layers)
                context.DrawGeometry(brush, null, geo);
    }

    /// <summary>Hit-test the whole badge rectangle, not just the painted glyph pixels (same treatment as
    /// <see cref="GlyphIcon"/>) — custom-drawn visuals otherwise only hit on their literal draw ops, punching
    /// pointer holes in the rows/buttons the badge sits in. Local coordinates, so test against the layout size.</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    private readonly record struct Def(IReadOnlyList<(IBrush Brush, Geometry Geo)> Layers, double Box, Point Center);

    // ext (lower-case, no dot) -> icon key. Aliases collapse onto the seven vendored Material icons.
    private static readonly Dictionary<string, string> ExtMap = new()
    {
        ["ts"] = "typescript", ["tsx"] = "typescript", ["mts"] = "typescript", ["cts"] = "typescript",
        ["js"] = "javascript", ["jsx"] = "javascript", ["mjs"] = "javascript", ["cjs"] = "javascript",
        ["json"] = "json",
        ["py"] = "python", ["pyi"] = "python",
        ["rs"] = "rust",
        ["md"] = "markdown", ["markdown"] = "markdown", ["mdx"] = "markdown",
        ["html"] = "html", ["htm"] = "html",
    };

    private static Def? Resolve(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return null;
        var key = ext.TrimStart('.').ToLowerInvariant();
        if (!ExtMap.TryGetValue(key, out var icon)) return null;
        return Icons.TryGetValue(icon, out var def) ? def : null;
    }

    // SVG fills kept verbatim from the Material pack; "F1" forces nonzero winding to match SVG (knockout letters
    // on the ts/js badges carve correctly). Box = the source viewBox edge; Center = combined geometry bounds.
    private static Def Make(double box, params (string Color, string Data)[] layers)
    {
        var built = layers
            .Select(l => ((IBrush)new SolidColorBrush(Color.Parse(l.Color)), (Geometry)Geometry.Parse("F1 " + l.Data)))
            .ToArray();
        var bounds = built.Select(l => l.Item2.Bounds).Aggregate((a, b) => a.Union(b));
        return new Def(built, box, bounds.Center);
    }

    private static readonly Dictionary<string, Def> Icons = new()
    {
        ["typescript"] = Make(16,
            ("#0288d1", "M2 2v12h12V2zm4 6h3v1H8v4H7V9H6zm5 0h2v1h-2v1h1a1.003 1.003 0 0 1 1 1v1a1.003 1.003 0 0 1-1 1h-2v-1h2v-1h-1a1.003 1.003 0 0 1-1-1V9a1.003 1.003 0 0 1 1-1")),

        ["javascript"] = Make(16,
            ("#ffca28", "M2 2v12h12V2zm6 6h1v4a1.003 1.003 0 0 1-1 1H7a1.003 1.003 0 0 1-1-1v-1h1v1h1zm3 0h2v1h-2v1h1a1.003 1.003 0 0 1 1 1v1a1.003 1.003 0 0 1-1 1h-2v-1h2v-1h-1a1.003 1.003 0 0 1-1-1V9a1.003 1.003 0 0 1 1-1")),

        ["json"] = Make(960,
            ("#f9a825", "M560-160v-80h120q17 0 28.5-11.5T720-280v-80q0-38 22-69t58-44v-14q-36-13-58-44t-22-69v-80q0-17-11.5-28.5T680-720H560v-80h120q50 0 85 35t35 85v80q0 17 11.5 28.5T840-560h40v160h-40q-17 0-28.5 11.5T800-360v80q0 50-35 85t-85 35zm-280 0q-50 0-85-35t-35-85v-80q0-17-11.5-28.5T120-400H80v-160h40q17 0 28.5-11.5T160-600v-80q0-50 35-85t85-35h120v80H280q-17 0-28.5 11.5T240-680v80q0 38-22 69t-58 44v14q36 13 58 44t22 69v80q0 17 11.5 28.5T280-240h120v80z")),

        ["python"] = Make(24,
            ("#0288d1", "M9.86 2A2.86 2.86 0 0 0 7 4.86v1.68h4.29c.39 0 .71.57.71.96H4.86A2.86 2.86 0 0 0 2 10.36v3.781a2.86 2.86 0 0 0 2.86 2.86h1.18v-2.68a2.85 2.85 0 0 1 2.85-2.86h5.25c1.58 0 2.86-1.271 2.86-2.851V4.86A2.86 2.86 0 0 0 14.14 2zm-.72 1.61c.4 0 .72.12.72.71s-.32.891-.72.891c-.39 0-.71-.3-.71-.89s.32-.711.71-.711"),
            ("#fdd835", "M17.959 7v2.68a2.85 2.85 0 0 1-2.85 2.859H9.86A2.85 2.85 0 0 0 7 15.389v3.75a2.86 2.86 0 0 0 2.86 2.86h4.28A2.86 2.86 0 0 0 17 19.14v-1.68h-4.291c-.39 0-.709-.57-.709-.96h7.14A2.86 2.86 0 0 0 22 13.64V9.86A2.86 2.86 0 0 0 19.14 7zM8.32 11.513l-.004.004.038-.004zm6.54 7.276c.39 0 .71.3.71.89a.71.71 0 0 1-.71.71c-.4 0-.72-.12-.72-.71s.32-.89.72-.89")),

        ["rust"] = Make(32,
            ("#ff7043", "m30 12-4-2V6h-4l-2-4-4 2-4-2-2 4H6v4l-4 2 2 4-2 4 4 2v4h4l2 4 4-2 4 2 2-4h4v-4l4-2-2-4ZM6 16a9.9 9.9 0 0 1 .842-4H10v8H6.842A9.9 9.9 0 0 1 6 16m10 10a9.98 9.98 0 0 1-7.978-4H16v-2h-2v-2h4c.819.819.297 2.308 1.179 3.37a1.89 1.89 0 0 0 1.46.63h3.34A9.98 9.98 0 0 1 16 26m-2-12v-2h4a1 1 0 0 1 0 2Zm11.158 6H24a2.006 2.006 0 0 1-2-2 2 2 0 0 0-2-2 3 3 0 0 0 3-3q0-.08-.004-.161A3.115 3.115 0 0 0 19.83 10H8.022a9.986 9.986 0 0 1 17.136 10")),

        ["markdown"] = Make(32,
            ("#42a5f5", "m14 10-4 3.5L6 10H4v12h4v-6l2 2 2-2v6h4V10zm12 6v-6h-4v6h-4l6 8 6-8z")),

        ["html"] = Make(32,
            ("#e65100", "m4 4 2 22 10 2 10-2 2-22Zm19.72 7H11.28l.29 3h11.86l-.802 9.335L15.99 25l-6.635-1.646L8.93 19h3.02l.19 2 3.86.77 3.84-.77.29-4H8.84L8 8h16Z")),
    };
}
