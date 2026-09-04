using Cairo;
using System;

namespace IS2Mod.ControlTypes
{
    /// <summary>
    /// Everything about a scrolling container's geometry for one layout pass: which bars are
    /// showing, how much room is left for the content, and how far it may be shifted.
    ///
    /// It is computed fresh from the container every time anybody asks, never stored. That is
    /// deliberate: the clip region, the stretching of children, the hit test and the drawing all
    /// need the same answer, and a cached one could disagree with the layout that produced it.
    /// Being a pure function of (box, content size, switches, thickness) also keeps the layout
    /// idempotent - running the pass twice cannot land on a different set of bars.
    /// </summary>
    public readonly struct ScrollLayout
    {
        /// <summary>The area the content is actually laid out into and clipped to.</summary>
        public LayoutRect Viewport { get; }

        public bool VerticalBarVisible { get; }
        public bool HorizontalBarVisible { get; }

        /// <summary>How far the content may be shifted on each axis, never negative.</summary>
        public PointD MaxOffset { get; }

        /// <summary>Bar width, in device pixels.</summary>
        public double Thickness { get; }

        /// <summary>The full padding box, before any bar was taken off.</summary>
        public LayoutRect FullBox { get; }

        private ScrollLayout(
            LayoutRect viewport, bool vertical, bool horizontal,
            PointD maxOffset, double thickness, LayoutRect fullBox)
        {
            Viewport = viewport;
            VerticalBarVisible = vertical;
            HorizontalBarVisible = horizontal;
            MaxOffset = maxOffset;
            Thickness = thickness;
            FullBox = fullBox;
        }

        /// <summary>
        /// Works out which bars are needed and what is left over for the content.
        ///
        /// The two axes depend on each other - showing a vertical bar costs width, which can be
        /// what tips the content into needing a horizontal one, and the other way round. Two
        /// rounds settle that: the second sees the space the first one took. A third round could
        /// only ever flip a bar back off after the second turned it on, which reads as flicker,
        /// so it stops here. The cost is one case where a bar shows although the content would
        /// have fit without it by a hair.
        /// </summary>
        public static ScrollLayout Resolve(
            LayoutRect fullBox,
            PointD contentSize,
            bool enableVertical,
            bool enableHorizontal,
            double thickness)
        {
            bool vertical = false;
            bool horizontal = false;

            for (int round = 0; round < 2; round++)
            {
                double availableWidth = fullBox.Width - (vertical ? thickness : 0);
                double availableHeight = fullBox.Height - (horizontal ? thickness : 0);

                vertical = enableVertical && contentSize.Y > availableHeight + 0.001;
                horizontal = enableHorizontal && contentSize.X > availableWidth + 0.001;
            }

            var viewport = new LayoutRect(
                fullBox.X,
                fullBox.Y,
                Math.Max(0, fullBox.Width - (vertical ? thickness : 0)),
                Math.Max(0, fullBox.Height - (horizontal ? thickness : 0)));

            var maxOffset = new PointD(
                Math.Max(0, contentSize.X - viewport.Width),
                Math.Max(0, contentSize.Y - viewport.Height));

            return new ScrollLayout(viewport, vertical, horizontal, maxOffset, thickness, fullBox);
        }

        /// <summary>
        /// The groove the vertical handle runs in: the strip to the right of the viewport, inset
        /// by the vanilla scrollbar padding, and stopping short of the horizontal bar when both
        /// are showing so the two do not overlap in the corner.
        /// </summary>
        public LayoutRect VerticalTrack(double scale)
        {
            double padding = ScrollbarStyle.UnscaledPadding * scale;

            return new LayoutRect(
                Viewport.Right + padding,
                Viewport.Y + padding,
                Math.Max(0, Thickness - padding * 2),
                Math.Max(0, Viewport.Height - padding * 2));
        }

        /// <summary>The strip below the viewport, mirrored.</summary>
        public LayoutRect HorizontalTrack(double scale)
        {
            double padding = ScrollbarStyle.UnscaledPadding * scale;

            return new LayoutRect(
                Viewport.X + padding,
                Viewport.Bottom + padding,
                Math.Max(0, Viewport.Width - padding * 2),
                Math.Max(0, Thickness - padding * 2));
        }
    }
}
