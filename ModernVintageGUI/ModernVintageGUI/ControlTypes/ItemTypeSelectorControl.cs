using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>Which item type was picked.</summary>
    public class ItemTypeSelectedEventArgs : EventArgs
    {
        /// <summary>The type, or null when the selection was cleared.</summary>
        public ItemStack? Stack { get; }

        /// <summary>Its code, the thing a mod usually wants to store.</summary>
        public AssetLocation? Code => Stack?.Collectible?.Code;

        public ItemTypeSelectedEventArgs(ItemStack? stack)
        {
            Stack = stack;
        }
    }

    /// <summary>
    /// A slot that picks an item *type* rather than holding an item.
    ///
    /// It looks like an inventory slot on purpose - that is what a player reads as "an item goes
    /// here" - but nothing can be dropped into it and nothing can be taken out. Clicking it opens
    /// the list of types the caller supplied, drawn like the survival handbook's Blocks and Items
    /// page, and picking one sets <see cref="SelectedItemType"/>.
    ///
    /// It is its own control rather than a mode of <see cref="ItemSlotControl"/>, and the reason
    /// is worth stating: a slot's whole job is to be one end of a stack move, and a type picker
    /// is not. Giving a real slot a mode in which its inventory is a fiction is how a grid ends
    /// up conjuring items - the two want different things and only share a look. That look is
    /// shared honestly here: the square *is* an ItemSlotControl, so the frame, the hover ring
    /// and the item tooltip are the real ones.
    /// </summary>
    public class ItemTypeSelectorControl : UIControl, IDisposable
    {
        #region Styling
        /// <summary>The caret in the corner, so the square reads as something that opens.</summary>
        private const double UnscaledCaretWidth = 9.0;
        private const double UnscaledCaretHeight = 5.0;
        private const double UnscaledCaretInset = 3.0;

        /// <summary>
        /// The room the slot's selection ring needs around it, which is the same room a grid
        /// leaves around its outermost slots.
        ///
        /// It has to be the same number, and not only so the ring is not clipped: a picker next
        /// to a single slot grid is the normal way these two are used, and a control that
        /// reserves nothing sits a ring's width higher than its neighbour. Two squares that are
        /// meant to look alike and are three pixels out of line is exactly the kind of thing
        /// that is impossible to unsee.
        /// </summary>
        private static double UnscaledInset => InventoryGridControl.UnscaledInset;

        /// <summary>GuiElementListMenu strokes its box with LineWidth 2.</summary>
        private const int ListBorderWidth = 2;
        #endregion

        #region Properties
        /// <summary>The types on offer, in list order. Replace them with <see cref="SetTypes"/>.</summary>
        public IReadOnlyList<ItemStack> Types => _types;

        /// <summary>
        /// The picked type, or null when nothing is picked. Assigning it picks the matching
        /// entry - by collectible, so a stack of a different size or from another source still
        /// finds its entry.
        /// </summary>
        public ItemStack? SelectedItemType
        {
            get => _selected;
            set => Select(IndexOfCollectible(value?.Collectible), notify: true);
        }

        /// <summary>The code of the picked type - what a mod usually stores and reloads.</summary>
        public AssetLocation? SelectedCode => _selected?.Collectible?.Code;

        /// <summary>The picked collectible, for a caller that wants the block or item itself.</summary>
        public CollectibleObject? SelectedCollectible => _selected?.Collectible;

        /// <summary>Its position in <see cref="Types"/>, or -1 when nothing is picked.</summary>
        public int SelectedIndex => _selectedIndex;

        /// <summary>
        /// Offer an entry that clears the selection. Off by default - a picker that must always
        /// hold something is the more common case.
        /// </summary>
        public bool AllowEmpty { get; set; }

        /// <summary>The caption of that entry.</summary>
        public string EmptyText { get; set; } = "None";

        /// <summary>How many types are listed before the list starts scrolling. 0 = unlimited.</summary>
        public int MaxVisibleItems { get; set; }

        /// <summary>The same limit as a height in author units. 0 = unlimited.</summary>
        public double MaxListHeight { get; set; }

        /// <summary>Raised when the picked type changes, by click, by keyboard or from code.</summary>
        public event EventHandler<ItemTypeSelectedEventArgs>? SelectionChanged;
        #endregion

        #region Private fields
        private readonly List<ItemStack> _types = new List<ItemStack>();
        private readonly List<DropdownItem> _entries = new List<DropdownItem>();
        private readonly ItemSlotControl _slot;
        private readonly RectangleControl _listBox;
        private readonly PopupHost _popup;
        private readonly DummySlot _preview = new DummySlot();

        private ItemStack? _selected;
        private int _selectedIndex = -1;
        private double _measuredItemWidth;
        private bool _isDisposed;
        #endregion

        /// <param name="_Margin">
        /// Zero by default, like <see cref="InventoryGridControl"/> and unlike most controls:
        /// the space around a slot is already in <see cref="UnscaledInset"/>, and a margin on
        /// top of it would push this square down against a grid placed beside it.
        /// </param>
        public ItemTypeSelectorControl(string _Name = "", double _Margin = 0)
            : base(_Name, _Size: null, Orientation.None, _Margin, _Padding: 0)
        {
            // The real slot control, so the frame, the hover ring and the item tooltip are the
            // ones the game draws rather than a second attempt at them. Its slot is a dummy: it
            // shows a type, it is not an end of any stack move.
            _slot = new ItemSlotControl(_Name: _Name + "_slot")
            {
                Slot = _preview
            };

            _slot.Clicked += (sender, e) => Toggle();

            Children.Add(_slot);

            _listBox = ContextMenuControl.CreateMenuBackground(_Name + "_typelist");
            _listBox.EnableVerticalScrollbar = true;

            _popup = new PopupHost(this, _listBox, _Name, ListBorderWidth / 2.0);
        }

        #region Types and selection
        /// <summary>
        /// Sets the types on offer. The picked type is kept when it is still among them and
        /// cleared otherwise, so refilling the list under a player does not silently pick
        /// something else for them.
        /// </summary>
        public void SetTypes(IEnumerable<ItemStack>? types)
        {
            CollectibleObject? previous = _selected?.Collectible;

            _types.Clear();

            if (types != null)
            {
                foreach (ItemStack stack in types)
                {
                    if (stack?.Collectible != null)
                    {
                        _types.Add(stack);
                    }
                }
            }

            RebuildEntries();

            _selectedIndex = -1;
            _selected = null;
            Select(IndexOfCollectible(previous), notify: false);
        }

        /// <summary>The same from collectibles, which is how a caller usually has them.</summary>
        public void SetTypes(IEnumerable<CollectibleObject>? collectibles)
        {
            var stacks = new List<ItemStack>();

            if (collectibles != null)
            {
                foreach (CollectibleObject collectible in collectibles)
                {
                    if (collectible?.Code != null)
                    {
                        stacks.Add(new ItemStack(collectible));
                    }
                }
            }

            SetTypes(stacks);
        }

        /// <summary>Picks by position; -1 clears the selection.</summary>
        public void Select(int index)
        {
            Select(index, notify: true);
        }

        /// <summary>Picks the type with this code, if it is on offer.</summary>
        public bool SelectByCode(AssetLocation? code)
        {
            if (code == null)
                return false;

            for (int i = 0; i < _types.Count; i++)
            {
                if (code.Equals(_types[i].Collectible?.Code))
                {
                    Select(i);
                    return true;
                }
            }

            return false;
        }

        private void Select(int index, bool notify)
        {
            if (index < -1 || index >= _types.Count)
                index = -1;

            if (index == _selectedIndex)
                return;

            _selectedIndex = index;
            _selected = index < 0 ? null : _types[index];

            // The square shows the pick. A dummy slot, so this is a display and not a move.
            _preview.Itemstack = _selected?.Clone();

            // The entries carry the "none" row at the top when it is on, so the highlight has to
            // be shifted by it.
            int entryIndex = index < 0 ? (AllowEmpty ? 0 : -1) : index + (AllowEmpty ? 1 : 0);

            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].SetSelected(i == entryIndex);
            }

            Dialog?.Refresh();

            if (notify)
            {
                SelectionChanged?.Invoke(this, new ItemTypeSelectedEventArgs(_selected));
            }
        }

        private int IndexOfCollectible(CollectibleObject? collectible)
        {
            if (collectible == null)
                return -1;

            for (int i = 0; i < _types.Count; i++)
            {
                if (_types[i].Collectible == collectible)
                    return i;
            }

            return -1;
        }

        private void RebuildEntries()
        {
            _listBox.Children.Clear();
            _entries.Clear();

            if (AllowEmpty)
            {
                var none = new DropdownItem(EmptyText, value: null);
                none.Clicked += (sender, e) => Pick(-1);
                _entries.Add(none);
            }

            for (int i = 0; i < _types.Count; i++)
            {
                int index = i;

                var entry = new DropdownItem(_types[i], value: _types[i].Collectible?.Code?.ToString());
                entry.Clicked += (sender, e) => Pick(index);

                _entries.Add(entry);
            }

            // Forced rather than left to Auto: a picker is an item list even when the "none"
            // entry is the only row, and that row has no icon.
            DropdownControl.FillListBox(_listBox, _entries, selectedIndex: -1, style: DropdownRowStyle.ItemList);

            _measuredItemWidth = DropdownControl.MeasureItemWidth(_entries, DropdownRowMetrics.ItemList);
        }

        private void Pick(int index)
        {
            Select(index, notify: true);
            Close();
        }
        #endregion

        #region Open / close
        public bool IsOpen => _popup.IsOpen;

        public void Open()
        {
            if (_isDisposed || _entries.Count == 0)
                return;

            DropdownControl.SizeListBox(
                _listBox,
                _entries.Count,
                DropdownRowMetrics.ItemList,
                _measuredItemWidth,
                MaxVisibleItems,
                MaxListHeight,
                minWidth: 0,
                availableHeight: DropdownControl.AvailableScreenHeight(this));

            _popup.Open();
        }

        public void Close()
        {
            _popup.Close();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }
        #endregion

        #region Layout
        /// <summary>One slot plus the room its ring needs, whatever else is going on.</summary>
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            double box = (ItemSlotControl.UnscaledSlotSize + UnscaledInset * 2) * LayoutScale;

            PointD measured = ClampToMaxSize(IsAutoSize
                ? new PointD(box, box)
                : ScaledExplicitSize);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            PlaceSlot();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            PlaceSlot();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            PlaceSlot();
        }

        /// <summary>
        /// The square keeps the size a slot draws itself at and sits in the middle of whatever
        /// box this control ended up with - centred rather than stretched, because
        /// <see cref="ItemSlotControl"/> draws one slot at scaled(48) no matter what layout size
        /// it is handed. Stretching it would move the frame away from the ring, the item and the
        /// caret without anything looking any bigger.
        /// </summary>
        private void PlaceSlot()
        {
            double size = ItemSlotControl.UnscaledSlotSize * LayoutScale;

            _slot.SetLayoutSize(new PointD(size, size));
            _slot.Position = new PointD(
                Position.X + (Size.X - size) / 2.0,
                Position.Y + (Size.Y - size) / 2.0);
        }
        #endregion

        #region Rendering
        /// <summary>
        /// The slot draws itself; this adds the caret that says the square opens something. A
        /// picker without it looks like an ordinary slot the player will try to drop items into.
        /// </summary>
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            base.GenerateRenderData(surface, ctx);

            double width = UnscaledCaretWidth * LayoutScale;
            double height = UnscaledCaretHeight * LayoutScale;
            double inset = UnscaledCaretInset * LayoutScale;

            // In the corner of the *slot*, not of the control: the control is a slot's width
            // plus the room for its ring, and a caret in that corner would float outside the
            // square it belongs to.
            double right = _slot.Position.X + _slot.Size.X - inset;
            double bottom = _slot.Position.Y + _slot.Size.Y - inset;

            ctx.Save();
            ctx.NewPath();
            ctx.LineTo(right - width, bottom - height);
            ctx.LineTo(right, bottom - height);
            ctx.LineTo(right - width / 2.0, bottom);
            ctx.ClosePath();

            ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.6);
            ctx.Fill();
            ctx.Restore();
        }
        #endregion

        #region Variants
        /// <summary>
        /// Every variant of a collectible, for a picker that offers one kind of thing in all its
        /// flavours - every rock, every plank.
        ///
        /// Variants in this game are codes that share everything before the first dash:
        /// <c>rock-granite</c> and <c>rock-andesite</c> are two variants of <c>rock</c>. That is
        /// a convention rather than a rule, so this is a convenience for the common case and not
        /// a definition - a caller who knows better passes its own list to
        /// <see cref="SetTypes(IEnumerable{ItemStack})"/>.
        /// </summary>
        public static List<ItemStack> CollectVariants(ICoreClientAPI capi, AssetLocation baseCode)
        {
            var stacks = new List<ItemStack>();

            if (capi == null || baseCode == null)
                return stacks;

            string prefix = BaseOf(baseCode.Path);

            foreach (Block block in capi.World.Blocks)
            {
                if (Matches(block?.Code, baseCode.Domain, prefix))
                {
                    stacks.Add(new ItemStack(block!));
                }
            }

            foreach (Item item in capi.World.Items)
            {
                if (Matches(item?.Code, baseCode.Domain, prefix))
                {
                    stacks.Add(new ItemStack(item!));
                }
            }

            return stacks;
        }

        private static bool Matches(AssetLocation? code, string domain, string prefix)
        {
            return code != null
                && code.Domain == domain
                && BaseOf(code.Path) == prefix;
        }

        private static string BaseOf(string path)
        {
            int dash = path.IndexOf('-');
            return dash < 0 ? path : path.Substring(0, dash);
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _popup.Dispose();
        }
        #endregion
    }
}
