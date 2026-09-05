using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Custom;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using IS2Mod.Interfaces;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>
    /// One row of a list: an icon column, a caption, and the three states a row can be in.
    ///
    /// A dropdown entry, a list view row and a tree node are the same object to a player, and
    /// this is where that sameness lives - the metrics, the banding, the hover and the item
    /// tooltip are decided once here rather than three times. What each of them adds on top is
    /// small and specific: a dropdown entry closes its list when picked, a list view row fills a
    /// detail panel, a tree node draws an expander and indents itself.
    ///
    /// A row is an atomic hit target. Its parts - the caption, an overlay a subclass adds - must
    /// never become the hovered control, or the click, the highlight and the tooltip would go to
    /// the part instead of to the row.
    /// </summary>
    public abstract class ListRowControl : UIControl, IItemTooltipSource
    {
        /// <summary>GuiStyle.SmallFontSize, what list rows are written in.</summary>
        protected const int RowFontSize = 16;

        #region Properties
        public string Text
        {
            get => Label.Text;
            set
            {
                if (Label.Text == value)
                    return;

                Label.Text = value;
                RecomposeToMain();
            }
        }

        /// <summary>The stack this row stands for, if it was built from one.</summary>
        public ItemStack? Stack { get; }

        /// <summary>A vanilla GUI icon name, for rows that are not items.</summary>
        public string? IconName { get; }

        /// <inheritdoc/>
        public ItemSlot? TooltipSlot { get; }

        /// <summary>True when this row brings anything to put in the icon column.</summary>
        public bool HasIcon => Stack != null || IconName != null;

        /// <summary>Whether this row is the picked one of its list.</summary>
        public bool IsSelected { get; private set; }

        /// <summary>
        /// Where this row sits in its list, which is what decides the alternating shade. Set by
        /// the container; a row that is never given one lands on 0 and simply gets the even
        /// shade, which is the right answer for a row standing on its own.
        /// </summary>
        public int RowIndex { get; internal set; }

        /// <summary>Alternate the background shade with <see cref="RowIndex"/>.</summary>
        public bool ZebraStriping { get; set; } = true;

        /// <summary>Draw the hairline that separates this row from the next.</summary>
        public bool ShowSeparator { get; set; } = true;

        /// <summary>Whether the rows of this list reserve room for icons. Set by the container.</summary>
        internal bool ShowIconColumn
        {
            get => _showIconColumn;
            set
            {
                if (_showIconColumn == value)
                    return;

                _showIconColumn = value;
                RecomposeToMain();
            }
        }

        /// <summary>
        /// The measurements this row is drawn with. Set by the container so every row of a list
        /// agrees; a row outside a list keeps the menu metrics.
        /// </summary>
        internal DropdownRowMetrics Metrics
        {
            get => _metrics;
            set
            {
                _metrics = value;

                // The stacking layout gives a child size + 2 x margin, so half the gap on each
                // row is the gap between two of them - and half of it above the first and below
                // the last, which is what the handbook list does too.
                Margin = value.RowSpacing / 2.0;

                RecomposeToMain();
            }
        }
        #endregion

        #region Parts
        /// <summary>The caption. Owned here, placed by <see cref="PlaceParts"/>.</summary>
        protected TextLabelControl Label { get; }

        private DropdownRowMetrics _metrics = DropdownRowMetrics.Menu;
        private bool _showIconColumn;
        private bool _isHovered;
        private bool _isFocused;
        #endregion

        protected ListRowControl(string text, string? iconName = null, ItemStack? stack = null)
            : base(_Orientation: Orientation.None, _Margin: 0, _Padding: 0)
        {
            IconName = iconName;
            Stack = stack;

            // A slot of its own rather than a borrowed one: the tooltip only reads the stack out
            // of it, and a row in a list does not belong to any inventory.
            TooltipSlot = stack == null ? null : new DummySlot(stack);

            Label = new TextLabelControl(
                text: text ?? "",
                fontName: GuiStyle.StandardFontName,
                fontSize: RowFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                padding: 0,
                _Name: text + "_label",
                _Margin: 0,
                _Padding: 0);

            Children.Add(Label);

            IsFocusable = true;

            Enter += OnRowEnter;
            Exit += OnRowExit;
            Clicked += OnRowClicked;
            GotFocus += OnRowGotFocus;
            LostFocus += OnRowLostFocus;
        }

        #region State
        /// <summary>Marks this row as the picked one of its list, or unmarks it.</summary>
        public void SetSelected(bool selected)
        {
            if (IsSelected == selected)
                return;

            IsSelected = selected;
            Dialog?.Refresh();
        }

        /// <summary>Gives this row the keyboard focus of its dialog.</summary>
        public void TakeFocus()
        {
            Dialog?.FocusControl(this);
        }

        /// <summary>What the row currently looks like, for <see cref="ListRowStyle"/>.</summary>
        protected ListRowState RowState
        {
            get
            {
                ListRowState state = ListRowState.None;

                if (IsSelected)
                    state |= ListRowState.Selected;

                if (_isFocused)
                    state |= ListRowState.Focused;

                if (_isHovered)
                    state |= ListRowState.Hovered;

                return state;
            }
        }
        #endregion

        #region Layout
        /// <summary>
        /// Where the caption starts, in author units. A tree node adds its indent here, which is
        /// what makes one override move the text, the icon and the hit test together.
        /// </summary>
        protected virtual double TextLeft =>
            _showIconColumn ? _metrics.TextLeft : _metrics.TextLeftWithoutIcon;

        /// <summary>
        /// The air the metrics leave at the right hand end of a row, in author units. What a row
        /// puts there - a second column - keeps this much clear of the frame, and of the
        /// scrollbar, which is where the right edge of a scrolling list actually is.
        /// </summary>
        protected double RightInset => _metrics.TextLeftWithoutIcon;

        /// <summary>
        /// The strip this row owns, gaps included - what <see cref="ListRowStyle"/> paints.
        ///
        /// The bleed into the gap is what makes an item row work: a stack is not drawn at the
        /// size it is asked for - a block's cube reaches past it - so a band that stopped at the
        /// row would have icons hanging over its edge. Every row takes half the gap on each
        /// side, so two lit rows meet exactly and never overlap.
        /// </summary>
        protected LayoutRect RowBand()
        {
            double bleed = ScaledMargin;

            return new LayoutRect(
                Position.X - bleed,
                Position.Y - bleed,
                Size.X + bleed * 2,
                Size.Y + bleed * 2);
        }

        /// <summary>
        /// A row is an atomic hit target. Without this the hit test would descend into the
        /// caption, and that would take the Enter, Exit and Clicked meant for the row - so it
        /// would never light up, never show a tooltip and never fire.
        /// </summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured = ClampToMaxSize(new PointD(
                Label.Size.X + ExtraWidth + (TextLeft + _metrics.TextLeftWithoutIcon) * LayoutScale,
                Math.Max(Label.Size.Y, _metrics.RowHeight * LayoutScale)));

            CalculatedSize = measured;
            SetLayoutSize(measured);

            PlaceParts();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            PlaceParts();
            base.NormalizeChildrenByDelta();
        }

        public override void CalculateAllPositions()
        {
            base.CalculateAllPositions();
            PlaceParts();
        }

        /// <summary>
        /// What a subclass's own parts need next to the caption, in device pixels. Counted into
        /// the measured width, so a row with a second column still measures wide enough for both
        /// of them when the list around it is auto sizing or scrolls sideways.
        /// </summary>
        protected virtual double ExtraWidth => 0;

        /// <summary>
        /// Puts the parts where the metrics say they go. Overridden by a row that has more than
        /// a caption - a second column, an expander - which must call the base first.
        /// </summary>
        protected virtual void PlaceParts()
        {
            double textLeft = TextLeft * LayoutScale;

            Label.SetLayoutSize(new PointD(Math.Max(0, Size.X - textLeft), Size.Y));
            Label.Position = new PointD(Position.X + textLeft, Position.Y);
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            ListRowStyle.DrawRow(
                ctx,
                RowBand(),
                RowIndex,
                RowState,
                LayoutScale,
                striped: ZebraStriping,
                separator: ShowSeparator);

            DrawRowContent(surface, ctx);

            base.GenerateRenderData(surface, ctx);

            DrawNamedIcon(ctx);
        }

        /// <summary>
        /// Anything a subclass draws between the band and the caption - a tree's expander. Empty
        /// here rather than abstract, because most rows have nothing to add.
        /// </summary>
        protected virtual void DrawRowContent(ImageSurface surface, Context ctx)
        {
        }

        /// <summary>
        /// A named GUI icon is Cairo and belongs in the surface; a stack cannot be and is drawn
        /// per frame in <see cref="GenerateInteractiveRenderData"/>.
        /// </summary>
        private void DrawNamedIcon(Context ctx)
        {
            if (IconName == null || !_showIconColumn)
                return;

            ICoreClientAPI? api = Dialog?.Api;
            if (api == null)
                return;

            double size = _metrics.IconSize * LayoutScale;

            api.Gui.Icons.DrawIcon(
                ctx,
                IconName,
                Position.X + IconLeft * LayoutScale,
                Position.Y + (Size.Y - size) / 2.0,
                size,
                size,
                GuiStyle.DialogDefaultTextColor);
        }

        /// <summary>
        /// Where the icon column starts, in author units. Follows <see cref="TextLeft"/> so an
        /// indented row moves its icon along with its caption.
        /// </summary>
        protected virtual double IconLeft =>
            _metrics.IconLeft + (TextLeft - (_showIconColumn ? _metrics.TextLeft : _metrics.TextLeftWithoutIcon));

        public override void GenerateInteractiveRenderData(ICoreClientAPI api, float deltaTime)
        {
            base.GenerateInteractiveRenderData(api, deltaTime);

            if (!_showIconColumn)
                return;

            RenderIconAt(api, deltaTime, GetScreenPosition(), Size.Y, Dialog?.SurfaceRenderZ ?? 0, LayoutScale);
        }

        /// <summary>
        /// How big a stack may be drawn in a row of this height.
        ///
        /// A stack is not drawn at the size it is given: the model is projected into the box and
        /// a long item - a wand, a spear - or the corner of a block cube reaches well past it.
        /// The game's own answer to "what fits in a box of height H" is the inventory slot,
        /// which draws its stack at 25.6 in a 48 box, so that ratio is used here rather than an
        /// absolute size. A row is then free to be any height and the icon still fits in it.
        /// </summary>
        internal static double StackSizeFor(double rowHeight)
        {
            return rowHeight * (ItemSlotControl.UnscaledItemSize / ItemSlotControl.UnscaledSlotSize);
        }

        /// <summary>
        /// Draws the stack in the icon column of a row that starts at <paramref name="origin"/>.
        /// Also used by a control that shows the icon of the current selection without owning a
        /// copy of the row - the closed box of a dropdown.
        /// </summary>
        /// <param name="layoutScale">
        /// The scale of the tree that is drawing, not this row's own. They are the same in an
        /// open list and they are not in a closed box: until the list has been opened once the
        /// rows hang off no dialog at all, so their LayoutScale is still the default 1 while the
        /// dialog around the box is at whatever the GUI slider says.
        /// </param>
        internal void RenderIconAt(
            ICoreClientAPI api,
            float deltaTime,
            PointD origin,
            double rowHeight,
            float surfaceZ,
            double layoutScale)
        {
            if (TooltipSlot?.Itemstack == null)
                return;

            double size = StackSizeFor(rowHeight);

            // Centred in the strip that was left for it - the same strip the caption is indented
            // by, so icon and text cannot collide however the row is sized.
            double strip = (_showIconColumn ? _metrics.TextLeft : 0) * layoutScale;

            api.Render.RenderItemstackToGui(
                TooltipSlot,
                origin.X + strip / 2.0,
                origin.Y + rowHeight / 2.0,
                surfaceZ + CustomDialogElement.SlotItemZOffset,
                (float)size,
                ColorUtil.WhiteArgb,
                deltaTime,
                shading: true,
                rotate: false,

                // A row stands for a *type*, not for an amount - the handbook list leaves the
                // number off for the same reason, and a "1" printed on every row is noise.
                showStackSize: false);
        }
        #endregion

        #region Interaction
        /// <summary>
        /// Hover, keyboard focus and selection are three independent reasons for a row to be
        /// lit, and a row can be in all three - so they are tracked apart and
        /// <see cref="ListRowStyle"/> decides which one wins. Writing a colour straight from the
        /// Enter handler would mean the cursor leaving a row unlights the one the keyboard or
        /// the selection is on.
        /// </summary>
        private void OnRowEnter(object? sender, MouseEventArgs e)
        {
            _isHovered = true;
            Dialog?.Refresh();

            // The tooltip of the game, for a row that stands for an item.
            ItemTooltip.Announce(Dialog?.Api, TooltipSlot, entered: true);

            // Hovering moves the keyboard selection too, the way menus work everywhere -
            // otherwise Enter picks the row the player is not looking at.
            if (FollowsCursorWithFocus)
            {
                Dialog?.FocusControl(this);
            }
        }

        /// <summary>
        /// Whether the cursor arriving on this row also moves the keyboard focus to it.
        ///
        /// True for a list that is open *because* the player is picking from it - a dropdown, a
        /// menu - where the focus is the pick and following the cursor is what everyone expects.
        /// False for a list that is one control among many on a dialog: dragging the cursor
        /// across it while filling in a form would silently move the focus out of the field the
        /// player is typing in.
        /// </summary>
        protected virtual bool FollowsCursorWithFocus => false;

        private void OnRowExit(object? sender, MouseEventArgs e)
        {
            _isHovered = false;
            Dialog?.Refresh();

            ItemTooltip.Announce(Dialog?.Api, TooltipSlot, entered: false);
        }

        private void OnRowGotFocus(object? sender, EventArgs e)
        {
            _isFocused = true;
            Dialog?.Refresh();
        }

        private void OnRowLostFocus(object? sender, EventArgs e)
        {
            _isFocused = false;
            Dialog?.Refresh();
        }

        private void OnRowClicked(object? sender, MouseEventArgs e)
        {
            OnActivated(e);
        }

        /// <summary>
        /// The row was clicked, or Enter was pressed on it. What that means is the subclass's
        /// business: picking an entry, opening a detail panel, expanding a node.
        /// </summary>
        protected abstract void OnActivated(MouseEventArgs e);

        /// <summary>
        /// Turns an event's screen coordinates into coordinates inside this row. A row that
        /// reacts differently to a click on one part of itself - a tree's expander - needs this,
        /// because an atomic hit target gets the whole row's clicks and has to sort them out.
        /// </summary>
        protected PointD ToLocal(MouseEventArgs e)
        {
            PointD dialogPosition = Dialog?.Position ?? new PointD(0, 0);

            return new PointD(
                e.X - dialogPosition.X - Position.X,
                e.Y - dialogPosition.Y - Position.Y);
        }
        #endregion

        #region List wide decisions
        // The four things a list has to decide *for* its rows, because a row cannot decide them
        // for itself: it does not know how many others there are, whether any of them has an
        // icon, or where in the column it sits. They are static and typed on the base rather
        // than on any one list, so a dropdown, a list view and a tree cannot drift apart on
        // them - IReadOnlyList is covariant, so a List<DropdownItem> goes straight in.

        /// <summary>
        /// Which metrics a list is drawn with. <see cref="DropdownRowStyle.Auto"/> gives a list
        /// that has an item stack anywhere in it the handbook's roomy rows and everything else
        /// the tight rows of a menu.
        /// </summary>
        public static DropdownRowMetrics ResolveMetrics(
            DropdownRowStyle style, IReadOnlyList<ListRowControl> rows)
        {
            if (style == DropdownRowStyle.Menu)
                return DropdownRowMetrics.Menu;

            if (style == DropdownRowStyle.ItemList)
                return DropdownRowMetrics.ItemList;

            foreach (ListRowControl row in rows)
            {
                if (row.Stack != null)
                    return DropdownRowMetrics.ItemList;
            }

            return DropdownRowMetrics.Menu;
        }

        /// <summary>
        /// One row with an icon gives every row the icon column, so the captions line up instead
        /// of stepping in and out down the list - and an item list keeps the column whether
        /// anything is in it or not.
        /// </summary>
        public static void AlignIconColumns(
            IReadOnlyList<ListRowControl> rows, DropdownRowMetrics metrics)
        {
            bool column = metrics.AlwaysIconColumn;

            if (!column)
            {
                foreach (ListRowControl row in rows)
                {
                    if (row.HasIcon)
                    {
                        column = true;
                        break;
                    }
                }
            }

            foreach (ListRowControl row in rows)
            {
                row.ShowIconColumn = column;
            }
        }

        /// <summary>
        /// Tells every row where it sits, which is what the alternating shade is worked out
        /// from, and whether that shade is wanted at all.
        ///
        /// Handing the position down from the list is also what keeps the banding stable while
        /// rows are hovered, selected and scrolled past: it depends on the list, not on the row.
        /// </summary>
        public static void NumberRows(IReadOnlyList<ListRowControl> rows, bool striped)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].RowIndex = i;
                rows[i].ZebraStriping = striped;

                // No line under the last row - it would sit on the frame of the list box and
                // read as a second, doubled border.
                rows[i].ShowSeparator = i < rows.Count - 1;
            }
        }

        /// <summary>Hands the metrics to every row, so a list cannot end up with two row heights.</summary>
        public static void ApplyMetrics(
            IReadOnlyList<ListRowControl> rows, DropdownRowMetrics metrics)
        {
            foreach (ListRowControl row in rows)
            {
                row.Metrics = metrics;
            }
        }

        /// <summary>
        /// The width the widest of these rows needs, in author units.
        ///
        /// Measured at scale 1 through a label of its own - an unparented control keeps its own
        /// LayoutScale - so the result stays valid when the GUI scale slider moves. It is worth
        /// doing once per change rather than once per layout pass: this is a Cairo text
        /// measurement per row, and an item type list runs to hundreds of them.
        /// </summary>
        public static double MeasureWidth(
            IReadOnlyList<ListRowControl> rows, DropdownRowMetrics metrics)
        {
            if (rows.Count == 0)
                return 0;

            var ruler = new TextLabelControl(
                text: "",
                fontName: GuiStyle.StandardFontName,
                fontSize: RowFontSize,
                orientation: TextOrientation.MiddleLeft,
                padding: 0)
            {
                LayoutScale = 1.0
            };

            double widest = 0;

            foreach (ListRowControl row in rows)
            {
                ruler.Text = row.Text;
                widest = Math.Max(widest, ruler.CalculateSize().X);
            }

            bool anyIcon = rows[0].ShowIconColumn;

            // What sits left of the caption, plus the same again on the right so the widest row
            // does not end flush against the frame.
            double left = anyIcon ? metrics.TextLeft : metrics.TextLeftWithoutIcon;

            return left + widest + metrics.TextLeftWithoutIcon;
        }
        #endregion
    }
}
