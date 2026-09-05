# ContextMenuItem Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

One entry of a [ContextMenuControl](ContextMenuControl) - a command, or the opener of a sub menu.

```csharp
public class ContextMenuItem : UIControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ContextMenuItem**

**Implements:** `INotifyPropertyChanged` (inherited)

## Remarks

Deliberately **not** a [ButtonControl](ButtonControl): vanilla menu entries are flat text rows on
the shared menu background, drawn by `GuiElementListMenu` - no border, no emboss, no shadow. A
button would bring its embossed frame and look nothing like the original, so an entry is its own
composite of a hover fill plus a label.

An entry is an atomic hit target. Without that the hit test would descend into the label or the
highlight rectangle, and those would receive `Enter`, `Exit` and `Clicked` instead of the entry - so
the entry would never light up and never fire.

An entry that has child items is not a command: clicking it opens its sub menu and it never raises
`Activated`.

The entry has **one** highlight fed by both hover and keyboard selection rather than one each: the
mouse leaving the menu must not unlight the entry the keyboard is on.

## Constructors

| | Description |
|---|---|
| `ContextMenuItem(string text, List<ContextMenuItem>? childItems = null)` | Passing child items makes this entry the opener of a sub menu. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Text` | `string` | The caption. |
| `ChildItems` | `IReadOnlyList<ContextMenuItem>` | The entries of the sub menu, empty for a command. Read-only. |
| `SubMenu` | `ContextMenuControl?` | The nested menu, present only when this entry has child items. Read-only. |
| `OwnerMenu` | `ContextMenuControl?` | The menu this entry belongs to. Named `OwnerMenu` on purpose - `Parent` is the layout parent on `UIControl` and must not be shadowed. Read-only from outside. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. Width from the text, height the fixed vanilla row height. |
| `NormalizeChildrenByDelta()` | `void` | `override`. Stretches the highlight and the label over the whole row. |
| `CalculateAllPositions()` | `void` | `override`. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. One hit target. |

## Events

| Name | Type | Description |
|---|---|---|
| `Activated` | `EventHandler` | Raised when an entry **without** child items is clicked. For the usual case subscribe to [`ContextMenuControl.ItemActivated`](ContextMenuControl#events) instead - it sees every level of a cascade with one subscription. |

## Examples

```csharp
var save = new ContextMenuItem("Save");
save.Activated += (sender, e) => Save();          // the rare single-entry case

var more = new ContextMenuItem("More", new List<ContextMenuItem>
{
    new ContextMenuItem("Text 1"),
    new ContextMenuItem("Text 2")
});
```

## See also

* [ContextMenuControl](ContextMenuControl) - the menu these sit in
* [UIControl](UIControl) - the base class
