using System;
using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Reactive;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

namespace Avalloy.Views.Controls;

/// <summary>A tiny indeterminate spinner driven by a COMPOSITION animation. Composition animations run
/// on the render thread (docs: graphics-animation/composition-animations), so this keeps turning even
/// while the UI thread is blocked — the case a normal transition can't survive (a big view realizing in
/// one UI-thread layout burst would freeze it mid-spin). Started when the control becomes visible,
/// before that burst runs.</summary>
public partial class BusySpinner : UserControl
{
    private bool _spinning;

    public BusySpinner()
    {
        InitializeComponent();
        // The composition visual exists once the newly-visible control has been laid out — start (or
        // retry) each time it flips visible.
        this.GetObservable(IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(v => { if (v) EnsureSpin(0); }));
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        EnsureSpin(0);
    }

    private void EnsureSpin(int attempt)
    {
        if (_spinning || !IsVisible) return;
        // Post at Render priority so it lands before any Background-priority realize burst; if the
        // composition visual isn't up yet (control just flipped visible), retry a few frames.
        Dispatcher.UIThread.Post(() =>
        {
            if (_spinning || !IsVisible) return;
            if (this.FindControl<Arc>("Ring") is not { } ring
                || ElementComposition.GetElementVisual(ring) is not { } visual)
            {
                if (attempt < 5) EnsureSpin(attempt + 1);
                return;
            }
            visual.CenterPoint = new Vector3((float)(Width / 2), (float)(Height / 2), 0f);
            var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(0f, 0f);
            anim.InsertKeyFrame(1f, (float)(Math.PI * 2));
            anim.Duration = TimeSpan.FromSeconds(0.9);
            anim.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngle", anim);
            _spinning = true;
        }, DispatcherPriority.Render);
    }
}
