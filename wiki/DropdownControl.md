# DropdownControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A closed box showing the current selection, and a list that opens under it. Entries can be text,
text with an icon, or an item stack.

```csharp
public class DropdownControl : UIControl, IDisposable
```

**Inheritance:** `Object` → [UIControl](UIControl) → **DropdownControl**

**Implements:** `System.IDisposable`, `INotifyPropertyChanged` (inherited)

## Remarks

The list is the same machinery a [ContextMenuControl](ContextMenuControl) uses - its own
[CustomDialogElement](CustomDialogElement) in the overlay band, dismissed by a click outside,
hosted by a [PopupHost](PopupHost) - because a list drawn inside the host dialog is clipped by it,
and a dropdown near the bottom edge of a dialog is exactly where that shows.

What it adds over a context menu is the part that makes it a dropdown: a selection that survives
closing, a closed box that shows it, and entries that can carry an icon. An entry built from an
`ItemStack` renders the stack as its icon and brings the game's own item tooltip with it.

Whatever `MaxVisibleItems` and `MaxListHeight` are set to, the list is still cut down to what fits
on the **screen** when it opens - a popup taller than the window cannot be placed anywhere sensible.

The box is what the player operates, so it is what Tab lands on: the box is focusable, the label
inside it is not, and the entries only exist while the list is open.

### The two row styles

`Auto`, the default, gives a list built from item stacks the handbook look and everything else the
menu look. Both sets of measurements are taken from the game rather than chosen:

| | `Menu` | `ItemList` |
|---|---|---|
| from | `GuiElementListMenu` | the survival handbook's Blocks and Items page |
| row height | 30 | 25 plus 4 above and below |
| row spacing | 0 | 10 |
| icon | only where there is one | always a column, 25 wide |

An item is drawn at the slot ratio the game uses - a stack renders larger than its nominal size, so
asking for the row height gives an icon that overflows the row. `ItemList` asks for
`rowHeight * 25.6/48`, the ratio between vanilla's item size and its slot size, which is what makes
the icon sit inside the row instead of on top of it.

## Constructors

| | Description |
|---|---|
| `DropdownControl(string _Name = "", PointD? _Size = null, double _Margin = 5)` | Builds the closed box, the list box and the popup host. Focusable. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Items` | `IReadOnlyList<DropdownItem>` | The entries, in list order. Replace them with `SetItems`. Read-only. |
| `SelectedIndex` | `int` | The selected entry's position, or `-1` for none. Setting it raises `SelectionChanged` exactly as a click would. |
| `SelectedItem` | [`DropdownItem?`](DropdownItem) | The selected entry. Read-only. |
| `SelectedValue` | `object?` | The payload attached to the selected entry. Read-only. |
| `SelectedStack` | `ItemStack?` | The stack of the selected entry, for a list built from item stacks. Read-only. |
| `PlaceholderText` | `string` | Shown in the closed box while nothing is selected. |
| `MaxVisibleItems` | `int` | Rows before the list scrolls. `0` - the default - means unlimited. |
| `MaxListHeight` | `double` | The same limit in author units. `0` means unlimited. The stricter of the two wins. |
| `RowStyle` | [`DropdownRowStyle`](Supporting-Types#dropdownrowstyle-enum) | `Auto`, `Menu` or `ItemList`. |
| `IsOpen` | `bool` | Whether the list is showing. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `SetItems(IEnumerable<DropdownItem> items)` | `void` | Replaces the entries. The selection is kept when the same entry is still in the list and cleared otherwise, so refilling a list under a player does not silently select something else for them. |
| `Select(int index)` | `void` | Selects by position; `-1` clears the selection. |
| `SelectByValue(object? value)` | `bool` | Selects the first entry whose `Value` matches. `false` when there is none. |
| `Open()` / `Close()` / `Toggle()` | `void` | The list. |
| `CreateListBox(string name, IReadOnlyList<DropdownItem> items, int selectedIndex = -1, DropdownRowStyle style = DropdownRowStyle.Auto)` | `RectangleControl` | `static`. A filled list box on its own, without a popup. Public so the layout harness renders the same list the game does. |
| `FillListBox(RectangleControl box, IReadOnlyList<DropdownItem> items, int selectedIndex = -1, DropdownRowStyle style = DropdownRowStyle.Auto)` | `void` | `static`. Puts the entries into a box that already exists, giving them all the metrics of the chosen style. |
| `SizeListBox(RectangleControl listBox, int itemCount, DropdownRowMetrics metrics, double measuredItemWidth, int maxVisibleItems, double maxListHeight, double minWidth, double availableHeight)` | `void` | `static`. Sizes a list box in author units. Shared with [ItemTypeSelectorControl](ItemTypeSelectorControl), which opens the same kind of list. |
| `MeasureItemWidth(IReadOnlyList<DropdownItem> items, DropdownRowMetrics metrics)` | `double` | `static`. The width the widest entry needs, in author units. |
| `AvailableScreenHeight(UIControl control)` | `double` | `static`. How tall the list may be and still fit on screen. `double.MaxValue` without a client. |
| `CalculateSize()` | `PointD` | `override`. Auto-sizing takes the widest entry plus the arrow button, so the box does not change width when the player picks something else. |
| `NormalizeChildrenByDelta()` / `CalculateAllPositions()` | `void` | `override`. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. One hit target. |
| `GenerateRenderData(ImageSurface, Context)` | `void` | `override`. The closed box, straight out of `GuiElementDropDown.ComposeElements`. |
| `GenerateInteractiveRenderData(ICoreClientAPI, float)` | `void` | `override`. The icon of the current selection, which cannot go into a Cairo surface. |
| `Dispose()` | `void` | Disposes the popup. |

## Events

| Name | Type | Description |
|---|---|---|
| `SelectionChanged` | `EventHandler<DropdownSelectionEventArgs>` | Raised when the selection changes, by click, by keyboard or from code. |

## DropdownSelectionEventArgs Class

```csharp
public class DropdownSelectionEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `Item` | `DropdownItem?` | The entry, or `null` when the selection was cleared. |
| `Index` | `int` | Its position, or `-1`. |
| `Value` | `object?` | Shorthand for `Item?.Value`. |

## Examples

```csharp
var dd = new DropdownControl(_Name: "mode");
dd.PlaceholderText = "Pick a mode";
dd.SetItems(new[]
{
    new DropdownItem("Fastest",  value: Mode.Fast),
    new DropdownItem("Cheapest", value: Mode.Cheap, iconName: GuiIcons.Dice)
});

dd.SelectionChanged += (sender, e) => Apply((Mode)e.Value!);
```

An item picker is the same control with stacks instead of strings:

```csharp
dd.SetItems(stacks.Select(stack => new DropdownItem(stack)));
dd.MaxVisibleItems = 8;                  // scroll after eight rows
```

## See also

* [DropdownItem](DropdownItem) - one entry
* [ItemTypeSelectorControl](ItemTypeSelectorControl) - the same list, opened from a slot
* [PopupHost](PopupHost) - what the list opens in
* [DropdownRowStyle and DropdownRowMetrics](Supporting-Types#dropdownrowstyle-enum)
