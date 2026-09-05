# ContextMenuControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A context menu that hangs on another control, positions itself at an anchor corner and supports
cascades.

```csharp
public class ContextMenuControl : UIControl, IDisposable
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ContextMenuControl**

**Implements:** `System.IDisposable`, `INotifyPropertyChanged` (inherited)

## Remarks

The control itself is a **zero sized anchor** inside the host tree: it costs no layout space, but
the layout gives it a position, and that is where the popup goes. The menu proper lives in its own
[CustomDialogElement](CustomDialogElement) in the overlay band, so it can extend past the host
dialog instead of being clipped by its surface.

Because the anchor is part of the tree, its position is recomputed by every layout pass - so
reopening the menu after the host moved or the GUI scale changed lands in the right place without
any tracking.

The constructor adds the menu to `owner.Children` itself. The popup cannot be built there: it needs
the `ICoreClientAPI`, which is only reachable through `Dialog` once the anchor is part of a laid
out tree, so it is created on the first `Show()`.

### Reacting to a pick

Subscribe once, on the menu. `ItemActivated` bubbles up the cascade, so picks from sub menus arrive
there too and you never have to keep a reference to a single entry. Order on a click: the entry's
own `Activated`, then `ItemActivated` bubbling upwards, then the cascade closes - the event comes
**before** the close, so a handler can still inspect the open menu.

An entry with children is not a command: it opens its sub menu and never raises `Activated`.
Clicking the opener again closes the menu, because the [`UIManager`](Input-Focus-and-Rendering#uimanager)
consumes that click for the dismissal.

### Keyboard

An open menu owns the keyboard: the arrow keys or Tab walk the entries, Enter picks one, Escape
closes - one level per press in a cascade, innermost first. Hovering an entry also moves the
keyboard selection, or the two could point at different entries and Enter would pick the one the
player is not looking at.

### Styling

Entries are deliberately not buttons. Vanilla menu entries are flat text rows on the shared menu
background, drawn by `GuiElementListMenu`, and the values come straight from there:

| | value |
|---|---|
| row height | 30 unscaled, independent of the text size |
| text | `sans-serif` 16, `#e9ddce`, left aligned, indent 5 |
| hover | `#a88b6c` across the full row at alpha 0.5 |
| box | `#403529` solid, border `rgba(0,0,0,0.5)` at width 2 |

## Constructors

| | Description |
|---|---|
| `ContextMenuControl(UIControl owner, List<ContextMenuItem> items, string contextMenuTitle = "ContextMenu", ContextMenuAnchor contextMenuAnchor = ContextMenuAnchor.BottomLeft)` | Adds itself to `owner.Children`. Throws `ArgumentNullException` when `owner` is `null`. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Owner` | `UIControl` | The control this menu hangs on. Read-only. |
| `Items` | `IReadOnlyList<ContextMenuItem>` | The entries. Read-only. |
| `Anchor` | [`ContextMenuAnchor`](Supporting-Types#contextmenuanchor-enum) | Which corner of `Owner` the popup is placed at. |
| `Offset` | `PointD` | Shift from that corner, in device pixels - for lining up with something inside the owner, like the burger icon of a title bar. |
| `IsOpen` | `bool` | Whether the popup is on screen. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `Show()` | `void` | Opens the popup at the anchor. Builds it on first use. |
| `Hide()` | `void` | Closes this menu. |
| `Toggle()` | `void` | One or the other. |
| `HideChain()` | `void` | Closes this menu **and every menu it was opened from** - picking an entry in a sub menu dismisses the whole cascade. |
| `CreateMenuBackground(string name)` | `RectangleControl` | `static`. The vanilla styled menu box. Public so the layout harness renders the same box the game does. |
| `CalculateSize()` | `PointD` | `override`. Zero - the anchor takes no space. |
| `GenerateRenderData(ImageSurface, Context)` | `void` | `override`. Draws nothing; the popup has its own surface. |
| `Dispose()` | `void` | Disposes the popup dialog. |

## Events

| Name | Type | Description |
|---|---|---|
| `ItemActivated` | `EventHandler<ContextMenuItemEventArgs>` | An entry of this menu - or of a menu nested below it - was picked. `sender` is the menu the entry actually belongs to. |

## ContextMenuItemEventArgs Class

```csharp
public class ContextMenuItemEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `Item` | `ContextMenuItem` | The entry that was picked. |
| `Path` | `IReadOnlyList<ContextMenuItem>` | The entry and every entry it is nested under, outermost first. Picking "Text 2" under "More" gives `["More", "Text 2"]`. |
| `Text` | `string` | Shorthand for `Item.Text`. |

## Examples

```csharp
var more = new ContextMenuItem("More", new List<ContextMenuItem>
{
    new ContextMenuItem("Text 1"),
    new ContextMenuItem("Text 2"),
    new ContextMenuItem("Text 3")
});

var menu = new ContextMenuControl(
    button,
    new List<ContextMenuItem> { new ContextMenuItem("Fixed"), new ContextMenuItem("Movable"), more },
    "positionMode",
    ContextMenuAnchor.BottomLeft);

button.Clicked += (sender, e) => menu.Toggle();

menu.ItemActivated += (sender, e) =>
    capi.ShowChatMessage(string.Join(" > ", e.Path.Select(i => i.Text)));
```

![A context menu with an entry hovered](https://raw.githubusercontent.com/DrakenRolle/ModernVintageGUI/master/docs/images/readme-context-menu-hover.png)

## See also

* [ContextMenuItem](ContextMenuItem) - one entry
* [PopupHost](PopupHost) - the same idea, generalised
* [TitleBarControl](TitleBarControl) - carries one behind its burger icon
