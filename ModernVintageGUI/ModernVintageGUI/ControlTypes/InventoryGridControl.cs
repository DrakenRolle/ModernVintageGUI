using Cairo;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using ModernVintageGUI.Inventory;
using Vintagestory.API.Config;

namespace IS2Mod.ControlTypes
{
    /// <summary>Tells a handler which slot was clicked.</summary>
    public class ItemSlotEventArgs : EventArgs
    {
        /// <summary>The control that was clicked.</summary>
        public ItemSlotControl SlotControl { get; }

        /// <summary>Its position in the grid, counting left to right then top to bottom.</summary>
        public int SlotIndex { get; }

        /// <summary>The inventory slot behind it, if the grid was given an inventory.</summary>
        public ItemSlot? Slot => SlotControl.Slot;

        /// <summary>Which button, and where - the original mouse arguments.</summary>
        public MouseEventArgs Mouse { get; }

        public ItemSlotEventArgs(ItemSlotControl slotControl, MouseEventArgs mouse)
        {
            SlotControl = slotControl;
            SlotIndex = slotControl.SlotIndex;
            Mouse = mouse;
        }
    }

    /// <summary>
    /// A grid of <see cref="ItemSlotControl"/>, laid out on the same lattice the vanilla
    /// inventory uses, and scrollable when there are more rows than fit.
    ///
    /// <see cref="SetInventory"/> is what makes it real: from then on it is a view of an actual
    /// inventory, every click goes through ActivateSlot the way vanilla's own grid does it, and
    /// the stack being carried is the one the whole game carries - so items move between this
    /// grid and the player's bag, a chest or the creative inventory exactly as they move between
    /// any two vanilla grids. For an inventory of the mod's own,
    /// <see cref="ModernVintageGUI.Inventory.ModInventoryAccess"/> builds one that the server knows
    /// about, which is what the server needs before it will accept a single move.
    ///
    /// It is a <see cref="RectangleControl"/> rather than something built from scratch, which
    /// is what makes the scrolling free: clipping, the viewport, the bars, the wheel and the
    /// drag all come from the container it already is. What it adds is the placement.
    ///
    /// The slots are placed by hand instead of being stacked into row containers, and that is
    /// deliberate. Vanilla puts slot (col, row) at exactly
    /// <c>col * (48 + 3)</c> by <c>row * (48 + 3)</c> scaled, with no gap before the first one
    /// and none after the last. Reproducing that with nested stacking containers means giving
    /// every slot half the gap as a margin, which leaves half a gap around the outside of the
    /// grid that vanilla does not have - close, but not the same picture. The lattice is a
    /// dozen lines and it is exact.
    /// </summary>
    public class InventoryGridControl : RectangleControl
    {
        #region Properties
        private int _columns = 1;
        /// <summary>
        /// How many slots per row. The number of rows follows from the slot count, the way
        /// GuiElementItemSlotGridBase derives it from cols and the inventory size.
        /// </summary>
        public int Columns
        {
            get => _columns;
            set
            {
                int columns = Math.Max(1, value);
                if (!SetProperty(ref _columns, columns))
                    return;

                RecomposeToMain();
            }
        }

        /// <summary>The slot controls, in grid order. Managed by the grid, not by the caller.</summary>
        public IReadOnlyList<ItemSlotControl> Slots => _slots;

        /// <summary>How many rows the current slot count needs.</summary>
        public int Rows => (int)Math.Ceiling(_slots.Count / (double)_columns);

        /// <summary>Raised when a slot is clicked, with the slot and the mouse arguments.</summary>
        public event EventHandler<ItemSlotEventArgs>? SlotClicked;

        /// <summary>Raised when the cursor enters a slot. Useful for a tooltip.</summary>
        public event EventHandler<ItemSlotEventArgs>? SlotEnter;

        /// <summary>
        /// Raised when the contents of a slot change, whoever changed them - a click in this
        /// grid, a shift click from the player's bag, a hopper, another player in a shared
        /// inventory, or the server correcting this client.
        ///
        /// This is the one to listen on for "what is in there now". <see cref="SlotClicked"/> is
        /// not: it reports a click in this grid and nothing else, so it misses every change that
        /// came from somewhere else.
        /// </summary>
        public event EventHandler<InventorySlotChangedEventArgs>? SlotChanged;

