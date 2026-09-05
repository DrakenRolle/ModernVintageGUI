using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.ControlTypes.Events;
using IS2Mod.Enums;
using IS2Mod.Interfaces;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>Where the details of the clicked row are shown.</summary>
    public enum ListViewDetailMode
    {
        /// <summary>Nowhere. Clicking a row selects it and raises the events, nothing opens.</summary>
        None,

        /// <summary>
        /// Inside the list, folded out under the row it belongs to - the row details of a
        /// DataGrid. The default.
        ///
        /// The panel is a child of the list like the rows are, so it pushes the rows below it
        /// down, scrolls with them, and is clipped at the same edge. Nothing floats over
        /// anything and the details cannot end up somewhere else on the screen than the row they
        /// describe.
        /// </summary>
        Inline,

        /// <summary>
        /// In <see cref="ListViewControl.DetailView"/>, which the caller has put somewhere in
        /// its own layout - beside the list, under it, on another tab. The list only fills it.
        /// For a master-detail screen, where the panel stands still while the list is browsed.
        /// </summary>
        Attached
    }

    /// <summary>Which row was picked, and where it sits in the list.</summary>
    public class ListViewSelectionEventArgs : EventArgs
    {
        /// <summary>The row, or null when the selection was cleared.</summary>
        public ListViewItem? Item { get; }

        /// <summary>Its position in the list, or -1 when nothing is selected.</summary>
        public int Index { get; }

        /// <summary>Shorthand for the payload the caller attached to the row.</summary>
        public object? Value => Item?.Value;

        public ListViewSelectionEventArgs(ListViewItem? item, int index)
        {
            Item = item;
            Index = index;
        }
    }

    /// <summary>
    /// A column of rows the player can scroll, pick from and open.
    ///
    /// It is a scrolling container first - it *is* an <see cref="IScrollable"/>, inherited whole
    /// from <see cref="RectangleControl"/> rather than reimplemented, so the wheel, the drag,
    /// the clipping and the vanilla scrollbar are the ones every other container has - and a
    /// list second: it owns its rows, keeps one of them selected, and hands the clicked one to a
    /// <see cref="DetailViewControl"/>.
    ///
    /// The difference from a dropdown is where it lives, and it is the whole difference: a
    /// dropdown's list exists only while it is open and disappears when something is picked,
    /// while this one stands on the dialog and is the thing the player works in.
    ///
    /// Clicking a row folds its details out under it, the way a DataGrid shows row details: the
    /// panel is a child of the list between two rows, so it pushes what is below it down and is
    /// scrolled and clipped with everything else. Clicking the same row again folds it back in.
    ///
    /// <code>
    /// var list = new ListViewControl();
    /// list.SetItems(new[]
    /// {
    ///     new ListViewItem("Granite") { Description = "A hard rock." },
    ///     new ListViewItem("Chalk")   { Description = "A soft one." }
    /// });
    /// </code>
    /// </summary>
    public class ListViewControl : RectangleControl, IScrollable
    {
        #region Styling
        /// <summary>What the list is sized to when the caller says nothing, in author units.</summary>
        public const double UnscaledDefaultWidth = 240.0;
        public const double UnscaledDefaultHeight = 200.0;

        /// <summary>GuiElementListMenu strokes its box with LineWidth 2.</summary>
        private const int ListBorderWidth = 2;
        #endregion

        #region Properties
        /// <summary>The rows, in list order. Replace them with <see cref="SetItems"/>.</summary>
        public IReadOnlyList<ListViewItem> Items => _items;

        /// <summary>
        /// The picked row's position, or -1 for none. Setting it raises
        /// <see cref="SelectionChanged"/> exactly as a click on the row would.
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => Select(value, notify: true);
        }

        public ListViewItem? SelectedItem =>
            _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        /// <summary>The payload the caller attached to the picked row.</summary>
        public object? SelectedValue => SelectedItem?.Value;

        /// <summary>
        /// How the rows are laid out. <see cref="DropdownRowStyle.Auto"/> - the default - gives a
        /// list built from item stacks the handbook look and everything else the menu look.
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

        /// <summary>Shade every other row a touch differently. On by default.</summary>
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

        /// <summary>
        /// The panel the clicked row is shown in. It exists from the start and is the same
        /// object in both modes that use one - what changes is who puts it on screen.
        ///
        /// In <see cref="ListViewDetailMode.Inline"/> that is this list, which folds it into
        /// itself under the picked row; adding it to a tree as well would put one control in two
        /// places at once. In <see cref="ListViewDetailMode.Attached"/> it is the caller: add it
        /// wherever it belongs and the list will only fill it.
        /// </summary>
        public DetailViewControl DetailView { get; }

        /// <summary>Where the details of the clicked row are shown.</summary>
        public ListViewDetailMode DetailMode
        {
            get => _detailMode;
            set
            {
                if (_detailMode == value)
                    return;

                _detailMode = value;

                // The panel is shaped by where it goes: folded into the list it grows and is
                // scrolled by the list, standing on its own it keeps a size and scrolls itself.
                ApplyDetailStyle();

                UpdateInlineDetails();
                RecomposeToMain();
            }
        }

        /// <summary>
        /// Whether the details are currently folded out. Always false in
        /// <see cref="ListViewDetailMode.None"/>.
        /// </summary>
        public bool AreDetailsOpen => _detailsOpen && _detailMode != ListViewDetailMode.None;

        /// <summary>
        /// Whether clicking the row whose details are already open folds them back in. On by
        /// default, which is what makes the row a toggle.
        ///
        /// Off gives the DataGrid's own rule instead - the details belong to whatever is
        /// selected and are only ever exchanged, never closed - which is what a screen wants
        /// where the panel must never be empty.
        /// </summary>
        public bool ToggleDetailsOnReclick { get; set; } = true;

        /// <summary>Raised when the picked row changes, by click, by keyboard or from code.</summary>
        public event EventHandler<ListViewSelectionEventArgs>? SelectionChanged;

        /// <summary>
        /// Raised when a row is clicked or Entered - which is also when the detail view is
        /// filled. Fires even when the same row is clicked again, unlike
        /// <see cref="SelectionChanged"/>: reopening the details of the row that is already
        /// picked is a thing a player does on purpose.
        /// </summary>
        public event EventHandler<ListViewSelectionEventArgs>? ItemActivated;
        #endregion

        #region Private fields
        private readonly List<ListViewItem> _items = new List<ListViewItem>();

        private DropdownRowStyle _rowStyle = DropdownRowStyle.Auto;
        private DropdownRowMetrics _metrics = DropdownRowMetrics.Menu;
        private ListViewDetailMode _detailMode = ListViewDetailMode.Inline;
        private bool _rowStriping = true;
        private bool _detailsOpen;
        private int _selectedIndex = -1;
        #endregion

        public ListViewControl(string _Name = "", double _Margin = 5)
            : base(
                borderWidth: ListBorderWidth,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.5),
                backgroundColor: new ElementColor(GuiStyle.DialogStrongBgColor),
                _Name: _Name,
                _Margin: _Margin,
                _Padding: 0)
        {
            InsideOrientation = Orientation.Top;

            // A size, not auto sizing: a list that grows to fit its rows has nothing left to
            // scroll, and scrolling is the point of it.
            Size = new PointD(UnscaledDefaultWidth, UnscaledDefaultHeight);
            IsAutoSize = false;
            EnableVerticalScrollbar = true;

            DetailView = new DetailViewControl(_Name + "_details");

            ApplyDetailStyle();
        }

        /// <summary>
        /// Shapes the detail panel for where it is going.
        ///
        /// Folded into the list it grows to its content, has no bar of its own and sits on a
        /// darker ground so it reads as opened out of the row rather than as a second list. On
        /// its own it is a panel: a size, a frame, and its own scrollbar for a long description.
        /// </summary>
        private void ApplyDetailStyle()
        {
            bool inline = _detailMode == ListViewDetailMode.Inline;

            DetailView.AutoHeight = inline;

            DetailView.BackgroundColor = inline
                ? new ElementColor(0.0, 0.0, 0.0, 0.25)
                : new ElementColor(GuiStyle.DialogStrongBgColor);
        }

        #region Items
        /// <summary>
        /// Replaces the rows. The selection is kept when the same row is still in the list and
        /// cleared otherwise, so refilling a list under a player does not silently pick
        /// something else for them.
        /// </summary>
        public void SetItems(IEnumerable<ListViewItem>? items)
        {
            ListViewItem? previous = SelectedItem;

            foreach (ListViewItem item in _items)
            {
                item.OwnerList = null;
            }

            // The panel goes with them. It is put back under the picked row further down, if
            // that row survived the replacement.
            Children.Clear();
            _items.Clear();

            if (items != null)
            {
                foreach (ListViewItem item in items)
                {
                    if (item == null)
                        continue;

                    item.OwnerList = this;
                    _items.Add(item);
                    Children.Add(item);
                }
            }

            ApplyRowStyle();

            int keep = previous == null ? -1 : _items.IndexOf(previous);
            _selectedIndex = -1;
            Select(keep, notify: false);

            // A selection that did not survive takes its open details with it: a panel left
            // standing would describe a row that is no longer in the list.
            if (keep < 0)
            {
                _detailsOpen = false;
            }

            UpdateInlineDetails();

            RecomposeToMain();
        }

        /// <summary>Adds one row to the end of the list.</summary>
        public ListViewItem AddItem(ListViewItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            item.OwnerList = this;
            _items.Add(item);

            // At the end of the rows, which is not the end of the children when the details are
            // folded out somewhere above - so the row goes in front of the panel rather than
            // after it.
            Children.Add(item);
            UpdateInlineDetails();

            ApplyRowStyle();
            RecomposeToMain();

            return item;
        }

        /// <summary>Adds a plain caption row, the everyday case.</summary>
        public ListViewItem AddItem(string text, object? value = null)
        {
            return AddItem(new ListViewItem(text, value));
        }

        /// <summary>Empties the list and clears the selection.</summary>
        public void Clear()
        {
            SetItems(null);
        }

        private void ApplyRowStyle()
        {
            _metrics = ListRowControl.ResolveMetrics(_rowStyle, _items);

            ListRowControl.AlignIconColumns(_items, _metrics);
            ListRowControl.NumberRows(_items, _rowStriping);
            ListRowControl.ApplyMetrics(_items, _metrics);
        }
        #endregion

        #region Selection
        /// <summary>Picks by position; -1 clears the selection.</summary>
        public void Select(int index)
        {
            Select(index, notify: true);
        }

        /// <summary>Picks the first row whose <see cref="ListViewItem.Value"/> matches.</summary>
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

            // Open details follow the selection, wherever it was moved from - a click, the
            // keyboard, or a mod restoring what the player had last time. A panel that stayed
            // under the old row would be describing something else than what is picked.
            if (_detailsOpen)
            {
                if (SelectedItem == null)
                {
                    _detailsOpen = false;
                }
                else
                {
                    FillDetails(DetailView, SelectedItem);
                }

                UpdateInlineDetails();
            }

            Dialog?.Refresh();

            if (notify)
            {
                SelectionChanged?.Invoke(this, new ListViewSelectionEventArgs(SelectedItem, index));
            }
        }

        /// <summary>
        /// Scrolls until the row at <paramref name="index"/> is inside the viewport, and no
        /// further - a list that jumped the picked row to the middle would throw away the
        /// context around it that the player was just reading.
        /// </summary>
        public void EnsureVisible(int index)
        {
            if (index < 0 || index >= _items.Count)
                return;

            ListViewItem row = _items[index];

            double top;
            double height;

            if (row.Size.Y > 0)
            {
                // Where the row sits in the content, from the layout: its position is already
                // shifted by the scrolling, so adding the offset back gives the unscrolled one.
                // This is the only answer that survives an open detail panel above the row,
                // which the row pitch alone knows nothing about.
                top = row.Position.Y - ContentBox().Y + ScrollOffset.Y;
                height = row.Size.Y;
            }
            else
            {
                // Never laid out - the caller is restoring a selection before the dialog is on
                // screen. The pitch is all there is, and it is right for a list with no panel
                // folded out, which is what a list that has not been shown yet is.
                height = (_metrics.RowHeight + _metrics.RowSpacing) * LayoutScale;
                top = index * height;

                if (height <= 0)
                    return;
            }

            double viewport = ViewportSize.Y;

            if (top < ScrollOffset.Y)
            {
                ScrollTo(ScrollOffset.X, top);
            }
            else if (top + height > ScrollOffset.Y + viewport)
            {
                ScrollTo(ScrollOffset.X, top + height - viewport);
            }
        }

        /// <summary>
        /// Called by a row when it is clicked or Entered: it becomes the selection, and its
        /// details fold out under it - or fold back in, when they were already open on that very
        /// row and <see cref="ToggleDetailsOnReclick"/> allows it.
        /// </summary>
        internal void OnRowActivated(ListViewItem item)
        {
            int index = _items.IndexOf(item);

            bool wasOpenHere = _detailsOpen && ReferenceEquals(SelectedItem, item);

            // Select first: the details are placed under the *selected* row, and moving the
            // selection carries them along.
            Select(index, notify: true);

            if (wasOpenHere && ToggleDetailsOnReclick)
            {
                CloseDetails();
            }
            else
            {
                ShowDetails(item);
            }

            ItemActivated?.Invoke(this, new ListViewSelectionEventArgs(item, index));
        }
        #endregion

        #region Details
        /// <summary>
        /// Fills the detail view from a row and folds it out under it. The row is picked first,
        /// because that is where the panel goes.
        ///
        /// Public so a caller can open the details of a row it chose from code - restoring a
        /// saved selection, say - without faking a click.
        /// </summary>
        public void ShowDetails(ListViewItem? item)
        {
            if (_detailMode == ListViewDetailMode.None || item == null)
                return;

            int index = _items.IndexOf(item);

            if (index < 0)
                return;

            Select(index, notify: true);

            FillDetails(DetailView, item);

            _detailsOpen = true;

            UpdateInlineDetails();
            RecomposeToMain();
        }

        /// <summary>
        /// Puts a row into the detail view. Overridden by a list that knows more about its rows
        /// than their captions - see <see cref="ItemListViewControl"/>, which reads the name,
        /// the description and the facts out of the item stack itself.
        /// </summary>
        protected virtual void FillDetails(DetailViewControl view, ListViewItem item)
        {
            view.Show(item);
        }

        /// <summary>Folds the details back in. The selection stays where it is.</summary>
        public void CloseDetails()
        {
            if (!_detailsOpen)
                return;

            _detailsOpen = false;

            UpdateInlineDetails();
            RecomposeToMain();
        }

        /// <summary>
        /// Puts the panel where it belongs: straight after the picked row, or nowhere.
        ///
        /// The panel is an ordinary child of the list sitting between two rows, which is what
        /// makes the row details behave: the stacking layout pushes everything below it down,
        /// the scrolling container scrolls it with the rows, and the clip cuts it at the same
        /// edge. Nothing here has to know that it is not a row.
        ///
        /// Cheap to call whenever anything moved - taking it out and putting it back in the same
        /// place is what it does when nothing changed.
        /// </summary>
        private void UpdateInlineDetails()
        {
            bool wanted = _detailsOpen && _detailMode == ListViewDetailMode.Inline;

            ListViewItem? selected = SelectedItem;
            int after = selected == null ? -1 : Children.IndexOf(selected);

            if (!wanted || after < 0)
            {
                Children.Remove(DetailView);
                return;
            }

            int target = after + 1;

            if (Children.IndexOf(DetailView) == target)
                return;

            Children.Remove(DetailView);

            // Removing it may have been in front of the row, so ask again.
            target = Children.IndexOf(selected!) + 1;

            Children.Insert(target, DetailView);
        }
        #endregion

        #region Layout
        /// <summary>
        /// Hands the folded out panel the width it is to lay itself out for, before anything
        /// measures it.
        ///
        /// It cannot work that width out for itself: the paragraph in it wraps, so it needs a
        /// width before it can have a height, and the only width it could read off itself is the
        /// one the previous arrange pass stretched it to - which would make the layout depend on
        /// how often it has run. The list has the answer up front, out of its own size.
        ///
        /// The scrollbar strip is taken off whether a bar is showing or not. That is a hair
        /// narrow for a list that does not scroll, and it is deliberate: whether the bar shows
        /// depends on how tall the content is, which is what is being measured here - so reading
        /// it back would be the same circle. Being a few pixels narrow only wraps the text a
        /// word earlier; the panel itself is stretched to the real width by the arrange pass.
        /// </summary>
        public override PointD CalculateSize()
        {
            // An auto sizing list takes its width from its widest row, so it has no width to
            // hand down before the rows have been measured - the panel keeps its own there.
            if (_detailMode == ListViewDetailMode.Inline && !IsAutoSize)
            {
                double strip = EnableVerticalScrollbar ? ScrollbarStyle.UnscaledWidth * LayoutScale : 0;
                double margins = DetailView.Margin * LayoutScale * 2;

                DetailView.ImposedWidth = Math.Max(
                    1, ScaledExplicitSize.X - ScaledPadding * 2 - strip - margins);
            }
            else
            {
                DetailView.ImposedWidth = 0;
            }

            return base.CalculateSize();
        }
        #endregion
    }

    /// <summary>
    /// One row of a <see cref="ListViewControl"/>: a caption, optionally a second column, and
    /// everything the detail view shows when it is clicked.
    ///
    /// The row carries its own details rather than the list looking them up. That is what lets a
    /// list of anything at all - a rock, a recipe, a player, a waypoint - open a detail panel
    /// without the list knowing what it is listing.
    /// </summary>
    public class ListViewItem : ListRowControl
    {
        #region Properties
        /// <summary>Whatever the caller wants to get back out of the selection.</summary>
        public object? Value { get; set; }

        /// <summary>
        /// A second column, right aligned and dimmer - a count, a state, a date. Null for a row
        /// that is only a caption.
        ///
        /// A row that has none does not carry an empty label for it: a control in the tree is a
        /// control that is measured, laid out and can be tabbed to, and one that is zero wide
        /// because it has nothing to say is a hole in the layout rather than a column.
        /// </summary>
        public string? Secondary
        {
            get => string.IsNullOrEmpty(_secondary.Text) ? null : _secondary.Text;
            set
            {
                string text = value ?? "";

                if (_secondary.Text == text)
                    return;

                _secondary.Text = text;

                if (text.Length == 0)
                {
                    Children.Remove(_secondary);
                }
                else if (!Children.Contains(_secondary))
                {
                    Children.Add(_secondary);
                }

                RecomposeToMain();
            }
        }

        /// <summary>True while the row has a second column to place.</summary>
        private bool HasSecondary => !string.IsNullOrEmpty(_secondary.Text);

        /// <summary>The paragraph the detail view shows. Null for a row with nothing to add.</summary>
        public string? Description { get; set; }

        /// <summary>The labelled facts the detail view shows under the paragraph.</summary>
        public IList<DetailEntry> Details { get; } = new List<DetailEntry>();

        /// <summary>The list this row belongs to. Set when it is handed to the list.</summary>
        internal ListViewControl? OwnerList { get; set; }
        #endregion

        private readonly TextLabelControl _secondary;

        /// <summary>A plain caption row, optionally with one of the game's GUI icons.</summary>
        public ListViewItem(string text, object? value = null, string? iconName = null)
            : this(text, value, iconName, null)
        {
        }

        /// <summary>
        /// A row that stands for an item stack: the stack is its icon, its name is the caption
        /// unless one is given, and hovering it shows the game's item tooltip.
        /// </summary>
        public ListViewItem(ItemStack stack, object? value = null, string? text = null)
            : this(text ?? stack?.GetName() ?? "", value, null, stack)
        {
        }

        private ListViewItem(string text, object? value, string? iconName, ItemStack? stack)
            : base(text, iconName, stack)
        {
            Value = value;

            _secondary = new TextLabelControl(
                text: "",
                fontName: GuiStyle.StandardFontName,
                fontSize: RowFontSize,

                // Dimmer than the caption: the second column is context, not the name of the
                // thing, and two columns of equal weight make a row with no obvious start.
                textColor: new ElementColor(1.0, 1.0, 1.0, 0.55),
                orientation: TextOrientation.MiddleRight,
                padding: 0,
                _Name: text + "_secondary",
                _Margin: 0,
                _Padding: 0);

            // Not added here: it joins the tree when it is given something to show.
        }

        /// <inheritdoc/>
        protected override double ExtraWidth =>
            HasSecondary ? _secondary.MeasureNaturalSize().X : 0;

        /// <summary>
        /// The second column takes the right hand end of the row and the caption gets what is
        /// left, so the two never overlap however the row is stretched. A row without a second
        /// column gives it nothing at all rather than a zero wide box at the right edge.
        /// </summary>
        protected override void PlaceParts()
        {
            base.PlaceParts();

            if (!HasSecondary)
                return;

            double textLeft = TextLeft * LayoutScale;
            double available = Math.Max(0, Size.X - textLeft);

            // The same air on the right that the caption has on the left, so the column does not
            // end flush against the frame - or against the scrollbar, which is where a list long
            // enough to need one puts its right edge.
            double inset = RightInset * LayoutScale;

            // Its natural width, capped at half the room - a long second column must not push
            // the caption out of the row it belongs to.
            double width = Math.Min(_secondary.MeasureNaturalSize().X, Math.Max(0, available / 2.0 - inset));

            _secondary.SetLayoutSize(new PointD(width, Size.Y));
            _secondary.Position = new PointD(Position.X + Size.X - width - inset, Position.Y);

            Label.SetLayoutSize(new PointD(Math.Max(0, available - width - inset), Size.Y));
        }

        protected override void OnActivated(MouseEventArgs e)
        {
            OwnerList?.OnRowActivated(this);
        }
    }
}
