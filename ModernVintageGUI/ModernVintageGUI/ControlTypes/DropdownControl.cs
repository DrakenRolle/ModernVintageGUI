using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using IS2Mod.Interfaces;
using ModernVintageGUI.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>How the rows of a dropdown are laid out.</summary>
    public enum DropdownRowStyle
    {
        /// <summary>
        /// Item lists get <see cref="ItemList"/>, everything else <see cref="Menu"/>. A list
        /// built from item stacks is an item list, and this saves saying so.
        /// </summary>
        Auto,

        /// <summary>Tight text rows, the way GuiElementListMenu draws a menu.</summary>
        Menu,

        /// <summary>
        /// The airy rows of the survival handbook's "Blocks and Items" list: a large icon, the
        /// name well clear of it, and room between the rows.
        /// </summary>
        ItemList
    }

    /// <summary>
    /// The measurements of one dropdown row, in author units.
    ///
    /// Both sets are taken from the game rather than chosen: <see cref="Menu"/> from
    /// GuiElementListMenu.ComposeDynamicElements, <see cref="ItemList"/> from
    /// GuiHandbookItemStackPage.RenderListEntryTo and the cell spacing of the list it sits in.
    /// </summary>
    public readonly struct DropdownRowMetrics
    {
        /// <summary>How tall a row is.</summary>
        public double RowHeight { get; init; }

        /// <summary>The gap between two rows. Half of it also sits above the first and below the last.</summary>
        public double RowSpacing { get; init; }

        /// <summary>The size an icon is drawn at.</summary>
        public double IconSize { get; init; }

        /// <summary>How far the icon sits from the left edge of the row.</summary>
        public double IconLeft { get; init; }

        /// <summary>Where the caption starts on a row that has an icon.</summary>
        public double TextLeft { get; init; }

        /// <summary>And on a row that has none.</summary>
        public double TextLeftWithoutIcon { get; init; }

        /// <summary>
        /// Keep the icon column even on rows that have nothing to put in it. True for an item
        /// list, where one iconless entry would otherwise pull its caption left and break the
        /// line the other rows form.
        /// </summary>
        public bool AlwaysIconColumn { get; init; }

        /// <summary>GuiElementListMenu: 30 high rows, text indented by 5, nothing between them.</summary>
        public static readonly DropdownRowMetrics Menu = new DropdownRowMetrics
        {
            RowHeight = 30.0,
            RowSpacing = 0.0,
            IconSize = ItemSlotControl.UnscaledItemSize,
            IconLeft = (30.0 - ItemSlotControl.UnscaledItemSize) / 2.0,
            TextLeft = 30.0,
            TextLeftWithoutIcon = 5.0
        };

        /// <summary>
        /// The handbook list. Its cell draws the stack at scaled(25) offset scaled(10) from the
        /// left and puts the name at <c>25 + scaled(25)</c>, and the list around it adds
        /// unscaledCellSpacing 10 plus 2 x UnscaledCellVerPadding 4 to every cell - which is
        /// where the height and the gap come from.
        /// </summary>
        public static readonly DropdownRowMetrics ItemList = new DropdownRowMetrics
        {
            RowHeight = 25.0 + 2 * 4.0,
            RowSpacing = 10.0,
            IconSize = 25.0,
            IconLeft = 10.0,
            TextLeft = 50.0,
            TextLeftWithoutIcon = 10.0,
            AlwaysIconColumn = true
        };
    }

    /// <summary>Which entry was picked, and where it sits in the list.</summary>
    public class DropdownSelectionEventArgs : EventArgs
    {
        /// <summary>The entry, or null when the selection was cleared.</summary>
        public DropdownItem? Item { get; }

        /// <summary>Its position in the list, or -1 when nothing is selected.</summary>
        public int Index { get; }

        /// <summary>Shorthand for the payload the caller attached to the entry.</summary>
        public object? Value => Item?.Value;

        public DropdownSelectionEventArgs(DropdownItem? item, int index)
        {
            Item = item;
            Index = index;
        }
    }

    /// <summary>
    /// A dropdown: a closed box showing the current selection, and a list that opens under it.
    ///
    /// The list is the same machinery a <see cref="ContextMenuControl"/> uses - its own
    /// <see cref="CustomDialogElement"/> in the overlay render band, dismissed by a click
    /// outside - because a list that is drawn inside the host dialog is clipped by it, and a
    /// dropdown near the bottom edge of a dialog is exactly where that shows.
    ///
    /// What it adds over a context menu is the part that makes it a dropdown: a selection that
    /// survives closing, a closed box that shows it, and entries that can carry an icon. An
    /// entry built from an <see cref="ItemStack"/> renders the stack as its icon and brings the
    /// game's own item tooltip with it - which is what an item picker needs and what a menu of
    /// text rows cannot give.
    /// </summary>
    public class DropdownControl : UIControl, IDisposable
    {
        #region Vanilla styling
        /// <summary>GuiElementDropDown draws its arrow button scaled(20) wide.</summary>
        private const double UnscaledArrowButtonWidth = 20.0;

        /// <summary>And fills the box itself with black at this alpha, radius 3.</summary>
        private const double BoxFillAlpha = 0.2;
        private const double BoxCornerRadius = 3.0;

        /// <summary>The triangle inside the arrow button, from ComposeDynamicElements.</summary>
        private const double UnscaledArrowHeight = 16.0;
        private const double UnscaledArrowInsetRight = 3.0;
        private const double UnscaledArrowWidth = 14.0;

        /// <summary>GuiStyle.SmallFontSize, what the list entries use.</summary>
        private const int FontSize = 16;

        /// <summary>GuiElementListMenu strokes its box with LineWidth 2.</summary>
        private const int ListBorderWidth = 2;
        #endregion

        #region Properties
        /// <summary>The entries, in list order. Replace them with <see cref="SetItems"/>.</summary>
        public IReadOnlyList<DropdownItem> Items => _items;

        /// <summary>
        /// The selected entry's position, or -1 for none. Setting it raises
        /// <see cref="SelectionChanged"/> exactly as a click on the entry would.
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => Select(value);
        }

        public DropdownItem? SelectedItem =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        /// <summary>The payload the caller attached to the selected entry.</summary>
        public object? SelectedValue => SelectedItem?.Value;

        /// <summary>The stack of the selected entry, for a dropdown built from item stacks.</summary>
        public ItemStack? SelectedStack => SelectedItem?.Stack;

        /// <summary>Shown in the closed box while nothing is selected.</summary>
        public string PlaceholderText
        {
            get => _placeholder;
            set
            {
                _placeholder = value ?? "";
                UpdateClosedBox();
            }
        }

        /// <summary>
        /// How many entries are shown before the list starts scrolling. Zero, the default, means
        /// no limit - the list is as long as it is.
        ///
        /// Whatever it is set to, the list is still cut down to what fits on the screen when it
        /// opens: a popup taller than the window cannot be placed anywhere sensible.
        /// </summary>
        public int MaxVisibleItems { get; set; }

        /// <summary>
        /// The same limit in author units, for a caller who thinks in height rather than in
        /// entries. Zero, the default, means no limit. Whichever of the two is stricter wins.
        /// </summary>
        public double MaxListHeight { get; set; }

        /// <summary>
        /// How the rows are laid out. <see cref="DropdownRowStyle.Auto"/> - the default - gives
        /// a list built from item stacks the handbook look and everything else the menu look.
        /// </summary>
        public DropdownRowStyle RowStyle
        {
            get => _rowStyle;
            set
            {
                if (_rowStyle == value)
                    return;

                _rowStyle = value;
                ApplyRowStyle();
                RecomposeToMain();
            }
        }

        /// <summary>
        /// Shade every other row a touch differently, so a wide row's caption and its icon can
        /// still be read as belonging together. On by default.
        ///
        /// Worth turning off only for a list of two or three rows, where the banding is a
        /// pattern without a job.
        /// </summary>
        public bool RowStriping
        {
            get => _rowStriping;
            set
            {
                if (_rowStriping == value)
                    return;

                _rowStriping = value;
                ApplyRowStyle();
                RecomposeToMain();
            }
        }

        public bool IsOpen => _popup.IsOpen;

        /// <summary>Raised when the selection changes, by click, by keyboard or from code.</summary>
        public event EventHandler<DropdownSelectionEventArgs>? SelectionChanged;
        #endregion

        #region Private fields
        private readonly List<DropdownItem> _items = new List<DropdownItem>();
        private readonly RectangleControl _listBox;
        private readonly TextLabelControl _label;

        private readonly PopupHost _popup;
        private int _selectedIndex = -1;
        private string _placeholder = "";
        private bool _isDisposed;
        private DropdownRowStyle _rowStyle = DropdownRowStyle.Auto;
        private bool _rowStriping = true;

        /// <summary>True while the cursor is on the closed box.</summary>
        private bool _isHovered;

        /// <summary>The metrics every row of this list is currently drawn with.</summary>
        private DropdownRowMetrics _metrics = DropdownRowMetrics.Menu;

        /// <summary>The widest entry, in author units. Recomputed only when the entries change.</summary>
        private double _measuredItemWidth;
        #endregion

        public DropdownControl(string _Name = "", PointD? _Size = null, double _Margin = 5)
            : base(_Name, _Size, Orientation.None, _Margin, _Padding: 0)
        {
            _label = new TextLabelControl(
                text: "",
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                padding: 0,
                _Name: _Name + "_label",
                _Margin: 0,
                _Padding: 0)
            {
                IsAutoSize = false
            };

            Children.Add(_label);

            _listBox = ContextMenuControl.CreateMenuBackground(_Name + "_list");
            _listBox.EnableVerticalScrollbar = true;

            // Half the list border stroke sits outside the box, so the panel leaves it that much
            // room - otherwise the popup surface clips it away.
            _popup = new PopupHost(this, _listBox, _Name, ListBorderWidth / 2.0);

            // The box is what the player operates, so it is what Tab lands on - not the label
            // inside it, and not the entries, which only exist while the list is open.
            IsFocusable = true;

            Clicked += (sender, e) => Toggle();

            // The box lights up under the cursor the way a button does. Without it the only
            // thing saying "this opens something" is the arrow, and a control that does not
            // react to the cursor at all reads as a read-only field.
            Enter += (sender, e) => SetHovered(true);
            Exit += (sender, e) => SetHovered(false);
        }

        #region Items and selection
        /// <summary>
        /// Replaces the entries. The selection is kept when the same entry is still in the list
        /// and cleared otherwise, so refilling a list under a player does not silently select
        /// something else for them.
        /// </summary>
        public void SetItems(IEnumerable<DropdownItem> items)
        {
            DropdownItem? previous = SelectedItem;

            foreach (DropdownItem item in _items)
            {
                item.OwnerDropdown = null;
            }

            _listBox.Children.Clear();
            _items.Clear();

            if (items != null)
            {
                foreach (DropdownItem item in items)
                {
                    item.OwnerDropdown = this;
                    _items.Add(item);
                    _listBox.Children.Add(item);
                }
            }

            ApplyRowStyle();

            _measuredItemWidth = MeasureWidestItem();

            int keep = previous == null ? -1 : _items.IndexOf(previous);
            _selectedIndex = -1;
            Select(keep, notify: false);

            SizeListBox();
            UpdateClosedBox();
            RecomposeToMain();
        }

        /// <summary>
        /// Works out which metrics this list is drawn with and hands them, the banding and the
        /// row numbers to every entry.
        ///
        /// All four are decisions a list makes *for* its rows - a row cannot count itself or
        /// know whether any of its neighbours has an icon - and all four are shared with the
        /// other list controls in <see cref="ListRowControl"/>, so a dropdown, a list view and a
        /// tree cannot drift apart on them.
        /// </summary>
        private void ApplyRowStyle()
        {
            _metrics = ListRowControl.ResolveMetrics(_rowStyle, _items);

            ListRowControl.AlignIconColumns(_items, _metrics);
            ListRowControl.NumberRows(_items, _rowStriping);
            ListRowControl.ApplyMetrics(_items, _metrics);
        }

        /// <summary>
        /// The list box a dropdown puts into its popup, filled the way the popup gets it.
        ///
        /// Public and static for the same reason
        /// <see cref="ContextMenuControl.CreateMenuBackground"/> is: the layout harness renders
        /// the documentation pictures without a client API, and a popup needs one - so the list
        /// has to be buildable on its own, or the picture and the real thing drift apart.
        /// </summary>
        public static RectangleControl CreateListBox(
            string name,
            IReadOnlyList<DropdownItem> items,
            int selectedIndex = -1,
            DropdownRowStyle style = DropdownRowStyle.Auto)
        {
            RectangleControl box = ContextMenuControl.CreateMenuBackground(name);
            FillListBox(box, items, selectedIndex, style);
            return box;
        }

        /// <summary>
        /// Puts the entries into a list box that already exists, giving them all the metrics of
        /// the chosen style. For a control that keeps its own box across openings - which is
        /// every control that opens one, because the box is what the panel is built around.
        /// </summary>
        public static void FillListBox(
            RectangleControl box,
            IReadOnlyList<DropdownItem> items,
            int selectedIndex = -1,
            DropdownRowStyle style = DropdownRowStyle.Auto)
        {
            box.Children.Clear();

            DropdownRowMetrics metrics = ListRowControl.ResolveMetrics(style, items);
            ListRowControl.AlignIconColumns(items, metrics);
            ListRowControl.NumberRows(items, striped: true);

            for (int i = 0; i < items.Count; i++)
            {
                items[i].Metrics = metrics;
                items[i].SetSelected(i == selectedIndex);
                box.Children.Add(items[i]);
            }
        }

        /// <summary>Selects by position; -1 clears the selection.</summary>
        public void Select(int index)
        {
            Select(index, notify: true);
        }

        /// <summary>Selects the first entry whose <see cref="DropdownItem.Value"/> matches.</summary>
        public bool SelectByValue(object? value)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (Equals(_items[i].Value, value))
                {
                    Select(i);
                    return true;
                }
            }

            return false;
        }

        private void Select(int index, bool notify)
        {
            if (index < -1 || index >= _items.Count)
                index = -1;

            if (index == _selectedIndex)
                return;

            _selectedIndex = index;

            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].SetSelected(i == index);
            }

            UpdateClosedBox();
            RecomposeToMain();

            if (notify)
            {
                SelectionChanged?.Invoke(this, new DropdownSelectionEventArgs(SelectedItem, index));
            }
        }

        /// <summary>Called by an entry when it is clicked.</summary>
        internal void OnItemPicked(DropdownItem item)
        {
            Select(_items.IndexOf(item));
            Close();
        }

        private void UpdateClosedBox()
        {
            DropdownItem? selected = SelectedItem;
            _label.Text = selected?.Text ?? _placeholder;
            Dialog?.Refresh();
        }
        #endregion

        #region Open / close
        public void Open()
        {
            if (_isDisposed || _items.Count == 0)
                return;

            // Sized before it is shown: the panel takes its size from what is inside it.
            SizeListBox();

            if (!_popup.Open())
                return;

            // The list opens on the current selection, so Up and Down carry on from what the
            // player is looking at rather than from the top of the list.
            SelectedItem?.TakeFocus();

            // The closed box draws itself differently while the list hangs under it - the arrow
            // turns over - and it lives in another dialog than the list, so nothing would
            // repaint it otherwise.
            Dialog?.Refresh();
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            _popup.Close();
            Dialog?.Refresh();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>
        /// Gives the list a size in author units rather than letting it grow: the width has to
        /// cover the widest entry *and* the closed box, and the height has to stop somewhere or
        /// a list of every block in the game runs off the screen.
        /// </summary>
        private void SizeListBox()
        {
            SizeListBox(
                _listBox,
                _items.Count,
                _metrics,
                _measuredItemWidth,
                MaxVisibleItems,
                MaxListHeight,
                minWidth: Size.X / Math.Max(LayoutScale, 0.0001),
                availableHeight: AvailableScreenHeight(this));
        }

        /// <summary>
        /// Sizes a list box in author units rather than letting it grow.
        ///
        /// Shared, because a dropdown is not the only thing that opens a list of these entries -
        /// <see cref="ItemTypeSelectorControl"/> opens the same one from a slot, and a list that
        /// is sized by a second copy of this arithmetic will disagree with the first one sooner
        /// or later.
        /// </summary>
        /// <param name="measuredItemWidth">The widest entry, from <see cref="MeasureItemWidth"/>.</param>
        /// <param name="minWidth">A width the list must not fall below - the box it hangs under.</param>
        /// <param name="availableHeight">What fits on the screen; see AvailableScreenHeight.</param>
        public static void SizeListBox(
            RectangleControl listBox,
            int itemCount,
            DropdownRowMetrics metrics,
            double measuredItemWidth,
            int maxVisibleItems,
            double maxListHeight,
            double minWidth,
            double availableHeight)
        {
            // A row occupies its height plus the gap the metrics ask for - the stacking layout
            // gives every child 2 x Margin, and Margin is half the gap.
            double rowPitch = metrics.RowHeight + metrics.RowSpacing;
            double content = itemCount * rowPitch;

            double limit = content;

            if (maxVisibleItems > 0)
            {
                limit = Math.Min(limit, maxVisibleItems * rowPitch);
            }

            if (maxListHeight > 0)
            {
                limit = Math.Min(limit, maxListHeight);
            }

            limit = Math.Min(limit, availableHeight);

            bool scrolls = content > limit + 0.001;

            double width = Math.Max(measuredItemWidth, minWidth);

            if (scrolls)
            {
                // The bar takes its strip out of the viewport, so the box has to be that much
                // wider or the widest entry loses its last few characters to it.
                width += ScrollbarStyle.UnscaledWidth;
            }

            listBox.Size = new PointD(width, limit);
            listBox.IsAutoSize = false;
        }

        /// <summary>
        /// How tall the list may be and still fit on the screen, in author units.
        ///
        /// This is not a policy like the other two limits, it is a fact about the window: the
        /// popup is placed at an absolute position, and one taller than the screen has nowhere
        /// to go. Without a client - the layout harness - there is no screen to ask, so nothing
        /// is capped.
        /// </summary>
        public static double AvailableScreenHeight(UIControl control)
        {
            ICoreClientAPI? capi = control.Dialog?.Api;

            if (capi == null)
                return double.MaxValue;

            double scale = Math.Max(control.LayoutScale, 0.0001);

            // A little air top and bottom, and room for the popup's own border padding.
            double margin = 40.0 * scale;

            return Math.Max(0, capi.Render.FrameHeight - margin) / scale;
        }

        #endregion

        #region Measuring
        /// <summary>
        /// The widest entry in author units, measured once per change instead of once per layout
        /// pass: this is a Cairo text measurement per entry, and an item type list runs to
        /// hundreds of entries.
        ///
        /// Measured at scale 1 through a label of its own - an unparented control keeps its own
        /// LayoutScale, so the result is in author units and stays valid when the GUI scale
        /// slider moves.
        /// </summary>
        private double MeasureWidestItem()
        {
            return MeasureItemWidth(_items, _metrics);
        }

        /// <summary>
        /// The width the widest of these entries needs, in author units. Kept as the name a
        /// caller already knows - <see cref="ItemTypeSelectorControl"/> opens the same kind of
        /// list - over the shared measurement in <see cref="ListRowControl.MeasureWidth"/>.
        /// </summary>
        public static double MeasureItemWidth(IReadOnlyList<DropdownItem> items, DropdownRowMetrics metrics)
        {
            return ListRowControl.MeasureWidth(items, metrics);
        }
        #endregion

        #region Layout
        /// <summary>
        /// Auto sizing takes the widest entry plus the arrow button, so the box does not change
        /// width when the player picks something else. A fixed size wins over that, as everywhere.
        /// </summary>
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured;

            if (IsAutoSize)
            {
                double width = (_measuredItemWidth + UnscaledArrowButtonWidth) * LayoutScale;
                double height = _metrics.RowHeight * LayoutScale;

                measured = ClampToMaxSize(new PointD(Math.Max(width, height), height));
            }
            else
            {
                measured = ClampToMaxSize(ScaledExplicitSize);
            }

            CalculatedSize = measured;
            SetLayoutSize(measured);

            StretchParts();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            StretchParts();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            StretchParts();
        }

        /// <summary>
        /// The label fills the box minus the arrow button, and minus the icon column when the
        /// selection has an icon - the icon itself is drawn per frame, so it is not a control.
        /// </summary>
        private void StretchParts()
        {
            double arrow = UnscaledArrowButtonWidth * LayoutScale;

            double textLeft = (SelectedItem?.HasIcon == true
                ? _metrics.TextLeft
                : _metrics.TextLeftWithoutIcon) * LayoutScale;

            _label.SetLayoutSize(new PointD(Math.Max(0, Size.X - arrow - textLeft), Size.Y));
            _label.Position = new PointD(Position.X + textLeft, Position.Y);
        }

        /// <summary>
        /// A dropdown is an atomic hit target: the label inside it must not become the hovered
        /// control, or the click never reaches the toggle.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }
        #endregion

        #region Rendering
        /// <summary>
        /// The closed box, straight out of GuiElementDropDown.ComposeElements: a black fill at
        /// 0.2 with an inward bevel, and the arrow button on the right in the dialog background
        /// colour with an outward one, tinted with the highlight colour and carrying a triangle.
        /// </summary>
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            double x = Position.X;
            double y = Position.Y;
            double width = Size.X;
            double height = Size.Y;

            if (width <= 0 || height <= 0)
                return;

            ctx.Save();

            ctx.SetSourceRGBA(0.0, 0.0, 0.0, BoxFillAlpha);
            GuiElement.RoundRectangle(ctx, x, y, width, height, BoxCornerRadius);
            ctx.Fill();

            // The cursor and the keyboard both lift the box, and the open list lifts it further
            // still - so a dropdown whose list is hanging under it does not look inert while the
            // player is reading the list.
            double lift = HighlightAlpha();

            if (lift > 0)
            {
                ctx.SetSourceRGBA(1.0, 1.0, 1.0, lift);
                GuiElement.RoundRectangle(ctx, x, y, width, height, BoxCornerRadius);
                ctx.Fill();
            }

            VanillaDraw.EmbossRoundRectangle(ctx, x, y, width, height, inverse: true, depth: 1, radius: 1);

            DrawArrowButton(ctx, x, y, width, height);

            ctx.Restore();

            base.GenerateRenderData(surface, ctx);
        }

        /// <summary>
        /// How much white is washed over the closed box: nothing at rest, a little for the
        /// keyboard focus, more under the cursor, and most while the list is open.
        /// </summary>
        private double HighlightAlpha()
        {
            if (IsOpen)
                return 0.14;

            if (_isHovered)
                return 0.09;

            return HasKeyboardFocus ? 0.05 : 0.0;
        }

        private void SetHovered(bool hovered)
        {
            if (_isHovered == hovered)
                return;

            _isHovered = hovered;
            Dialog?.Refresh();
        }

        private void DrawArrowButton(Context ctx, double x, double y, double width, double height)
        {
            double buttonWidth = Math.Min(UnscaledArrowButtonWidth * LayoutScale, width);
            double buttonX = x + width - buttonWidth;

            ctx.SetSourceRGB(
                GuiStyle.DialogDefaultBgColor[0],
                GuiStyle.DialogDefaultBgColor[1],
                GuiStyle.DialogDefaultBgColor[2]);
            GuiElement.RoundRectangle(ctx, buttonX, y, buttonWidth, height, GuiStyle.ElementBGRadius);
            ctx.FillPreserve();

            ctx.SetSourceRGBA(1.0, 1.0, 1.0, 0.1);
            ctx.Fill();

            VanillaDraw.EmbossRoundRectangle(ctx, buttonX, y, buttonWidth, height, inverse: false, depth: 2, radius: 1);

            ctx.SetSourceRGBA(GuiStyle.DialogHighlightColor);
            GuiElement.RoundRectangle(ctx, buttonX, y, buttonWidth, height, 1.0);
            ctx.Fill();

            // The triangle, centred vertically and inset from the right edge of the button.
            double triangleHeight = Math.Min(height - 6.0 * LayoutScale, UnscaledArrowHeight * LayoutScale);
            double top = y + (height - triangleHeight) / 2.0;
            double bottom = top + triangleHeight;
            double right = buttonX + buttonWidth - UnscaledArrowInsetRight * LayoutScale;
            double left = right - UnscaledArrowWidth * LayoutScale;

            ctx.NewPath();

            // It points at where the list is: down at a list that will open below, up at one
            // that is already open. The player then never has to remember whether the last
            // click opened or closed it.
            if (IsOpen)
            {
                ctx.LineTo(left, bottom);
                ctx.LineTo(right, bottom);
                ctx.LineTo((left + right) / 2.0, top);
            }
            else
            {
                ctx.LineTo(left, top);
                ctx.LineTo(right, top);
                ctx.LineTo((left + right) / 2.0, bottom);
            }

            ctx.ClosePath();

            ctx.SetSourceRGBA(1.0, 1.0, 1.0, IsOpen || _isHovered ? 0.85 : 0.6);
            ctx.Fill();
        }

        /// <summary>The icon of the current selection, which cannot go into a Cairo surface.</summary>
        public override void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            base.GenerateInteractiveRenderData(api, deltaTime);

            SelectedItem?.RenderIconAt(api, deltaTime, GetScreenPosition(), Size.Y, Dialog?.SurfaceRenderZ ?? 0, LayoutScale);
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

    /// <summary>
    /// One entry of a <see cref="DropdownControl"/>.
    ///
    /// A <see cref="ListRowControl"/> with two additions and nothing else: the payload the
    /// caller wants back out of the selection, and the part that makes it an entry rather than
    /// a row - clicking it picks it and closes the list it was opened from.
    /// </summary>
    public class DropdownItem : ListRowControl
    {
        /// <summary>Whatever the caller wants to get back out of the selection.</summary>
        public object? Value { get; }

        /// <summary>The dropdown this entry belongs to. Set when it is handed to SetItems.</summary>
        internal DropdownControl? OwnerDropdown { get; set; }

        /// <summary>A plain text entry, optionally with one of the game's GUI icons.</summary>
        public DropdownItem(string text, object? value = null, string? iconName = null)
            : this(text, value, iconName, null)
        {
        }

        /// <summary>
        /// An entry that stands for an item stack: the stack is its icon, its name is the
        /// caption unless one is given, and hovering it shows the game's item tooltip.
        /// </summary>
        public DropdownItem(ItemStack stack, object? value = null, string? text = null)
            : this(text ?? stack?.GetName() ?? "", value, null, stack)
        {
        }

        private DropdownItem(string text, object? value, string? iconName, ItemStack? stack)
            : base(text, iconName, stack)
        {
            Value = value;
        }

        /// <summary>
        /// A dropdown list is open because the player is picking from it, so the row under the
        /// cursor is the row Enter should take - which is what moving the focus with the cursor
        /// means here.
        /// </summary>
        protected override bool FollowsCursorWithFocus => true;

        protected override void OnActivated(MouseEventArgs e)
        {
            // The tooltip belongs to an entry that is about to disappear with the list.
            ItemTooltip.Announce(Dialog?.Api, TooltipSlot, entered: false);

            OwnerDropdown?.OnItemPicked(this);
        }
    }
}
