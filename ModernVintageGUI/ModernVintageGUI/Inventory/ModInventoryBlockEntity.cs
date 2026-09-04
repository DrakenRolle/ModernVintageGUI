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
        private readonly ModInventory _inventory;
        private readonly string _className;

        protected ModInventoryBlockEntity(int size, string inventoryClassName)
        {
            _inventory = new ModInventory(size);
            _className = inventoryClassName;
        }

        public override InventoryBase Inventory => _inventory;

        public override string InventoryClassName => _className;

        /// <summary>The inventory, already bound once the block entity has been initialized.</summary>
        public ModInventory ModInventory => _inventory;

        /// <inheritdoc/>
        ModInventory? IModInventoryHolder.GetModInventory() => _inventory;
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
        /// <summary>The inventory, or null when there is none to open.</summary>
        ModInventory? GetModInventory();
    }
}