        /// <summary>Something arrived in a slot. See <see cref="InventoryWatcher.ItemPutIn"/>.</summary>
        public event EventHandler<InventorySlotChangedEventArgs>? ItemPutIn;

        /// <summary>Something left a slot. See <see cref="InventoryWatcher.ItemTakenOut"/>.</summary>
        public event EventHandler<InventorySlotChangedEventArgs>? ItemTakenOut;
        #endregion

        private readonly List<ItemSlotControl> _slots = new List<ItemSlotControl>();

        /// <summary>Slot pitch in device pixels: the slot plus the gap after it.</summary>
        private double Pitch =>
            (ItemSlotControl.UnscaledSlotSize + ItemSlotControl.UnscaledSlotPadding) * LayoutScale;

        private double SlotSize => ItemSlotControl.UnscaledSlotSize * LayoutScale;

        /// <summary>
        /// The lattice sits this far inside the grid on every side, so the selection ring of the
        /// outermost slots has room. Add it twice to the width and the height when giving the
        /// grid a fixed size, or the visible row count comes out one ring short.
        ///
        /// It is part of the *content* rather than the control's padding, and that is the whole
        /// point: a clipping container cuts at its padding box, so padding moves the lattice and
        /// the cut by the same amount and buys the ring nothing. Room inside the clip is what
        /// the ring needs.
        /// </summary>
        public const double UnscaledInset = ItemSlotControl.UnscaledHighlightReach;

        /// <summary>That inset in device pixels.</summary>
        private double Inset => UnscaledInset * LayoutScale;

        /// <param name="internalInventory">
        /// Give the grid an inventory of its own instead of being handed one. The server keeps
        /// it per player and saves it with them, and it is reachable through
        /// <see cref="Inventory"/> from the moment the dialog is first shown - which is when it
        /// can be built, because that is when the client API is reachable.
        ///
        /// It is a real inventory like any other, not a client side stand-in: the player can
        /// move items in and out of it, and what they leave in it is still there next time.
        /// </param>
        /// <param name="slotCount">How many slots that inventory has. Ignored without it.</param>
        public InventoryGridControl(
            int columns = 1,
            string _Name = "",
            bool internalInventory = false,
            int slotCount = 1)
            : base(_Name: _Name, _Margin: 0, _Padding: 0)
        {
            _columns = Math.Max(1, columns);
            _wantsInternalInventory = internalInventory;
            _internalSlotCount = Math.Max(1, slotCount);

            // The grid is a container of slots and nothing else. InsideOrientation is not used -
            // placement is the lattice below - but None keeps the base measure pass from
            // stacking the slots on top of each other into a single slot sized box.
            InsideOrientation = Orientation.None;

            if (_wantsInternalInventory)
            {
                // Something to look at before the dialog is shown and the real one arrives.
                SetSlotCount(_internalSlotCount);
            }
        }

        /// <summary>
        /// A single slot the player can put one thing into, backed by an inventory of its own.
        /// The 1x1 case of the grid, named so a caller does not have to spell it out.
        /// </summary>
        public static InventoryGridControl SingleSlot(string name = "")
        {
            return new InventoryGridControl(columns: 1, _Name: name, internalInventory: true, slotCount: 1);
        }

        #region Building
        /// <summary>
        /// Fills the grid with empty slots. Use this for a fixed size grid whose contents are
        /// assigned later through <see cref="Slots"/>.
        /// </summary>
        public void SetSlotCount(int count)
        {
            count = Math.Max(0, count);

            while (_slots.Count > count)
            {
                ItemSlotControl removed = _slots[_slots.Count - 1];
                _slots.RemoveAt(_slots.Count - 1);
                Children.Remove(removed);
            }

            while (_slots.Count < count)
            {
                AddSlot(null);
            }

            RecomposeToMain();
        }

