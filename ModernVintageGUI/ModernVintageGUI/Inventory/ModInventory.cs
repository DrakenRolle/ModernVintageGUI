using System;
using Vintagestory.API.Common;

namespace ModernVintageGUI.Inventory
{
    /// <summary>
    /// An inventory a mod owns. Nothing more than an <see cref="InventoryGeneric"/> with a
    /// constructor that takes only a size - which is the point: it *is* a vanilla inventory,
    /// so everything that works with one works with this.
    ///
    /// <code>
    /// var inventory = new ModInventory(16);
    /// </code>
    ///
    /// It starts unbound, exactly the way BlockEntityContainer keeps its inventory before the
    /// block entity is placed in the world. What binds it is an id and an API, and who supplies
    /// those is what tells the three cases apart:
    ///
    /// - a block entity binds it in Initialize, through
    ///   <see cref="ModInventoryBlockEntity"/> or by calling LateInitialize itself. The id
    ///   carries the position, so three blocks of the same kind hold three inventories;
    /// - a shared inventory is bound by <see cref="ModInventorySystem"/> under a name, so any
    ///   number of blocks can open the same one and see each other's changes;
    /// - a per player inventory is bound by the same system under a class name and the player.
    ///
    /// In every case the *server* is the one that owns the contents and decides the size. The
    /// client holds a copy under the same id and the game keeps the two in step - see
    /// <see cref="ModInventoryAccess"/> for why the id is the whole story.
    /// </summary>
    public class ModInventory : InventoryGeneric
    {
        /// <summary>
        /// An inventory of <paramref name="size"/> slots that is not bound to anything yet.
        /// Bind it with LateInitialize, or hand it to something that does.
        /// </summary>
        /// <param name="maxSlotStackSize">
        /// How much one slot may hold. Zero, the default, means as much as the item itself
        /// allows - see <see cref="MaxSlotStackSize"/>.
        /// </param>
        public ModInventory(int size, int maxSlotStackSize = 0)
            : base(Math.Max(1, size), null, null, SlotFactory(maxSlotStackSize))
        {
            MaxSlotStackSize = Math.Max(0, maxSlotStackSize);
        }

        /// <summary>Bound straight away, for a caller that already has both.</summary>
        public ModInventory(int size, string id, ICoreAPI api, int maxSlotStackSize = 0)
            : base(Math.Max(1, size), id, api, SlotFactory(maxSlotStackSize))
        {
            MaxSlotStackSize = Math.Max(0, maxSlotStackSize);
        }

        /// <summary>True once it has an id and an API and can be opened.</summary>
        public bool IsBound => Api != null && InventoryID != null;

        /// <summary>
        /// How much one slot of this inventory holds, or 0 for no limit of its own.
        ///
        /// It is a cap on top of the item's own maximum, never instead of it: a slot capped at
        /// 32 still holds only one pickaxe, because a pickaxe stacks to one. The effective limit
        /// is the smaller of the two, and both are enforced by the game rather than here -
        /// <c>ItemSlot</c> checks the slot's cap in all three click paths (into an empty slot,
        /// merging onto a stack, and swapping), and the item's own maximum comes out of
        /// <c>Collectible.GetMergableQuantity</c>.
        ///
        /// Because the check lives in the slot, it holds on the server too, and a client that
        /// tried to put more in would simply be corrected.
        /// </summary>
        public int MaxSlotStackSize { get; }

        /// <summary>
        /// The slots to build. Null for the default, which is what InventoryGeneric makes on its
        /// own - no cap means no reason to take over its slot creation.
        /// </summary>
        private static NewSlotDelegate? SlotFactory(int maxSlotStackSize)
        {
            if (maxSlotStackSize <= 0)
                return null;

            // ItemSlotSurvival is what InventoryGeneric builds by default, so a capped inventory
            // differs from an uncapped one in the cap and in nothing else.
            return (slotId, self) => new ItemSlotSurvival(self) { MaxSlotStackSize = maxSlotStackSize };
        }
    }
}
