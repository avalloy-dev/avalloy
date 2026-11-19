using Avalloy.Controls.Logs;
using Avalloy.Services;

namespace Avalloy.Commands;

/// <summary>Open (or re-activate) the <see cref="LogViewerWindow"/>. Lives in
/// Avalloy.Desktop, so assembly scanning won't discover it — apps opt in with
/// <c>Commands.Register&lt;ShowLogViewerCommand&gt;()</c> plus a keymap row.</summary>
[Command("ShowLogViewer", DisplayName = "Show Log Viewer", Category = "Debug",
    AllowWhenTextInputFocused = true)]
public sealed class ShowLogViewerCommand(ShellHost shell) : IAlloyCommand
{
    public Task ExecuteAsync()
    {
        LogViewerWindow.ShowOrActivate(shell.MainWindow);
        return Task.CompletedTask;
    }
}