        /// <summary>
        /// Shows a real inventory: one slot control per inventory slot, in inventory order, and
        /// from now on clicks actually move items.
        ///
        /// Without this the grid is decoration - <see cref="SetSlotCount"/> gives it empty slots
        /// that nothing can be put into, because there is no inventory behind them and no server
        /// that would accept the move.
        /// </summary>
        /// <param name="sendPacket">
        /// Where the packets produced by a slot move go. Pass
        /// <c>p => capi.Network.SendPacketClient((Packet_Client)p)</c> - the same thing vanilla
        /// asks for in GuiComposer.AddItemSlotGrid. Without it the move happens on the client
        /// only and the server corrects it back on the next sync.
        /// </param>
        /// <param name="announceOpen">
        /// Send Open and Close for this inventory while the dialog is on screen. Leave it on for
        /// a block or entity inventory the server already knows about; turn it off when opening
        /// is arranged elsewhere - <see cref="ModernVintageGUI.Inventory.DialogInventory"/> does
        /// it on both sides, which an inventory the server has to be told about first needs.
        ///
        /// Either way it has to happen: InventoryBase.CanPlayerModify is
        /// CanPlayerAccess &amp;&amp; HasOpened, so an inventory the player has not opened
        /// refuses every move, on the client and on the server alike.
        /// </param>
        /// <summary>
        /// The simple way in: an inventory of the mod's own, with everything it needs to work
        /// already attached.
        ///
        /// <code>
        /// grid.SetInventory(ModInventoryAccess.ForShared(capi, "guildbank", 32));
        /// grid.SetInventory(ModInventoryAccess.ForBlock(capi, pos, blockEntity.Inventory));
        /// </code>
        ///
        /// The packets a slot move produces and the opening and closing along with the dialog
        /// are taken from the access - which is the difference to the overload below, where the
        /// caller has to supply both and gets a grid that silently refuses every move if it does
        /// not.
        /// </summary>
        public void SetInventory(ModInventoryAccess access)
        {
            if (access == null)
                throw new ArgumentNullException(nameof(access));

            IInventory? inventory = access.Inventory;

            if (inventory == null)
                throw new InvalidOperationException(
                    "The inventory is not available yet - there is no player. Build the dialog " +
                    "when the game is running, not while the mod is starting.");

            _access = access;

            SetInventory(
                inventory,
                access.Capi,
                sendPacket: access.SendPacket,

                // The access opens it, on both sides. A grid cannot: the server has to be told
                // about an inventory before it will accept a single move for it.
                announceOpen: false);
        }

        public void SetInventory(
            IInventory inventory,
            ICoreClientAPI capi,
            Action<object>? sendPacket = null,
            bool announceOpen = true)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            _capi = capi ?? throw new ArgumentNullException(nameof(capi));
            _inventory = inventory;
            _sendPacket = sendPacket;
            _announceOpen = announceOpen;

            WatchInventory(inventory);

            SetSlotCount(0);

            for (int i = 0; i < inventory.Count; i++)
            {
                AddSlot(inventory[i]);
            }

            RecomposeToMain();
        }

        #region Inventory interaction
        private IInventory? _inventory;
        private ICoreClientAPI? _capi;
        private Action<object>? _sendPacket;
        private bool _announceOpen;
        private bool _weOpenedInventory;
        private bool _warnedNotOpened;

        private ModInventoryAccess? _access;
        private InventoryWatcher? _watcher;
        private readonly bool _wantsInternalInventory;
        private readonly int _internalSlotCount;

        /// <summary>
        /// The inventory the grid is showing - the one it was given, or the one it made for
        /// itself when it was built with <c>internalInventory: true</c>.
        ///
        /// Null until the dialog has been shown once in the internal case: the inventory needs
        /// the client API, and that is only reachable through a dialog.
        /// </summary>
        public IInventory? Inventory => _inventory;

