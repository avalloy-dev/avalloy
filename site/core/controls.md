# Controls

Every control here binds theme tokens, never literals, and works under either theme engine.

## TabStrip

A horizontal tab strip with drag-to-reorder and a FLIP neighbour-slide, cross-strip transfer
between siblings sharing a `DragOverlay` canvas, pin, lock, duplicate and close actions, a
new-tab button, and a themable active-tab indicator. It drives any `ITabHost`; the per-tab
template binds to `ITabItem`, so a file manager's panes and an agent app's sessions back a
tab without a shared base class. Optional affordances are gated by the host's capability
flags, so a simpler host gets a simpler strip. `TabActivity` renders a live status on
background tabs, orthogonal to the identity icon.

## The sidebar framework

Two pieces, extracted whole so the feel survives reuse. `SidebarDockController` owns the
window-side mechanics: the reserved column at rail width when unpinned and at the persisted,
clamped docked width when pinned, with the cubic-ease dock morph between them, live width
tracking while pinned, and the passive edge pull on the seam with a dwell before the reveal.
`SidebarPeekBehavior` is the hover-peek choreography: open on a `HoverIntent` dwell, never on
a straight pass, close on leave after a grace, held open only by text entry inside the
surface. The app implements `ISidebarHost` for the state; `SidebarDockOptions` holds the tuned
constants.

## Fields and pickers

| Control | What |
| --- | --- |
| `AlloyTextBox` | A restyled native `TextBox`, so caret, selection, IME and undo come from the primitive. Optional uppercase label, leading glyph, clear affix, status glyph, stepper, prefix and suffix segments, helper text. Variants `sm`, `search`, `area`, `number`. |
| `AlloyTextArea` | The labelled multi-line field over an `AlloyTextBox`. `Mono` for line-oriented lists. |
| `AlloySelect` | A restyled native `ComboBox` for pick-one lists. |
| `AlloyDropDown` | The trigger-anchored frame for rich popovers: search, sections, trees. |
| `TokenBox` | A text field with trigger-char autocomplete, `$token` by default, over `TokenCompletion`. |
| `HotkeyBox` | Click and press a chord; stores it as a portable `Mod+` string. |
| `FontSizeStepper` | The shared text-size stepper with step, clamp, flash readout and tooltips. |

## Overlays and settings

`OverlayCard` is the modal-overlay shell with header, body and footer slots and a real,
keyboard-accessible close button that raises a bubbling `CloseRequested`; the window handles
it once for every overlay. `PreferencesOverlay` is the settings shell on top of it: a
search-first section rail driven by `PrefsNavigator`, a flat list of `PrefSection`, and the
section bodies bound to `Selected`. `PreferenceRow` is one settings row, title and description
left, a trailing control right. `CommandPaletteView` is the [palette](/core/commands#the-palette).

## Chrome and motion

- `DrawerPull`, the gold tab on a seam. `Edge` picks the shadow, `PointsLeft` the chevron.
- `BusySpinner`, driven by a composition animation so it keeps turning while the UI thread is
  blocked, which is exactly when you need it.
- `MorphPane`, a pane whose width morphs on the render thread as a clip rather than a layout
  animation, so a busy UI thread never stutters it.
- `HeightMorph`, animates its own height toward its child's, so a state swap morphs instead
  of snapping.
- `CopyGlyph`, the copy affordance that flips to a check for a beat as the receipt.
- `AlloySplashWindow`, the launch splash, a token-themed card with a live status line.
- `LogViewer` and `LogViewerWindow`, the embeddable log tail; see [Services](/core/services#logging).

## Attached behaviours

| Property | What |
| --- | --- |
| `HoverIntent` | The affordance wakes only after the pointer dwells; a straight pass never flashes chrome. Declarative via `RevealClass`, or imperative for pointer-math zones. |
| `AutoScroll` | Keeps a `ScrollViewer` pinned to the bottom as content grows, only while the user is already at the end. |
| `InlineRename` | The label-to-box swap: focus and select-all when editing starts, Enter and lost-focus commit, Escape cancels. |
| `OverlayScrollbars` | The macOS behaviour where the bar also fades in while content moves. |
| `TextCaret` | A caret drawn at the true text-end, because Avalonia's lands one or two DIP left at end of line. |
| `MacOSTitleBar` | The thick, transparent macOS title bar for extended-client-area windows. |

## Typography

`FontCatalog` is the faces one axis offers, UI, mono or display, and how to apply the picked
one: the family, its own OpenType features, and its own plain and emphatic weights, because
neither travels with a family name. `FontFaceDef` is one bundled face. `FontFaceVerification`
checks a face delivers what it declared against the live font manager, because Avalonia
answers a missing weight by emboldening and a missing italic by shearing, silently.
`CapCentering` optically centres a capital in a box using the face's real cap height.
