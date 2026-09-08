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
    /// <summary>Which splitter moved, and where every panel stands now.</summary>
    public class SplitterMovedEventArgs : EventArgs
    {
        /// <summary>The splitter that was dragged: 0 sits between panel 0 and panel 1.</summary>
        public int SplitterIndex { get; }

        /// <summary>The share of the length each panel has now, in order. They add up to 1.</summary>
        public IReadOnlyList<double> Fractions { get; }

        public SplitterMovedEventArgs(int splitterIndex, IReadOnlyList<double> fractions)
        {
            SplitterIndex = splitterIndex;
            Fractions = fractions;
        }
    }

    /// <summary>
    /// A box cut into two or more panels with a grab bar between each pair. The player drags a
    /// bar to give one panel room at the cost of its neighbour; the other panels stay where they
    /// are.
    ///
    /// <see cref="UIControl.InsideOrientation"/> decides where the bars sit - the same way it
    /// decides the stacking direction of any container, because that is what it is here too:
    /// <see cref="Orientation.Left"/> (the default) puts the panels side by side, so the bars
    /// are vertical; <see cref="Orientation.Top"/> stacks them, so the bars are horizontal.
    ///
    /// <code>
    /// var split = new SplitPanelControl(panelCount: 3, Orientation.Left)
    /// {
    ///     Size = new PointD(420, 240)
    /// };
    ///
    /// split.Panels[0].Children.Add(tree);
    /// split.Panels[1].Children.Add(list);
    /// split.Panels[2].Children.Add(details);
    /// split.SetFractions(0.25, 0.45, 0.30);
    /// </code>
    ///
    /// The panels are ordinary <see cref="RectangleControl"/>s that clip what is put in them, so
    /// content larger than its panel is cut at the bar rather than squashed. Give the split panel
    /// a size: its panels are shares of a whole, and a whole that grew to fit its content would
    /// leave nothing to share out. The shares are kept as fractions rather than pixels, which is
    /// what makes them survive a change of the GUI scale.
    /// </summary>
    public class SplitPanelControl : UIControl
    {
        #region Defaults
        public const double UnscaledDefaultWidth = 400.0;
        public const double UnscaledDefaultHeight = 240.0;

        /// <summary>How wide a grab bar is, in author units.</summary>
        public const double UnscaledDefaultSplitterThickness = 8.0;

        /// <summary>How small a panel may be dragged, in author units.</summary>
        public const double UnscaledDefaultMinPanelSize = 24.0;

        /// <summary>How far one arrow key press moves a focused splitter, in author units.</summary>
        public const double UnscaledKeyboardStep = 10.0;
        #endregion

        #region Properties
        /// <summary>The panels, in order. Put your controls into their <c>Children</c>.</summary>
        public IReadOnlyList<RectangleControl> Panels => _panels;

        /// <summary>Shorthand for <c>Panels[index]</c>.</summary>
        public RectangleControl this[int index] => _panels[index];

        public int PanelCount => _panels.Count;

        /// <summary>The number of grab bars, always one less than the number of panels.</summary>
        public int SplitterCount => _splitters.Count;

        /// <summary>
        /// The share of the length each panel has, in order, adding up to 1. Hidden panels keep
        /// their share and get it back when they are shown again; while hidden their share is
        /// spread over the visible ones.
        /// </summary>
        public IReadOnlyList<double> Fractions => _fractions;

        /// <summary>
        /// True when the panels are stacked and the bars run horizontally, i.e. when
        /// <see cref="UIControl.InsideOrientation"/> is Top or Bottom.
        /// </summary>
        public bool IsStacked =>
            InsideOrientation == Orientation.Top || InsideOrientation == Orientation.Bottom;

        private double _splitterThickness = UnscaledDefaultSplitterThickness;

        /// <summary>The width of a grab bar, in author units.</summary>
        public double SplitterThickness
        {
            get => _splitterThickness;
            set
            {
                if (SetProperty(ref _splitterThickness, Math.Max(1, value)))
                    InvalidateLayout();
            }
        }

        private double _minPanelSize = UnscaledDefaultMinPanelSize;

        /// <summary>
        /// The smallest a panel can be dragged to, in author units. A panel never disappears
        /// under a drag; hide it with <see cref="UIControl.IsVisible"/> instead.
        /// </summary>
        public double MinPanelSize
        {
            get => _minPanelSize;
            set
            {
                if (SetProperty(ref _minPanelSize, Math.Max(0, value)))
                    InvalidateLayout();
            }
        }

        /// <summary>The colour of a bar at rest.</summary>
        public ElementColor SplitterColor { get; set; } = new ElementColor(0.0, 0.0, 0.0, 0.25);

        /// <summary>The colour of a bar under the cursor or while it is being dragged.</summary>
        public ElementColor SplitterHoverColor { get; set; } = new ElementColor(0.0, 0.0, 0.0, 0.45);

        /// <summary>The colour of the grip dots drawn in the middle of a bar.</summary>
        public ElementColor GripColor { get; set; } = new ElementColor(
            GuiStyle.DialogDefaultTextColor[0],
            GuiStyle.DialogDefaultTextColor[1],
            GuiStyle.DialogDefaultTextColor[2],
            0.6);

        /// <summary>Whether the player may drag the bars at all. On by default.</summary>
        public bool IsResizable { get; set; } = true;

        /// <summary>Raised whenever a bar is moved, by drag, by keyboard or from code.</summary>
        public event EventHandler<SplitterMovedEventArgs>? SplitterMoved;
        #endregion

        private readonly List<RectangleControl> _panels = new List<RectangleControl>();
        private readonly List<SplitterControl> _splitters = new List<SplitterControl>();
        private double[] _fractions;

        /// <param name="panelCount">
        /// How many panels the box is cut into. Two panels have one bar between them, three have
        /// two, and so on. At least two.
        /// </param>
        /// <param name="orientation">
        /// Becomes <see cref="UIControl.InsideOrientation"/>: Left or Right for panels side by
        /// side with vertical bars, Top or Bottom for stacked panels with horizontal bars.
        /// Anything else is read as Left.
        /// </param>
        public SplitPanelControl(
            int panelCount = 2,
            Orientation orientation = Orientation.Left,
            string _Name = "",
            double _Margin = 5)
            : base(_Name, new PointD(UnscaledDefaultWidth, UnscaledDefaultHeight), NormalizeOrientation(orientation), _Margin, _Padding: 0)
        {
            IsAutoSize = false;

            panelCount = Math.Max(2, panelCount);
            _fractions = new double[panelCount];

            for (int i = 0; i < panelCount; i++)
            {
                _fractions[i] = 1.0 / panelCount;
                AppendPanel(i);
            }
        }

        /// <summary>
        /// A split panel has to stack in *some* direction: an overlay (None) would put every
        /// panel on the same spot, and the cross alignment values are not directions at all.
        /// </summary>
        private static Orientation NormalizeOrientation(Orientation orientation)
        {
            switch (orientation)
            {
                case Orientation.Top:
                case Orientation.Bottom:
                case Orientation.Left:
                case Orientation.Right:
                    return orientation;

                default:
                    return Orientation.Left;
            }
        }

        #region Panels
        /// <summary>
        /// Adds a panel at the end, with a bar in front of it. The new panel gets an equal share
        /// and the others give it up proportionally.
        /// </summary>
        public RectangleControl AddPanel()
        {
            int count = _panels.Count + 1;
            var fractions = new double[count];

            for (int i = 0; i < count - 1; i++)
            {
                fractions[i] = _fractions[i] * (count - 1) / count;
            }

            fractions[count - 1] = 1.0 / count;
            _fractions = fractions;

            RectangleControl panel = AppendPanel(count - 1);

            InvalidateLayout();
            return panel;
        }

        /// <summary>
        /// Takes a panel out, together with the bar next to it. Its share goes to the others
        /// proportionally. A split panel keeps at least two panels.
        /// </summary>
        public bool RemovePanel(int index)
        {
            if (index < 0 || index >= _panels.Count || _panels.Count <= 2)
                return false;

            RectangleControl panel = _panels[index];

            // The bar to remove is the one in front of the panel - or, for the first panel, the
            // one behind it. Either way the count of bars ends up one less than the panels.
            SplitterControl splitter = _splitters[Math.Max(0, index - 1)];

            Children.Remove(panel);
            Children.Remove(splitter);
            _panels.RemoveAt(index);
            _splitters.Remove(splitter);

            double removed = _fractions[index];
            var fractions = new double[_panels.Count];
            double rest = 1.0 - removed;

            for (int i = 0, j = 0; i < _fractions.Length; i++)
            {
                if (i == index)
                    continue;

                fractions[j++] = rest > 0 ? _fractions[i] / rest : 1.0 / _panels.Count;
            }

            _fractions = fractions;
            RenumberSplitters();

            InvalidateLayout();
            return true;
        }

        private RectangleControl AppendPanel(int index)
        {
            if (index > 0)
            {
                var splitter = new SplitterControl(this, index - 1, Name + "_splitter" + (index - 1));
                _splitters.Add(splitter);
                Children.Add(splitter);
            }

            var panel = new RectangleControl(_Name: Name + "_panel" + index)
            {
                InsideOrientation = Orientation.Top,

                // Content larger than the panel is cut at the bar, not squashed - a list in a
                // panel dragged narrow should lose its right edge and keep its rows readable.
                ClipsChildren = true
            };

            _panels.Add(panel);
            Children.Add(panel);

            return panel;
        }

        private void RenumberSplitters()
        {
            for (int i = 0; i < _splitters.Count; i++)
            {
                _splitters[i].Index = i;
            }
        }
        #endregion

        #region Fractions
        /// <summary>
        /// Gives every panel its share of the length. Fewer values than panels leave the rest
        /// sharing what is left equally; the values are normalised, so 1, 2, 1 means a quarter,
        /// a half and a quarter.
        /// </summary>
        public void SetFractions(params double[] fractions)
        {
            if (fractions == null || fractions.Length == 0)
                return;

            var next = new double[_panels.Count];
            double given = 0;
            int specified = Math.Min(fractions.Length, next.Length);

            for (int i = 0; i < specified; i++)
            {
                next[i] = Math.Max(0, fractions[i]);
                given += next[i];
            }

            if (given <= 0)
                return;

            // Panels the caller did not mention share what is left, or - when the given values
            // already fill the whole - an equal share alongside them.
            int unspecified = next.Length - specified;

            if (unspecified > 0)
            {
                double each = given / specified;

                for (int i = specified; i < next.Length; i++)
                {
                    next[i] = each;
                    given += each;
                }
            }

            for (int i = 0; i < next.Length; i++)
            {
                next[i] /= given;
            }

            _fractions = next;
            InvalidateLayout();
        }

        /// <summary>
        /// Puts a bar at a position given as a fraction of the whole length - 0.3 puts the first
        /// bar at thirty percent. Only the two panels either side of the bar change.
        /// </summary>
        public void SetSplitterPosition(int splitterIndex, double position)
        {
            if (splitterIndex < 0 || splitterIndex >= _splitters.Count)
                return;

            int left = splitterIndex;
            int right = NextVisiblePanel(left);

            if (right < 0 || !_panels[left].IsVisible)
                return;

            double before = 0;

            for (int i = 0; i < left; i++)
            {
                if (_panels[i].IsVisible)
                    before += _fractions[i];
            }

            // The position is a share of what is on screen, so a hidden panel's share is left out.
            double visibleTotal = VisibleFractionTotal(_fractions);
            double pair = _fractions[left] + _fractions[right];
            double wanted = Math.Clamp(position, 0, 1) * visibleTotal - before;

            ApplyPair(splitterIndex, left, right, pair, wanted, _fractions);
        }

        /// <summary>
        /// Moves a bar by a distance in device pixels, positive towards the end of the box.
        /// This is what the keyboard uses; the drag goes through the same arithmetic with the
        /// shares it started from, so a long drag cannot drift.
        /// </summary>
        public void MoveSplitter(int splitterIndex, double deltaPixels)
        {
            MoveSplitter(splitterIndex, deltaPixels, _fractions);
        }

        internal void MoveSplitter(int splitterIndex, double deltaPixels, double[] baseFractions)
        {
            if (splitterIndex < 0 || splitterIndex >= _splitters.Count)
                return;

            int left = splitterIndex;
            int right = NextVisiblePanel(left);

            if (right < 0 || !_panels[left].IsVisible)
                return;

            double available = AvailableLength();

            if (available <= 0)
                return;

            // The shares are of the visible length; a hidden panel's share is not on screen and
            // must not count towards how far a pixel moves the bar.
            double visibleTotal = VisibleFractionTotal(baseFractions);

            if (visibleTotal <= 0)
                return;

            double deltaFraction = deltaPixels / available * visibleTotal;
            double pair = baseFractions[left] + baseFractions[right];

            ApplyPair(splitterIndex, left, right, pair, baseFractions[left] + deltaFraction, baseFractions);
        }

        /// <summary>
        /// Splits <paramref name="pair"/> - what the two panels either side of a bar have
        /// together - so that the left one gets <paramref name="wantedLeft"/>, held inside the
        /// minimum size on both ends.
        /// </summary>
        private void ApplyPair(int splitterIndex, int left, int right, double pair, double wantedLeft, double[] baseFractions)
        {
            double available = AvailableLength();
            double visibleTotal = VisibleFractionTotal(baseFractions);

            // The minimum, as a share. Capped at half the pair so that two panels that are both
            // at the minimum already do not fight over which one gets to break it.
            double minFraction = available > 0 && visibleTotal > 0
                ? Math.Min(MinPanelSize * LayoutScale / available * visibleTotal, pair / 2)
                : 0;

            double newLeft = Math.Clamp(wantedLeft, minFraction, pair - minFraction);
            double newRight = pair - newLeft;

            if (Math.Abs(newLeft - _fractions[left]) < 0.000001 && Math.Abs(newRight - _fractions[right]) < 0.000001)
                return;

            var next = (double[])baseFractions.Clone();
            next[left] = newLeft;
            next[right] = newRight;
            _fractions = next;

            InvalidateLayout();

            SplitterMoved?.Invoke(this, new SplitterMovedEventArgs(splitterIndex, _fractions));
        }

        /// <summary>The next panel after <paramref name="index"/> that is showing, or -1.</summary>
        private int NextVisiblePanel(int index)
        {
            for (int i = index + 1; i < _panels.Count; i++)
            {
                if (_panels[i].IsVisible)
                    return i;
            }

            return -1;
        }

        private double VisibleFractionTotal(double[] fractions)
        {
            double total = 0;

            for (int i = 0; i < _panels.Count && i < fractions.Length; i++)
            {
                if (_panels[i].IsVisible)
                    total += fractions[i];
            }

            return total;
        }

        /// <summary>The length the panels share, in device pixels: the box minus the bars.</summary>
        private double AvailableLength()
        {
            LayoutRect box = ArrangeBox();
            double total = IsStacked ? box.Height : box.Width;
            int visibleSplitters = 0;

            foreach (SplitterControl splitter in _splitters)
            {
                if (splitter.IsVisible)
                    visibleSplitters++;
            }

            return Math.Max(0, total - visibleSplitters * SplitterThickness * LayoutScale);
        }
        #endregion

        #region Layout
        /// <summary>
        /// The box is what it was told to be; the panels are shares of it. The children are
        /// still measured, because their own descendants need their natural sizes for the
        /// arrange pass, but nothing they measure to has any say over the whole.
        /// </summary>
        public override PointD CalculateSize()
        {
            foreach (UIControl child in Children)
            {
                if (child.IsVisible)
                    child.CalculateSize();
            }

            PointD wanted = ScaledExplicitSize;

            // Zero on an axis - a caller that only gave a width, say - falls back to the default.
            PointD measured = ClampToMaxSize(new PointD(
                wanted.X > 0 ? wanted.X : UnscaledDefaultWidth * LayoutScale,
                wanted.Y > 0 ? wanted.Y : UnscaledDefaultHeight * LayoutScale));

            MeasuredContentSize = measured;
            CalculatedSize = measured;
            SetLayoutSize(measured);

            ArrangePanels();

            return measured;
        }

        public override void NormalizeChildrenByDelta()
        {
            // Our sizes first, then the base pass carries the cross axis into the panels and
            // walks on into their children.
            ArrangePanels();
            base.NormalizeChildrenByDelta();
        }

        /// <summary>
        /// Gives every panel and bar its size. Positions are not touched: with the sizes set,
        /// the ordinary stacking of the base class puts each child exactly after the one before
        /// it, which is where a panel or a bar belongs.
        ///
        /// A pure function of the fractions and the box, so running it twice changes nothing.
        /// </summary>
        private void ArrangePanels()
        {
            SyncSplitterVisibility();

            bool stacked = IsStacked;
            LayoutRect box = ArrangeBox();
            double cross = stacked ? box.Width : box.Height;
            double thickness = SplitterThickness * LayoutScale;
            double available = AvailableLength();

            double[] lengths = ResolveLengths(available);

            for (int i = 0; i < _panels.Count; i++)
            {
                RectangleControl panel = _panels[i];

                if (!panel.IsVisible)
                    continue;

                panel.SetLayoutSize(stacked
                    ? new PointD(cross, lengths[i])
                    : new PointD(lengths[i], cross));
            }

            foreach (SplitterControl splitter in _splitters)
            {
                if (!splitter.IsVisible)
                    continue;

                splitter.SetLayoutSize(stacked
                    ? new PointD(cross, thickness)
                    : new PointD(thickness, cross));
            }
        }

        /// <summary>
        /// A bar shows only between two panels that are both showing: the one in front of it and
        /// some later one. A bar next to a hidden panel would be a handle with nothing behind it.
        /// </summary>
        private void SyncSplitterVisibility()
        {
            for (int i = 0; i < _splitters.Count; i++)
            {
                bool visible = _panels[i].IsVisible && NextVisiblePanel(i) >= 0;

                if (_splitters[i].IsVisible != visible)
                {
                    _splitters[i].IsVisible = visible;
                }
            }
        }

        /// <summary>
        /// Turns the shares into device pixels: hidden panels give theirs to the visible ones,
        /// every visible panel gets at least the minimum, and the last visible panel takes
        /// whatever rounding left over so the sum is exactly the available length.
        /// </summary>
        private double[] ResolveLengths(double available)
        {
            var lengths = new double[_panels.Count];
            double visibleTotal = VisibleFractionTotal(_fractions);
            int visibleCount = 0;

            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].IsVisible)
                    visibleCount++;
            }

            if (visibleCount == 0 || available <= 0)
                return lengths;

            double min = Math.Min(MinPanelSize * LayoutScale, available / visibleCount);
            double assigned = 0;
            double flexible = 0;

            // First round: everyone gets their share, nobody less than the minimum.
            for (int i = 0; i < _panels.Count; i++)
            {
                if (!_panels[i].IsVisible)
                    continue;

                double share = visibleTotal > 0 ? _fractions[i] / visibleTotal : 1.0 / visibleCount;
                lengths[i] = Math.Max(min, share * available);
                assigned += lengths[i];

                if (lengths[i] > min)
                    flexible += lengths[i] - min;
            }

            // Raising the small ones may have overshot; take the excess back from the ones
            // above the minimum, in proportion to how far above it they are.
            double excess = assigned - available;

            if (excess > 0 && flexible > 0)
            {
                for (int i = 0; i < _panels.Count; i++)
                {
                    if (!_panels[i].IsVisible || lengths[i] <= min)
                        continue;

                    lengths[i] -= excess * (lengths[i] - min) / flexible;
                }
            }

            // Whatever the doubles left behind lands on the last visible panel, so the panels
            // and the bars together are the box to the pixel and nothing is clipped by a hair.
            double sum = 0;
            int last = -1;

            for (int i = 0; i < _panels.Count; i++)
            {
                if (!_panels[i].IsVisible)
                    continue;

                sum += lengths[i];
                last = i;
            }

            if (last >= 0)
            {
                lengths[last] = Math.Max(0, lengths[last] + (available - sum));
            }

            return lengths;
        }
        #endregion

        #region Splitter look
        internal ElementColor BarColor(bool lit) => lit ? SplitterHoverColor : SplitterColor;
        #endregion
    }

    /// <summary>
    /// The grab bar between two panels. It draws a strip with three grip dots, lights up under
    /// the cursor, and while the button is held it moves with the cursor - through the dialog's
    /// mouse capture, so the drag survives the cursor running ahead of the bar.
    ///
    /// Focusable, so that a player without a mouse can still move it: with the bar focused, the
    /// arrow keys along its axis move it a step at a time.
    /// </summary>
    internal sealed class SplitterControl : UIControl
    {
        private readonly SplitPanelControl _owner;

        /// <summary>Which bar this is: 0 sits between panel 0 and panel 1.</summary>
        public int Index { get; set; }

        private bool _isHovered;
        private bool _isDragging;
        private double _dragStart;
        private double[] _dragStartFractions = Array.Empty<double>();

        public SplitterControl(SplitPanelControl owner, int index, string name)
            : base(name, _Size: null, Orientation.None, _Margin: 0, _Padding: 0)
        {
            _owner = owner;
            Index = index;

            IsFocusable = true;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            KeyDown += OnKeyDown;

            Enter += (sender, e) => { _isHovered = true; InvalidateVisual(); };
            Exit += (sender, e) => { _isHovered = false; InvalidateVisual(); };
            GotFocus += (sender, e) => InvalidateVisual();
            LostFocus += (sender, e) => InvalidateVisual();
        }

        /// <summary>A bar is one hit target.</summary>
        protected override UIControl? HitTestRecursive(UIControl control, double localX, double localY)
        {
            return control.ContainsLocalPoint(localX, localY) ? control : null;
        }

        /// <summary>
        /// The owner sizes the bar in its own arrange step; this only has to give it something
        /// sensible for the moment between the measure and the arrange.
        /// </summary>
        public override PointD CalculateSize()
        {
            double thickness = _owner.SplitterThickness * LayoutScale;
            PointD measured = new PointD(thickness, thickness);

            CalculatedSize = measured;
            SetLayoutSize(measured);

            return measured;
        }

        #region Interaction
        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (!_owner.IsResizable || e.Button != EnumMouseButton.Left)
                return;

            _isDragging = true;
            _dragStart = _owner.IsStacked ? e.Y : e.X;
            _dragStartFractions = new double[_owner.Fractions.Count];

            for (int i = 0; i < _dragStartFractions.Length; i++)
            {
                _dragStartFractions[i] = _owner.Fractions[i];
            }

            Dialog?.CaptureMouse(this);
            InvalidateVisual();
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            double current = _owner.IsStacked ? e.Y : e.X;

            // Always from where the drag started, never from the last move: a move that was
            // clamped at the minimum would otherwise lose the difference, and the bar would lag
            // behind the cursor on the way back.
            _owner.MoveSplitter(Index, current - _dragStart, _dragStartFractions);
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            Dialog?.ReleaseMouseCapture();
            InvalidateVisual();
        }

        private void OnKeyDown(object? sender, IS2Mod.ControlTypes.Events.KeyEventArgs e)
        {
            if (!_owner.IsResizable)
                return;

            bool stacked = _owner.IsStacked;
            double step = SplitPanelControl.UnscaledKeyboardStep * LayoutScale;

            // Only the keys along the bar's own axis. The other pair stays with the dialog, which
            // uses Up and Down to move the focus - so a vertical bar can still be left by keyboard.
            if (stacked ? e.Key == GlKeys.Up : e.Key == GlKeys.Left)
            {
                _owner.MoveSplitter(Index, -step);
                e.Handled = true;
            }
            else if (stacked ? e.Key == GlKeys.Down : e.Key == GlKeys.Right)
            {
                _owner.MoveSplitter(Index, step);
                e.Handled = true;
            }
        }
        #endregion

        #region Rendering
        public override void GenerateRenderData(ImageSurface surface, Context ctx)
        {
            if (Size.X <= 0 || Size.Y <= 0)
                return;

            bool lit = _isHovered || _isDragging;
            ElementColor bar = _owner.BarColor(lit);

            ctx.Save();

            ctx.Rectangle(Position.X, Position.Y, Size.X, Size.Y);
            ctx.SetSourceRGBA(bar.RNormalized, bar.GNormalized, bar.BNormalized, bar.ANormalized);
            ctx.Fill();

            DrawGrip(ctx);

            if (HasKeyboardFocus)
            {
                // The same highlight the other controls use for their focus ring, as a thin
                // outline just inside the bar.
                double inset = 1.0 * LayoutScale;

                ctx.Rectangle(Position.X + inset / 2, Position.Y + inset / 2, Size.X - inset, Size.Y - inset);
                ctx.SetSourceRGBA(GuiStyle.DialogHighlightColor);
                ctx.LineWidth = inset;
                ctx.Stroke();
            }

            ctx.Restore();

            base.GenerateRenderData(surface, ctx);
        }

        /// <summary>Three dots in the middle of the bar, the way every split view marks its handle.</summary>
        private void DrawGrip(Context ctx)
        {
            bool stacked = _owner.IsStacked;
            double thickness = stacked ? Size.Y : Size.X;
            double radius = Math.Max(1.0, thickness * 0.18);
            double gap = radius * 3.5;

            double centerX = Position.X + Size.X / 2;
            double centerY = Position.Y + Size.Y / 2;

            ElementColor grip = _owner.GripColor;
            ctx.SetSourceRGBA(grip.RNormalized, grip.GNormalized, grip.BNormalized, grip.ANormalized);

            for (int i = -1; i <= 1; i++)
            {
                double x = stacked ? centerX + i * gap : centerX;
                double y = stacked ? centerY : centerY + i * gap;

                ctx.Arc(x, y, radius, 0, Math.PI * 2);
                ctx.Fill();
            }
        }
        #endregion
    }
}
