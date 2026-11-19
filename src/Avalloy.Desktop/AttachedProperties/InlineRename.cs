using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Avalloy.AttachedProperties;

/// <summary>
/// Inline-rename behavior for a <see cref="TextBox"/> used as an in-place edit box (the label↔box swap pattern).
/// When <see cref="IsEditingProperty"/> turns true the box focuses + selects-all — deferred to the next layout
/// pass, since it usually becomes visible the same frame the flag flips. <b>Enter</b> and <b>lost-focus</b> run
/// <see cref="CommitCommandProperty"/>; <b>Escape</b> runs <see cref="CancelCommandProperty"/> — each passed the
/// box's <c>DataContext</c> (the row item) as the command parameter.
///
/// Generic: references no app types. Collapses the per-row KeyDown/LostFocus/focus code-behind a list view would
/// otherwise duplicate for every renameable node kind. Bind <c>IsEditing</c> to the row's "is renaming" flag and
/// the two commands to handlers that take the row item.
/// </summary>
public static class InlineRename
{
    /// <summary>True while the row is in edit mode — flipping to true focuses + selects-all the box.</summary>
    public static readonly AttachedProperty<bool> IsEditingProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEditing", typeof(InlineRename));

    /// <summary>Run on Enter / lost-focus, with the box's DataContext as the parameter.</summary>
    public static readonly AttachedProperty<ICommand?> CommitCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("CommitCommand", typeof(InlineRename));

    /// <summary>Run on Escape, with the box's DataContext as the parameter.</summary>
    public static readonly AttachedProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("CancelCommand", typeof(InlineRename));

    public static void SetIsEditing(TextBox o, bool v) => o.SetValue(IsEditingProperty, v);
    public static bool GetIsEditing(TextBox o) => o.GetValue(IsEditingProperty);
    public static void SetCommitCommand(TextBox o, ICommand? v) => o.SetValue(CommitCommandProperty, v);
    public static ICommand? GetCommitCommand(TextBox o) => o.GetValue(CommitCommandProperty);
    public static void SetCancelCommand(TextBox o, ICommand? v) => o.SetValue(CancelCommandProperty, v);
    public static ICommand? GetCancelCommand(TextBox o) => o.GetValue(CancelCommandProperty);

    static InlineRename()
    {
        IsEditingProperty.Changed.AddClassHandler<TextBox>((tb, e) =>
        {
            if (e.GetNewValue<bool>()) FocusDeferred(tb);
        });
        // Hook the key/blur handlers once either command is attached (idempotent — safe across rebinds/recycling).
        CommitCommandProperty.Changed.AddClassHandler<TextBox>((tb, _) => Hook(tb));
        CancelCommandProperty.Changed.AddClassHandler<TextBox>((tb, _) => Hook(tb));
    }

    private static void Hook(TextBox tb)
    {
        tb.KeyDown -= OnKeyDown;
        tb.KeyDown += OnKeyDown;
        tb.LostFocus -= OnLostFocus;
        tb.LostFocus += OnLostFocus;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.Key == Key.Enter) { Run(GetCommitCommand(tb), tb); e.Handled = true; }
        else if (e.Key == Key.Escape) { Run(GetCancelCommand(tb), tb); e.Handled = true; }
    }

    // Blur commits (matches Enter). When Escape already cancelled (IsEditing→false), the commit handler the VM
    // wires is expected to no-op on a not-editing row — same contract the per-row code-behind relied on.
    private static void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) Run(GetCommitCommand(tb), tb);
    }

    private static void Run(ICommand? cmd, TextBox tb)
    {
        var p = tb.DataContext;
        if (cmd is not null && cmd.CanExecute(p)) cmd.Execute(p);
    }

    // The box usually becomes visible the same frame IsEditing flips, so defer focus to the next layout pass.
    private static void FocusDeferred(TextBox tb) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!GetIsEditing(tb)) return;   // cancelled before the deferred call ran
            tb.Focus();
            tb.SelectAll();
        }, DispatcherPriority.Loaded);
}
