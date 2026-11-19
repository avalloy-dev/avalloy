using Avalonia;
using Avalonia.Controls.Primitives;

namespace Avalloy.Views.Controls;

/// <summary>The gold drawer-pull tab that sits on a seam: an 18×46 Surface.Raised chip, Edge.Card
/// hairline, r9, directional Shadow.Drawer, Accent.Gold chevron.
/// Two independent axes: <see cref="Edge"/> picks the shadow (which seam the tab lives on — the shadow
/// always casts INTO the neighbouring pane, so it never flips with state), <see cref="PointsLeft"/>
/// picks the chevron (which way the action pulls — a sidebar's flips with collapsed/expanded).
/// Placement (seam-centering negative margin) and reveal (opacity fade + hit-test gating — e.g. a
/// passive seam watcher, or hover styles) stay at the usage site; the tab itself is only the chrome,
/// so it never gets hand-rolled again. Theme in Avalloy.Themes/Themes/AlloyControls.axaml.</summary>
public class DrawerPull : TemplatedControl
{
    public enum SeamEdge { Left, Right }

    /// <summary>Which window edge's seam the tab sits on. Left (default) casts Shadow.Drawer rightward
    /// into the content pane; Right casts Shadow.Drawer.Left leftward.</summary>
    public static readonly StyledProperty<SeamEdge> EdgeProperty =
        AvaloniaProperty.Register<DrawerPull, SeamEdge>(nameof(Edge));

    /// <summary>False (default) = › pointing right; true = ‹ pointing left. The chevron points the way
    /// the pull opens/closes, so it may flip at runtime while Edge stays fixed.</summary>
    public static readonly StyledProperty<bool> PointsLeftProperty =
        AvaloniaProperty.Register<DrawerPull, bool>(nameof(PointsLeft));

    public SeamEdge Edge { get => GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }
    public bool PointsLeft { get => GetValue(PointsLeftProperty); set => SetValue(PointsLeftProperty, value); }
}
