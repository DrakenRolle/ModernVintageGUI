# ItemSlotControl Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A single inventory slot, drawn exactly like the one the game draws: the vanilla frame, the stack,
its count and the hover ring.

```csharp
public class ItemSlotControl : UIControl, IItemTooltipSource
```

**Inheritance:** `Object` → [UIControl](UIControl) → **ItemSlotControl**

**Implements:** [`IS2Mod.Interfaces.IItemTooltipSource`](Supporting-Types#iitemtooltipsource-interface),
`INotifyPropertyChanged` (inherited)

## Remarks

Every number in here comes from `GuiElementItemSlotGridBase.ComposeElements` and
`GuiElementPassiveItemSlot`, so a grid of these is indistinguishable from a vanilla one.

The slot is drawn in **two passes**, the same split vanilla makes. The background is Cairo and
lands in the dialog surface; the item stack cannot be, because the game renders it out of the item
atlas with its own shader, so it is drawn per frame in `GenerateInteractiveRenderData`.

Depth is why the count of a hovered slot used to appear over the stack on the cursor: the game
draws a stack size 100 units nearer than its stack, so anything meant to cover a carried stack has
to clear that by more than 100. The constants that keep those layers apart are on
[CustomDialogElement](CustomDialogElement#fields).

The slot backgrounds are drawn onto **one shared surface per pixel size** and blitted. That is not
an optimisation for its own sake: `SurfaceTransformBlur` works on the surface buffer and knows
nothing about Cairo, so blurring a slot drawn straight onto the shared dialog surface would pull in
whatever a neighbour three units away had already drawn.

It implements `IItemTooltipSource`, which is what puts the game's own item tooltip on it, and it is
focusable, so a slot is in the tab order.

## Constructors

| | Description |
|---|---|
| `ItemSlotControl(string _Name = "", int _SlotIndex = 0)` | A fixed 48 x 48 author units, focusable, and lights itself on hover and on focus. |

## Fields

Vanilla's own measurements, in author units.

| Name | Type | Value | Description |
|---|---|---|---|
| `UnscaledSlotSize` | `const double` | `48.0` | `GuiElementPassiveItemSlot.unscaledSlotSize`. |
| `UnscaledSlotPadding` | `const double` | `3.0` | The gap between two slots. |
| `UnscaledItemSize` | `const double` | `25.6` | The size the stack is rendered at, a good deal smaller than the slot it sits in. Getting this wrong is immediately visible - the items look bloated and touch the frame. |
| `UnscaledHighlightOverhang` | `const double` | `2.0` | How far outside the slot the selection ring is drawn - the offset of its path. |
| `UnscaledHighlightLineWidth` | `const double` | `3.0` | How thick that ring is stroked. |
| `UnscaledHighlightReach` | `const double` | `3.5` | How far the **ink** of the ring reaches: the path offset plus half the stroke, because Cairo centres a stroke on its path. A container that clips its slots has to keep this much room around the lattice; forgetting the half stroke gives the top row a flat-topped hover ring. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Slot` | `ItemSlot?` | The inventory slot shown here, or `null` for an empty decorative slot. Assigning it needs no redraw - the stack is drawn fresh every frame anyway. |
| `IsHighlighted` | `bool` | Draw the vanilla active-slot ring. Set by hover and focus, and settable from code to mark a slot. |
| `SlotIndex` | `int` | Index of this slot inside its grid. Handed to the grid events. |
| `TooltipSlot` | `ItemSlot?` | From `IItemTooltipSource`. Returns `Slot`. Read-only. |

## Methods

| Name | Returns | Description |
|---|---|---|
| `CalculateSize()` | `PointD` | `override`. The fixed slot size. |
| `HitTestRecursive(UIControl, double, double)` | `UIControl?` | `protected override`. A slot is one piece. |
| `GenerateRenderData(ImageSurface, Context)` | `void` | `override`. The frame, from the shared background surface. |
| `GenerateInteractiveRenderData(ICoreClientAPI, float)` | `void` | `override`. The stack and its count. |

## Examples

A lone slot, showing a stack from somewhere:

```csharp
var slot = new ItemSlotControl(_Name: "preview");
slot.Slot = someInventory[0];
panel.Children.Add(slot);
```

Marking a slot from code:

```csharp
grid.Slots[3].IsHighlighted = true;
grid.Dialog?.Refresh();
```

For slots the player can actually move items into, use [InventoryGridControl](InventoryGridControl)
with a real inventory behind it - see [Inventories](Inventories).

## See also

* [InventoryGridControl](InventoryGridControl) - a lattice of these
* [ItemTypeSelectorControl](ItemTypeSelectorControl) - a slot that picks a type instead of holding one
* [Inventories](Inventories) - what makes a slot real
