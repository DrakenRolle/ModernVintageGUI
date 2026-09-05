# InventoryGridControl Class

**Namespace:** `IS2Mod.ControlTypes`
**Assembly:** `ModernVintageGUI.dll`

A grid of [ItemSlotControl](ItemSlotControl) onto a **real** inventory, laid out on the same lattice
the vanilla inventory uses, and scrollable when there are more rows than fit.

```csharp
public class InventoryGridControl : RectangleControl
```

**Inheritance:** `Object` → [UIControl](UIControl) → [RectangleControl](RectangleControl) →
**InventoryGridControl**

**Implements:** [`IScrollable`](Supporting-Types#iscrollable-interface) (inherited),
`INotifyPropertyChanged` (inherited)

## Remarks

[`SetInventory`](#methods) is what makes it real: from then on it is a view of an actual inventory,
every click goes through `ActivateSlot` the way vanilla's own grid does it, and the stack being
carried is the one the whole game carries - so items move between this grid and the player's bag, a
chest or the creative inventory exactly as they move between any two vanilla grids, shift click
included. Without it the grid is decoration: `SetSlotCount` gives it empty slots that nothing can
be put into, because there is no inventory behind them and no server that would accept the move.

It is a [RectangleControl](RectangleControl) rather than something built from scratch, which is
what makes the scrolling free: clipping, the viewport, the bars, the wheel and the drag all come
from the container it already is. What it adds is the placement - the slots are placed by hand on a
lattice instead of being stacked into row containers, because vanilla puts slot `(col, row)` at
exactly `col * (48 + 3)` by `row * (48 + 3)` scaled, with no gap before the first one and none after
the last.

A per-slot stack cap belongs to the **inventory**, not to the grid - see
[Inventories → How much fits in a slot](Inventories#how-much-fits-in-a-slot).

## Constructors

| | Description |
|---|---|
| `InventoryGridControl(int columns = 1, string _Name = "", bool internalInventory = false, int slotCount = 1)` | `internalInventory: true` gives the grid an inventory of its own, kept per player and saved with them, reachable through `Inventory` from the moment the dialog is first shown. `slotCount` is how many slots that inventory has and is ignored without it. |

## Fields

| Name | Type | Value | Description |
|---|---|---|---|
| `UnscaledInset` | `const double` | `ItemSlotControl.UnscaledHighlightReach` (3.5) | The lattice sits this far inside the grid on every side, so the selection ring of the outermost slots has room. Add it **twice** to the width and the height when giving the grid a fixed size. It is part of the *content* rather than the padding: a clipping container cuts at its padding box, so padding would move the lattice and the cut by the same amount and buy the ring nothing. |

## Properties

| Name | Type | Description |
|---|---|---|
| `Columns` | `int` | Slots per row. The number of rows follows from the slot count. |
| `Rows` | `int` | How many rows the current slot count needs. Read-only. |
| `Slots` | `IReadOnlyList<ItemSlotControl>` | The slot controls, in grid order. Managed by the grid, not by the caller. Read-only. |
| `Inventory` | `IInventory?` | The inventory the grid shows - the one it was given, or the one it made for itself. `null` until the dialog has been shown once in the internal case, because the inventory needs the client API. Read-only. |

Everything on [RectangleControl](RectangleControl#properties) applies too, `EnableVerticalScrollbar`
in particular.

## Methods

| Name | Returns | Description |
|---|---|---|
| `SetInventory(ModInventoryAccess access)` | `void` | The simple way in: an inventory of the mod's own, with the packets a slot move produces and the opening and closing already attached. Throws `ArgumentNullException` for `null`, and `InvalidOperationException` when the inventory is not available yet - build the dialog when the game is running, not while the mod is starting. |
| `SetInventory(IInventory inventory, ICoreClientAPI capi, Action<object>? sendPacket = null, bool announceOpen = true)` | `void` | For an inventory the game already owns - a chest you did not create, the player's bag. `sendPacket` is where the packets a slot move produces go; without it the move happens on the client only and the server corrects it back. `announceOpen` sends Open and Close while the dialog is on screen. |
| `SetSlotCount(int count)` | `void` | Fills the grid with empty slots, for a fixed size grid whose contents are assigned later through `Slots`. |
| `SingleSlot(string name = "")` | `InventoryGridControl` | `static`. The 1x1 case, with an inventory of its own. |
| `InternalInventoryName(string? dialogName, string? controlName)` | `string` | `static`. The id a grid derives for an inventory of its own. Public because the **server** has to declare that inventory before it will accept a move into it, and the server has no grid to ask. |
| `OnDialogShown()` | `void` | `override`. Announces the inventory as opened, unless somebody else already has it open. Not optional bookkeeping: `InventoryBase.CanPlayerModify` returns `HasOpened(player)`, so an inventory the player has not opened refuses every move - the player's own backpack included. |
| `OnDialogHidden()` | `void` | `override`. The counterpart. |
| `CalculateSize()` | `PointD` | `override`. Measures the **full** lattice, including rows that will be scrolled out of sight - that is what the scrolling container compares against its viewport. |
| `NormalizeChildrenByDelta()` | `void` | `override`. Slots keep their own size; the stretching that is right for a list of rows is wrong for a grid of fixed squares. |
| `CalculateAllPositions()` | `void` | `override`. Places the slots on the lattice, then applies the scroll offset. |
| `GenerateRenderData(ImageSurface, Context)` | `void` | `override`. |

## Events

| Name | Type | Description |
|---|---|---|
| `SlotClicked` | `EventHandler<ItemSlotEventArgs>` | A slot in **this grid** was clicked, with the slot control, the index and the mouse arguments. |
| `SlotEnter` | `EventHandler<ItemSlotEventArgs>` | The cursor entered a slot. |
| `SlotChanged` | `EventHandler<InventorySlotChangedEventArgs>` | The contents of a slot changed, **whoever** changed them - a click here, a shift click from the player's bag, a hopper, another player in a shared inventory, or the server correcting this client. This is the one to listen on for "what is in there now"; `SlotClicked` misses every change that came from somewhere else. |
| `ItemPutIn` | `EventHandler<InventorySlotChangedEventArgs>` | Something arrived in a slot. |
| `ItemTakenOut` | `EventHandler<InventorySlotChangedEventArgs>` | Something left a slot. |

## ItemSlotEventArgs Class

```csharp
public class ItemSlotEventArgs : EventArgs
```

| Member | Type | Description |
|---|---|---|
| `SlotControl` | `ItemSlotControl` | The control that was clicked. |
| `SlotIndex` | `int` | Its position in the grid, left to right then top to bottom. |
| `Slot` | `ItemSlot?` | The inventory slot behind it, if the grid was given an inventory. |
| `Mouse` | `MouseEventArgs` | Which button, and where. |

`InventorySlotChangedEventArgs`, carried by the three change events, is described under
[Inventories](Inventories#knowing-what-changed).

## Examples

```csharp
var grid = new InventoryGridControl(columns: 6, _Name: "crate");
grid.SetInventory(ModInventoryAccess.ForBlock(capi, pos, blockEntity.Inventory));

grid.ItemPutIn    += (s, e) => Log($"{e.After.StackSize}x {e.After.GetName()} into slot {e.SlotId}");
grid.ItemTakenOut += (s, e) => Log($"{e.Before.GetName()} left slot {e.SlotId}");
```

A grid with an inventory of its own - and the line the **server** needs, because it decides what
exists and how big it is:

```csharp
// client
var grid = new InventoryGridControl(6, "loadout", internalInventory: true, slotCount: 24);

// server, in StartServerSide
inventorySystem.RegisterPlayerInventory(
    InventoryGridControl.InternalInventoryName("myDialog", "loadout"), 24);
```

A window onto a large inventory:

```csharp
grid.Size = new PointD(
    latticeWidth  + InventoryGridControl.UnscaledInset * 2 + ScrollbarStyle.UnscaledWidth,
    latticeHeight + InventoryGridControl.UnscaledInset * 2);

grid.IsAutoSize = false;
grid.EnableVerticalScrollbar = true;
```

## See also

* [Inventories](Inventories) - the three ways to own one, and the change events in full
* [ItemSlotControl](ItemSlotControl) - one slot
* [RectangleControl](RectangleControl) - where the scrolling comes from
