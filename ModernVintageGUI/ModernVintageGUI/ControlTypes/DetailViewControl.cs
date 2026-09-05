using Cairo;
using IS2Mod.ControlTypes;
using IS2Mod.Enums;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ModernVintageGUI.ControlTypes
{
    /// <summary>One labelled fact in a <see cref="DetailViewControl"/>.</summary>
    public sealed class DetailEntry
    {
        public string Label { get; }

        public string Value { get; }

        public DetailEntry(string label, string value)
        {
            Label = label ?? "";
            Value = value ?? "";
        }
    }

    /// <summary>
    /// The panel that shows everything about one row of a list: its name, a picture of it, a
    /// paragraph of text and a column of labelled facts.
    ///
    /// It is a control of its own rather than something a list draws, because where it goes is
    /// the caller's decision and not the list's. A settings screen wants it beside the list, a
    /// crowded dialog wants it to open over the list and disappear again, and a mod that has its
    /// own idea of what "details" look like wants to fill the panel itself. All three are the
    /// same object here - see <see cref="ListViewControl.DetailMode"/>.
    ///
    /// A panel of its own scrolls, so a long description does not force the dialog to grow. One
    /// that a list has taken inside itself grows instead and lets the list scroll it - see
    /// <see cref="AutoHeight"/>.
    ///
    /// What it never takes from its content is its *width*: the wrapped paragraph needs to know
    /// how wide it may be before it can say how tall it is, so a panel that measured its width
    /// from its content would be asking the paragraph a question that depends on the answer. The
    /// width is either its own <see cref="UIControl.Size"/> or handed down by the list that
    /// holds it.
    /// </summary>
    public class DetailViewControl : RectangleControl
    {
        #region Styling
        /// <summary>What the panel is sized to when the caller says nothing, in author units.</summary>
        public const double UnscaledDefaultWidth = 240.0;
        public const double UnscaledDefaultHeight = 220.0;

        private const int TitleFontSize = 20;
        private const int BodyFontSize = 16;

        /// <summary>The rule under the title.</summary>
        private const double UnscaledRuleHeight = 1.0;

        /// <summary>Room between the blocks of the panel.</summary>
        private const double UnscaledBlockGap = 4.0;

        /// <summary>GuiElementListMenu strokes its box with LineWidth 2.</summary>
        private const int PanelBorderWidth = 2;
        #endregion

        #region Properties
        /// <summary>The heading. Empty while the panel shows nothing.</summary>
        public string Title => _title.Text;

        /// <summary>The paragraph under the picture, or null when there is none.</summary>
        public string? Description { get; private set; }

        /// <summary>The labelled facts, in the order they are shown.</summary>
        public IReadOnlyList<DetailEntry> Entries => _entries;

        /// <summary>True once <see cref="Show"/> has put something in the panel.</summary>
        public bool HasContent { get; private set; }

        /// <summary>
        /// Take the height from the content instead of scrolling inside a fixed one.
        ///
        /// This is what a panel does inside a list: the row details of a list have to push the
        /// rows below them down and be scrolled by the list, and a panel with a scrollbar of its
        /// own inside a list with a scrollbar of its own is two bars next to each other, neither
        /// of which the player wanted.
        ///
        /// The width still comes from outside - see <see cref="ImposedWidth"/>.
        /// </summary>
        public bool AutoHeight
        {
            get => _autoHeight;
            set
            {
                if (_autoHeight == value)
                    return;

                _autoHeight = value;

                // Growing and scrolling are alternatives, so switching one on switches the other
                // off. Clipping goes with it: what a growing panel draws is inside it by
                // construction, and the list around it clips anyway.
                EnableVerticalScrollbar = !value;
                ClipsChildren = !value;

                // The measure pass has to be told to take the height from the content. The
                // explicit size is left alone, so switching back restores the panel's own size
                // rather than whatever it last grew to.
                IsAutoSize = value;

                RecomposeToMain();
            }
        }

        /// <summary>
        /// The width the panel is laid out for, in device pixels, or zero to use its own size.
        ///
        /// Set by a list that holds the panel, once per measure pass. It has to be handed down
        /// rather than read back off the panel's own box: the box is what the arrange pass
        /// stretched it to, and measuring against that would make the wrapped paragraph depend
        /// on the previous layout rather than on the list - so laying the same tree out twice
        /// would give two answers.
        /// </summary>
        internal double ImposedWidth { get; set; }

        /// <summary>The stack the panel is showing a picture of, if any.</summary>
        public ItemStack? PreviewStack => _preview.Itemstack;

        /// <summary>Raised whenever the panel is filled with something else, or emptied.</summary>
        public event EventHandler? ContentChanged;
        #endregion

        #region Parts
        private readonly TextLabelControl _title;
        private readonly RectangleControl _rule;
        private readonly RectangleControl _previewHost;
        private readonly ItemSlotControl _previewSlot;
        private readonly ImageControl _previewIcon;
        private readonly TextLabelControl _description;
        private readonly RectangleControl _entryHost;

        private readonly DummySlot _preview = new DummySlot();
        private readonly List<DetailEntry> _entries = new List<DetailEntry>();
        private UIControl? _content;
        private bool _autoHeight;
        #endregion

        public DetailViewControl(string _Name = "", double _Margin = 5)
            : base(
                borderWidth: PanelBorderWidth,
                borderColor: new ElementColor(0.0, 0.0, 0.0, 0.5),
                backgroundColor: new ElementColor(GuiStyle.DialogStrongBgColor),
                _Name: _Name,
                _Margin: _Margin,
                _Padding: 8)
        {
            InsideOrientation = Orientation.Top;

            Size = new PointD(UnscaledDefaultWidth, UnscaledDefaultHeight);
            IsAutoSize = false;

            // A description of any length has to fit into a panel of a fixed height, and the
            // alternative - growing - is not open to it, see the class comment.
            EnableVerticalScrollbar = true;

            _title = new TextLabelControl(
                text: "",
                fontName: GuiStyle.DecorativeFontName,
                fontSize: TitleFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                padding: 0,
                _Name: _Name + "_title",
                _Margin: 0,
                _Padding: 0);

            _rule = new RectangleControl(
                borderWidth: 0,
                borderColor: ElementColor.Transparent,
                backgroundColor: new ElementColor(1.0, 1.0, 1.0, 0.15),
                _Name: _Name + "_rule",
                _Size: new PointD(0, UnscaledRuleHeight),
                _Margin: UnscaledBlockGap / 2.0,
                _Padding: 0)
            {
                IsAutoSize = false
            };

            // The picture sits in a row of its own so that an empty one costs no height: the
            // host is removed from the tree rather than left standing as a hole.
            _previewHost = new RectangleControl(_Name: _Name + "_previewHost")
            {
                InsideOrientation = Orientation.Left,
                Margin = UnscaledBlockGap / 2.0
            };

            _previewSlot = new ItemSlotControl(_Name: _Name + "_previewSlot")
            {
                Slot = _preview,

                // A picture of the item, not a slot the player operates: it takes nothing and
                // gives nothing, so it has no business in the tab order. The hover tooltip stays
                // - that is the slot describing what it is showing, which is the panel's job.
                IsFocusable = false
            };

            _previewIcon = new ImageControl(
                _Name: _Name + "_previewIcon",
                _Size: new PointD(ItemSlotControl.UnscaledSlotSize, ItemSlotControl.UnscaledSlotSize));

            _description = new TextLabelControl(
                text: "",
                fontName: GuiStyle.StandardFontName,
                fontSize: BodyFontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.TopLeft,
                wordWrap: true,
                padding: 0,
                _Name: _Name + "_description",
                _Margin: UnscaledBlockGap / 2.0,
                _Padding: 0);

            _entryHost = new RectangleControl(_Name: _Name + "_entries")
            {
                InsideOrientation = Orientation.Top,
                Margin = UnscaledBlockGap / 2.0
            };

            Children.Add(_title);
            Children.Add(_rule);
        }

        #region Content
        /// <summary>
        /// Puts something in the panel. Everything but the title is optional, and what is left
        /// out costs no room - an entry with nothing to say is taken off the tree rather than
        /// left standing as an empty line.
        /// </summary>
        public void Show(
            string title,
            string? description = null,
            IEnumerable<DetailEntry>? entries = null,
            ItemStack? stack = null,
            string? iconName = null,
            UIControl? content = null)
        {
            _title.Text = title ?? "";
            Description = string.IsNullOrWhiteSpace(description) ? null : description;

            // A control from a previous row has to go before the new one arrives - it may be the
            // same object, and adding a control that is already in the tree twice would give it
            // two parents.
            if (_content != null && !ReferenceEquals(_content, content))
            {
                Children.Remove(_content);
            }

            _content = content;

            _entries.Clear();

            if (entries != null)
            {
                _entries.AddRange(entries);
            }

            // A clone, not the caller's stack: the panel outlives the click that filled it, and
            // a stack that is still owned by an inventory can be emptied under it.
            _preview.Itemstack = stack?.Clone();
            _previewIcon.IconName = iconName;

            HasContent = true;

            Rebuild();
        }

        /// <summary>Fills the panel from a list row - what a list does when a row is clicked.</summary>
        public void Show(ListViewItem item)
        {
            if (item == null)
            {
                Clear();
                return;
            }

            Show(item.Text, item.Description, item.Details, item.Stack, item.IconName, item.DetailContent);
        }

        /// <summary>Empties the panel. The frame stays, the content does not.</summary>
        public void Clear()
        {
            _title.Text = "";
            Description = null;
            _entries.Clear();
            _preview.Itemstack = null;
            _previewIcon.IconName = null;

            if (_content != null)
            {
                Children.Remove(_content);
                _content = null;
            }

            HasContent = false;

            Rebuild();
        }

        /// <summary>
        /// Rebuilds the tree under the panel from what it was last told to show.
        ///
        /// Blocks are added and removed rather than hidden, because there is no hidden: a
        /// control in the tree takes layout space and can be tabbed to, and an empty description
        /// label would leave a line of air under every title that has no paragraph.
        /// </summary>
        private void Rebuild()
        {
            Children.Clear();

            Children.Add(_title);
            Children.Add(_rule);

            if (_preview.Itemstack != null || _previewIcon.IconName != null)
            {
                _previewHost.Children.Clear();
                _previewHost.Children.Add(_preview.Itemstack != null ? _previewSlot : (UIControl)_previewIcon);

                Children.Add(_previewHost);
            }

            if (Description != null)
            {
                _description.Text = Description;
                Children.Add(_description);
            }

            _entryHost.Children.Clear();

            foreach (DetailEntry entry in _entries)
            {
                _entryHost.Children.Add(new DetailRowControl(entry));
            }

            if (_entryHost.Children.Count > 0)
            {
                Children.Add(_entryHost);
            }

            // Last, under everything the panel says about the row itself - a list of what is
            // *in* it reads as a continuation, not as a heading.
            if (_content != null)
            {
                Children.Add(_content);
            }

            // Back to the top: the panel is showing something else now, and leaving it scrolled
            // to where the previous content ended would open it half way down.
            ScrollTo(0, 0);

            ContentChanged?.Invoke(this, EventArgs.Empty);
            RecomposeToMain();
        }
        #endregion

        #region Layout
        /// <summary>
        /// Hands the wrapped paragraph its width before anything measures it.
        ///
        /// A wrapping label answers "how tall am I" out of how wide it is allowed to be, and the
        /// stretching that would give it that width happens *after* the measure pass. Left to
        /// itself it would measure against whatever width it was stretched to last time, which
        /// is a layout that reads its own previous output - the one thing the measure pass must
        /// never do, because it makes the result depend on how often the dialog has been laid
        /// out. Handing it the width up front is what keeps a second pass on an unchanged tree
        /// producing the same answer.
        /// </summary>
        public override PointD CalculateSize()
        {
            double outer = ImposedWidth > 0 ? ImposedWidth : ScaledExplicitSize.X;
            double inner = Math.Max(1, outer - ScaledPadding * 2 - ScrollbarStrip());

            _description.SetLayoutSize(new PointD(inner, _description.Size.Y));
            _rule.SetLayoutSize(new PointD(inner, UnscaledRuleHeight * LayoutScale));

            if (!_autoHeight)
                return base.CalculateSize();

            // Auto sizing measures both axes from the content, and the width that comes out of
            // that is the widest child - not what the panel was told to be. So the height is
            // taken from the measurement and the width is put back afterwards.
            PointD measured = base.CalculateSize();

            if (outer <= 0)
                return measured;

            measured = new PointD(outer, measured.Y);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }

        /// <summary>
        /// The width the vertical bar takes, whether one is showing or not.
        ///
        /// Reserving it unconditionally costs a few pixels on a panel that does not scroll and
        /// buys the paragraph a width that does not depend on its own height - which it would,
        /// if the bar appeared because the text wrapped to one line more.
        /// </summary>
        private double ScrollbarStrip()
        {
            return EnableVerticalScrollbar ? ScrollbarStyle.UnscaledWidth * LayoutScale : 0;
        }
        #endregion
    }

    /// <summary>
    /// One "label: value" line of a <see cref="DetailViewControl"/>.
    ///
    /// It places its two labels itself rather than stacking them, so the values of a whole
    /// column line up: two labels stacked left to right each keep their natural width, and a
    /// column whose second half starts at a different place on every line is a table that has
    /// stopped being a table.
    /// </summary>
    public class DetailRowControl : UIControl
    {
        /// <summary>How much of the line the label column takes.</summary>
        public double LabelFraction { get; set; } = 0.45;

        private const int FontSize = 16;

        private readonly TextLabelControl _label;
        private readonly TextLabelControl _value;

        public DetailRowControl(DetailEntry entry)
            : base(_Orientation: Orientation.None, _Margin: 1, _Padding: 0)
        {
            _label = new TextLabelControl(
                text: entry.Label,
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,

                // Dimmer than the value: the label is the question and the value is the answer,
                // and a column where both are equally loud reads as noise.
                textColor: new ElementColor(1.0, 1.0, 1.0, 0.55),
                orientation: TextOrientation.MiddleLeft,
                padding: 0,
                _Margin: 0,
                _Padding: 0);

            _value = new TextLabelControl(
                text: entry.Value,
                fontName: GuiStyle.StandardFontName,
                fontSize: FontSize,
                textColor: new ElementColor(GuiStyle.DialogDefaultTextColor),
                orientation: TextOrientation.MiddleLeft,
                padding: 0,
                _Margin: 0,
                _Padding: 0);

            Children.Add(_label);
            Children.Add(_value);
        }

        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                child.CalculateSize();
            }

            PointD measured = ClampToMaxSize(new PointD(
                _label.Size.X + _value.Size.X,
                Math.Max(_label.Size.Y, _value.Size.Y)));

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

        private void PlaceParts()
        {
            double labelWidth = Math.Max(0, Size.X * LabelFraction);

            _label.SetLayoutSize(new PointD(labelWidth, Size.Y));
            _label.Position = Position;

            _value.SetLayoutSize(new PointD(Math.Max(0, Size.X - labelWidth), Size.Y));
            _value.Position = new PointD(Position.X + labelWidth, Position.Y);
        }
    }
}
