namespace Avalloy.Services;

/// <summary>
/// Minimum dialog surface every Avalloy-built app provides. Used by
/// commands that need to surface information or confirm an action. Apps that
/// want richer interactions (multi-button, custom layouts) register their own
/// IDialogService implementation; the default lives in
/// <see cref="DialogService"/>.
/// </summary>
public interface IDialogService
{
    /// <summary>Show an informational dialog with the given title and message
    /// and resolve when the user dismisses it.</summary>
    Task ShowInfoAsync(string title, string message);

    /// <summary>Show a confirm prompt and resolve to <c>true</c> on accept,
    /// <c>false</c> on cancel. Commands that need confirmation inject this
    /// and prompt inside their <see cref="Commands.IAlloyCommand.ExecuteAsync"/>
    /// body.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirm");

    /// <summary>Confirm prompt with an additional "Don't show this again"
    /// checkbox. Returns (Confirmed, SuppressFuture); the caller persists
    /// the suppression flag into whatever pref gates this dialog.
    /// </summary>
    /// <param name="confirmLabel">Text for the accept button. Defaults to
    /// "Confirm"; callers pass a verb (e.g. "Move", "Copy", "Delete") when
    /// that reads more clearly than a generic confirm.</param>
    Task<(bool Confirmed, bool SuppressFuture)> ConfirmWithSuppressAsync(string title, string message, string confirmLabel = "Confirm");
}
