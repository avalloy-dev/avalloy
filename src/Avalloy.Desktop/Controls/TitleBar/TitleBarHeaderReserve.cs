using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;

namespace Avalloy.Controls.TitleBar;

/// <summary>
/// Keeps a titlebar-hosted header zone clear of BOTH ends of the bar: the right margin reserves
/// the actions cluster's span; the left margin tracks a live leading width (typically the
/// sidebar's reserved column, so header content starts over the canvas — updated every
/// dock-animation frame and splitter drag). The zone spans the bar's full width (a Panel
/// overlay), so a centered transient title still centers against the whole strip.
/// </summary>
public static class TitleBarHeaderReserve
{
    /// <param name="leadingWidth">Live leading reserve (e.g. the sidebar column's ActualWidth);
    /// null = no left margin management.</param>
    /// <param name="relayoutSource">Control whose LayoutUpdated re-runs the math (the body grid —
    /// fires on every dock-animation frame); null = only bar/cluster bounds changes re-run it.</param>
    /// <param name="leadingPad">Added to the leading width — lands the header text on the content
    /// pane's inner line rather than the bare pane edge.</param>
    /// <param name="trailingPad">Breathing room after the cluster.</param>
    public static void Wire(
        AlloyTitleBar bar, Control cluster, Control headerZone,
        Func<double>? leadingWidth = null, Control? relayoutSource = null,
        double leadingPad = 11, double trailingPad = 10)
    {
        void Update()
        {
            if (bar.Bounds.Width <= 0) return;
            if (cluster.TranslatePoint(new Point(0, 0), bar) is not { } clusterOrigin) return;
            var reserve = Math.Max(0, bar.Bounds.Width - clusterOrigin.X) + trailingPad;
            var left = leadingWidth is not null ? Math.Max(0, leadingWidth() + leadingPad) : 0;
            headerZone.Margin = new Thickness(left, 0, reserve, 0);
        }

        cluster.GetObservable(Visual.BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => Update()));
        bar.GetObservable(Visual.BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ => Update()));
        if (relayoutSource is not null) relayoutSource.LayoutUpdated += (_, _) => Update();
    }
}
