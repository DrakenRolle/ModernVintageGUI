using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ModernVintageGUI.Inventory
{
    /// <summary>
    /// A block entity that holds a <see cref="ModInventory"/> the server knows about, so a
    /// dialog of this framework can open it.
    ///
    /// It exists so a mod does not have to know any of the plumbing:
    ///
    /// <code>
    /// public class BlockEntityMyCrate : ModInventoryBlockEntity
    /// {
    ///     public BlockEntityMyCrate() : base(size: 16, inventoryClassName: "mycrate") { }
    /// }
    /// </code>
    ///
    /// Everything else comes from BlockEntityContainer, and deliberately so rather than being
    /// rebuilt here: the inventory is bound to <c>class-position</c> when the block is placed,
    /// its contents are saved with the chunk, and they drop on the ground when the block is
    /// broken. That is why blocks own their inventories through a block entity - a registry of
    /// our own would have to reinvent all three, and would keep the contents of blocks that no
    /// longer exist.
    ///
    /// Three blocks of this kind hold three separate inventories, because the position is part
    /// of the id. For three blocks that share one, do not use this - give them a shared
    /// inventory from <see cref="ModInventorySystem"/> and open that by name.
    /// </summary>
    public abstract class ModInventoryBlockEntity : BlockEntityContainer, IModInventoryHolder
    {
        private readonly int _declaredSize;
        private readonly int _declaredMaxSlotStackSize;
        private readonly string _className;

        private ModInventory? _inventory;

        /// <param name="maxSlotStackSize">
        /// How much one slot may hold, on top of what the item itself allows. Zero, the default,
        /// leaves it to the item. Both sides build the block entity the same way, so the cap is
        /// the server's rule and not a suggestion from the client - see
        /// <see cref="ModInventory.MaxSlotStackSize"/>.
        /// </param>
        protected ModInventoryBlockEntity(int size, string inventoryClassName, int maxSlotStackSize = 0)
        {
            _declaredSize = size;
            _declaredMaxSlotStackSize = maxSlotStackSize;
            _className = inventoryClassName;
        }

        public override InventoryBase Inventory => ModInventory;

        public override string InventoryClassName => _className;

        /// <summary>The inventory, already bound once the block entity has been initialized.</summary>
        public ModInventory ModInventory => _inventory ??= new ModInventory(SlotCount, MaxSlotStackSize);

        /// <summary>
        /// How many slots this block entity has. The constructor argument by default; override
        /// it to decide per block, which is what a block with variants needs:
        ///
        /// <code>
        /// protected override int SlotCount => Block?.Variant["metal"] == "titanium" ? 4 : 1;
        /// </code>
        ///
        /// The inventory is built the first time anything asks for it, which is late enough for
        /// <c>Block</c> to be set - the game assigns it before it loads the contents and before
        /// Initialize - and early enough to be the inventory the contents are loaded into. That
        /// is the whole window this may depend on: <c>Block</c> and <c>Pos</c>, nothing that
        /// arrives later, because a slot count that changes afterwards would silently drop
        /// whatever was in the slots that went away.
        /// </summary>
        protected virtual int SlotCount => _declaredSize;

        /// <summary>
        /// How much one slot holds, on top of what the item itself allows. As with
        /// <see cref="SlotCount"/>, override it to decide per block.
        /// </summary>
        protected virtual int MaxSlotStackSize => _declaredMaxSlotStackSize;

        /// <inheritdoc/>
        ModInventory? IModInventoryHolder.GetModInventory() => ModInventory;
    }

    /// <summary>
    /// Implemented by anything the server can ask for a <see cref="ModInventory"/> - a block
    /// entity, most of the time.
    ///
    /// <see cref="ModInventorySystem"/> looks for it when a client asks to open the inventory of
    /// a block: an inventory it cannot reach this way is one it will not open, which is what
    /// keeps a client from opening whatever it likes by sending a position.
    /// </summary>
    public interface IModInventoryHolder
    {
        /// <summary>The inventory a dialog may open, or null when there is none.</summary>
        ModInventory? GetModInventory();
    }
}
