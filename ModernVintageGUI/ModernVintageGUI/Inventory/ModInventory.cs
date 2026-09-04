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
        public ModInventory(int size)
            : base(Math.Max(1, size), null, null)
        {
        }

        /// <summary>Bound straight away, for a caller that already has both.</summary>
        public ModInventory(int size, string id, ICoreAPI api)
            : base(Math.Max(1, size), id, api)
        {
        }

        /// <summary>True once it has an id and an API and can be opened.</summary>
        public bool IsBound => Api != null && InventoryID != null;
    }
}
