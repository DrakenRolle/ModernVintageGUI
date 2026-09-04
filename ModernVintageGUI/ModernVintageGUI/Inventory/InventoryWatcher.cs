using System;
using Vintagestory.API.Common;

namespace ModernVintageGUI.Inventory
{
    /// <summary>What happened to a slot.</summary>
    public enum InventoryChange
    {
        /// <summary>The slot was empty and now holds something.</summary>
        PutIn,

        /// <summary>It held something and is now empty.</summary>
        TakenOut,

        /// <summary>It holds a different thing than before.</summary>
        Replaced,

        /// <summary>The same thing, in a different amount.</summary>
        CountChanged,

        /// <summary>The same thing in the same amount - its attributes changed, or it was durability.</summary>
        Other
    }

    /// <summary>What changed in a slot, and what it looked like before.</summary>
    public class InventorySlotChangedEventArgs : EventArgs
    {
        /// <summary>The inventory the slot belongs to.</summary>
        public IInventory Inventory { get; }

        /// <summary>Its position in that inventory.</summary>
        public int SlotId { get; }

        /// <summary>The slot itself, as it is now.</summary>
        public ItemSlot Slot { get; }

        /// <summary>
        /// What was in it before, as a copy taken when the slot last changed. Null when it was
        /// empty. A copy because the real stack no longer exists once it has been moved away.
        /// </summary>
        public ItemStack? Before { get; }

        /// <summary>What is in it now, or null when it is empty.</summary>
        public ItemStack? After => Slot.Itemstack;

        public InventoryChange Change { get; }

        /// <summary>
        /// How the amount moved: positive when the slot holds more than it did, negative when it
        /// holds less. A replacement counts the old stack out and the new one in, so this is the
        /// difference of the two.
        /// </summary>
        public int CountDelta { get; }

        public InventorySlotChangedEventArgs(
            IInventory inventory,
            int slotId,
            ItemSlot slot,
            ItemStack? before,
            InventoryChange change,
            int countDelta)
        {
            Inventory = inventory;
            SlotId = slotId;
            Slot = slot;
            Before = before;
            Change = change;
            CountDelta = countDelta;
        }
    }

    /// <summary>
    /// Watches an inventory and says what changed in it, with the state before the change.
    ///
    /// This is the hook a mod actually needs and the one the game does not give: InventoryBase
    /// raises <c>SlotModified(slotId)</c> and nothing else - no previous contents, no telling
    /// apart "something arrived" from "something left". The previous contents are kept here, as
    /// a copy per slot, because by the time anything is told about a move the old stack is gone.
    ///
    /// It sees every change, not only the ones the player made in this dialog: a shift click
    /// from the player's own bag, a hopper filling a crate, another player in a shared
    /// inventory, and the server correcting the client all end up in the same place, because the
    /// client raises SlotModified when it applies a slot update from the server too.
    ///
    /// Works on either side. On the server it is the same class watching the same events.
    /// </summary>
    public sealed class InventoryWatcher : IDisposable
    {
        private readonly IInventory _inventory;
        private ItemStack?[] _before;
        private bool _isDisposed;

        /// <summary>Anything at all changed in a slot.</summary>
        public event EventHandler<InventorySlotChangedEventArgs>? SlotChanged;

        /// <summary>
        /// Something arrived in a slot - it was empty and is not any more, it holds more than it
        /// did, or it now holds something else. The stack that arrived is in
        /// <see cref="InventorySlotChangedEventArgs.After"/>.
        /// </summary>
        public event EventHandler<InventorySlotChangedEventArgs>? ItemPutIn;

        /// <summary>
        /// Something left a slot - emptied, reduced, or replaced by something else. What left is
        /// in <see cref="InventorySlotChangedEventArgs.Before"/>.
        /// </summary>
        public event EventHandler<InventorySlotChangedEventArgs>? ItemTakenOut;

        public InventoryWatcher(IInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _before = new ItemStack?[inventory.Count];

            Snapshot();

            if (inventory is InventoryBase watchable)
            {
                watchable.SlotModified += OnSlotModified;
            }
        }

        /// <summary>
        /// Takes the picture the next change is compared against. Called at the start, and worth
        /// calling again after filling an inventory from code if those fills should not be
        /// reported as arrivals.
        /// </summary>
        public void Snapshot()
        {
            EnsureSize();

            for (int i = 0; i < _inventory.Count; i++)
            {
                _before[i] = _inventory[i]?.Itemstack?.Clone();
            }
        }

        private void EnsureSize()
        {
            // An inventory can grow - InventoryGeneric resizes itself when it is read back from
            // saved data - and a snapshot from before that would be indexed out of range.
            if (_before.Length >= _inventory.Count)
                return;

            var grown = new ItemStack?[_inventory.Count];
            Array.Copy(_before, grown, _before.Length);
            _before = grown;
        }

        private void OnSlotModified(int slotId)
        {
            if (_isDisposed || slotId < 0)
                return;

            EnsureSize();

            if (slotId >= _inventory.Count)
                return;

            ItemSlot? slot = _inventory[slotId];

            if (slot == null)
                return;

            ItemStack? before = _before[slotId];
            ItemStack? after = slot.Itemstack;

            // Recorded before anything is raised: a handler is free to move the stack on, and
            // the next change has to be compared against what is there now, not against what
            // was there when this one started.
            _before[slotId] = after?.Clone();

            int beforeCount = before?.StackSize ?? 0;
            int afterCount = after?.StackSize ?? 0;

            InventoryChange change = Classify(before, after);

            if (change == InventoryChange.Other && beforeCount == afterCount)
            {
                // Nothing worth reporting as a movement, but a redraw may still be due.
                Raise(SlotChanged, slotId, slot, before, change, 0);
                return;
            }

            int delta = afterCount - beforeCount;

            Raise(SlotChanged, slotId, slot, before, change, delta);

            // A replacement is both: the old stack left and a new one arrived. Reporting only
            // one of the two would lose half of what happened.
            if (change == InventoryChange.TakenOut || change == InventoryChange.Replaced || delta < 0)
            {
                Raise(ItemTakenOut, slotId, slot, before, change, delta);
            }

            if (change == InventoryChange.PutIn || change == InventoryChange.Replaced || delta > 0)
            {
                Raise(ItemPutIn, slotId, slot, before, change, delta);
            }
        }

        private static InventoryChange Classify(ItemStack? before, ItemStack? after)
        {
            if (before == null && after == null)
                return InventoryChange.Other;

            if (before == null)
                return InventoryChange.PutIn;

            if (after == null)
                return InventoryChange.TakenOut;

            if (before.Collectible?.Id != after.Collectible?.Id
                || before.Class != after.Class)
            {
                return InventoryChange.Replaced;
            }

            return before.StackSize != after.StackSize
                ? InventoryChange.CountChanged
                : InventoryChange.Other;
        }

        private void Raise(
            EventHandler<InventorySlotChangedEventArgs>? handler,
            int slotId,
            ItemSlot slot,
            ItemStack? before,
            InventoryChange change,
            int delta)
        {
            handler?.Invoke(this, new InventorySlotChangedEventArgs(_inventory, slotId, slot, before, change, delta));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_inventory is InventoryBase watchable)
            {
                watchable.SlotModified -= OnSlotModified;
            }
        }
    }
}
