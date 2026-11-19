# Services

## Dialogs

`IDialogService` is the minimum dialog surface every app provides, used by commands that need
to inform or confirm. `DialogService` is the default, parented to the main window through
`ShellHost`, and routes confirms through `ConfirmationDialog`, a chromeless card styled
entirely through the theme tokens so every app gets the same themed prompt. It also exposes
`AskAsync`, `AskWithSuppressAsync` for a don't-ask-again checkbox, and a three-way
`AskThreeWayAsync`. Register your own `IDialogService` for richer interactions.
`IFolderPicker` and `FolderPicker` wrap the storage-provider folder picker the same way.

## Toasts

`ToastService` is the app-wide facade for transient notifications over Avalonia's
`WindowNotificationManager`; call `Attach` from the shell window once the top-level exists.
`ToastSeverity` has a `Notice` tier between success and warning: the action finished, but not
everything happened, a skipped conflict or a cancelled operation. Nothing went wrong, so it
renders neutral.

## Logging

`LogBuffer` (from `Avalloy.Common`) is a thread-safe bounded ring of recent entries, retained
from process start so a viewer opened later still sees startup. `AvalloyApp` sets
`LogBuffer.DefaultPost` to the dispatcher, so any buffer raises its batches on the UI thread. `LogViewer` is the embeddable panel over it: live tail,
level, category and text filters, pause, copy, and reveal-the-log-file through `FileReveal`.
`LogViewerWindow` is the ready-made non-modal host, process-singleton; apps register
`ShowLogViewerCommand` and bind a key.

`IEventRecorder` records named app events, a feature used or a milestone reached, through
ordinary logging with no analytics backend, so they land in every sink. A reporter that models
breadcrumbs and keeps Information-level entries, as Sentry does, attaches them to any later error.

`EventPump<T>` bridges a background `IAsyncEnumerable` event stream onto the UI thread with
one dispatcher post per burst, and tests without a dispatcher through its `uiPost` seam.

## macOS

All of these no-op elsewhere.

| Helper | What |
| --- | --- |
| `MacAppActivation` | Bring the app and a window to the foreground for real, through AppKit, where `Window.Activate` only orders front. |
| `MacAppIcon` | Set the Dock and Cmd-Tab icon at startup, so it shows during `dotnet run`. |
| `MacDockBadge` | The red pill on the Dock tile. |
| `MacCtrlTabMonitor` | Intercept physical Ctrl+Tab, which AppKit consumes before Avalonia sees it. |
| `MacWebViewFirstMouse` | Make a `WKWebView` accept the click that activates the window. |
| `NativeHostFocus` | Hand OS keyboard focus back from an embedded native web view to the Avalonia top-level. |

`RelativeTime` is the one "N min ago" formatter, and `BrushColorConverter` feeds theme brushes
into APIs that want a `Color`.
