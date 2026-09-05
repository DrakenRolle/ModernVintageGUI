# ItemTypeSelectorControl Class

**Namespace:** `ModernVintageGUI.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A slot that picks an item **type** rather than holding an item: a filter, a recipe output, "what
should this machine make".

```csharp
public class ItemTypeSelectorControl : UIControl, IDisposable
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ItemTypeSelectorControl**

**Implements:** `System.IDisposable`, `INotifyPropertyChanged` (inherited)

## Remarks

It looks like an inventory slot on purpose - that is what a player reads as "an item goes here" -
but nothing can be dropped into it and nothing taken out. Clicking it opens the list of types the
caller supplied, drawn like the survival handbook's Blocks and Items page, in the same
[PopupHost](PopupHost) a [DropdownControl](DropdownControl) uses.

The square really **is** an [ItemSlotControl](ItemSlotControl), so the frame, the hover ring and
the item tooltip are the real ones; what it holds is a `DummySlot`. The control draws a small caret
in the corner on top of that, or a picker looks like an ordinary slot the player will try to drop
items into.

That it is its own control rather than a mode of `ItemSlotControl` is deliberate. A slot's whole
job is to be one end of a stack move, and a type picker is not. Giving a real slot a mode in which
its inventory is a fiction is exactly how a grid ends up conjuring items - the two want different
things and only share a look.

`SelectedCode` is what a mod usually stores and reloads: an `AssetLocation` survives a restart, an
`ItemStack` does not.

## Constructors

| | Description |
|---|---|
| `ItemTypeSelectorControl(string _Name = "", double _Margin = 0)` | `_Margin` is `0` by default, like [InventoryGridControl](InventoryGridControl) and unlike most controls: the space around a slot is already in the inset, and a margin on top of it would push this square out of line with a grid beside it. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Types` | `IReadOnlyList<ItemStack>` | The types on offer, in list order. Replace them with `SetTypes`. Read-only. |
| `SelectedItemType` | `ItemStack?` | The picked type, or `null`. Assigning it picks the matching entry - by collectible, so a stack of a different size or from another source still finds its entry. |
| `SelectedCode` | `AssetLocation?` | The code of the picked type. Read-only. |
| `SelectedCollectible` | `CollectibleObject?` | The picked collectible itself. Read-only. |
| `SelectedIndex` | `int` | Its position in `Types`, or `-1`. Read-only. |
| `AllowEmpty` | `bool` | Offer an entry that clears the selection. Off by default. |
| `EmptyText` | `string` | The caption of that entry. Default `"None"`. |
| `MaxVisibleItems` | `int` | Types listed before the list scrolls. `0` = unlimited. |
| `MaxListHeight` | `double` | The same limit as a height in author units. `0` = unlimited. |
| `IsOpen` | `bool` | Whether the list is showing. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `SetTypes(IEnumerable<ItemStack>? types)` | `void` | Sets the types on offer. The picked type is kept when it is still among them and cleared otherwise. |
| `SetTypes(IEnumerable<CollectibleObject>? collectibles)` | `void` | The same from collectibles, which is how a caller usually has them. |
| `Select(int index)` | `void` | Picks by position; `-1` clears the selection. |
| `SelectByCode(AssetLocation? code)` | `bool` | Picks the type with this code, if it is on offer. |
| `Open()` / `Close()` / `Toggle()` | `void` | The list. |
| `CollectVariants(ICoreClientAPI capi, AssetLocation baseCode)` | `List<ItemStack>` | `static`. Every variant of one block or item - `rock-granite` and `rock-andesite` are two variants of `rock`. A convention rather than a rule, so it is a convenience for the common case; a caller who knows better passes its own list to `SetTypes`. |
| `CalculateSize()` | `PointD` | `override`. One slot plus the room its ring needs. |
| `NormalizeChildrenByDelta()` / `CalculateAllPositions()` | `void` | `override`. |
| `GenerateRenderData(ImageSurface, Context)` | `void` | `override`. The slot draws itself; this adds the caret. |
| `Dispose()` | `void` | Disposes the popup. |

## Events

| Name | Type | Description |
|---|---|---|
| `SelectionChanged` | `EventHandler<ItemTypeSelectedEventArgs>` | Raised when the picked type changes, by click, by keyboard or from code. |

## ItemTypeSelectedEventArgs Class

```csharp
public class ItemTypeSelectedEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `Stack` | `ItemStack?` | The type, or `null` when the selection was cleared. |
| `Code` | `AssetLocation?` | Its code - the thing a mod usually wants to store. |

## Examples

```csharp
var picker = new ItemTypeSelectorControl(_Name: "filter");
picker.SetTypes(ItemTypeSelectorControl.CollectVariants(capi, new AssetLocation("game:plank")));
picker.AllowEmpty = true;

picker.SelectionChanged += (sender, e) => SetFilter(e.Code);
```

Restoring a saved pick:

```csharp
picker.SelectByCode(new AssetLocation(tree.GetString("filter")));
```

## See also

* [ItemSlotControl](ItemSlotControl) - the square this is built on
* [DropdownControl](DropdownControl) - the list it opens
* [Inventories](Inventories) - for slots that really hold items
