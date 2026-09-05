# Inventories

An inventory grid in this framework shows a **real inventory**. Not a copy, not a client side
stand-in: the server knows about it, so the player moves items in and out of it exactly as they
would with a chest - shift click, the creative inventory, the item tooltip and the stack on the
cursor all behave the way they do in a vanilla grid, and what a player leaves in it is still there
next time.

* [Why the server has to know](#why-the-server-has-to-know)
* [Creating one](#creating-one)
* [How much fits in a slot](#how-much-fits-in-a-slot)
* [Showing it](#showing-it)
* [Knowing what changed](#knowing-what-changed)
* [A grid with its own inventory](#a-grid-with-its-own-inventory)
* [What a grid does per click](#what-a-grid-does-per-click)

---

## Why the server has to know

This is the whole mechanism, and everything else on this page follows from it.

`ServerSystemInventory` resolves **every** slot move by inventory id through the player's inventory
manager:

```csharp
private void HandleMoveItemstack(Packet_Client packet, ConnectedClient client)
{
    if (player.InventoryManager.GetInventory(sourceInventoryId, out var invFound))
    {
        ...
    }
}
```

An inventory that is not in that manager does not exist as far as the server is concerned, so every
move for it is dropped and the client is corrected back on the next sync. That is what a
client-side "inventory" looks like from the outside: items that will not go in, or that reappear a
moment later.

`PlayerInventoryManager.OpenInventory` does both halves at once:

```csharp
public object OpenInventory(IInventory inventory)
{
    Inventories[inventory.InventoryID] = (InventoryBase)inventory;
    return inventory.Open(player);
}
```

From that moment the server syncs the inventory's dirty slots to that client by itself, exactly as
it does for the hotbar - `ServerSystemInventory.SendDirtySlots` walks the same list. Shift click
works for the same reason (`TryTransferAway` walks the opened inventories), and so does dragging
something out of the creative inventory (`HandleCreateItemstack` resolves its target the same way).

`ModInventorySystem` is the piece that makes that call at the right moment for the right inventory.

## Creating one

`ModInventory` is an `InventoryGeneric` with a constructor that takes only a size. That is the
point: it **is** a vanilla inventory, so everything that works with one works with it.

```csharp
var inventory = new ModInventory(16);
```

It starts unbound. What binds it is an id and an API, and who supplies those is what tells the
three cases apart.

### A block

The block entity owns it. Derive from `ModInventoryBlockEntity` and everything else comes from
`BlockEntityContainer`: the inventory is bound to `class-position` when the block is placed, its
contents are saved with the chunk, and they drop on the ground when the block is broken.

```csharp
public class BlockEntityMyCrate : ModInventoryBlockEntity
{
    public BlockEntityMyCrate() : base(size: 16, inventoryClassName: "mycrate") { }
}
```

Three blocks of this kind hold three separate inventories, because the position is part of the id.
Nothing has to be registered - the block entity is the authority, and the server finds it by
looking up the position the client sent.

That lookup is guarded by a distance check. The position comes from a client, and without one a
client could open the inventory of any block anywhere on the map by sending its coordinates.

### One the server shares

For several blocks that share a single inventory, or a bank that is the same for everyone. The
server holds one instance and syncs it to everyone who has it open, so a change one player makes
reaches the others.

```csharp
// server
inventorySystem.RegisterSharedInventory("guildbank", 32);

// client
var access = ModInventoryAccess.ForShared(capi, "guildbank", 32);
```

Saved in the savegame under its name.

### One per player

A personal stash, a loadout. Created when the player first opens it, saved with that player.

```csharp
// server
inventorySystem.RegisterPlayerInventory("loadout", 24);

// client
var access = ModInventoryAccess.ForPlayer(capi, "loadout", 24);
```

### The server half

One per mod, in `StartServerSide`:

```csharp
public override void StartServerSide(ICoreServerAPI api)
{
    inventorySystem = new ModInventorySystem(api);
    inventorySystem.RegisterPlayerInventory("loadout", 24);
    inventorySystem.RegisterSharedInventory("guildbank", 32);
}
```

Sizes are registered here and **never taken from the client**. A client that could name a size
could ask for an inventory of any size it liked. Server code can reach the contents through
`GetShared(name)` and `GetForPlayer(player, className)`.

### How much fits in a slot

Every one of these takes a per slot limit. It is a cap **on top of** what the item itself allows,
never instead of it - a slot capped at 16 takes sixteen planks and still only one pickaxe:

```csharp
var inventory = new ModInventory(4, maxSlotStackSize: 16);

public class BlockEntityMyCrate : ModInventoryBlockEntity
{
    public BlockEntityMyCrate() : base(size: 16, inventoryClassName: "mycrate", maxSlotStackSize: 8) { }
}

inventorySystem.RegisterSharedInventory("guildbank", 32, maxSlotStackSize: 64);
var access = ModInventoryAccess.ForShared(capi, "guildbank", 32, maxSlotStackSize: 64);

var grid = new InventoryGridControl(2, "loadout", internalInventory: true, slotCount: 4, maxSlotStackSize: 16);
```

Zero, the default, means no limit of its own.

Nothing enforces it here, and that is the point: the limit sits on the slots as vanilla's own
`ItemSlot.MaxSlotStackSize`, and the game checks it in all three paths a stack can arrive by - into
an empty slot, merged onto one that already holds something, and swapped with the stack on the
cursor. The item's own maximum comes from `Collectible.GetMergableQuantity` and applies
independently, so the effective limit is the smaller of the two without anyone having to work it
out.

Because the check lives in the slot it holds on the server as well, which is what makes it a rule
rather than a hint. Both sides do have to build the inventory with the same number - the server
from its registration, the client from the access - or the client would show a limit the server
does not keep and be corrected a moment later.

## Showing it

```csharp
var grid = new InventoryGridControl(columns: 6, _Name: "crate");
grid.SetInventory(ModInventoryAccess.ForBlock(capi, pos, blockEntity.Inventory));
```

One argument. The access carries the packets a slot move produces and opens and closes the
inventory along with the dialog - which a grid cannot do by itself, because the server has to be
told about an inventory before it will accept a single move for it.

The older overload is still there for an inventory the game already owns - a chest you did not
create, the player's own bag:

```csharp
grid.SetInventory(
    inventory,
    capi,
    sendPacket: p => capi.Network.SendPacketClient(p),
    announceOpen: true);
```

`announceOpen` sends Open and Close while the dialog is on screen. It has to happen one way or
another: `InventoryBase.CanPlayerModify` is `CanPlayerAccess && HasOpened`, so an inventory the
player has not opened refuses every move on both sides.

### Sizing the grid

The grid measures its full lattice, including rows that will be scrolled out of sight, so a fixed
size turns it into a window onto the inventory:

```csharp
double latticeWidth  = columns * ItemSlotControl.UnscaledSlotSize
                     + (columns - 1) * ItemSlotControl.UnscaledSlotPadding;

double latticeHeight = visibleRows * ItemSlotControl.UnscaledSlotSize
                     + (visibleRows - 1) * ItemSlotControl.UnscaledSlotPadding;

grid.Size = new PointD(
    latticeWidth + InventoryGridControl.UnscaledInset * 2 + ScrollbarStyle.UnscaledWidth,
    latticeHeight + InventoryGridControl.UnscaledInset * 2);

grid.IsAutoSize = false;
grid.EnableVerticalScrollbar = true;
```

`UnscaledInset` is the room the selection ring of the outermost slots needs. It is part of the
grid's **content** rather than its padding, because a clipping container cuts at its padding box -
padding would move the lattice and the cut by the same amount and buy the ring nothing.

## Knowing what changed

```csharp
grid.ItemPutIn    += (s, e) => Log($"{e.After.StackSize}x {e.After.GetName()} into slot {e.SlotId}");
grid.ItemTakenOut += (s, e) => Log($"{e.Before.GetName()} left slot {e.SlotId}");
grid.SlotChanged  += (s, e) => Log($"{e.Change}, {e.CountDelta:+#;-#;0}");
```

These fire for **every** change, not only for clicks in your grid: a shift click from the player's
bag, a hopper filling a crate, another player in a shared inventory, and the server correcting the
client all arrive here, because the client raises `SlotModified` when it applies a slot update from
the server too.

| on the arguments | |
|---|---|
| `SlotId`, `Slot` | which slot, and the slot itself as it is now |
| `Before` | a **copy** of what was in it, taken before the change |
| `After` | what is in it now |
| `Change` | `PutIn`, `TakenOut`, `Replaced`, `CountChanged`, `Other` |
| `CountDelta` | positive when the slot holds more than it did |

`Before` has to be a copy: by the time anything is told about a move, the old stack has already
been moved away. The game gives only `SlotModified(slotId)` - no previous contents, and no way to
tell an arrival from a departure - which is why `InventoryWatcher` exists.

A replacement counts as both: the old stack left and a new one arrived, so it raises `ItemTakenOut`
and `ItemPutIn`.

For an inventory without a GUI - server side rules, automation - use the watcher directly. It works
the same on either side:

```csharp
var watcher = new InventoryWatcher(inventory);
watcher.ItemPutIn += (s, e) => ...;
```

Call `Snapshot()` after filling an inventory from code if those fills should not be reported as
arrivals.

## A grid with its own inventory

For a dialog that just needs somewhere to put things, the grid can bring its own:

```csharp
var grid = new InventoryGridControl(6, "loadout", internalInventory: true, slotCount: 24);
var slot = InventoryGridControl.SingleSlot("output");   // the 1x1 case

grid.Inventory;   // from the first time the dialog is shown
```

It is a real inventory like any other, so the server still has to declare it. The name is derived
from the dialog and the control so it is the same one every session:

```csharp
inventorySystem.RegisterPlayerInventory(
    InventoryGridControl.InternalInventoryName("myDialog", "loadout"), 24);
```

That second line is the price of the inventory being real rather than a stand-in. The alternative -
a grid that invents its contents on the client - either conjures items out of nothing or swallows
the player's, depending on which way they move.

## What a grid does per click

`HandleSlotClick` is deliberately close to `GuiElementItemSlotGridBase.SlotClick`: the inventory
decides what a click means through `ActivateSlot`, and the objects it hands back are the packets
that tell the server the same thing.

```csharp
ItemSlot cursorSlot = capi.World.Player.InventoryManager
    .GetOwnInventory(GlobalConstants.mousecursorInvClassName)[0];

var op = new ItemStackMoveOperation(capi.World, mouse.Button, modifiers, EnumMergePriority.AutoMerge)
{
    ActingPlayer = capi.World.Player
};

packets = inventory.ActivateSlot(slotId, cursorSlot, ref op);
```

The stack being carried is **the one the whole game carries**. A grid with a cursor of its own
would mean two stacks carried at once, drawn on top of each other, and items crossing between a
server backed inventory and one only this client believes in.

Doing the move by hand - swapping the stacks yourself - works on the client and is reverted by the
next server sync.

The grid also raises what the game raises: the pick up and put down sounds,
`TriggerOnMouseClickSlot`, and `TriggerOnMouseEnterSlot` / `LeaveSlot` on hover. The last two are
not decoration - they are what fills the item tooltip. See
[Input, Focus and Rendering](Input-Focus-and-Rendering) for the patch that keeps that tooltip alive
while the cursor is over one of our dialogs.