        /// <summary>
        /// Announces the inventory as opened, unless somebody else already has it open.
        ///
        /// This is not optional bookkeeping, it is what makes the grid work at all:
        /// InventoryBase.CanPlayerModify returns HasOpened(player), so an inventory the player
        /// has not opened refuses every move. The player's own backpack is no exception - the
        /// game opens it when the vanilla inventory dialog opens and closes it again right
        /// after, so from our dialog it is closed like any other.
        /// </summary>
        public override void OnDialogShown()
        {
            base.OnDialogShown();

            EnsureInternalInventory();

            // An inventory of the mod's own opens itself on both sides - which is the part a
            // grid cannot do, because the server has to be told about it first.
            _access?.Open();

            if (_inventory == null || _capi == null || !_announceOpen || _weOpenedInventory)
                return;

            IPlayer player = _capi.World.Player;

            // Already open - the vanilla inventory is on screen next to us, say. Opening it a
            // second time would be harmless, closing it afterwards would not: the open state is
            // a set of player ids, not a counter, so one Close takes it away from everyone.
            if (_inventory.HasOpened(player))
                return;

            SendToServer(_inventory.Open(player));
            _weOpenedInventory = true;
        }

        public override void OnDialogHidden()
        {
            base.OnDialogHidden();

            _access?.Close();

            if (_inventory == null || _capi == null || !_weOpenedInventory)
                return;

            SendToServer(_inventory.Close(_capi.World.Player));
            _weOpenedInventory = false;
        }

        /// <summary>
        /// Builds the grid's own inventory, the first time the dialog is shown.
        ///
        /// Not in the constructor, because an inventory needs the client API and that is only
        /// reachable through a dialog. The name it is stored under is built from the dialog and
        /// the control, so it is the same one every session - otherwise what the player left in
        /// it would be in a different inventory next time.
        /// </summary>
        private void EnsureInternalInventory()
        {
            if (!_wantsInternalInventory || _access != null)
                return;

            ICoreClientAPI? capi = Dialog?.Api;

            if (capi == null)
                return;

            SetInventory(ModInventoryAccess.ForPlayer(
                capi,
                InternalInventoryName(Dialog?.DialogName, Name),
                _internalSlotCount));
        }

        /// <summary>
        /// The name the grid's own inventory is stored under, built from the dialog and the
        /// control so it is the same one every session.
        ///
        /// It is public because the server has to declare that inventory before it will accept a
        /// move into it, and the server has no grid to ask:
        ///
        /// <code>
        /// // client
        /// var grid = new InventoryGridControl(6, "loadout", internalInventory: true, slotCount: 24);
        /// // server
        /// system.RegisterPlayerInventory(
        ///     InventoryGridControl.InternalInventoryName("myDialog", "loadout"), 24);
        /// </code>
        ///
        /// That second line is the price of the inventory being real rather than a client side
        /// stand-in - the server decides what exists and how big it is, or a client could ask
        /// for anything it liked.
        /// </summary>
        public static string InternalInventoryName(string? dialogName, string? controlName)
        {
            return (string.IsNullOrEmpty(dialogName) ? "dialog" : dialogName)
                 + "_"
                 + (string.IsNullOrEmpty(controlName) ? "grid" : controlName);
        }

