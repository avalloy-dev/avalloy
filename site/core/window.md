# Windows and the title bar

## ShellWindow

`ShellWindow` is the base `Window`. It pre-wires the extended client area so the window paints
into the title bar region, the thick macOS title bar via `MacOSTitleBar`, frontmost activation
on first open, and the keyboard path: a tunnelled `KeyDown` handler that gives the subclass
first crack through `OnPreviewKeyDownAsync`, then the injected `KeyboardDispatcher`, then
`OnUnhandledKeyDown`. It also implements `ICommandSurface` by delegating to that dispatcher,
so toolbars and the palette fire commands by name with nothing to wire. All of the macOS
plumbing no-ops elsewhere.

`ShellDialog` is the same window for modal children shown through `ShowDialog`. It keeps the
chrome but fixes the activation cascade a modal child needs on macOS: it does not re-activate
the app on open, which would re-key the parent and leave the dialog with dimmed traffic
lights, and it makes itself key one dispatcher tick after opening, once AppKit has it in the
window list.

## AlloyTitleBar

The integrated title bar for extended-client-area windows. It never draws caption buttons.
Native chrome owns them, and the control measures around them:

- `Content` fills the bar, padded by `Padding`, whose platform default clears the macOS
  traffic lights. Set `Padding="0"` for content that centers itself.
- `RightContent` is pinned right and inset past the OS caption buttons. It uses
  `WindowDecorationMargin` when the platform reports it, else the exact DWM caption-button
  bounds on Windows, DPI-aware and three-or-four-button-aware, else a worst-case reservation.
- Empty bar space drags the window through `BeginMoveDrag`, and a double-tap toggles Normal
  and Maximized, honouring the window's `CanMaximize`. A press on any focusable child, a
  button, a menu item, a text box, is left alone. `CanDrag` and
  `CanToggleWindowStateOnDoubleTapped` switch each off.

```xml
<tb:AlloyTitleBar MinHeight="44" Padding="0" Background="{DynamicResource Surface.Window}">
  <TextBlock Text="My App" Classes="secondary" HorizontalAlignment="Center" VerticalAlignment="Center"/>
  <tb:AlloyTitleBar.RightContent>
    <StackPanel Orientation="Horizontal" Spacing="4">
      <Button Classes="bare"><icons:GlyphIcon Glyph="moon" Size="15"/></Button>
    </StackPanel>
  </tb:AlloyTitleBar.RightContent>
</tb:AlloyTitleBar>
```

`TitleBarHeaderReserve` keeps a header zone hosted in the bar clear of both ends: the right
margin reserves the actions cluster, and the left margin tracks a live leading width,
typically the sidebar's reserved column, updated every dock-animation frame.

`ShellChrome.axaml`, included once from `avares://Avalloy.Desktop/Themes/ShellChrome.axaml`,
supplies the `shell` class that collapses the caption-button and sizing boilerplate and the
`ShellTitleBarContentInset` resource that keeps the traffic-light measurement in Avalloy.

## Geometry

`WindowGeometry.BindGeometry` restores size, position and maximized state onto a window and
writes them back as they change. The model is an `IWindowGeometry` on a preferences object,
or `IMaximizableWindowGeometry` to remember the maximized flag. Position is nullable: null
means let the OS place the window, which is the first launch and after a reset. Keep geometry
in the per-machine state file rather than roaming preferences; it is display-bound.

`WindowPlacement` centres a top-level window over another without parenting it. On macOS an
owned window never becomes key, so its traffic lights stay grey; these helpers give the
opens-over-the-window-I-am-in placement while keeping it an independent top-level.
