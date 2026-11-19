using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avalloy.Views.Controls;

/// <summary>The standard trigger-anchored dropdown frame for RICH popovers — search, sections, trees.
/// Put the trigger chip in <see cref="Trigger"/> and the popover body in Content; bind
/// <see cref="IsOpen"/> TwoWay to the owning VM's *MenuOpen flag (pick handlers set it back to false).
/// Keyboard: the trigger is a real Button (tabbable, Enter/Space toggles), F4/Alt+Down open, Escape closes
/// and returns focus to the trigger; light dismiss covers pointer-away. The popover Border carries the shared
/// <c>popoverframe</c> class + an inner clip, so bodies with full-bleed header/footer bars need no chrome
/// of their own. Plain "pick one" lists want <see cref="AlloySelect"/> (a native ComboBox) instead — new
/// dropdowns compose one of these two, never another hand-rolled Popup + Border copy.</summary>
public class AlloyDropDown : ContentControl
{
    /// <summary>The always-visible trigger chip. Any content; the template wraps it in a bare Button.</summary>
    public static readonly StyledProperty<object?> TriggerProperty =
        AvaloniaProperty.Register<AlloyDropDown, object?>(nameof(Trigger));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<AlloyDropDown, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Fixed popover width (fixed width, scrolls — the host never grows). NaN = size to content.</summary>
    public static readonly StyledProperty<double> PopoverWidthProperty =
        AvaloniaProperty.Register<AlloyDropDown, double>(nameof(PopoverWidth), double.NaN);

    public static readonly StyledProperty<PlacementMode> PlacementProperty =
        AvaloniaProperty.Register<AlloyDropDown, PlacementMode>(nameof(Placement), PlacementMode.BottomEdgeAlignedLeft);

    /// <summary>Vertical gap between trigger and surface (spec: 6 below). Top-anchored menus want -6.</summary>
    public static readonly StyledProperty<double> PopoverOffsetProperty =
        AvaloniaProperty.Register<AlloyDropDown, double>(nameof(PopoverOffset), 6d);

    public object? Trigger { get => GetValue(TriggerProperty); set => SetValue(TriggerProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public double PopoverWidth { get => GetValue(PopoverWidthProperty); set => SetValue(PopoverWidthProperty, value); }
    public PlacementMode Placement { get => GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }
    public double PopoverOffset { get => GetValue(PopoverOffsetProperty); set => SetValue(PopoverOffsetProperty, value); }

    /// <summary>Layout room the template's frame reserves for the painted Menu.Shadow (popup hosts clip
    /// rendering to their content size). Mirrors the Tokens.axaml "Menu.ShadowRoom" Thickness — keep in sync.</summary>
    private const double ShadowRoom = 20;

    private Button? _trigger;
    private Border? _frame;
    private Popup? _popup;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_trigger is { } oldT)
        {
            oldT.Click -= OnTriggerClick;
            oldT.KeyDown -= OnTriggerKeyDown;
        }
        if (_frame is { } oldF) oldF.KeyDown -= OnFrameKeyDown;
        if (_popup is { } oldP) oldP.Opened -= OnPopupOpened;

        _trigger = e.NameScope.Find<Button>("PART_Trigger");
        _frame = e.NameScope.Find<Border>("PART_Frame");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        if (_trigger is { } t)
        {
            t.Click += OnTriggerClick;
            t.KeyDown += OnTriggerKeyDown;
            // Anchor to the trigger (not the whole control) so the popover hugs the chip.
            if (_popup is { } p) p.PlacementTarget = t;
        }
        if (_frame is { } f) f.KeyDown += OnFrameKeyDown;
        if (_popup is { } pop) pop.Opened += OnPopupOpened;
    }

    // Spec: popover min-width = trigger width. The room margin lives inside the popup, so widen by it
    // (a plain MinWidth binding would shrink the visible card 2×room narrower than the trigger).
    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (_popup is { } p) p.MinWidth = Bounds.Width + ShadowRoom * 2;
    }

    private void OnTriggerClick(object? sender, RoutedEventArgs e) =>
        SetCurrentValue(IsOpenProperty, !IsOpen);

    // The platform combobox open gestures; Enter/Space already toggle via Button.Click.
    private void OnTriggerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F4 || (e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            SetCurrentValue(IsOpenProperty, true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsOpen)
        {
            SetCurrentValue(IsOpenProperty, false);
            e.Handled = true;
        }
    }

    // Escape from inside the popover (focus moved into a search box, tree, …): close + hand focus back.
    private void OnFrameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        SetCurrentValue(IsOpenProperty, false);
        _trigger?.Focus();
        e.Handled = true;
    }
}