        /// <summary>
        /// Moves items between the clicked slot and the stack on the cursor.
        ///
        /// This is GuiElementItemSlotGridBase.SlotClick, kept deliberately close to it: the
        /// inventory decides what a click means through ActivateSlot, and the objects it hands
        /// back are the packets that tell the server the same thing. Doing the move by hand -
        /// swapping the stacks ourselves - would work on the client and be reverted by the next
        /// server sync.
        /// </summary>
        private void HandleSlotClick(ItemSlotControl slotControl, MouseEventArgs mouse)
        {
            if (_inventory == null || _capi == null)
                return;

            // The one cursor the whole game shares. Everything the player can pick a stack up
            // from puts it here - the hotbar, the backpack, a chest, the creative inventory -
            // which is exactly why a grid must use it too: a cursor of one's own would mean two
            // stacks carried at once, drawn on top of each other, and items crossing between a
            // server backed inventory and one that only this client believes in.
            ItemSlot? cursorSlot = _capi.World.Player.InventoryManager
                .GetOwnInventory(GlobalConstants.mousecursorInvClassName)?[0];

            if (cursorSlot == null)
                return;

            // A closed inventory silently refuses every move, and from the outside that looks
            // exactly like the click not arriving at all. Say so once instead of leaving the
            // next person to guess.
            if (!_warnedNotOpened && !_inventory.HasOpened(_capi.World.Player))
            {
                _warnedNotOpened = true;
                _capi.Logger.Warning(
                    "[ModernVintageGUI] Inventory '{0}' is not opened for this player, so it will " +
                    "refuse every slot move. Pass announceOpen: true to SetInventory, or open it " +
                    "yourself before showing the dialog.",
                    _inventory.InventoryID);
            }

            bool shift = _capi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft]
                      || _capi.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];
            bool ctrl = _capi.Input.KeyboardKeyState[(int)GlKeys.ControlLeft]
                     || _capi.Input.KeyboardKeyState[(int)GlKeys.ControlRight];
            bool alt = _capi.Input.KeyboardKeyState[(int)GlKeys.AltLeft];

            EnumModifierKey modifiers =
                (shift ? EnumModifierKey.SHIFT : 0) |
                (ctrl ? EnumModifierKey.CTRL : 0) |
                (alt ? EnumModifierKey.ALT : 0);

            var op = new ItemStackMoveOperation(
                _capi.World, mouse.Button, modifiers, EnumMergePriority.AutoMerge)
            {
                ActingPlayer = _capi.World.Player
            };

            int slotId = slotControl.SlotIndex;
            object? packets;

            if (shift)
            {
                // Shift click moves the whole stack somewhere sensible rather than onto the
                // cursor, so the source and the target are both inside this inventory.
                ItemSlot? source = _inventory[slotId];
                if (source == null)
                    return;

                op.RequestedQuantity = source.StackSize;
                packets = _inventory.ActivateSlot(slotId, source, ref op);
            }
            else
            {
                op.CurrentPriority = EnumMergePriority.DirectMerge;

                bool wasCarrying = cursorSlot.Itemstack != null;
                CollectibleObject? carried = cursorSlot.Itemstack?.Collectible;

                packets = _inventory.ActivateSlot(slotId, cursorSlot, ref op);

                PlayClickSound(cursorSlot, wasCarrying, carried);
            }

            SendToServer(packets);

            // Vanilla raises this from SlotClick, and things listen: the item info panel of the
            // survival inventory is redrawn from it, and mods hang their own reactions off it.
            _capi.Input.TriggerOnMouseClickSlot(_inventory[slotId]);

            // The stack changed, so the slot has to be drawn again. The background does not
            // depend on the stack, but the highlight and any overlay might.
            Dialog?.Refresh();
        }

        /// <summary>
        /// The pick up and put down sounds, decided the way GuiElementItemSlotGridBase decides
        /// them: from what the cursor held before the move and what it holds after.
        /// </summary>
        private void PlayClickSound(ItemSlot cursorSlot, bool wasCarrying, CollectibleObject? carried)
        {
            if (_capi == null)
                return;

            ItemStack? now = cursorSlot.Itemstack;

            if (!wasCarrying && now != null)
            {
                _capi.World.PlaySound(now.Collectible?.HeldSounds?.InvPickup ?? HeldSounds.InvPickUpDefault);
                return;
            }

            if ((wasCarrying && now == null) || carried?.Id != now?.Collectible?.Id)
            {
                _capi.World.PlaySound(carried?.HeldSounds?.InvPlace ?? HeldSounds.InvPlaceDefault);
            }
        }

        /// <summary>
        /// ActivateSlot returns either one packet or an array of them - forward whatever came
        /// back, and quietly do nothing when the caller did not give us a sender.
        /// </summary>
        private void SendToServer(object? packets)
        {
            if (packets == null || _sendPacket == null)
                return;

            if (packets is object[] many)
            {
                foreach (object packet in many)
                {
                    _sendPacket(packet);
                }

                return;
            }

            _sendPacket(packets);
        }
        #endregion

        /// <summary>
        /// Starts reporting what changes in the inventory, and stops reporting the previous one.
        ///
        /// The redraw is the reason this exists even for a grid nobody subscribed to: a stack
        /// that arrives from anywhere but a click in this grid - a shift click, a hopper, the
        /// server - changes nothing this control knows about, so without being told the grid
        /// would keep drawing what was there before until something else made it redraw.
        /// </summary>
        private void WatchInventory(IInventory inventory)
        {
            _watcher?.Dispose();
            _watcher = new InventoryWatcher(inventory);

            _watcher.SlotChanged += (sender, e) =>
            {
                Dialog?.Refresh();
                SlotChanged?.Invoke(this, e);
            };

            _watcher.ItemPutIn += (sender, e) => ItemPutIn?.Invoke(this, e);
            _watcher.ItemTakenOut += (sender, e) => ItemTakenOut?.Invoke(this, e);
        }

        private void AddSlot(ItemSlot? slot)
        {
            var control = new ItemSlotControl(
                _Name: Name + "_slot" + _slots.Count,
                _SlotIndex: _slots.Count)
            {
                Slot = slot
            };

            // MouseDown, not Clicked. Vanilla moves items in OnMouseDownOnElement, and it
            // matters: a click only completes when press and release land on the same control,
            // so picking a stack up and dropping it somewhere else in one motion would never
            // register.
            control.MouseDown += OnSlotMouseDown;
            control.Enter += OnSlotEnter;

            _slots.Add(control);
            Children.Add(control);
        }

        private void OnSlotMouseDown(object? sender, MouseEventArgs e)
        {
            if (sender is not ItemSlotControl slot)
                return;

            // The move first, then the notification - so a handler that looks at the slot sees
            // what the click actually did rather than the state before it.
            HandleSlotClick(slot, e);

            SlotClicked?.Invoke(this, new ItemSlotEventArgs(slot, e));
        }

        private void OnSlotEnter(object? sender, MouseEventArgs e)
        {
            if (sender is ItemSlotControl slot)
                SlotEnter?.Invoke(this, new ItemSlotEventArgs(slot, e));
        }
        #endregion

        #region Layout
        /// <summary>
        /// The grid measures to its full lattice - every row, including the ones that will be
        /// scrolled out of sight. That is what the scrolling container compares against its
        /// viewport to decide whether a bar is needed, so it has to be the whole thing and not
        /// just the visible part.
        ///
        /// The control's own size is separate: give it a fixed one to get a window onto the
        /// grid, or leave it auto sizing to get all of it at once.
        /// </summary>
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD lattice = LatticeSize();
            MeasuredContentSize = lattice;

            PointD measured = ClampToMaxSize(IsAutoSize
                ? new PointD(lattice.X + ScaledPadding * 2, lattice.Y + ScaledPadding * 2)
                : ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }

        /// <summary>
        /// The size of the full grid: n slots wide plus the gaps between them, and no gap at
        /// either edge - hence (n - 1) gaps rather than n - plus the inset the selection ring
        /// needs on all four sides.
        ///
        /// The inset belongs in here and not in the padding because this is also what the
        /// scrolling container compares against its viewport: counted as content, the last row
        /// can be scrolled far enough for its ring to be shown whole.
        /// </summary>
        private PointD LatticeSize()
        {
            if (_slots.Count == 0)
                return new PointD(0, 0);

            int columns = Math.Min(_columns, _slots.Count);
            int rows = Rows;

            double gap = ItemSlotControl.UnscaledSlotPadding * LayoutScale;

            return new PointD(
                columns * SlotSize + (columns - 1) * gap + Inset * 2,
                rows * SlotSize + (rows - 1) * gap + Inset * 2);
        }

        /// <summary>
        /// Slots keep their own size. The normalization that stretches children to the width of
        /// their container is right for a list of rows and wrong for a grid of fixed squares.
        /// </summary>
        public override void NormalizeChildrenByDelta()
        {
        }

        public override void CalculateAllPositions()
        {
            if (Parent == null)
            {
                Position = new PointD(0, 0);
            }

            LayoutRect box = ArrangeBox();

            for (int i = 0; i < _slots.Count; i++)
            {
                int column = i % _columns;
                int row = i / _columns;

                ItemSlotControl slot = _slots[i];
                slot.SetLayoutSize(new PointD(SlotSize, SlotSize));
                slot.Position = new PointD(
                    box.X + Inset + column * Pitch,
                    box.Y + Inset + row * Pitch);

                slot.CalculateAllPositions();
            }

            // Placed first, scrolled second. The other way round the lattice would overwrite
            // the shift and the grid would never move.
            ApplyScrollOffsetToChildren();
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);
        }

        #endregion
    }
}
