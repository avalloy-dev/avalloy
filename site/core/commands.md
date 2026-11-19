# Commands and keybindings

A command is a named, invokable action. Implementations are resolved per invocation from the
DI container, so dependencies are constructor parameters and there is no context bag threaded
through.

```csharp
[Command("app.settings", DisplayName = "Settings", Icon = "Settings", Category = "Shell",
    AllowWhenTextInputFocused = true)]
public sealed class OpenSettingsCommand(ShellViewModel shell) : IAlloyCommand
{
    public Task ExecuteAsync() { shell.SettingsOpen = true; return Task.CompletedTask; }
}
```

`[Command]` publishes the metadata menus, toolbars, the palette and the shortcut editor need:
`DisplayName`, `Category`, `Icon` (a glyph id), `IsUndoable`, `RequiresConfirmation`,
`ShowInToolbar`, and `AllowWhenTextInputFocused`, which lets a chord fire while a text box has
focus. `IConditionalCommand` adds `CanExecute` for commands gated on runtime state; the
dispatcher checks it and surfaces read it for enable/disable.

## Registry and dispatcher

`CommandRegistry` maps a name to a `CommandDescriptor` and the implementation type.
Register explicitly, or scan an assembly:

```csharp
Commands.Register<OpenSettingsCommand>();
Commands.RegisterFromAssembly(typeof(App).Assembly);
```

`KeyboardDispatcher` inverts the keymap into gesture to command name, resolves the command
through DI, and invokes it. The keymap is a delegate, so the dispatcher is decoupled from any
preferences backend: apps merge built-in defaults with user overrides and call `Rebuild` when
those change. `RunByNameAsync` is the path toolbars and the palette use through
`ICommandSurface`.

Gestures are strings parsed by `GestureParser`, which wraps Avalonia's parser with `Mod+`
sugar resolving to Cmd on macOS and Ctrl elsewhere, so a keymap in `preferences.json` is
portable across platforms.

```csharp
protected override IReadOnlyDictionary<string, IReadOnlyList<string>> BuildKeymap() =>
    new Dictionary<string, IReadOnlyList<string>>
    {
        ["app.settings"] = ["Mod+OemComma"],
        ["app.quit"] = ["Mod+Q"],
        ["ShowLogViewer"] = ["Shift+F8"],
    };
```

## The palette

`CommandPaletteViewModel` is a filtered, keyboard-driven list over a fixed set of
`PaletteItem`: type to narrow, arrows to move, Enter to run. `CommandPaletteView` is its
surface, an overlay card with a search box and result list. It claims the arrows and Enter
wherever focus is in it, the search box or the list, so the selection still wraps and skips
disabled rows after Tab. `HandleKey` on the view model decides what each key does. The
selected row is scrolled into view however it moved. The card is at most 640 wide and shrinks
to the window less 24 px a side. The search box is named after the placeholder and the list
"Results" for screen readers.

The view's look can be tuned per palette, with today's look as every default: `ShowGlyphs`
(true), `RowHeight` (40), `HintFontSize` (11), `HintForeground` (the `Text.Ghost` token, set
by a style so a local value or an app style wins), and `ListName` ("Results"). To follow
another theme token, bind it: `HintForeground="{DynamicResource Text.Muted}"`.
`PaletteOptions.IgnoreModifiedKeys` leaves arrows and Enter alone while a modifier is held,
so Shift+arrow selects text in the search box.

It is deliberately not tied to the registry. `FromRegistry` builds one over your commands,
but the same control is the natural scoped picker, "Move to project...", where the list is a
handful of one kind of thing no registry knows about. Scoped callers pass their own
`Placeholder`.

### Richer rows, recents and ranking

All of this is opt-in; a palette built with the defaults looks and behaves as it always has.
The options go through their own entry points, `CommandPaletteViewModel.Create(items, close,
options)` and `CommandPaletteViewModel.FromRegistryWith(registry, dispatcher, options)`. They
are not overloads, so an existing call such as `FromRegistry(registry, dispatcher, null)`
still means what it did.

- **Disabled rows.** `PaletteItem.IsEnabled = false` lists a row greyed out, with an optional
  `Note` saying why. The arrows step over it, clicking or Enter does nothing, it sorts below
  enabled rows, and screen readers hear it as unavailable. `FromRegistryWith` keeps closed-gate
  commands this way with `KeepDisabled`.
- **Several hints.** `PaletteItem.Hints` takes every shortcut in your own notation (`⌘D`,
  `⌃A H`). The row prints the first two and "+N", the tooltip lists them all, and the
  accessible name reads them all. Hints are not searched. In `FromRegistryWith`, `Hints` is a
  formatter from command to strings; to show all the dispatcher's gestures, pass
  `d => dispatcher.GesturesFor(d.Name)`.
- **Your own rows.** Any `PaletteItem` sits beside registry rows, such as one row per
  parameter, each with its own `Run`. `Key` identifies a row for recents; registry rows are
  keyed by command name. `Detail` is quiet text after the label, such as a category.
- **Recents.** `PaletteOptions.Recent` lists keys, most recent first. With an empty query
  those rows float up, and with a query they break ties. `PaletteOptions.Used` is called with
  each row the palette runs, and `PaletteRecents` is a capped list that plugs into both.
  Saving it is up to the app.
- **Scored search.** With `PaletteMatching.Scored`, a match at the very start or at word
  starts ranks first, and contiguous letters beat scattered ones. A row that matches on its
  label beats one that matches only on its hint. `MaxResults` (default 40) applies after
  ranking.

```csharp
var recents = new PaletteRecents(seed: saved);
var rows = CommandPaletteViewModel.FromRegistryWith(Commands, Dispatcher, new RegistryPaletteOptions
{
    KeepDisabled = true,
    DisabledNote = _ => "not available here",
    Hints = d => MyKeyLabels(d.Name),
});
var vm = CommandPaletteViewModel.Create([.. rows, .. tabRows], Close, new PaletteOptions
{
    Matching = PaletteMatching.Scored,
    Recent = recents.Keys,
    Used = recents.Use,
});
```

## The shortcut editor

`KeyboardShortcutsEditor` binds to a `KeyboardShortcutsViewModel`, one row per registered
command, each row a list of recordable gesture chips. Press a chord into a chip to bind it,
Escape to unbind. The view model resolves conflicts by moving the gesture off whatever
command held it, with a toast if one is wired.

Everything app-specific goes through `IKeymapStore`: the effective gestures for a command,
the user override if one exists else the defaults, and where overrides persist. Mutations are
silent by design; batch several edits, then `RaiseChanged` once so the preferences chokepoint
sees one change and triggers a single save and dispatcher rebuild.

`HotkeyBox` is the chip on its own: a click-and-press field that stores the captured chord as
a `Mod+` string.
