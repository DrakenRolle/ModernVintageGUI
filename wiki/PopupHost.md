# PopupHost Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A panel that opens next to a control and closes when the player clicks elsewhere: the list of a
dropdown, the type picker of a selector, a menu.

```csharp
public sealed class PopupHost : IDisposable
```

**Inheritance:** `Object` → **PopupHost**

**Implements:** `System.IDisposable`

> `PopupHost` is **not** a control - it is a helper a control owns. Reach for it when a control of
> yours has to show something that must not be clipped by the dialog it lives in.

## Remarks

The panel is a [CustomDialogElement](CustomDialogElement) of its own in the **overlay** render band
rather than a child of the host dialog, and that is the whole point: a panel drawn inside its host
is clipped by the host's surface, so a list opening at the bottom edge of a dialog is cut off
exactly where it needs the room. A dialog of its own is clipped by nothing.

`Open()` places the panel under the owner, or above it when there is no room below - which is what
makes a picker at the bottom of a dialog usable at all. The host control keeps its position through
every layout pass, so a panel reopened after the dialog moved or the GUI scale changed lands in the
right place with no tracking.

The content stays yours: fill it and resize it while the panel is closed, then show it again.

The `padding` argument is not cosmetic. A frame drawn with a stroke has half of that stroke outside
its own box, and without room for that half the popup surface clips it away.

## Constructors

| | Description |
|---|---|
| `PopupHost(UIControl owner, UIControl content, string name, double padding = 0)` | `owner` is the control the panel opens next to; `content` is what goes inside and is kept by the caller; `padding` is the room to leave around it, in author units. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Dialog` | `CustomDialogElement?` | The dialog behind the panel, once it has been opened at least once. Read-only. |
| `IsOpen` | `bool` | Whether the panel is on screen. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `Open()` | `bool` | Shows the panel under the owner, or above it when there is no room below. Returns `false` when there is nothing to open into yet: the panel needs the client API, and that is only reachable once the owner is part of a shown dialog. |
| `Close()` | `void` | Hides it. |
| `Toggle()` | `void` | One or the other. |
| `Dispose()` | `void` | Disposes the panel dialog. Called for you when the owning dialog is disposed. |

## Events

| Name | Type | Description |
|---|---|---|
| `Opened` | `EventHandler` | Raised after the panel has been shown and placed. |

## Examples

```csharp
public class MyPickerControl : UIControl, IDisposable
{
    private readonly RectangleControl _listBox;
    private readonly PopupHost _popup;

    public MyPickerControl(string _Name = "") : base(_Name)
    {
        _listBox = ContextMenuControl.CreateMenuBackground(_Name + "_list");
        _listBox.EnableVerticalScrollbar = true;

        // Half the list border stroke sits outside the box, so leave it that much room.
        _popup = new PopupHost(this, _listBox, _Name, padding: 1);

        Clicked += (sender, e) => _popup.Toggle();
    }

    public void Dispose() => _popup.Dispose();
}
```

## See also

* [CustomDialogElement](CustomDialogElement) - what a panel actually is
* [DropdownControl](DropdownControl) · [ItemTypeSelectorControl](ItemTypeSelectorControl) · [ContextMenuControl](ContextMenuControl) - the three that use it
* [Writing a Custom Control](Writing-a-Custom-Control)
